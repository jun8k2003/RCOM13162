using System;
using System.Threading.Tasks;

namespace RCOM.Channel
{
    /// <summary>
    /// Layer1 チャネル層のインターフェース。
    /// メッセージの送受信を抽象化し、実装の差し替え（テスト用モック等）を可能にする。
    /// </summary>
    public interface IRoomChannel : IDisposable
    {
        /// <summary>
        /// メッセージ受信イベント。受信した payload（JSON 文字列）を通知する。
        /// </summary>
        event Action<string> OnReceived;

        /// <summary>
        /// ルーム内の全員にメッセージを送信する。
        /// </summary>
        /// <param name="payload">JSON-RPC 2.0 フォーマットの JSON 文字列</param>
        Task SendAsync(string payload);
    }
}
