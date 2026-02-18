using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RCOM.Channel
{
    /// <summary>
    /// Layer1: 名前付きパイプによる IPC チャネル実装。
    /// 同一 PC 内のプロセス間通信に使用する。
    /// パイプ名がマッチングキーの役割を果たし、同じパイプ名で接続した 2 プロセスが通信する。
    /// </summary>
    public class IpcRoomChannel : IRoomChannel
    {
        private readonly PipeStream _pipe;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// メッセージ受信ハンドラ。受信した payload（JSON 文字列）を通知する。
        /// </summary>
        public Action<string> OnReceived { get; set; }

        /// <summary>
        /// 接続が切断されたときに呼ばれるハンドラ。
        /// </summary>
        public Action OnDisconnected { get; set; }

        private IpcRoomChannel(PipeStream pipe)
        {
            _pipe = pipe;
        }

        /// <summary>
        /// サーバー側（先に待機する側）として IPC チャネルを作成する。
        /// 相手が接続するまで待機する。
        /// </summary>
        /// <param name="pipeName">パイプ名（マッチングキー相当）</param>
        public static async Task<IpcRoomChannel> CreateServerAsync(string pipeName)
        {
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await Task.Factory.FromAsync(server.BeginWaitForConnection, server.EndWaitForConnection, null);

            var channel = new IpcRoomChannel(server);
            // 受信ループをバックグラウンドで開始（fire-and-forget）
#pragma warning disable CS4014
            Task.Run(() => channel.ReceiveLoopAsync(channel._cts.Token));
#pragma warning restore CS4014
            return channel;
        }

        /// <summary>
        /// クライアント側（後から接続する側）として IPC チャネルを作成する。
        /// </summary>
        /// <param name="pipeName">パイプ名（マッチングキー相当）</param>
        /// <param name="serverName">サーバー名（デフォルト: ローカルマシン "."）</param>
        public static async Task<IpcRoomChannel> CreateClientAsync(string pipeName, string serverName = ".")
        {
            var client = new NamedPipeClientStream(
                serverName,
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            // .NET Framework 4.8 では BeginConnect/EndConnect が存在しないため Task.Run で非同期化
            await Task.Run(() => client.Connect(5000));

            var channel = new IpcRoomChannel(client);
            // 受信ループをバックグラウンドで開始（fire-and-forget）
#pragma warning disable CS4014
            Task.Run(() => channel.ReceiveLoopAsync(channel._cts.Token));
#pragma warning restore CS4014
            return channel;
        }

        /// <summary>
        /// Adaptive Establishment で IPC チャネルを作成する。
        /// まずクライアントとして接続を試行し、サーバー不在で失敗した場合はサーバーとして待機に切り替える。
        /// 既に他プロセスがサーバー化済みの場合は、再度クライアント接続を試行する。
        /// </summary>
        /// <param name="pipeName">パイプ名（マッチングキー相当）</param>
        /// <param name="serverName">サーバー名（デフォルト: ローカルマシン "."）</param>
        /// <param name="initialClientTimeoutMs">最初のクライアント接続タイムアウト（ミリ秒）</param>
        /// <param name="retryClientTimeoutMs">再試行クライアント接続タイムアウト（ミリ秒）</param>
        public static async Task<IpcRoomChannel> CreateAdaptiveAsync(
            string pipeName,
            string serverName = ".",
            int initialClientTimeoutMs = 1000,
            int retryClientTimeoutMs = 5000)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
                throw new ArgumentException("pipeName is required", nameof(pipeName));

            try
            {
                return await CreateClientInternalAsync(pipeName, serverName, initialClientTimeoutMs);
            }
            catch (TimeoutException)
            {
                // サーバー不在想定。サーバー昇格を試みる。
            }

            try
            {
                return await CreateServerAsync(pipeName);
            }
            catch (IOException)
            {
                // 同時昇格で先行サーバーが確立されたケース。
                // 再度クライアントとして接続する。
                return await CreateClientInternalAsync(pipeName, serverName, retryClientTimeoutMs);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                var headerBuf = new byte[4];
                while (!ct.IsCancellationRequested && _pipe.IsConnected)
                {
                    // 長さプレフィックス（4 バイト little-endian）を読む
                    var bytesRead = await ReadExactAsync(_pipe, headerBuf, 0, 4, ct);
                    if (bytesRead < 4) break;

                    var length = BitConverter.ToInt32(headerBuf, 0);
                    if (length <= 0 || length > 4 * 1024 * 1024) break;

                    var bodyBuf = new byte[length];
                    bytesRead = await ReadExactAsync(_pipe, bodyBuf, 0, length, ct);
                    if (bytesRead < length) break;

                    var payload = Encoding.UTF8.GetString(bodyBuf);
                    OnReceived?.Invoke(payload);
                }
            }
            catch (IOException)
            {
                // パイプ切断は正常系
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
        /// メッセージを送信する。長さプレフィックス + UTF-8 バイト列で書き込む。
        /// </summary>
        public async Task SendAsync(string payload)
        {
            if (!_pipe.IsConnected)
                throw new InvalidOperationException("Not connected");

            var body = Encoding.UTF8.GetBytes(payload);
            var header = BitConverter.GetBytes(body.Length);

            await _pipe.WriteAsync(header, 0, header.Length);
            await _pipe.WriteAsync(body, 0, body.Length);
            await _pipe.FlushAsync();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _pipe.Dispose();
        }

        private static async Task<int> ReadExactAsync(
            Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, ct);
                if (read == 0) return totalRead;
                totalRead += read;
            }
            return totalRead;
        }

        private static async Task<IpcRoomChannel> CreateClientInternalAsync(
            string pipeName,
            string serverName,
            int connectTimeoutMs)
        {
            var client = new NamedPipeClientStream(
                serverName,
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                await Task.Run(() => client.Connect(connectTimeoutMs));

                var channel = new IpcRoomChannel(client);
#pragma warning disable CS4014
                Task.Run(() => channel.ReceiveLoopAsync(channel._cts.Token));
#pragma warning restore CS4014
                return channel;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }
}
