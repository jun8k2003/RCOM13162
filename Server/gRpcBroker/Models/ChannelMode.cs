namespace gRpcBroker.Models
{
    /// <summary>
    /// チャネルモード。Peer は 1:1（最大2人）、Group はブロードキャスト（無制限）。
    /// </summary>
    public enum ChannelMode
    {
        Peer,
        Group
    }
}
