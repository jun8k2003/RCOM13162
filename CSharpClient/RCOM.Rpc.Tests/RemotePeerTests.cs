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

    // ────────────────────────────────────────────
    // CallAsync（リクエスト送信）
    // ────────────────────────────────────────────

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

    // ────────────────────────────────────────────
    // NotifyAsync（一方向通知送信）
    // ────────────────────────────────────────────

    /// <summary>
    /// NotifyAsync が JSON-RPC 2.0 Notification フォーマット（id なし）で
    /// メッセージを送信することを検証する。
    /// </summary>
    [TestMethod]
    public async Task NotifyAsync_SendsJsonRpc20NotificationWithoutId()
    {
        // Act
        await _peer.NotifyAsync("logEvent", new { level = "info" });

        // Assert
        Assert.HasCount(1, _mockChannel.SentMessages);

        var sent = JObject.Parse(_mockChannel.SentMessages[0]);
        Assert.AreEqual("2.0", sent["jsonrpc"]?.ToString());
        Assert.AreEqual("logEvent", sent["method"]?.ToString());
        Assert.IsNull(sent["id"]);
        Assert.AreEqual("info", sent["params"]?["level"]?.ToString());
    }

    // ────────────────────────────────────────────
    // OnRequest（リクエスト受信 → 応答自動返送）
    // ────────────────────────────────────────────

    /// <summary>
    /// 相手からの JSON-RPC Request を受信すると OnRequest が呼ばれ、
    /// 戻り値が JSON-RPC Response として自動返送されることを検証する。
    /// </summary>
    [TestMethod]
    public async Task OnRequest_HandlesIncomingRequestAndSendsResponse()
    {
        // Arrange
        _peer.OnRequest = (method, @params) =>
        {
            if (method == "Add")
            {
                var a = @params["a"].Value<int>();
                var b = @params["b"].Value<int>();
                return Task.FromResult<object>(new { sum = a + b });
            }
            throw new RpcException(-32601, "Method not found");
        };

        // Act: 相手からのリクエストをシミュレート
        _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            id = "req-001",
            method = "Add",
            @params = new { a = 3, b = 5 }
        }));

        // async void のため少し待つ
        await Task.Delay(50);

        // Assert: レスポンスが送信されている
        Assert.HasCount(1, _mockChannel.SentMessages);

        var response = JObject.Parse(_mockChannel.SentMessages[0]);
        Assert.AreEqual("2.0", response["jsonrpc"]?.ToString());
        Assert.AreEqual("req-001", response["id"]?.ToString());
        Assert.AreEqual(8, response["result"]?["sum"]?.Value<int>());
        Assert.IsNull(response["error"]);
    }

    /// <summary>
    /// OnRequest ハンドラが RpcException をスローした場合、
    /// JSON-RPC Error Response が自動返送されることを検証する。
    /// </summary>
    [TestMethod]
    public async Task OnRequest_SendsErrorResponseOnRpcException()
    {
        // Arrange
        _peer.OnRequest = (method, @params) =>
        {
            throw new RpcException(-32601, "Method not found");
        };

        // Act
        _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            id = "req-002",
            method = "Unknown"
        }));

        await Task.Delay(50);

        // Assert
        Assert.HasCount(1, _mockChannel.SentMessages);

        var response = JObject.Parse(_mockChannel.SentMessages[0]);
        Assert.AreEqual("req-002", response["id"]?.ToString());
        Assert.AreEqual(-32601, response["error"]?["code"]?.Value<int>());
        Assert.AreEqual("Method not found", response["error"]?["message"]?.ToString());
    }

    /// <summary>
    /// OnRequest ハンドラが通常の例外をスローした場合、
    /// JSON-RPC Internal Error (-32603) として返送されることを検証する。
    /// </summary>
    [TestMethod]
    public async Task OnRequest_SendsInternalErrorOnGenericException()
    {
        // Arrange
        _peer.OnRequest = (method, @params) =>
        {
            throw new InvalidOperationException("Something went wrong");
        };

        // Act
        _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            id = "req-003",
            method = "Broken"
        }));

        await Task.Delay(50);

        // Assert
        Assert.HasCount(1, _mockChannel.SentMessages);

        var response = JObject.Parse(_mockChannel.SentMessages[0]);
        Assert.AreEqual("req-003", response["id"]?.ToString());
        Assert.AreEqual(-32603, response["error"]?["code"]?.Value<int>());
        Assert.AreEqual("Something went wrong", response["error"]?["message"]?.ToString());
    }

    // ────────────────────────────────────────────
    // OnNotify（一方向通知受信）
    // ────────────────────────────────────────────

    /// <summary>
    /// 相手からの JSON-RPC Notification（id なし、method あり）を受信した場合、
    /// OnNotify ハンドラが呼ばれ、method と params が正しく渡されることを検証する。
    /// </summary>
    [TestMethod]
    public void OnNotify_FiresForNotificationMessages()
    {
        // Arrange
        string receivedMethod = null;
        JToken receivedParams = null;
        _peer.OnNotify = (method, @params) =>
        {
            receivedMethod = method;
            receivedParams = @params;
        };

        // Act: id なし、method ありの通知を受信
        _mockChannel.SimulateReceive(JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            method = "statusUpdate",
            @params = new { status = "online" }
        }));

        // Assert
        Assert.AreEqual("statusUpdate", receivedMethod);
        Assert.AreEqual("online", receivedParams?["status"]?.ToString());
    }

    // ────────────────────────────────────────────
    // エッジケース
    // ────────────────────────────────────────────

    /// <summary>
    /// 不正な JSON 文字列を受信した場合、例外がスローされず、
    /// ハンドラも呼ばれないことを検証する（静かに無視される）。
    /// </summary>
    [TestMethod]
    public void OnReceived_IgnoresInvalidJson()
    {
        // Arrange
        var notifyCalled = false;
        _peer.OnNotify = (_, __) => notifyCalled = true;

        // Act: 不正な JSON を送信
        _mockChannel.SimulateReceive("this is not json");

        // Assert: 例外もハンドラ呼び出しも発生しない
        Assert.IsFalse(notifyCalled);
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
}
