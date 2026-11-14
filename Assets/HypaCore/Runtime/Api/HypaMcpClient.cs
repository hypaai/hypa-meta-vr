using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HypaCore
{
    /// <summary>
    /// Client for Hypa's MCP endpoint (POST {base}/v2/mcp, stateless Streamable HTTP, JSON-RPC tools/call).
    /// Contract checked with live calls on 2026-11-10 (tools/list schemas, list_languages, check_language_pair):
    /// responses arrive as SSE with result.structuredContent; tools take language labels such as "Yoruba".
    /// Free tools work without a key; paid tools require one (keyless paid calls share a capped anonymous budget
    /// and must not be relied on).
    /// </summary>
    public sealed class HypaMcpClient
    {
        public const string Path = "/v2/mcp";
        static readonly HashSet<string> FreeTools = new HashSet<string>
            { "list_languages", "list_voices", "check_language_pair", "list_models", "word_of_the_day" };

        readonly HypaConfig _cfg;
        int _id;

        public HypaMcpClient(HypaConfig cfg = null) { _cfg = cfg ?? HypaConfig.Current; }

        public async Task<JObject> CallToolAsync(string tool, JObject args, CancellationToken ct)
        {
            if (!_cfg.HasKey && !FreeTools.Contains(tool))
                throw new HypaException(HypaErrorKind.MissingKey, $"'{tool}' needs a Hypa API key.");
            var req = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Interlocked.Increment(ref _id),
                ["method"] = "tools/call",
                ["params"] = new JObject { ["name"] = tool, ["arguments"] = args ?? new JObject() },
            };
            var body = await HypaHttp.PostAsync(_cfg, Path, req.ToString(Formatting.None), ct).ConfigureAwait(false);
            return ParseToolResult(body, tool);
        }

        /// <summary>Turns a tools/call response body into the tool's structured output, or throws HypaException. Pure.</summary>
        public static JObject ParseToolResult(string body, string tool)
        {
            JObject env = null;
            foreach (var p in HypaHttp.JsonPayloads(body))
            {
                try
                {
                    var o = JObject.Parse(p);
                    if (o["result"] != null || o["error"] != null) env = o;
                }
                catch (JsonException) { }
            }
            if (env == null) throw new HypaException(HypaErrorKind.InvalidResponse, $"{tool}: no JSON-RPC result in response.");

            if (env["error"] is JObject rpcErr)
                throw new HypaException(HypaErrorKind.BadRequest, $"{tool}: {(string)rpcErr["message"] ?? rpcErr.ToString()}", 0, (string)rpcErr["code"]);

            var result = env["result"] as JObject;
            if (result == null) throw new HypaException(HypaErrorKind.InvalidResponse, $"{tool}: empty result.");

            var structured = result["structuredContent"] as JObject;
            if (structured == null)
            {
                var text = (string)result["content"]?.FirstOrDefault(c => (string)c["type"] == "text")?["text"];
                if (text != null) { try { structured = JObject.Parse(text); } catch (JsonException) { structured = new JObject { ["error"] = text }; } }
            }

            if ((bool?)result["isError"] == true)
            {
                var code = (string)structured?["code"];
                var msg = (string)structured?["error"] ?? "tool failed";
                var kind = code == "INSUFFICIENT_BALANCE" ? HypaErrorKind.InsufficientBalance : HypaErrorKind.ToolError;
                throw new HypaException(kind, $"{tool}: {msg}", 0, code);
            }
            if (structured == null) throw new HypaException(HypaErrorKind.InvalidResponse, $"{tool}: result had no content.");
            return structured;
        }

        // ---------- Typed tools ----------

        public async Task<IReadOnlyList<HypaLanguage>> ListLanguagesAsync(CancellationToken ct)
        {
            var o = await CallToolAsync("list_languages", null, ct).ConfigureAwait(false);
            return ParseLanguages(o);
        }

        public static IReadOnlyList<HypaLanguage> ParseLanguages(JObject o)
        {
            var list = new List<HypaLanguage>();
            foreach (var l in (o["languages"] as JArray) ?? new JArray())
                list.Add(new HypaLanguage
                {
                    Name = (string)l["name"], Code = (string)l["code"],
                    Voices = ((l["voices"] as JArray) ?? new JArray()).Select(v => (string)v).ToArray(),
                });
            return list;
        }

        /// <summary>Free. sourceLang null = auto-detect.</summary>
        public async Task<HypaPairCheck> CheckLanguagePairAsync(string targetLang, string sourceLang, CancellationToken ct)
        {
            var args = new JObject { ["targetLang"] = targetLang };
            if (!string.IsNullOrEmpty(sourceLang)) args["sourceLang"] = sourceLang;
            var o = await CallToolAsync("check_language_pair", args, ct).ConfigureAwait(false);
            return new HypaPairCheck
            {
                Supported = (bool?)o["supported"] ?? false,
                Models = ((o["models"] as JArray) ?? new JArray()).Select(v => (string)v).ToArray(),
                Unknown = ((o["unknown"] as JArray) ?? new JArray()).Select(v => (string)v).ToArray(),
            };
        }

        /// <summary>sourceLang null = auto-detect; the detected label comes back in DetectedLanguage (may be null).</summary>
        public async Task<HypaTranslation> TranslateAsync(string text, string targetLang, string sourceLang, CancellationToken ct)
        {
            var args = new JObject { ["text"] = text, ["targetLang"] = targetLang };
            if (!string.IsNullOrEmpty(sourceLang)) args["sourceLang"] = sourceLang;
            var o = await CallToolAsync("translate_message", args, ct).ConfigureAwait(false);
            return new HypaTranslation { Text = (string)o["translation"], DetectedLanguage = (string)o["language"], Model = (string)o["model"] };
        }

        /// <summary>Audio comes back as a signed URL (default) valid about an hour.</summary>
        /// <summary>voice only works with the model that lists it (list_voices), so pass both, e.g. "openai/gpt-4o-mini-tts" + "fable".
        /// Neither = Auto picks for the language.</summary>
        public async Task<HypaSpeech> TextToSpeechAsync(string text, string language, CancellationToken ct, string voice = null, string model = null)
        {
            var args = new JObject { ["text"] = text, ["language"] = language };
            if (!string.IsNullOrEmpty(voice)) args["voice"] = voice;
            if (!string.IsNullOrEmpty(model)) args["model"] = model;
            var o = await CallToolAsync("text_to_speech", args, ct).ConfigureAwait(false);
            return ParseSpeech(o);
        }

        static HypaSpeech ParseSpeech(JObject o) => new HypaSpeech
        {
            AudioUrl = (string)o["audioUrl"], AudioBase64 = (string)o["audioBase64"], Mime = (string)o["mime"],
            SampleRate = (int?)o["sampleRate"] ?? 0, Voice = (string)o["voice"],
        };

        /// <summary>
        /// Speech to text. <paramref name="audio"/> is a whole file (e.g. WAV from <see cref="WavEncoder"/>), sent as base64.
        /// sourceLang null = auto-detect. Schema from tools/list (2026-11-13); not yet called with a key.
        /// </summary>
        public async Task<string> SpeechToTextAsync(byte[] audio, string sourceLang, CancellationToken ct)
        {
            var args = new JObject { ["audio"] = System.Convert.ToBase64String(audio), ["audioType"] = "base64" };
            if (!string.IsNullOrEmpty(sourceLang)) args["sourceLang"] = sourceLang;
            var o = await CallToolAsync("speech_to_text", args, ct).ConfigureAwait(false);
            return (string)o["transcript"] ?? "";
        }

        /// <summary>word: 1..120 chars. language null = English.</summary>
        public async Task<HypaDictionaryEntry> LookUpTermAsync(string word, string language, CancellationToken ct)
        {
            var args = new JObject { ["word"] = word };
            if (!string.IsNullOrEmpty(language)) args["language"] = language;
            var o = await CallToolAsync("look_up_term", args, ct).ConfigureAwait(false);
            return new HypaDictionaryEntry { Raw = o["result"] as JObject };
        }
    }
}
