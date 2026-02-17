# gRpcBroker Server

gRPC ベースの双方向ストリーミング Broker サーバーです。

## ビルドと発行

```bash
cd Server/gRpcBroker
dotnet publish -c Release -o ./publish
```

## 起動方法

### コンソールアプリケーションとして起動（Windows）

publish フォルダ内の exe を直接実行します。

```cmd
.\publish\gRpcBroker.exe
```

Ctrl+C で停止します。

### Windows サービスとして起動

管理者権限のコマンドプロンプトでサービスを登録・起動します。

```cmd
sc create gRpcBroker binPath= "C:\path\to\publish\gRpcBroker.exe"
sc start gRpcBroker
```

`binPath=` の後のスペースは必須です。パスは実際の配置先に置き換えてください。

停止と削除:

```cmd
sc stop gRpcBroker
sc delete gRpcBroker
```

### Linux で起動

Linux 上では .NET ランタイムを使用して DLL を実行します。

```bash
dotnet ./publish/gRpcBroker.dll
```

GCE へのデプロイなど詳細な手順については [spec.md](../../spec.md) を参照してください。

## リッスンポートの変更

デフォルトではポート 5000 (`http://0.0.0.0:5000`) でリッスンします。

### appsettings.json で変更する

publish フォルダ内の `appsettings.json` を編集します。

```json
{
  "Kestrel": {
    "Endpoints": {
      "Grpc": {
        "Url": "http://0.0.0.0:8080"
      }
    }
  }
}
```

### 環境変数で変更する

```bash
ASPNETCORE_URLS=http://0.0.0.0:8080 dotnet ./publish/gRpcBroker.dll
```

### コマンドライン引数で変更する

```bash
dotnet ./publish/gRpcBroker.dll --urls "http://0.0.0.0:8080"
```

環境変数・コマンドライン引数は appsettings.json の設定より優先されます。
