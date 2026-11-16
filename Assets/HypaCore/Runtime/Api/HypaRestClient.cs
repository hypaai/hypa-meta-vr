using System.Collections.Generic;
using System.Text.RegularExpressions;
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
            if (!_cfg.CanCallPaidTools) throw new HypaException(HypaErrorKind.MissingKey, $"REST '{endpoint}' needs a Hypa API key (or the proxy).");
            var raw = await HypaHttp.PostAsync(_cfg, BasePath + endpoint, body.ToString(Formatting.None), ct).ConfigureAwait(false);
            var payloads = HypaHttp.JsonPayloads(raw);
            if (payloads.Count == 0) throw new HypaException(HypaErrorKind.InvalidResponse, $"{endpoint}: empty response.");
            var last = payloads[payloads.Count - 1];
            try { return JToken.Parse(last); }
            catch (JsonException e)
            {
                // an error event carries plain text ("Your request could not be processed... model was not found"): say that
                var text = last.Length > 300 ? last.Substring(0, 300) + "…" : last;
                throw new HypaException(HypaErrorKind.InvalidResponse, $"{endpoint}: {text}", 0, null, e);
            }
        }

        /// <summary>One chat message: role "system", "user" or "assistant".</summary>
        public struct ChatMessage
        {
            public string Role, Content;
            public ChatMessage(string role, string content) { Role = role; Content = content; }
        }

        /// <summary>
        /// Chat (verified with a keyed call, 2026-11-14; see docs/api-contract.md): messages + provider + model (both required,
        /// e.g. "meta" + "muse-spark-1.3") → the assistant's reply. The reply comes as server-sent events even with
        /// stream=false; an "event: error" carries a plain-text message (e.g. high traffic). top_p is always sent: some
        /// models (OpenAI's) reject the provider's default. A leading reasoning block (&lt;think&gt;…&lt;/think&gt;) is removed.
        /// </summary>
        public async Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, string provider, string model, CancellationToken ct)
        {
            if (!_cfg.CanCallPaidTools) throw new HypaException(HypaErrorKind.MissingKey, "chat needs a Hypa API key (or the proxy).");
            var raw = await HypaHttp.PostAsync(_cfg, BasePath + Endpoints.Chat, ChatBody(messages, provider, model).ToString(Formatting.None), ct)
                .ConfigureAwait(false);
            return ParseChat(raw);
        }

        /// <summary>The chat request body. Pure, unit tested.</summary>
        public static JObject ChatBody(IReadOnlyList<ChatMessage> messages, string provider, string model)
        {
            var list = new JArray();
            foreach (var m in messages) list.Add(new JObject { ["role"] = m.Role, ["content"] = m.Content });
            return new JObject { ["messages"] = list, ["stream"] = false, ["provider"] = provider, ["model"] = model, ["top_p"] = 1 };
        }

        /// <summary>The reply text from a chat response (JSON or SSE). Throws on an error event or no content. Pure, unit tested.</summary>
        public static string ParseChat(string raw)
        {
            var error = SseError(raw);
            if (error != null) throw new HypaException(HypaErrorKind.Unavailable, "chat: " + error);
            var payloads = HypaHttp.JsonPayloads(raw);
            for (var i = payloads.Count - 1; i >= 0; i--)
            {
                JToken j;
                try { j = JToken.Parse(payloads[i]); } catch (JsonException) { continue; }
                var content = (string)j.SelectToken("choices[0].message.content");
                if (string.IsNullOrWhiteSpace(content)) continue;
                content = StripReasoning(content);
                if (!string.IsNullOrWhiteSpace(content)) return content;
            }
            throw new HypaException(HypaErrorKind.InvalidResponse, "chat: no reply in the response.");
        }

        /// <summary>The message of an "event: error" in a server-sent event body, or null. Pure.</summary>
        public static string SseError(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var lines = raw.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "event: error") continue;
                for (var k = i + 1; k < lines.Length && lines[k].Length > 0; k++)
                    if (lines[k].StartsWith("data:")) return lines[k].Substring(5).Trim();
                return "error";
            }
            return null;
        }

        static readonly Regex Think = new Regex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase);

        /// <summary>Removes reasoning blocks some models put before the answer (an unclosed one: everything after it goes). Pure.</summary>
        public static string StripReasoning(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = Think.Replace(text, "");
            var open = text.IndexOf("<think>", System.StringComparison.OrdinalIgnoreCase);
            if (open >= 0) text = text.Substring(0, open);
            return text.Trim();
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
