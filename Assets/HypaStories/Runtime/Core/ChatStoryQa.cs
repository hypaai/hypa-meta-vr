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
    /// Answers with the story's chat models (<see cref="StoryInfo.Models"/>) through Hypa /v2/developer/chat: the storyteller
    /// persona, the story so far (up to the interrupted line, in the listener's language) and the question. The model answers in
    /// the language directly. Not streamed yet: the reply is split into sentences, which are spoken one after another.
    ///
    /// Every question starts at the top of the story's list (Meta's Muse first). A model that fails hands the same question to
    /// the next one at once, so a quick failure costs a second or two, not the answer. A model the service says doesn't exist
    /// (or this key can't use) is skipped for the rest of the session. The whole question has <see cref="DeadlineMs"/>: past it,
    /// it counts as offline and the story goes on, so the storyteller never goes quiet.
    /// </summary>
    public sealed class ChatStoryQa : IStoryQa
    {
        /// <summary>Reasoning models think before answering, and max_tokens covers that too.</summary>
        const int MaxTokens = 1200, MaxSentences = 4;
        public int DeadlineMs = 12000;

        readonly HypaRestClient _rest = new HypaRestClient();
        /// <summary>Models the service said it doesn't have: not tried again this session.</summary>
        static readonly HashSet<string> Missing = new HashSet<string>();

        public string Mode => "chat";

        /// <summary>The models to try for a story, in order, without those known to be missing. Pure (given the missing set).</summary>
        public static List<string> Order(IEnumerable<string> models, ICollection<string> missing)
        {
            var list = (models ?? StoryLibrary.DefaultModels).Where(m => !string.IsNullOrEmpty(m) && !missing.Contains(m)).Distinct().ToList();
            return list.Count > 0 ? list : (models ?? StoryLibrary.DefaultModels).Take(1).ToList(); // all "missing": try the first anyway
        }

        /// <summary>The service's way of saying a model doesn't exist or this key can't use it. Pure.</summary>
        public static bool IsMissingModel(string message) =>
            message != null && (message.IndexOf("model was not found", StringComparison.OrdinalIgnoreCase) >= 0
                                || message.IndexOf("may not exist", StringComparison.OrdinalIgnoreCase) >= 0);

        public async Task AnswerAsync(QaRequest req, Action<string, int, bool> onSentence, CancellationToken ct)
        {
            var story = StoryLibrary.Find(req.StoryId);
            var messages = Messages(req, story?.Title);
            Exception last = null;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(DeadlineMs);
                foreach (var model in Order(story?.Models, Missing))
                {
                    string reply;
                    try { reply = await _rest.ChatAsync(model, messages, MaxTokens, deadline.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException($"no answer within {DeadlineMs / 1000} s (last tried {model})");
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        if (IsMissingModel(e.Message)) Missing.Add(model);
                        Debug.LogWarning($"Hypa Stories: {model} couldn't answer ({e.Message}); trying the next model.");
                        last = e;
                        continue;
                    }
                    var sentences = Sentences(reply);
                    if (sentences.Count == 0)
                    {
                        last = new HypaException(HypaErrorKind.InvalidResponse, $"{model} gave an empty answer.");
                        Debug.LogWarning($"Hypa Stories: {last.Message} Trying the next model.");
                        continue;
                    }
                    Debug.Log($"Hypa Stories: {model} answered “{req.Question}”: {reply}");
                    for (var i = 0; i < sentences.Count; i++) onSentence(sentences[i], i, i == sentences.Count - 1);
                    return;
                }
            }
            throw last ?? new HypaException(HypaErrorKind.InvalidResponse, "no model could answer.");
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
        // a reasoning model's thinking, if it ever comes back with the reply: never spoken
        static readonly Regex Thinking = new Regex(@"<think>.*?(</think>|$)", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        static readonly Regex Markup = new Regex(@"[*_#`]+|^\s*[-•]\s*", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>A reply as spoken sentences: markdown stripped, at most <see cref="MaxSentences"/>. Pure.</summary>
        public static List<string> Sentences(string reply)
        {
            var text = Markup.Replace(Thinking.Replace(reply ?? "", ""), "").Trim();
            return SentenceEnd.Split(text).Select(s => s.Trim()).Where(s => s.Length > 0).Take(MaxSentences).ToList();
        }
    }
}
