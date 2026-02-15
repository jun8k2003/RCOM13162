namespace RCOM.Channel
{
    /// <summary>
    /// チャネルの接続モード。
    /// </summary>
    public enum ChannelMode
    {
        /// <summary>
        /// 1:1 通信モード。ルームの最大参加人数は2人。
        /// 3人目の接続はサーバーがエラーを返す。
        /// Layer2（JSON-RPC 対管理）はこのモードを前提とする。
        /// </summary>
        Peer,

        /// <summary>
        /// ブロードキャスト通信モード。ルームの参加人数に制限なし。
        /// メッセージは送信者以外の全メンバーに配信される。
        /// </summary>
        Group
    }
}
