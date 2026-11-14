using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HypaCore
{
    /// <summary>
    /// Client for Hypa's REST developer API (POST {base}/v2/developer/{endpoint}, header x-api-key).
    /// All endpoints need a key. Responses may be JSON or server-sent events; <see cref="PostAsync"/> returns the
    /// final JSON payload either way.
    ///
    /// STATUS: transport is shared with the live-verified MCP client. Request/response field names for the REST
    /// endpoints have NOT been confirmed with a keyed live call yet, except <see cref="ChatAsync"/>; <see cref="MtAsync"/> and
    /// <see cref="DictionaryAsync"/> are typed from the API notes. Confirm each with one live call (README > API)
    /// before building features on them. Prefer <see cref="HypaMcpClient"/> where a tool exists: typed schemas,
    /// verified, and audio as a link.
    /// </summary>
    public sealed class HypaRestClient
    {
        public const string BasePath = "/v2/developer/";

        public static class Endpoints
        {
            public const string Mt = "mt", Tts = "tts", Asr = "asr", Chat = "chat", Dictionary = "dictionary", Ocr = "ocr";
        }

        readonly HypaConfig _cfg;
        public HypaRestClient(HypaConfig cfg = null) { _cfg = cfg ?? HypaConfig.Current; }

        /// <summary>Generic call for any endpoint. Returns the last JSON payload of the response.</summary>
        public async Task<JToken> PostAsync(string endpoint, JObject body, CancellationToken ct)
        {
            if (!_cfg.HasKey) throw new HypaException(HypaErrorKind.MissingKey, $"REST '{endpoint}' needs a Hypa API key.");
            var raw = await HypaHttp.PostAsync(_cfg, BasePath + endpoint, body.ToString(Formatting.None), ct).ConfigureAwait(false);
            var payloads = HypaHttp.JsonPayloads(raw);
            if (payloads.Count == 0) throw new HypaException(HypaErrorKind.InvalidResponse, $"{endpoint}: empty response.");
            try { return JToken.Parse(payloads[payloads.Count - 1]); }
            catch (JsonException e) { throw new HypaException(HypaErrorKind.InvalidResponse, $"{endpoint}: {e.Message}", 0, null, e); }
        }

        /// <summary>
        /// Machine translation. Empty source = auto-detect. Non-streaming; the final event carries
        /// choices[0].message.content. UNVERIFIED field names (source_lang/target_lang) - confirm live.
        /// </summary>
        public async Task<string> MtAsync(string text, string targetLang, string sourceLang, CancellationToken ct)
        {
            var body = new JObject
            {
                ["text"] = text, ["target_lang"] = targetLang, ["source_lang"] = sourceLang ?? "",
                ["stream"] = false, ["provider"] = "auto",
            };
            var res = await PostAsync(Endpoints.Mt, body, ct).ConfigureAwait(false);
            var content = (string)res.SelectToken("choices[0].message.content");
            if (string.IsNullOrEmpty(content)) throw new HypaException(HypaErrorKind.InvalidResponse, "mt: no choices[0].message.content.");
            return content;
        }

        /// <summary>
        /// Chat, not streamed. model = "provider/model" (e.g. "meta/muse-wonder-1.0"); messages = [{ role, content }] with roles
        /// system / user / assistant. maxTokens covers the model's reasoning too, so leave room. Verified live (Meta): the final
        /// event carries choices[0].message.content.
        /// </summary>
        public async Task<string> ChatAsync(string model, JArray messages, int maxTokens, CancellationToken ct)
        {
            var slash = model?.IndexOf('/') ?? -1;
            if (slash <= 0) throw new HypaException(HypaErrorKind.InvalidResponse, $"chat: model must be \"provider/model\", got \"{model}\".");
            var body = new JObject
            {
                ["provider"] = model.Substring(0, slash), ["model"] = model.Substring(slash + 1), ["messages"] = messages,
                ["stream"] = false, ["thinking"] = false, ["max_tokens"] = maxTokens,
            };
            var res = await PostAsync(Endpoints.Chat, body, ct).ConfigureAwait(false);
            var content = (string)res.SelectToken("choices[0].message.content");
            if (string.IsNullOrWhiteSpace(content)) throw new HypaException(HypaErrorKind.InvalidResponse, "chat: no choices[0].message.content.");
            return content;
        }

        /// <summary>Dictionary lookup. UNVERIFIED request shape - confirm live.</summary>
        public async Task<HypaDictionaryEntry> DictionaryAsync(string word, string language, CancellationToken ct)
        {
            var body = new JObject { ["word"] = word, ["language"] = language ?? "English", ["fun_facts"] = false, ["cultural_context"] = false };
            var res = await PostAsync(Endpoints.Dictionary, body, ct).ConfigureAwait(false);
            return new HypaDictionaryEntry { Raw = (res["result"] as JObject) ?? (res as JObject) };
        }
    }
}
