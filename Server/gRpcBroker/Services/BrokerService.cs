using Grpc.Core;
using gRpcBroker.Interfaces;
using gRpcBroker.Models;
using RCOM.Channel.Proto;

namespace gRpcBroker.Services
{
    /// <summary>
    /// gRPC のエンドポイント。
    /// クライアントの接続を受け付け、IRoomRegistry に委譲する。
    /// </summary>
    public class BrokerService : Broker.BrokerBase
    {
        private readonly IRoomRegistry _registry;
        private readonly ILogger<BrokerService> _logger;

        public BrokerService(IRoomRegistry registry, ILogger<BrokerService> logger)
        {
            _registry = registry;
            _logger = logger;
        }

        public override async Task Connect(
            IAsyncStreamReader<Message> requestStream,
            IServerStreamWriter<Message> responseStream,
            ServerCallContext context)
        {
            // ヘッダーからマッチングキーを取得
            var matchingKey = context.RequestHeaders.GetValue("matching-key");
            if (string.IsNullOrEmpty(matchingKey))
            {
                throw new RpcException(new Status(
                    StatusCode.InvalidArgument,
                    "matching-key header is required"));
            }

            // ヘッダーからチャネルモードを取得（デフォルト: peer）
            var modeHeader = context.RequestHeaders.GetValue("channel-mode") ?? "peer";
            if (!Enum.TryParse<ChannelMode>(modeHeader, ignoreCase: true, out var mode))
            {
                throw new RpcException(new Status(
                    StatusCode.InvalidArgument,
                    $"Invalid channel-mode: {modeHeader}"));
            }

            // ルームに参加
            var (room, memberId) = _registry.Join(matchingKey, mode, responseStream);
            _logger.LogInformation(
                "Client joined room {MatchingKey} (mode={Mode}, memberId={MemberId})",
                matchingKey, mode, memberId);

            try
            {
                // クライアントからのメッセージを受信し続けるループ
                await foreach (var message in requestStream.ReadAllAsync(
                    context.CancellationToken))
                {
                    await room.BroadcastAsync(memberId, message);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                // クライアントが切断した（正常系）
            }
            finally
            {
                // 切断時の後片付け（例外発生時も必ず実行される）
                _registry.Leave(matchingKey, memberId);
                _logger.LogInformation(
                    "Client left room {MatchingKey} (memberId={MemberId})",
                    matchingKey, memberId);
            }
        }
    }
}
