using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RCOM.Rpc
{
    /// <summary>
    /// リモートピアとの 1:1 通信インターフェース。
    /// JSON-RPC 2.0 によるリモートメソッド呼び出しを抽象化する。
    /// </summary>
    public interface IRemotePeer : IDisposable
    {
        /// <summary>
        /// リモートメソッドを呼び出し、レスポンスを非同期で待つ（JSON-RPC Request）。
        /// </summary>
        /// <param name="method">メソッド名</param>
        /// <param name="params">パラメータ（null 可）</param>
        /// <param name="timeout">タイムアウト（省略時 30 秒）</param>
        Task<JsonRpcResponse> CallAsync(string method, object @params = null, TimeSpan timeout = default);

        /// <summary>
        /// 相手に一方向通知を送信する（JSON-RPC Notification、応答なし）。
        /// </summary>
        /// <param name="method">メソッド名</param>
        /// <param name="params">パラメータ（null 可）</param>
        Task NotifyAsync(string method, object @params = null);

        /// <summary>
        /// 相手からのリクエスト受信ハンドラ。
        /// method と params を受け取り、戻り値が JSON-RPC Response として自動返送される。
        /// 単一デリゲートとして設定する（event ではない）。
        /// </summary>
        Func<string, JToken, Task<object>> OnRequest { get; set; }

        /// <summary>
        /// 相手からの一方向通知受信ハンドラ。
        /// method と params を受け取る（応答は返さない）。
        /// 単一デリゲートとして設定する（event ではない）。
        /// </summary>
        Action<string, JToken> OnNotify { get; set; }
    }
}
