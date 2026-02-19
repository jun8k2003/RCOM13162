using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RCOM.Channel;

namespace RCOM.SampleApp.Config
{
    /// <summary>
    /// rcom.json からクライアント設定を読み込む。
    /// </summary>
    internal class RcomConfig
    {
        public GrpcTlsOptions GrpcTls { get; private set; } = new GrpcTlsOptions();

        /// <summary>
        /// config に有効な TLS 設定が存在するかどうか。
        /// true の場合は config ドリブンで TLS を制御し、false の場合は UI の ChkTls を使用する。
        /// </summary>
        public bool HasTlsConfig =>
            !string.IsNullOrEmpty(GrpcTls.TrustedCertFile) || GrpcTls.AllowInvalidCertificate;

        /// <summary>
        /// AppDomain.CurrentDomain.BaseDirectory にある指定ファイルを読み込む。
        /// ファイルが存在しない場合はデフォルト値（TLS 設定なし）を返す。
        /// </summary>
        public static RcomConfig Load(string fileName = "rcom.json")
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (!File.Exists(path))
                return new RcomConfig();

            JObject obj;
            try
            {
                obj = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return new RcomConfig();
            }

            var grpc = obj["Grpc"];
            var cfg = new RcomConfig();
            if (grpc != null)
            {
                cfg.GrpcTls = new GrpcTlsOptions
                {
                    TrustedCertFile         = grpc.Value<string>("TrustedCertFile") ?? "",
                    AllowInvalidCertificate = grpc.Value<bool>("AllowInvalidCertificate")
                };
            }
            return cfg;
        }
    }
}
