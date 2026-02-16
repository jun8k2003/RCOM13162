# RCOM

Remote Communication Framework — gRPC ベースのリモート通信ライブラリ

## 概要

RCOM は、gRPC 双方向ストリーミングを利用したリモート通信フレームワークです。
2 つのレイヤーで構成されており、用途に応じて使い分けることができます。

| レイヤー | プロジェクト | 役割 |
|---|---|---|
| Layer 2 | `RCOM.Rpc` | JSON-RPC 2.0 による 1:1 リクエスト/レスポンス通信 |
| Layer 1 | `RCOM.Channel` | gRPC 双方向ストリーミングによる汎用メッセージ送受信 |

```
┌─────────────────────────────────┐
│  あなたのアプリケーション        │
├─────────────────────────────────┤
│  Layer 2: RCOM.Rpc (RemotePeer) │  ← JSON-RPC 2.0 で通信
├─────────────────────────────────┤
│  Layer 1: RCOM.Channel          │  ← gRPC ストリーミング
├─────────────────────────────────┤
│  gRPC / Broker サーバー          │
└─────────────────────────────────┘
```

通常は **Layer 2（`RemotePeer`）** のみを使用します。Layer 1 の存在を意識する必要はありません。

---

## Layer 2: RemotePeer の使い方

`RemotePeer` は、リモートの相手と 1:1 で JSON-RPC 2.0 通信を行うクラスです。

### 接続

```csharp
// マッチングキー（GUID）を共有する 2 者が同じルームに接続される
var peer = await RemotePeer.ConnectAsync(
    matchingKey: "550e8400-e29b-41d4-a716-446655440000",
    host: "broker.example.com",
    port: 443);
```

### リモートメソッド呼び出し

```csharp
// 相手側のメソッドを呼び出し、結果を受け取る
var response = await peer.CallAsync("Add", new { a = 1, b = 2 });
Console.WriteLine(response.Result); // => 3
```

### タイムアウトの指定

```csharp
// 5 秒以内に応答がなければ TimeoutException がスローされる
var response = await peer.CallAsync(
    "SlowMethod",
    timeout: TimeSpan.FromSeconds(5));
```

### 相手からのリクエストに応答する

相手が `CallAsync` で呼び出したリクエストを受け取り、結果を返します。

```csharp
peer.OnRequest = async (method, @params) =>
{
    if (method == "Add")
    {
        var a = @params["a"].Value<int>();
        var b = @params["b"].Value<int>();
        return new { result = a + b };
    }
    throw new RpcException(-32601, "Method not found");
};
```

### 一方向通知の送受信

応答を必要としない通知を送受信します。

```csharp
// 送信（応答なし）
await peer.NotifyAsync("Log", new { message = "hello" });

// 受信
peer.OnNotify = (method, @params) =>
{
    Console.WriteLine($"通知: {method}");
};
```

### エラーハンドリング

```csharp
try
{
    var response = await peer.CallAsync("FailingMethod");
}
catch (RpcException ex)
{
    Console.WriteLine(ex.RpcError.Code);    // エラーコード
    Console.WriteLine(ex.RpcError.Message); // エラーメッセージ
}
```

### 破棄

```csharp
peer.Dispose();
```

---

## Layer 1: RoomChannel の使い方

通常は Layer 2 経由で使用するため、直接操作する必要はありません。
ただし、独自のプロトコルを実装したい場合や、ブロードキャスト通信を行いたい場合は Layer 1 を直接使用できます。

### チャネルモード

Layer 1 には 2 つのモードがあります。

| モード | 最大人数 | 用途 |
|---|---|---|
| `Peer` | 2 名 | 1:1 通信（Layer 2 はこのモードを使用） |
| `Group` | 無制限 | ブロードキャスト通信 |

### 直接接続

```csharp
// Peer モード（1:1）
var channel = await RoomChannel.CreateAsync(
    matchingKey: "550e8400-e29b-41d4-a716-446655440000",
    host: "broker.example.com",
    port: 443,
    mode: ChannelMode.Peer);

// Group モード（ブロードキャスト）
var channel = await RoomChannel.CreateAsync(
    matchingKey: "group-room-key",
    host: "broker.example.com",
    port: 443,
    mode: ChannelMode.Group);
```

### メッセージの送受信

```csharp
// 受信
channel.OnReceived = (payload) =>
{
    Console.WriteLine(payload);
};

// 送信
await channel.SendAsync("{\"type\":\"chat\",\"text\":\"Hello!\"}");
```

### モードの制約

- 同じマッチングキーのルームには、最初に接続したクライアントのモードが適用されます
- 異なるモードで接続を試みるとサーバーからエラーが返されます
- `Peer` モードでは 3 人目以降の接続はサーバーから拒否されます

---

## ブロードキャスト通信の実現

Layer 2（`RemotePeer`）は 1:1 通信のみをサポートしています。
これは JSON-RPC 2.0 のリクエスト/レスポンス対応付けが、1:1 を前提としているためです。

複数人へのブロードキャスト通信が必要な場合は、Layer 1（`RoomChannel`）を `ChannelMode.Group` で使用し、独自のメッセージプロトコルを実装してください。

```csharp
// 例: Group モードで独自のブロードキャスト通信を実装
var channel = await RoomChannel.CreateAsync(
    matchingKey: "broadcast-room",
    host: "broker.example.com",
    mode: ChannelMode.Group);

channel.OnReceived = (payload) =>
{
    // 独自のメッセージ形式を解析して処理
    var msg = JsonConvert.DeserializeObject<MyMessage>(payload);
    HandleBroadcast(msg);
};

await channel.SendAsync(JsonConvert.SerializeObject(
    new MyMessage { Type = "announce", Data = "全員へのお知らせ" }));
```

---

## プロジェクト構成

```
CSharpClient/
├── RCOM.Channel/            # Layer 1: gRPC チャネル層
│   ├── Interfaces/
│   │   └── IRoomChannel.cs
│   ├── Enums/
│   │   └── ChannelMode.cs
│   └── RoomChannel.cs
├── RCOM.Rpc/                # Layer 2: JSON-RPC 2.0 通信層
│   ├── Interfaces/
│   │   └── IRemotePeer.cs
│   ├── Models/
│   │   ├── JsonRpcResponse.cs
│   │   └── JsonRpcError.cs
│   └── RemotePeer.cs
├── RCOM.Channel.Tests/      # Layer 1 テスト
├── RCOM.Rpc.Tests/          # Layer 2 テスト
└── RCOM13162.slnx
```

## 動作環境

- .NET Framework 4.8
- gRPC (Grpc.Core 2.46.6)
- Newtonsoft.Json 13.0.3
