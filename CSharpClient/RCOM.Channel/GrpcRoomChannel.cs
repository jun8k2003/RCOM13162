using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using RCOM.Channel.Proto;

namespace RCOM.Channel
{
    /// <summary>
    /// Layer1: gRPC 双方向ストリーミングによるチャネル実装。
    /// マッチングキーを使ってサーバーのルームに接続し、メッセージを送受信する。
    /// </summary>
    public class GrpcRoomChannel : IRoomChannel
    {
        private readonly string _matchingKey;
        private readonly ChannelMode _mode;
        private readonly Grpc.Core.Channel _grpcChannel;
        private AsyncDuplexStreamingCall<Message, Message> _call;
        private CancellationTokenSource _cts;

        /// <summary>
        /// メッセージ受信ハンドラ。受信した payload（JSON 文字列）を通知する。
        /// </summary>
        public Action<string> OnReceived { get; set; }

        /// <summary>
        /// 接続が切断されたときに呼ばれるハンドラ。
        /// </summary>
        public Action OnDisconnected { get; set; }

        private GrpcRoomChannel(string matchingKey, ChannelMode mode, Grpc.Core.Channel grpcChannel)
        {
            _matchingKey = matchingKey;
            _mode = mode;
            _grpcChannel = grpcChannel;
        }

        /// <summary>
        /// マッチングキーで初期化してサーバーに接続する。
        /// </summary>
        /// <param name="matchingKey">ルーム識別子（GUID 文字列）</param>
        /// <param name="host">サーバーホスト名</param>
        /// <param name="port">サーバーポート番号</param>
        /// <param name="mode">チャネルモード（Peer: 1:1、Group: ブロードキャスト）</param>
        /// <param name="useTls">TLS を使用するか（本番: true、開発: false）</param>
        /// <param name="tlsOptions">
        /// TLS オプション。useTls=true のときのみ有効。
        /// null の場合はシステム CA ストアを使用する（従来の動作）。
        /// </param>
        public static async Task<GrpcRoomChannel> CreateAsync(
            string matchingKey,
            string host,
            int port = 443,
            ChannelMode mode = ChannelMode.Peer,
            bool useTls = true,
            GrpcTlsOptions tlsOptions = null)
        {
            ChannelCredentials credentials;

            if (!useTls)
            {
                credentials = ChannelCredentials.Insecure;
            }
            else if (tlsOptions != null && tlsOptions.AllowInvalidCertificate)
            {
                // 開発用: 証明書検証をスキップ（TLS 暗号化は維持）
                credentials = new SslCredentials(null, null, _ => true);
            }
            else if (tlsOptions != null && !string.IsNullOrEmpty(tlsOptions.TrustedCertFile))
            {
                // サイドロード: 指定した PEM 証明書を信頼アンカーとして使用
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var certPath = Path.IsPathRooted(tlsOptions.TrustedCertFile)
                    ? tlsOptions.TrustedCertFile
                    : Path.Combine(baseDir, tlsOptions.TrustedCertFile);

                if (!File.Exists(certPath))
                    throw new FileNotFoundException($"サーバー証明書が見つかりません: {certPath}");

                credentials = new SslCredentials(File.ReadAllText(certPath));
            }
            else
            {
                // デフォルト: システム CA ストアを使用（従来の動作）
                credentials = new SslCredentials();
            }

            var grpcChannel = new Grpc.Core.Channel(host, port, credentials);
            var channel = new GrpcRoomChannel(matchingKey, mode, grpcChannel);
            await channel.ConnectAsync();
            return channel;
        }

        private Task ConnectAsync()
        {
            var client = new Broker.BrokerClient(_grpcChannel);

            // マッチングキーとチャネルモードをヘッダーに付与して接続
            var headers = new Metadata
            {
                { "matching-key", _matchingKey },
                { "channel-mode", _mode.ToString().ToLowerInvariant() }
            };

            _cts = new CancellationTokenSource();
            _call = client.Connect(headers, cancellationToken: _cts.Token);

            // 受信ループをバックグラウンドで開始
            Task.Run(() => ReceiveLoopAsync(_cts.Token));

            return Task.FromResult(0);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                // .NET Framework 4.8 では IAsyncEnumerable 非対応のため MoveNext ループを使用
                while (await _call.ResponseStream.MoveNext(ct))
                {
                    var message = _call.ResponseStream.Current;
                    OnReceived?.Invoke(message.Payload);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                // キャンセルは正常系
            }
            catch (OperationCanceledException)
            {
                // キャンセルは正常系
            }
            finally
            {
                OnDisconnected?.Invoke();
            }
        }

        /// <summary>
        /// ルーム内の全員にメッセージを送信する。
        /// </summary>
        /// <param name="payload">JSON-RPC 2.0 フォーマットの JSON 文字列</param>
        public async Task SendAsync(string payload)
        {
            if (_call == null)
                throw new InvalidOperationException("Not connected");

            await _call.RequestStream.WriteAsync(new Message
            {
                MatchingKey = _matchingKey,
                Payload = payload,
                MessageId = Guid.NewGuid().ToString()
            });
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _call?.Dispose();
            _grpcChannel?.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
        }
    }
}
