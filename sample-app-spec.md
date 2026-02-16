# RCOM サンプルアプリケーション 仕様書

## 1. 概要

### 1.1 目的

RCOM.Rpc（`RemotePeer`）の双方向通信を画面操作で確認できるサンプルアプリケーション。

### 1.2 動作方式

- 同一アプリを **2 インスタンス起動**し、同じマッチングキーで接続して通信する
- デバッグ時はプロジェクトをコピーし、2 つの Visual Studio インスタンスからそれぞれ起動する

### 1.3 技術スタック

| 項目 | 内容 |
|---|---|
| フレームワーク | .NET Framework 4.8 |
| UI | WPF |
| 通信ライブラリ | RCOM.Rpc（`RemotePeer`） |
| プロジェクト名 | `RCOM.SampleApp` |
| ソリューション | `CSharpClient/RCOM13162.slnx` に追加 |

---

## 2. 画面仕様

### 2.1 画面レイアウト

```
┌──────────────────────────────────────────────────────────────┐
│ RCOM Sample App                                              │
├──────────────────────────────────────────────────────────────┤
│ ■ 接続設定                                                    │
│  URL: [localhost:50051      ]  Key: [xxxxxxxx-xxxx-...  ]    │
│  [接続]  [切断]                          状態: 未接続         │
├──────────────────────────────────────────────────────────────┤
│ ■ CallAsync 送信                 │ ■ OnRequest 待機設定       │
│  メソッド: [          ]          │  待機メソッド: [          ] │
│  パラメータ(JSON):               │  レスポンス種別:            │
│  [                          ]    │   ◉ Success  ○ Error      │
│  [送信]                          │  メッセージ:               │
│                                  │  [                        ]│
│  応答: (結果が表示される)         │                            │
│  所要時間: --- ms                │                            │
├──────────────────────────────────────────────────────────────┤
│ ■ NotifyAsync 送信               │ ■ OnNotify 受信            │
│  メソッド: [          ]          │  (受信ログが自動表示)       │
│  パラメータ(JSON):               │  ─────────────────────     │
│  [                          ]    │  [受信ログ一覧]            │
│  [送信]                          │                            │
├──────────────────────────────────────────────────────────────┤
│ ■ ログ                                                       │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ 2026-02-16 14:30:00.123 [CONN] 接続しました               │ │
│ │ 2026-02-16 14:30:05.456 [SEND] CallAsync "Add" 送信       │ │
│ │ 2026-02-16 14:30:05.512 [RECV] CallAsync "Add" 応答 (56ms)│ │
│ │ ...                                                        │ │
│ └──────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### 2.2 接続設定エリア

| 項目 | コントロール | 説明 |
|---|---|---|
| URL | TextBox | 接続先サーバーの `host:port`（例: `localhost:50051`） |
| マッチングキー | TextBox | GUID 文字列。2 インスタンスで同じ値を入力する |
| 接続ボタン | Button | `RemotePeer.ConnectAsync` を呼び出す |
| 切断ボタン | Button | `RemotePeer.Dispose` を呼び出す |
| 接続状態 | TextBlock | 「未接続」「接続中」「接続済」「エラー」を表示 |

#### 接続処理

```csharp
// URL を host と port に分割
var parts = url.Split(':');
var host = parts[0];
var port = int.Parse(parts[1]);

_peer = await RemotePeer.ConnectAsync(
    matchingKey: matchingKey,
    host: host,
    port: port,
    useTls: false);  // 開発環境では TLS なし

// ハンドラを設定
_peer.OnRequest = HandleRequest;
_peer.OnNotify = HandleNotify;
```

### 2.3 CallAsync 送信エリア

| 項目 | コントロール | 説明 |
|---|---|---|
| メソッド | TextBox | 送信する JSON-RPC メソッド名（例: `"Add"`） |
| パラメータ | TextBox | JSON 文字列を直接入力（例: `{"a":1,"b":2}`） |
| 送信ボタン | Button | `RemotePeer.CallAsync` を実行する |
| 応答結果 | TextBlock | 受信した `JsonRpcResponse.Result` を JSON 文字列で表示 |
| 所要時間 | TextBlock | 送信〜応答受信の経過時間をミリ秒で表示 |

#### 送信処理

```csharp
var sw = Stopwatch.StartNew();
try
{
    // パラメータは JSON 文字列をそのまま JToken にパース
    var @params = string.IsNullOrWhiteSpace(paramsJson)
        ? null
        : JToken.Parse(paramsJson);

    var response = await _peer.CallAsync(method, @params);
    sw.Stop();

    var resultJson = response.Result?.ToString(Formatting.None) ?? "(null)";
    Log("CALL", $"CallAsync \"{method}\" 応答: {resultJson} ({sw.ElapsedMilliseconds}ms)");
    // UI に resultJson と sw.ElapsedMilliseconds を表示
}
catch (RpcException ex)
{
    sw.Stop();
    Log("ERR", $"CallAsync \"{method}\" エラー: [{ex.RpcError.Code}] {ex.RpcError.Message} ({sw.ElapsedMilliseconds}ms)");
}
catch (TimeoutException ex)
{
    sw.Stop();
    Log("ERR", $"CallAsync \"{method}\" タイムアウト ({sw.ElapsedMilliseconds}ms)");
}
```

### 2.4 OnRequest 待機設定エリア

| 項目 | コントロール | 説明 |
|---|---|---|
| 待機メソッド | TextBox | このメソッド名で呼ばれたら応答する |
| レスポンス種別 | RadioButton | `Success` または `Error` |
| メッセージ | TextBox | レスポンスの内容（正常・エラー兼用、文字列） |

#### 受信処理（OnRequest ハンドラ）

```csharp
_peer.OnRequest = async (method, @params) =>
{
    Log("RECV", $"OnRequest \"{method}\" params={@params?.ToString(Formatting.None)}");

    if (method == waitingMethod)
    {
        if (responseType == ResponseType.Success)
        {
            Log("RESP", $"OnRequest \"{method}\" → Success: {responseMessage}");
            return new { message = responseMessage };
        }
        else
        {
            Log("RESP", $"OnRequest \"{method}\" → Error: {responseMessage}");
            throw new RpcException(-32000, responseMessage);
        }
    }

    // 待機メソッド名と一致しない場合
    Log("RESP", $"OnRequest \"{method}\" → Method not found");
    throw new RpcException(-32601, "Method not found");
};
```

### 2.5 NotifyAsync 送信エリア

| 項目 | コントロール | 説明 |
|---|---|---|
| メソッド | TextBox | 送信する通知のメソッド名 |
| パラメータ | TextBox | JSON 文字列を直接入力 |
| 送信ボタン | Button | `RemotePeer.NotifyAsync` を実行する |

#### 送信処理

```csharp
var @params = string.IsNullOrWhiteSpace(paramsJson)
    ? null
    : JToken.Parse(paramsJson);

await _peer.NotifyAsync(method, @params);
Log("NOTIFY", $"NotifyAsync \"{method}\" 送信: {@params?.ToString(Formatting.None)}");
```

### 2.6 OnNotify 受信エリア

| 項目 | コントロール | 説明 |
|---|---|---|
| 受信ログ | ListBox | 受信した通知を時系列で表示する |

#### 受信処理（OnNotify ハンドラ）

```csharp
_peer.OnNotify = (method, @params) =>
{
    var paramsStr = @params?.ToString(Formatting.None) ?? "(null)";
    Log("NOTIFY", $"OnNotify \"{method}\" 受信: {paramsStr}");
    // UI の受信ログリストに追加（Dispatcher.Invoke で UI スレッドに戻す）
};
```

### 2.7 ログエリア

| 項目 | コントロール | 説明 |
|---|---|---|
| ログ一覧 | ListBox | 全通信イベントをタイムスタンプ付きで表示する |

---

## 3. ログ仕様

### 3.1 出力先

| 出力先 | 説明 |
|---|---|
| 画面 | ログエリア（ListBox）にリアルタイム表示 |
| ファイル | `%TEMP%` フォルダに出力 |

### 3.2 ログファイル名

```
{yyyyMMdd}_{GUID先頭7文字}.log
```

- `yyyyMMdd` — アプリ起動日
- `GUID先頭7文字` — アプリ起動時に生成する GUID の先頭 7 文字（インスタンス識別用）

例: `20260216_a3b4c5d.log`

### 3.3 ログ行フォーマット

```
yyyy-MM-dd HH:mm:ss.fff [種別] メッセージ
```

### 3.4 ログ種別

| 種別 | 用途 |
|---|---|
| `CONN` | 接続・切断イベント |
| `CALL` | CallAsync の送信と応答（所要時間含む） |
| `RECV` | OnRequest でリクエストを受信した時点 |
| `RESP` | OnRequest でレスポンスを返送した時点 |
| `NOTIFY` | NotifyAsync 送信 / OnNotify 受信 |
| `ERR` | エラー発生（RpcException, TimeoutException 等） |

### 3.5 ログ出力例

```
2026-02-16 14:30:00.123 [CONN] 接続開始 host=localhost port=50051 key=550e8400-...
2026-02-16 14:30:00.456 [CONN] 接続完了
2026-02-16 14:30:05.100 [CALL] CallAsync "Add" 送信 params={"a":1,"b":2}
2026-02-16 14:30:05.156 [CALL] CallAsync "Add" 応答: {"result":3} (56ms)
2026-02-16 14:30:10.200 [RECV] OnRequest "Multiply" params={"x":3,"y":4}
2026-02-16 14:30:10.201 [RESP] OnRequest "Multiply" → Success: {"product":12}
2026-02-16 14:30:15.300 [NOTIFY] NotifyAsync "Log" 送信: {"message":"hello"}
2026-02-16 14:30:15.350 [NOTIFY] OnNotify "StatusUpdate" 受信: {"status":"online"}
2026-02-16 14:30:20.400 [ERR] CallAsync "Fail" エラー: [-32603] Internal error (45ms)
2026-02-16 14:35:00.000 [CONN] 切断
```

---

## 4. 動作シナリオ

### 4.1 基本シナリオ（CallAsync 双方向）

```
インスタンス A                              インスタンス B
─────────────                              ─────────────
1. URL: localhost:50051                    1. URL: localhost:50051
   Key: 550e8400-...（同じ値）                Key: 550e8400-...（同じ値）
   [接続] クリック                            [接続] クリック

2. OnRequest 待機設定:                      3. CallAsync 送信:
   待機メソッド: "Add"                         メソッド: "Add"
   種別: Success                              パラメータ: {"a":1,"b":2}
   メッセージ: {"result":3}                    [送信] クリック

   ← リクエスト受信、自動応答 ──────           → 応答受信: {"result":3} (56ms)

4. CallAsync 送信:                          5. OnRequest 待機設定:
   メソッド: "Greet"                           待機メソッド: "Greet"
   パラメータ: {"name":"Alice"}                種別: Success
   [送信] クリック                              メッセージ: Hello, Alice!

   → 応答受信: Hello, Alice! (32ms)          ← リクエスト受信、自動応答
```

### 4.2 一方向通知シナリオ（NotifyAsync）

```
インスタンス A                              インスタンス B
─────────────                              ─────────────
（接続済み）                                  （接続済み）

1. NotifyAsync 送信:
   メソッド: "StatusUpdate"
   パラメータ: {"status":"busy"}
   [送信] クリック

   → 送信完了（応答なし）                      ← OnNotify 受信ログに表示:
                                                "StatusUpdate" {"status":"busy"}
```

### 4.3 エラーシナリオ

```
インスタンス A                              インスタンス B
─────────────                              ─────────────
1. OnRequest 待機設定:                      2. CallAsync 送信:
   待機メソッド: "Fail"                        メソッド: "Fail"
   種別: Error                                パラメータ: (空)
   メッセージ: Something went wrong            [送信] クリック

   ← リクエスト受信、エラー応答                 → RpcException 表示:
                                                [-32000] Something went wrong (28ms)
```

---

## 5. プロジェクト構成

```
CSharpClient/
├── RCOM.Channel/            # Layer 1（既存）
├── RCOM.Rpc/                # Layer 2（既存）
├── RCOM.Channel.Tests/      # テスト（既存）
├── RCOM.Rpc.Tests/          # テスト（既存）
├── RCOM.SampleApp/          # ★ 新規作成
│   ├── RCOM.SampleApp.csproj
│   ├── App.xaml
│   ├── App.xaml.cs
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   └── AppLogger.cs         # ログ出力クラス
└── RCOM13162.slnx
```

### 5.1 依存関係

```
RCOM.SampleApp
  └── RCOM.Rpc（プロジェクト参照）
        └── RCOM.Channel（プロジェクト参照）
```

### 5.2 NuGet パッケージ（RCOM.SampleApp 固有）

| パッケージ | 用途 |
|---|---|
| Newtonsoft.Json | JSON パラメータのパース（RCOM.Rpc 経由で間接参照） |

---

## 6. 注意事項

- UI スレッドへの戻し: `OnRequest` / `OnNotify` はバックグラウンドスレッドから呼ばれるため、UI 更新は `Dispatcher.Invoke` で行う
- アプリ終了時: `RemotePeer.Dispose()` を確実に呼ぶ（`Window.Closing` イベントで処理）
- ログファイルは追記モードで開き、各行を即時フラッシュする
- 2 インスタンスが同じマッチングキーで接続するまで、CallAsync はタイムアウトする可能性がある
