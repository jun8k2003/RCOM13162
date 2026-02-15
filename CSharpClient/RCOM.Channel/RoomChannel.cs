using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using RCOM.Channel.Proto;

namespace RCOM.Channel
{
    /// <summary>
    /// Layer1: gRPC 双方向ストリーミングによるチャネル層。
    /// マッチングキーを使ってサーバーのルームに接続し、メッセージを送受信する。
    /// </summary>
    public class RoomChannel : IRoomChannel
    {
        private readonly string _matchingKey;
        private readonly ChannelMode _mode;
        private readonly Grpc.Core.Channel _grpcChannel;
        private AsyncDuplexStreamingCall<Message, Message> _call;
        private CancellationTokenSource _cts;

        /// <summary>
        /// メッセージ受信イベント。受信した payload（JSON 文字列）を通知する。
        /// </summary>
        public event Action<string> OnReceived;

        private RoomChannel(string matchingKey, ChannelMode mode, Grpc.Core.Channel grpcChannel)
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
        public static async Task<RoomChannel> CreateAsync(
            string matchingKey,
            string host,
            int port = 443,
            ChannelMode mode = ChannelMode.Peer,
            bool useTls = true)
        {
            var credentials = useTls
                ? new SslCredentials()
                : ChannelCredentials.Insecure;

            var grpcChannel = new Grpc.Core.Channel(host, port, credentials);
            var channel = new RoomChannel(matchingKey, mode, grpcChannel);
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
