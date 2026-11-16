using System.Collections.Generic;
using NUnit.Framework;

namespace HypaCore.Tests
{
    /// <summary>Hypa REST chat (verified 2026-11-14): the request body and the reply as it really comes back.</summary>
    public class ChatTests
    {
        // Captured from a live call (provider meta, model muse-spark-1.3), trimmed.
        const string LiveReply = "\nid: 1\ndata: {\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Welcome to my small hut, traveler. " +
                                 "I am weaving a soft blanket.\"},\"finish_reason\":\"stop\"}],\"usage\":{\"completion_tokens\":310},\"model\":\"muse-spark-1.3\",\"provider\":\"meta\"}\n\n";

        [Test] public void Body_HasMessagesProviderModel_AndTopP()
        {
            var body = HypaRestClient.ChatBody(new List<HypaRestClient.ChatMessage> { new HypaRestClient.ChatMessage("system", "Be Ama."), new HypaRestClient.ChatMessage("user", "Hi") }, "meta", "muse-spark-1.3");
            Assert.AreEqual("meta", (string)body["provider"]);
            Assert.AreEqual("muse-spark-1.3", (string)body["model"]);
            Assert.AreEqual(false, (bool)body["stream"]);
            Assert.AreEqual(1, (int)body["top_p"], "OpenAI models reject the provider default");
            Assert.AreEqual("user", (string)body["messages"][1]["role"]);
        }

        [Test] public void Parse_TheLiveReply()
            => Assert.AreEqual("Welcome to my small hut, traveler. I am weaving a soft blanket.", HypaRestClient.ParseChat(LiveReply));

        [Test] public void Parse_ErrorEvent_Throws_WithItsMessage()
        {
            var e = Assert.Throws<HypaException>(() => HypaRestClient.ParseChat("\nevent: error\nid: 1\ndata: Hypa is currently experiencing high traffic.\n\n"));
            StringAssert.Contains("high traffic", e.Message);
        }

        [Test] public void Parse_DropsReasoningBlocks()
        {
            Assert.AreEqual("I weave shawls.", HypaRestClient.StripReasoning("<think>\n**My hands**\nhmm</think>\nI weave shawls."));
            Assert.AreEqual("", HypaRestClient.StripReasoning("<think> still thinking"), "an unclosed block is all reasoning");
        }
    }
}
