using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RCOM.Rpc
{
    /// <summary>
    /// JSON-RPC 2.0 メッセージの汎用デシリアライズ用モデル。
    /// Request / Response / Notification のいずれかを判別するために使用する。
    /// </summary>
    internal class JsonRpcMessage
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public JToken Params { get; set; }

        [JsonProperty("result")]
        public JToken Result { get; set; }

        [JsonProperty("error")]
        public JsonRpcError Error { get; set; }

        /// <summary>method フィールドがあればリクエストまたは通知。</summary>
        public bool IsRequest => Method != null;

        /// <summary>method があり id がなければ通知。</summary>
        public bool IsNotification => Method != null && Id == null;
    }
}
