using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RCOM.Rpc
{
    public class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("result")]
        public JToken Result { get; set; }

        [JsonProperty("error")]
        public JsonRpcError Error { get; set; }
    }
}
