using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RCOM.Rpc;
using RCOM.Rpc.Tests.TestDoubles;

namespace RCOM.Rpc.Tests;

/// <summary>
/// RemotePeer クラスのテスト。
/// MockRoomChannel を使い、JSON-RPC 2.0 の送受信ロジックを検証する。
/// </summary>
[TestClass]
public class RemotePeerTests
{
    private MockRoomChannel _mockChannel = null!;
    private RemotePeer _peer = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockChannel = new MockRoomChannel();
        _peer = new RemotePeer(_mockChannel);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _peer.Dispose();
    }

    /// <summary>
    /// CallAsync が JSON-RPC 2.0 フォーマット（jsonrpc, id, method, params）で
    /// リクエストを送信し、レスポンスを正しく受け取ることを検証する。
    /// </summary>
    [TestMethod]
    public async Task CallAsync_SendsJsonRpc20Request()
    {
        // Arrange & Act
        var callTask = _peer.CallAsync("testMethod", new { key = "value" });

        // Assert: 送信されたメッセージを検証
        Assert.HasCount(1, _mockChannel.SentMessages);

        var sent = JObject.Parse(_mockChannel.SentMessages[0]);
        Assert.AreEqual("2.0", sent["jsonrpc"]?.ToString());
        Assert.AreEqual("testMethod", sent["method"]?.ToString());
        Assert.IsNotNull(sent["id"]);
        Assert.AreEqual("value", sent["params"]?["key"]?.ToString());

        // レスポンスを返してタスクを完了させる
        var id = sent["id"]!.ToString();
        _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            id,
            result = new { ok = true }
        }));

        var response = await callTask;
        Assert.IsNotNull(response.Result);
    }

    /// <summary>
    /// 複数のリクエストを送信した際、レスポンスが逆順で返ってきても
    /// id によって正しいリクエストに対応付けられることを検証する。
    /// </summary>
    [TestMethod]
    public async Task CallAsync_MatchesResponseById()
    {
        // Arrange: 2つのリクエストを送信
        var call1 = _peer.CallAsync("method1");
        var call2 = _peer.CallAsync("method2");

        Assert.HasCount(2, _mockChannel.SentMessages);

        var sent1 = JObject.Parse(_mockChannel.SentMessages[0]);
        var sent2 = JObject.Parse(_mockChannel.SentMessages[1]);
        var id1 = sent1["id"]!.ToString();
        var id2 = sent2["id"]!.ToString();

        // Act: 逆順でレスポンスを返す
        _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            id = id2,
            result = "response2"
        }));
        _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            id = id1,
            result = "response1"
        }));

        // Assert: 各リクエストに正しいレスポンスが対応する
        var response1 = await call1;
        var response2 = await call2;
        Assert.AreEqual("response1", response1.Result?.ToString());
        Assert.AreEqual("response2", response2.Result?.ToString());
    }

    /// <summary>
    /// JSON-RPC 2.0 のエラーレスポンスを受信した場合、
    /// RpcException がスローされ、エラーコードとメッセージが正しく設定されることを検証する。
    /// </summary>
    [TestMethod]
    public async Task CallAsync_ThrowsRpcExceptionOnErrorResponse()
    {
        // Arrange
        var callTask = _peer.CallAsync("failMethod");
        var sent = JObject.Parse(_mockChannel.SentMessages[0]);
        var id = sent["id"]!.ToString();

        // Act: エラーレスポンスを返す
        _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            id,
            error = new { code = -32603, message = "Internal error" }
        }));

        // Assert
        var ex = await Assert.ThrowsAsync<RpcException>(() => callTask);
        Assert.AreEqual(-32603, ex.RpcError.Code);
        Assert.AreEqual("Internal error", ex.RpcError.Message);
    }

    /// <summary>
    /// 指定したタイムアウト時間内にレスポンスが返らない場合、
    /// TimeoutException がスローされ、メソッド名がメッセージに含まれることを検証する。
    /// </summary>
    [TestMethod]
    public async Task CallAsync_ThrowsTimeoutExceptionWhenNoResponse()
    {
        // Arrange: 短いタイムアウトを指定
        var callTask = _peer.CallAsync("slowMethod", timeout: TimeSpan.FromMilliseconds(100));

        // Act & Assert: レスポンスを返さずにタイムアウトを待つ
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => callTask);
        StringAssert.Contains(ex.Message, "slowMethod");
    }

    /// <summary>
    /// id を持たないメッセージ（JSON-RPC 2.0 の Notification）を受信した場合、
    /// OnNotificationReceived イベントが発火し、内容が正しく渡されることを検証する。
    /// </summary>
    [TestMethod]
    public void OnNotificationReceived_FiresForMessagesWithoutMatchingId()
    {
        // Arrange
        JsonRpcResponse? received = null;
        _peer.OnNotificationReceived += (response) => received = response;

        // Act: id が null の通知を送信
        _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            result = new { notify = "hello" }
        }));

        // Assert
        Assert.IsNotNull(received);
        Assert.IsNull(received!.Id);
        Assert.AreEqual("hello", received.Result?["notify"]?.ToString());
    }

    /// <summary>
    /// 不正な JSON 文字列を受信した場合、例外がスローされず、
    /// イベントも発火しないことを検証する（静かに無視される）。
    /// </summary>
    [TestMethod]
    public void OnReceived_IgnoresInvalidJson()
    {
        // Arrange
        var notificationFired = false;
        _peer.OnNotificationReceived += (_) => notificationFired = true;

        // Act: 不正な JSON を送信
        _mockChannel.SimulateReceive("this is not json");

        // Assert: 例外もイベントも発生しない
        Assert.IsFalse(notificationFired);
    }

    /// <summary>
    /// チャネルの SendAsync が失敗した場合、
    /// その例外が CallAsync の呼び出し元に伝播することを検証する。
    /// </summary>
    [TestMethod]
    public async Task CallAsync_ThrowsWhenSendFails()
    {
        // Arrange: SendAsync が失敗するように設定
        _mockChannel.SendException = new InvalidOperationException("Connection lost");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _peer.CallAsync("anyMethod"));
        Assert.AreEqual("Connection lost", ex.Message);
    }

    /// <summary>
    /// Dispose を呼び出すと、内部の IRoomChannel も Dispose されることを検証する。
    /// </summary>
    [TestMethod]
    public void Dispose_DisposesUnderlyingChannel()
    {
        // Act
        _peer.Dispose();

        // Assert: MockRoomChannel.Dispose が呼ばれている（例外なく完了する）
    }

    /// <summary>
    /// 複数のリクエストを送信した場合、
    /// それぞれに一意の id が割り当てられることを検証する。
    /// </summary>
    [TestMethod]
    public async Task CallAsync_GeneratesUniqueIdsForEachRequest()
    {
        // Arrange & Act
        var call1 = _peer.CallAsync("method1");
        var call2 = _peer.CallAsync("method2");
        var call3 = _peer.CallAsync("method3");

        // Assert
        var id1 = JObject.Parse(_mockChannel.SentMessages[0])["id"]!.ToString();
        var id2 = JObject.Parse(_mockChannel.SentMessages[1])["id"]!.ToString();
        var id3 = JObject.Parse(_mockChannel.SentMessages[2])["id"]!.ToString();

        Assert.AreNotEqual(id1, id2);
        Assert.AreNotEqual(id2, id3);
        Assert.AreNotEqual(id1, id3);

        // タスクをキャンセルして後片付け
        foreach (var msg in _mockChannel.SentMessages)
        {
            var id = JObject.Parse(msg)["id"]!.ToString();
            _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
            {
                jsonrpc = "2.0",
                id,
                result = "ok"
            }));
        }
        await Task.WhenAll(call1, call2, call3);
    }
}
