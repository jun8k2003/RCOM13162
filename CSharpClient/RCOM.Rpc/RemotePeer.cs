using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RCOM.Channel;

namespace RCOM.Rpc
{
    /// <summary>
    /// リモートピアとの 1:1 通信を実現するクラス。
    /// 内部で IRoomChannel（Layer1）を保持し、JSON-RPC 2.0 による Request/Response 対管理を行う。
    /// </summary>
    public class RemotePeer : IRemotePeer
    {
        private readonly IRoomChannel _channel;

        // 送信中のリクエストを ID で管理
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>>
            _pending = new ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>>();

        /// <summary>
        /// 相手からのリクエスト受信ハンドラ。
        /// method と params を受け取り、戻り値が JSON-RPC Response として自動返送される。
        /// </summary>
        public Func<string, JToken, Task<object>> OnRequest { get; set; }

        /// <summary>
        /// 相手からの一方向通知受信ハンドラ。
        /// method と params を受け取る（応答は返さない）。
        /// </summary>
        public Action<string, JToken> OnNotify { get; set; }

        /// <summary>
        /// 相手が切断したときに呼ばれるハンドラ。
        /// 相手の Dispose による正常切断、プロセス終了による異常切断の両方で発火する。
        /// </summary>
        public Action OnPeerLeave { get; set; }

        /// <summary>
        /// 初期化済みの IRoomChannel を指定して生成する。
        /// GrpcRoomChannel や IpcRoomChannel など任意の実装を注入できる。
        /// </summary>
        public RemotePeer(IRoomChannel channel)
        {
            _channel = channel;
            _channel.OnReceived = OnReceived;
            _channel.OnDisconnected = () => OnPeerLeave?.Invoke();
        }

        /// <summary>
        /// リモートメソッドを呼び出し、レスポンスを非同期で待つ（JSON-RPC Request）。
        /// </summary>
        /// <param name="method">メソッド名</param>
        /// <param name="params">パラメータ（null 可）</param>
        /// <param name="timeout">タイムアウト（省略時 30 秒）</param>
        public Task<JsonRpcResponse> CallAsync(
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

        /// <summary>
        /// 相手に一方向通知を送信する（JSON-RPC Notification、id なし、応答なし）。
        /// </summary>
        /// <param name="method">メソッド名</param>
        /// <param name="params">パラメータ（null 可）</param>
        public Task NotifyAsync(string method, object @params = null)
        {
            var notification = JsonConvert.SerializeObject(new
            {
                jsonrpc = "2.0",
                method,
                @params
            });

            return _channel.SendAsync(notification);
        }

        private void OnReceived(string json)
        {
            JsonRpcMessage message;
            try
            {
                message = JsonConvert.DeserializeObject<JsonRpcMessage>(json);
            }
            catch
            {
                return;
            }

            if (message == null) return;

            if (message.IsRequest)
            {
                // 相手からのリクエストまたは通知
                if (message.IsNotification)
                {
                    // Notification（id なし）→ OnNotify
                    OnNotify?.Invoke(message.Method, message.Params);
                }
                else
                {
                    // Request（id あり）→ OnRequest → 応答を自動返送
                    HandleRequestAsync(message);
                }
            }
            else
            {
                // 自分が送った CallAsync へのレスポンス
                if (message.Id != null && _pending.TryRemove(message.Id, out var tcs))
                {
                    if (message.Error != null)
                        tcs.TrySetException(new RpcException(message.Error));
                    else
                    {
                        var response = new JsonRpcResponse
                        {
                            JsonRpc = message.JsonRpc,
                            Id = message.Id,
                            Result = message.Result,
                            Error = message.Error
                        };
                        tcs.TrySetResult(response);
                    }
                }
            }
        }

        private async void HandleRequestAsync(JsonRpcMessage request)
        {
            var handler = OnRequest;
            if (handler == null) return;

            try
            {
                var result = await handler(request.Method, request.Params);

                var response = JsonConvert.SerializeObject(new
                {
                    jsonrpc = "2.0",
                    id = request.Id,
                    result
                });

                await _channel.SendAsync(response);
            }
            catch (RpcException ex)
            {
                var errorResponse = JsonConvert.SerializeObject(new
                {
                    jsonrpc = "2.0",
                    id = request.Id,
                    error = new { code = ex.RpcError.Code, message = ex.RpcError.Message, data = ex.RpcError.Data }
                });

                await _channel.SendAsync(errorResponse);
            }
            catch (Exception ex)
            {
                var errorResponse = JsonConvert.SerializeObject(new
                {
                    jsonrpc = "2.0",
                    id = request.Id,
                    error = new { code = -32603, message = ex.Message }
                });

                await _channel.SendAsync(errorResponse);
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

        public RpcException(int code, string message)
            : base(string.Format("[{0}] {1}", code, message))
        {
            RpcError = new JsonRpcError { Code = code, Message = message };
        }
    }
}
