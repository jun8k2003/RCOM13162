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
        /// メッセージ受信ハンドラ。受信した payload（JSON 文字列）を通知する。
        /// 単一デリゲートとして設定する（event ではない）。
        /// </summary>
        Action<string> OnReceived { get; set; }

        /// <summary>
        /// 接続が切断されたときに呼ばれるハンドラ。
        /// 相手の Dispose やプロセス終了など、受信ループの終了時に発火する。
        /// </summary>
        Action OnDisconnected { get; set; }

        /// <summary>
        /// ルーム内の全員にメッセージを送信する。
        /// </summary>
        /// <param name="payload">JSON-RPC 2.0 フォーマットの JSON 文字列</param>
        Task SendAsync(string payload);
    }
}
