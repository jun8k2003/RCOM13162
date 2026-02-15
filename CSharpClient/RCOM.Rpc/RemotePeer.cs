using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RCOM.Channel;

namespace RCOM.Rpc
{
    /// <summary>
    /// リモートピアとの 1:1 通信を実現するクラス。
    /// 内部で RoomChannel（Layer1）を保持し、JSON-RPC 2.0 による Request/Response 対管理を行う。
    /// </summary>
    public class RemotePeer : IRemotePeer
    {
        private readonly IRoomChannel _channel;

        // 送信中のリクエストを ID で管理
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>>
            _pending = new ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>>();

        /// <summary>
        /// 相手からの一方向通知（Notification）受信イベント。
        /// </summary>
        public event Action<JsonRpcResponse> OnNotificationReceived;

        /// <summary>
        /// IRoomChannel を直接指定して生成する（テスト用）。
        /// </summary>
        public RemotePeer(IRoomChannel channel)
        {
            _channel = channel;
            _channel.OnReceived += OnReceived;
        }

        /// <summary>
        /// マッチングキーでサーバーに接続し、RemotePeer を生成する。
        /// Layer1（RoomChannel）の存在を隠蔽する。
        /// </summary>
        /// <param name="matchingKey">ルーム識別子（GUID 文字列）</param>
        /// <param name="host">サーバーホスト名</param>
        /// <param name="port">サーバーポート番号</param>
        /// <param name="useTls">TLS を使用するか（本番: true、開発: false）</param>
        public static async Task<RemotePeer> ConnectAsync(
            string matchingKey,
            string host,
            int port = 443,
            bool useTls = true)
        {
            var channel = await RoomChannel.CreateAsync(
                matchingKey, host, port, ChannelMode.Peer, useTls);
            return new RemotePeer(channel);
        }

        /// <summary>
        /// リモートメソッドを呼び出し、レスポンスを非同期で待つ。
        /// </summary>
        /// <param name="method">メソッド名</param>
        /// <param name="params">パラメータ（null 可）</param>
        /// <param name="timeout">タイムアウト（省略時 30 秒）</param>
        public System.Threading.Tasks.Task<JsonRpcResponse> CallAsync(
            string method,
            object @params = null,
            TimeSpan timeout = default)
        {
            var id = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<JsonRpcResponse>();
            _pending[id] = tcs;

            // JSON-RPC 2.0 フォーマットでリクエストを送信
            var request = JsonConvert.SerializeObject(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params
            });

            var effectiveTimeout = timeout == default ? TimeSpan.FromSeconds(30) : timeout;
            var cts = new CancellationTokenSource(effectiveTimeout);
            cts.Token.Register(() =>
            {
                if (_pending.TryRemove(id, out var t))
                    t.TrySetException(new TimeoutException(string.Format("RPC timeout: {0}", method)));
                cts.Dispose();
            });

            // 送信してから返す
            return _channel.SendAsync(request).ContinueWith(sendTask =>
            {
                if (sendTask.IsFaulted)
                {
                    _pending.TryRemove(id, out _);
                    cts.Dispose();
                    throw sendTask.Exception.InnerException ?? sendTask.Exception;
                }
                return tcs.Task;
            }).Unwrap();
        }

        private void OnReceived(string json)
        {
            JsonRpcResponse response;
            try
            {
                response = JsonConvert.DeserializeObject<JsonRpcResponse>(json);
            }
            catch
            {
                return;
            }

            if (response == null) return;

            if (response.Id != null && _pending.TryRemove(response.Id, out var tcs))
            {
                // リクエストへのレスポンスとして解決
                if (response.Error != null)
                    tcs.TrySetException(new RpcException(response.Error));
                else
                    tcs.TrySetResult(response);
            }
            else
            {
                // 相手からの Notification（一方向通知）
                OnNotificationReceived?.Invoke(response);
            }
        }

        public void Dispose()
        {
            _channel.Dispose();
        }
    }

    /// <summary>
    /// JSON-RPC 2.0 エラーを表す例外。
    /// </summary>
    public class RpcException : Exception
    {
        public JsonRpcError RpcError { get; }

        public RpcException(JsonRpcError error)
            : base(string.Format("[{0}] {1}", error.Code, error.Message))
        {
            RpcError = error;
        }
    }
}
