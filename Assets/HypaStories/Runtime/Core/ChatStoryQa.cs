using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Answers with the story's chat model (<see cref="StoryInfo.Model"/>) through Hypa /v2/developer/chat: the storyteller persona,
    /// the story so far (up to the interrupted line, in the listener's language) and the question. The model answers in the
    /// language directly. Not streamed yet: the reply is split into sentences, which are spoken one after another.
    /// A reply slower than <see cref="DeadlineMs"/> counts as offline, so the storyteller never goes quiet.
    /// </summary>
    public sealed class ChatStoryQa : IStoryQa
    {
        /// <summary>Muse models reason before answering, and max_tokens covers that too.</summary>
        const int MaxTokens = 1200, MaxSentences = 4;
        public int DeadlineMs = 12000;

        readonly HypaRestClient _rest = new HypaRestClient();

        public string Mode => "chat";

        public async Task AnswerAsync(QaRequest req, Action<string, int, bool> onSentence, CancellationToken ct)
        {
            var story = StoryLibrary.Find(req.StoryId);
            var model = story?.Model ?? StoryLibrary.DefaultModel;
            string reply;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(DeadlineMs);
                try { reply = await _rest.ChatAsync(model, Messages(req, story?.Title), MaxTokens, deadline.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"{model} took over {DeadlineMs / 1000} s");
                }
            }
            var sentences = Sentences(reply);
            if (sentences.Count == 0) throw new HypaException(HypaErrorKind.InvalidResponse, $"{model} gave an empty answer.");
            Debug.Log($"Hypa Stories: {model} answered “{req.Question}”: {reply}");
            for (var i = 0; i < sentences.Count; i++) onSentence(sentences[i], i, i == sentences.Count - 1);
        }

        /// <summary>The chat messages for a question. Pure.</summary>
        public static JArray Messages(QaRequest req, string title)
        {
            var lang = req.Language?.HypaLabel ?? "English";
            var name = string.IsNullOrEmpty(title) ? "this story" : $"“{title.Replace('\n', ' ')}”";
            var system =
                $"You are the storyteller of {name}, telling it aloud to a listener in virtual reality. " +
                "The listener has paused the story to ask you a question.\n" +
                $"Answer in {lang}, in 2 or 3 short sentences that sound natural spoken aloud. " +
                "No lists, headings, markdown, emoji or stage directions.\n" +
                "Use only what the story has told so far. Never reveal or hint at what happens next: if the answer comes later in " +
                "the story, warmly tell them they will find out soon.\n" +
                "If the question is not about the story, answer briefly and kindly, then lead back to the story. " +
                "Stay in your storyteller's voice, suitable for all ages.";
            var user = $"The story so far:\n{req.StorySoFar}\n\nThe listener asks: {req.Question}";
            return new JArray
            {
                new JObject { ["role"] = "system", ["content"] = system },
                new JObject { ["role"] = "user", ["content"] = user },
            };
        }

        // a sentence ends at . ! ? … (and any closing quote) before a capital or an opening mark: "“Why?” he asks." stays whole
        static readonly Regex SentenceEnd = new Regex(@"(?<=[.!?…。！？][""”»’)]*)\s+(?=[\p{Lu}¿¡“""«‘(\d])", RegexOptions.CultureInvariant);
        static readonly Regex Markup = new Regex(@"[*_#`]+|^\s*[-•]\s*", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>A reply as spoken sentences: markdown stripped, at most <see cref="MaxSentences"/>. Pure.</summary>
        public static List<string> Sentences(string reply)
        {
            var text = Markup.Replace(reply ?? "", "").Trim();
            return SentenceEnd.Split(text).Select(s => s.Trim()).Where(s => s.Length > 0).Take(MaxSentences).ToList();
        }
    }
}
