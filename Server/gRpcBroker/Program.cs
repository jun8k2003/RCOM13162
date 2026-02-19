using gRpcBroker.Interfaces;
using gRpcBroker.Services;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

// ── TLS 設定 ──────────────────────────────────────────────────────────────────
// appsettings.json の Tls セクションで Enabled:true にすると HTTPS で起動する。
// 証明書ファイルは AppContext.BaseDirectory からの相対パスまたは絶対パスで指定する。
// 設定例: appsettings.tls-sample.json を参照。
var tlsSection = builder.Configuration.GetSection("Tls");
if (tlsSection.GetValue<bool>("Enabled"))
{
    var baseDir  = AppContext.BaseDirectory;
    var certFile = tlsSection.GetValue<string>("CertPemFile")
                   ?? throw new InvalidOperationException("Tls:CertPemFile が未設定です。");
    var keyFile  = tlsSection.GetValue<string>("KeyPemFile")
                   ?? throw new InvalidOperationException("Tls:KeyPemFile が未設定です。");

    var certPath = Path.IsPathRooted(certFile) ? certFile : Path.Combine(baseDir, certFile);
    var keyPath  = Path.IsPathRooted(keyFile)  ? keyFile  : Path.Combine(baseDir, keyFile);

    if (!File.Exists(certPath))
        throw new FileNotFoundException($"TLS 証明書が見つかりません: {certPath}");
    if (!File.Exists(keyPath))
        throw new FileNotFoundException($"TLS 秘密鍵が見つかりません: {keyPath}");

    builder.WebHost.ConfigureKestrel(k =>
        k.ConfigureHttpsDefaults(h =>
            h.ServerCertificate = X509Certificate2.CreateFromPemFile(certPath, keyPath)));

    // Kestrel:Endpoints:Grpc:Url が http:// のままでも自動的に https:// に補正する
    var endpointUrl = builder.Configuration["Kestrel:Endpoints:Grpc:Url"] ?? "";
    if (endpointUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        builder.Configuration["Kestrel:Endpoints:Grpc:Url"] = "https://" + endpointUrl[7..];
}
// ────────────────────────────────────────────────────────────────────────────

builder.Services.AddGrpc();
builder.Services.AddSingleton<IRoomRegistry, InMemoryRoomRegistry>();

var app = builder.Build();

app.MapGrpcService<BrokerService>();

app.Run();
