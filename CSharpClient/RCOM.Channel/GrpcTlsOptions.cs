namespace RCOM.Channel
{
    /// <summary>
    /// gRPC チャネルの TLS オプション。
    /// GrpcRoomChannel.CreateAsync() の tlsOptions パラメータに渡す。
    /// </summary>
    public class GrpcTlsOptions
    {
        /// <summary>
        /// サーバー証明書 PEM ファイルのパス。
        /// 相対パスの場合は AppDomain.CurrentDomain.BaseDirectory を基準に解決する。
        /// null または空の場合はシステム CA ストアを使用する。
        /// </summary>
        public string TrustedCertFile { get; set; }

        /// <summary>
        /// true にすると証明書検証エラーを無視して接続する（開発・テスト専用）。
        /// TLS 暗号化は維持されるが、証明書チェーンとホスト名の検証をスキップする。
        /// 本番環境では必ず false にすること。
        /// </summary>
        public bool AllowInvalidCertificate { get; set; }
    }
}
