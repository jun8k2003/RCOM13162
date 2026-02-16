using Grpc.Core;
using gRpcBroker.Models;
using RCOM.Channel.Proto;

namespace gRpcBroker.Interfaces
{
    /// <summary>
    /// ルーム管理インターフェース。
    /// デモ環境ではインメモリ実装、本番環境では Redis 実装に差し替え可能。
    /// </summary>
    public interface IRoomRegistry
    {
        /// <summary>
        /// マッチングキーのルームに参加する。
        /// ルームが存在しない場合は指定モードで新規作成する。
        /// </summary>
        (Room room, Guid memberId) Join(
            string matchingKey,
            ChannelMode mode,
            IServerStreamWriter<Message> stream);

        /// <summary>
        /// マッチングキーのルームから退室する。
        /// ルームが空になった場合はルームごと削除する。
        /// </summary>
        void Leave(string matchingKey, Guid memberId);
    }
}
