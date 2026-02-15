using System;
using System.Threading.Tasks;
namespace RCOM.Rpc
{
    /// <summary>
    /// リモートピアとの 1:1 通信インターフェース。
    /// JSON-RPC 2.0 によるリモートメソッド呼び出しを抽象化する。
    /// </summary>
    public interface IRemotePeer : IDisposable
    {
        /// <summary>
        /// 相手からの一方向通知（Notification）受信イベント。
        /// </summary>
        event Action<JsonRpcResponse> OnNotificationReceived;

        /// <summary>
        /// リモートメソッドを呼び出し、レスポンスを非同期で待つ。
        /// </summary>
        /// <param name="method">メソッド名</param>
        /// <param name="params">パラメータ（null 可）</param>
        /// <param name="timeout">タイムアウト（省略時 30 秒）</param>
        Task<JsonRpcResponse> CallAsync(string method, object @params = null, TimeSpan timeout = default);
    }
}
