using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HypaCore.Tests
{
    public class HypaCoreTests
    {
        // Shapes captured from live calls to POST https://api.hypaintelligence.com/v2/mcp on 2026-11-10 (trimmed).
        const string LiveListLanguages =
            "event: message\n" +
            "data: {\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{}\"}],\"structuredContent\":{\"languages\":[" +
            "{\"name\":\"Albanian\",\"code\":\"sq\",\"voices\":[]}," +
            "{\"name\":\"Yoruba\",\"code\":\"yo\",\"voices\":[\"Eniola\",\"male\",\"female\"]}]}},\"jsonrpc\":\"2.0\",\"id\":1}\n\n";

        const string LivePairCheck =
            "event: message\n" +
            "data: {\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"supported\\\":true,\\\"models\\\":[\\\"mansa/mansa-translate\\\"]}\"}]," +
            "\"structuredContent\":{\"supported\":true,\"models\":[\"mansa/mansa-translate\"]}},\"jsonrpc\":\"2.0\",\"id\":2}\n\n";

        // ---------- Transport / MCP parsing ----------

        [Test] public void Env_ParsesKeysQuotesCommentsAndExport()
        {
            var d = HypaConfig.ParseEnv("# comment\nHYPA_API_KEY=\"abc 123\"\r\nexport HYPA_BASE_URL=https://x.test # note\nEMPTY=\nbad line\n");
            Assert.AreEqual("abc 123", d["HYPA_API_KEY"]);
            Assert.AreEqual("https://x.test", d["HYPA_BASE_URL"]);
            Assert.AreEqual("", d["EMPTY"]);
            Assert.AreEqual(3, d.Count);
        }

        [Test] public void Sse_ExtractsDataPayloads_AndSkipsDone()
        {
            var p = HypaHttp.JsonPayloads("event: message\ndata: {\"a\":1}\n\ndata: [DONE]\n\n");
            Assert.AreEqual(1, p.Count);
            Assert.AreEqual("{\"a\":1}", p[0]);
        }

        [Test] public void Sse_PlainJsonBody_PassesThrough()
            => Assert.AreEqual("{\"x\":true}", HypaHttp.JsonPayloads("  {\"x\":true}").Single());

        [Test] public void Sse_MultiLineData_IsJoined()
            => Assert.AreEqual("{\n\"a\":1}", HypaHttp.JsonPayloads("data: {\ndata: \"a\":1}\n\n").Single());

        [Test] public void Mcp_ParsesLiveListLanguages()
        {
            var langs = HypaMcpClient.ParseLanguages(HypaMcpClient.ParseToolResult(LiveListLanguages, "list_languages"));
            Assert.AreEqual(2, langs.Count);
            Assert.AreEqual("Yoruba", langs[1].Name);
            Assert.AreEqual("yo", langs[1].Code);
            Assert.IsTrue(langs[1].HasTts);
            Assert.IsFalse(langs[0].HasTts);
        }

        [Test] public void Mcp_PrefersStructuredContent()
        {
            var o = HypaMcpClient.ParseToolResult(LivePairCheck, "check_language_pair");
            Assert.IsTrue((bool)o["supported"]);
        }

        [Test] public void Mcp_FallsBackToTextContent()
        {
            const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"translation\\\":\\\"E kaaro\\\",\\\"language\\\":\\\"English\\\"}\"}]}}";
            var o = HypaMcpClient.ParseToolResult(body, "translate_message");
            Assert.AreEqual("E kaaro", (string)o["translation"]);
        }

        [Test] public void Mcp_IsError_ThrowsToolError_WithCode()
        {
            const string body = "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":\"x\"}],\"structuredContent\":{\"error\":\"Out of credit\",\"code\":\"INSUFFICIENT_BALANCE\"}}}\n\n";
            var e = Assert.Throws<HypaException>(() => HypaMcpClient.ParseToolResult(body, "translate_message"));
            Assert.AreEqual(HypaErrorKind.InsufficientBalance, e.Kind);
            Assert.AreEqual("INSUFFICIENT_BALANCE", e.Code);
        }

        [Test] public void Mcp_JsonRpcError_Throws()
        {
            var e = Assert.Throws<HypaException>(() => HypaMcpClient.ParseToolResult("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32602,\"message\":\"bad args\"}}", "t"));
            Assert.AreEqual(HypaErrorKind.BadRequest, e.Kind);
        }

        [Test] public void Mcp_Garbage_ThrowsInvalidResponse()
            => Assert.AreEqual(HypaErrorKind.InvalidResponse, Assert.Throws<HypaException>(() => HypaMcpClient.ParseToolResult("<html>", "t")).Kind);

        [Test] public void Mcp_PaidToolWithoutKey_ThrowsMissingKey_BeforeAnyNetwork()
        {
            var mcp = new HypaMcpClient(new HypaConfig(null));
            var ex = Assert.Throws<AggregateException>(() => mcp.TranslateAsync("hi", "Yoruba", null, default).Wait());
            Assert.AreEqual(HypaErrorKind.MissingKey, ((HypaException)ex.InnerException).Kind);
        }

        [TestCase(401, "{\"error\":\"Invalid API key\"}", HypaErrorKind.Unauthorized)]
        [TestCase(400, "{\"error\":{\"code\":\"INSUFFICIENT_BALANCE\",\"message\":\"zero\"}}", HypaErrorKind.InsufficientBalance)]
        [TestCase(429, "slow down", HypaErrorKind.RateLimited)]
        [TestCase(503, "{\"error\":\"Anonymous MCP daily allowance exhausted\"}", HypaErrorKind.Unavailable)]
        [TestCase(500, "", HypaErrorKind.Server)]
        [TestCase(422, "{\"message\":\"bad\"}", HypaErrorKind.BadRequest)]
        public void Http_StatusMapping(int status, string body, HypaErrorKind kind)
            => Assert.AreEqual(kind, HypaException.FromHttp(status, body).Kind);

        [Test] public void Config_TrimsAndDefaults()
        {
            var c = new HypaConfig("  k  ", "https://proxy.example/ ");
            Assert.AreEqual("k", c.ApiKey);
            Assert.AreEqual("https://proxy.example", c.BaseUrl);
            Assert.AreEqual(HypaConfig.DefaultBaseUrl, new HypaConfig(null).BaseUrl);
            Assert.IsFalse(new HypaConfig("  ").HasKey);
        }

        // ---------- Languages / dictionary ----------

        [Test] public void Languages_FilterTts_AndFindByNameOrCode()
        {
            var all = HypaMcpClient.ParseLanguages(HypaMcpClient.ParseToolResult(LiveListLanguages, "list_languages"));
            Assert.AreEqual(1, HypaLanguages.Filter(all, HypaFeature.Tts).Count);
            Assert.AreEqual(2, HypaLanguages.Filter(all, HypaFeature.Mt).Count);
            Assert.AreEqual("Yoruba", HypaLanguages.Find(all, "YO").Name);
            Assert.AreEqual("yo", HypaLanguages.Find(all, "yoruba").Code);
        }

        [Test] public void Dictionary_RendersKnownFieldsFirst_ThenOthers()
        {
            var d = new HypaDictionaryEntry { Raw = JObject.Parse("{\"word\":\"fish\",\"zeta\":\"z\",\"definition\":\"an animal that lives in water\",\"examples\":[\"I ate fish\"]}") };
            var s = d.ToDisplayText();
            StringAssert.StartsWith("Definition: an animal", s);
            StringAssert.Contains("Examples: I ate fish", s);
            StringAssert.Contains("Zeta: z", s);
            StringAssert.DoesNotContain("Word:", s);
        }

        [Test] public void Dictionary_EmptyResult_IsNull_NeverBlankText()
        {
            Assert.IsNull(new HypaDictionaryEntry { Raw = new JObject() }.ToDisplayText());
            Assert.IsNull(new HypaDictionaryEntry { Raw = JObject.Parse("{\"meaning\":null}") }.ToDisplayText());
            Assert.IsNull(new HypaDictionaryEntry().ToDisplayText());
        }

        // ---------- Audio ----------

        [Test] public void Wav_Decodes16BitMono()
        {
            var wav = MakeWav(new short[] { 0, 16384, -32768 }, 16000);
            Assert.IsTrue(WavDecoder.TryDecode(wav, out var s, out var ch, out var rate));
            Assert.AreEqual(1, ch); Assert.AreEqual(16000, rate); Assert.AreEqual(3, s.Length);
            Assert.AreEqual(0.5f, s[1], 1e-4); Assert.AreEqual(-1f, s[2], 1e-4);
        }

        [Test] public void Wav_RejectsNonWav() => Assert.IsFalse(WavDecoder.TryDecode(new byte[100], out _, out _, out _));

        [Test] public void WavEncoder_RoundTrips16BitMono_AndClamps()
        {
            var wav = WavEncoder.Encode16(new[] { 0f, 0.5f, -1f, 2f }, 4, 1, 16000);
            Assert.IsTrue(WavDecoder.TryDecode(wav, out var s, out var ch, out var rate));
            Assert.AreEqual(1, ch); Assert.AreEqual(16000, rate); Assert.AreEqual(4, s.Length);
            Assert.AreEqual(0.5f, s[1], 1e-3); Assert.AreEqual(-1f, s[2], 1e-4); Assert.AreEqual(1f, s[3], 1e-3);
        }

        static byte[] MakeWav(short[] pcm, int rate)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write("RIFF".ToCharArray()); w.Write(36 + pcm.Length * 2); w.Write("WAVE".ToCharArray());
                w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(pcm.Length * 2);
                foreach (var v in pcm) w.Write(v);
                return ms.ToArray();
            }
        }

        // ---------- Interaction ----------

        [Test] public void Pinch_Short_And_Long()
        {
            var t = new PinchTracker { LongPressSeconds = 0.6f };
            t.Update(true, 0); Assert.AreEqual(PinchKind.Short, t.Update(false, 0.2));
            t.Update(true, 1); Assert.AreEqual(PinchKind.Long, t.Update(true, 1.7));
            Assert.AreEqual(PinchKind.None, t.Update(true, 2.0));
            Assert.AreEqual(PinchKind.None, t.Update(false, 2.1));
        }

        [Test] public void Wrap_BreaksOnWords_AndKeepsNewlines()
            => Assert.AreEqual("aaa bb\ncc\nd", Ui.Wrap("aaa bb cc\nd", 6));

        // ---------- Repo rules (HYP-176): Lens and Stories never reference each other ----------

        [Test] public void Assemblies_LensAndStories_DoNotReferenceEachOther()
        {
            foreach (var asm in Directory.GetFiles("Assets/HypaLens", "*.asmdef", SearchOption.AllDirectories))
                StringAssert.DoesNotContain("HypaStories", File.ReadAllText(asm), asm);
            foreach (var asm in Directory.GetFiles("Assets/HypaStories", "*.asmdef", SearchOption.AllDirectories))
                StringAssert.DoesNotContain("HypaLens", File.ReadAllText(asm), asm);
        }

        [Test] public void NoKeyFileInsideAssets()
            => Assert.IsEmpty(Directory.GetFiles("Assets", HypaConfig.FileName, SearchOption.AllDirectories));
    }
}
