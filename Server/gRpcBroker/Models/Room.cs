using System.Collections.Concurrent;
using Grpc.Core;
using RCOM.Channel.Proto;

namespace gRpcBroker.Models
{
    /// <summary>
    /// ルーム内のメンバー管理とブロードキャストを担当する。
    /// </summary>
    public class Room
    {
        private readonly ConcurrentDictionary<Guid, IServerStreamWriter<Message>>
            _members = new();

        /// <summary>
        /// このルームのチャネルモード。
        /// </summary>
        public ChannelMode Mode { get; }

        public Room(ChannelMode mode)
        {
            Mode = mode;
        }

        /// <summary>
        /// ルームに参加する。メンバーIDを発行して返す。
        /// Peer モードでは最大2人まで。
        /// </summary>
        public Guid Join(IServerStreamWriter<Message> stream)
        {
            if (Mode == ChannelMode.Peer && _members.Count >= 2)
            {
                throw new RpcException(new Status(
                    StatusCode.FailedPrecondition,
                    "Peer mode room is full (max 2 members)"));
            }

            var memberId = Guid.NewGuid();
            _members[memberId] = stream;
            return memberId;
        }

        /// <summary>
        /// ルームから退室する。
        /// </summary>
        public void Leave(Guid memberId)
        {
            _members.TryRemove(memberId, out _);
        }

        /// <summary>
        /// 送信者以外の全メンバーにメッセージをブロードキャストする。
        /// </summary>
        public async Task BroadcastAsync(Guid senderId, Message message)
        {
            var tasks = _members
                .Where(m => m.Key != senderId)
                .Select(m => m.Value.WriteAsync(message));

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// メンバーが0人かどうか。空ルームの削除判定に使用する。
        /// </summary>
        public bool IsEmpty => _members.IsEmpty;
    }
}
