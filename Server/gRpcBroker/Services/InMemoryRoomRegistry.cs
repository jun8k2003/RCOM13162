using System.Collections.Concurrent;
using Grpc.Core;
using gRpcBroker.Interfaces;
using gRpcBroker.Models;
using RCOM.Channel.Proto;

namespace gRpcBroker.Services
{
    /// <summary>
    /// インメモリによるルーム管理（デモ環境用）。
    /// プロセス内の静的メモリで全ルームを管理する。
    /// </summary>
    public class InMemoryRoomRegistry : IRoomRegistry
    {
        private readonly ConcurrentDictionary<string, Room> _rooms = new();

        /// <summary>
        /// マッチングキーのルームに参加する。
        /// ルームが存在しない場合は指定モードで新規作成する。
        /// モード不一致や Peer モードの人数超過時は例外をスローする。
        /// </summary>
        public (Room room, Guid memberId) Join(
            string matchingKey,
            ChannelMode mode,
            IServerStreamWriter<Message> stream)
        {
            var room = _rooms.GetOrAdd(matchingKey, _ => new Room(mode));

            // モード競合チェック
            if (room.Mode != mode)
            {
                throw new RpcException(new Status(
                    StatusCode.FailedPrecondition,
                    $"Room mode mismatch: room is {room.Mode}, requested {mode}"));
            }

            // Room.Join 内で Peer モードの人数制限をチェック
            var memberId = room.Join(stream);
            return (room, memberId);
        }

        /// <summary>
        /// マッチングキーのルームから退室する。
        /// ルームが空になった場合はルームごと削除する（メモリリーク防止）。
        /// </summary>
        public void Leave(string matchingKey, Guid memberId)
        {
            if (!_rooms.TryGetValue(matchingKey, out var room))
                return;

            room.Leave(memberId);

            if (room.IsEmpty)
                _rooms.TryRemove(matchingKey, out _);
        }
    }
}
