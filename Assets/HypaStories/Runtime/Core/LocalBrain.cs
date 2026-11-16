using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>One exchange of a visit: what you asked and what the local said. The brain remembers the last few.</summary>
    public sealed class LocalTurn
    {
        public string Question, Answer;
    }

    /// <summary>A question for a local (ticket 3I): who they are, the language to answer in, and the visit so far.</summary>
    public sealed class LocalRequest
    {
        public string Question;
        public LocalPersona Persona;
        public StoryLanguage Language;
        public IReadOnlyList<LocalTurn> History = new LocalTurn[0];
    }

    /// <summary>
    /// The local's mind (ticket 3I), the same shape as <see cref="IStoryQa"/>: question → answer sentences, each delivered as
    /// soon as it is ready so it can be spoken while the next one comes. In character, 2-3 short sentences, in the language asked.
    /// </summary>
    public interface ILocalBrain
    {
        /// <summary>For the timing log, e.g. "meta/muse-spark-1.3" or "stub".</summary>
        string Mode { get; }
        /// <summary>Calls <paramref name="onSentence"/>(text, index, isLast) per sentence on the caller's thread. Throws when offline.</summary>
        Task AnswerAsync(LocalRequest request, Action<string, int, bool> onSentence, CancellationToken ct);
    }

    /// <summary>What the chat model is told: who to be, what to talk about, how long and in which language. Pure, unit tested.</summary>
    public static class LocalPrompt
    {
        public const int MemoryTurns = 4;

        public static string System(LocalPersona p, StoryLanguage language)
        {
            var sb = new StringBuilder();
            sb.Append($"You are {p.DisplayName}, a {p.Role}");
            if (!string.IsNullOrWhiteSpace(p.Home)) sb.Append($" who lives in {p.Home}");
            sb.Append(". A visitor has come to see you and is talking with you face to face.\n");
            if (!string.IsNullOrWhiteSpace(p.Knows)) sb.Append($"Your world and life: {p.Knows.Trim()}\n");
            if (!string.IsNullOrWhiteSpace(p.DoesNotKnow)) sb.Append($"You don't know about: {p.DoesNotKnow.Trim()}\n");
            if (!string.IsNullOrWhiteSpace(p.NativeLanguage)) sb.Append($"You grew up speaking {p.NativeLanguage}.\n");
            sb.Append("Stay in character the whole time. Only talk about your own world and life; if asked about anything else, say kindly that you ");
            sb.Append("wouldn't know and bring the talk back to your village. Never mention being an AI, a model or a story.\n");
            sb.Append($"Answer in {language?.HypaLabel ?? "English"}, in 2 or 3 short spoken sentences, warm and simple. No lists, no stage directions, no emojis.");
            return sb.ToString();
        }

        /// <summary>System prompt, the last <see cref="MemoryTurns"/> exchanges of the visit, then the question.</summary>
        public static List<HypaRestClient.ChatMessage> Messages(LocalRequest r, int memoryTurns = MemoryTurns)
        {
            var list = new List<HypaRestClient.ChatMessage> { new HypaRestClient.ChatMessage("system", System(r.Persona, r.Language)) };
            var history = r.History ?? new LocalTurn[0];
            foreach (var t in history.Skip(Math.Max(0, history.Count - memoryTurns)))
            {
                list.Add(new HypaRestClient.ChatMessage("user", t.Question));
                list.Add(new HypaRestClient.ChatMessage("assistant", t.Answer));
            }
            list.Add(new HypaRestClient.ChatMessage("user", r.Question));
            return list;
        }
    }

    /// <summary>Splits a reply into sentences to speak one by one. Pure, unit tested.</summary>
    public static class Sentences
    {
        // Space after a sentence ending (and any closing quotes or brackets, which stay with their sentence).
        static readonly Regex Boundary = new Regex(@"(?<=[\.\!\?…。！？][\""”’»)]*)\s+", RegexOptions.Compiled);

        public static List<string> Split(string text, int max = 3)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            text = Regex.Replace(text.Replace("\n", " "), @"\s+", " ").Trim();
            text = Regex.Replace(text, @"^\*+|\*+$", "").Trim(); // stray markdown emphasis
            foreach (var s in Boundary.Split(text))
            {
                var t = s.Trim();
                if (t.Length == 0) continue;
                if (list.Count == max) { list[max - 1] += " " + t; continue; } // never drop words: the rest rides on the last sentence
                list.Add(t);
            }
            return list;
        }
    }

    /// <summary>
    /// The offline brain (ticket 3I): canned, in-character answers from the persona (keywords → answer, else the fallback), with
    /// a short think time, so the demo never stalls without a key or a network. Modes: "stub", "slow" (about 3 s: exercises the
    /// second filler), "offline" (every call fails).
    /// </summary>
    public sealed class StubLocalBrain : ILocalBrain
    {
        public string Mode { get; set; } = "stub";
        readonly System.Random _rng = new System.Random();

        /// <summary>The canned answer whose keyword appears in the question (longest keyword wins), else the fallback. Pure.</summary>
        public static string AnswerFor(LocalPersona p, string question)
        {
            var q = " " + (question ?? "").ToLowerInvariant() + " ";
            string best = null; var bestLen = 0;
            foreach (var c in p.Canned ?? new CannedAnswer[0])
            {
                if (c == null || string.IsNullOrWhiteSpace(c.Answer)) continue;
                foreach (var raw in (c.Keywords ?? "").Split(','))
                {
                    var k = raw.Trim().ToLowerInvariant();
                    if (k.Length > 0 && k.Length > bestLen && q.Contains(k)) { best = c.Answer; bestLen = k.Length; }
                }
            }
            return best ?? (string.IsNullOrWhiteSpace(p.Fallback) ? "That is a good question. Ask me about my village." : p.Fallback);
        }

        public async Task AnswerAsync(LocalRequest req, Action<string, int, bool> onSentence, CancellationToken ct)
        {
            if (Mode == "offline") throw new InvalidOperationException("offline");
            await Task.Delay(Mode == "slow" ? 3200 : 400 + _rng.Next(250), ct);
            var sentences = Sentences.Split(AnswerFor(req.Persona, req.Question));
            for (var i = 0; i < sentences.Count; i++)
            {
                if (i > 0) await Task.Delay(200, ct);
                onSentence(sentences[i], i, i == sentences.Count - 1);
            }
        }
    }

    /// <summary>
    /// The real brain: Hypa's chat (POST /v2/developer/chat) with the persona as the system prompt and the last few turns as
    /// memory. Any provider/model Hypa's chat serves, e.g. Muse ("meta", "muse-spark-1.3"). Goes through the dev key or the
    /// judges' proxy (HypaConfig.baseUrl): no key lives in the app.
    /// </summary>
    public sealed class HypaChatBrain : ILocalBrain
    {
        public readonly string Provider, Model;
        readonly HypaRestClient _rest;
        public string Mode => Provider + "/" + Model;

        public HypaChatBrain(string provider, string model, HypaRestClient rest = null)
        {
            Provider = provider; Model = model; _rest = rest ?? new HypaRestClient();
        }

        public async Task AnswerAsync(LocalRequest req, Action<string, int, bool> onSentence, CancellationToken ct)
        {
            var reply = await _rest.ChatAsync(LocalPrompt.Messages(req), Provider, Model, ct); // resumes on the caller's thread
            ct.ThrowIfCancellationRequested();
            var sentences = Sentences.Split(reply);
            for (var i = 0; i < sentences.Count; i++) onSentence(sentences[i], i, i == sentences.Count - 1);
        }
    }

    /// <summary>
    /// The chat brain with the offline brain behind it: if chat fails (offline, busy, a bad model id), the local still answers in
    /// character from the persona instead of going quiet. The timing log shows which one answered.
    /// </summary>
    public sealed class FallbackBrain : ILocalBrain
    {
        readonly ILocalBrain _primary, _backup;
        public string Mode { get; private set; }
        public Exception LastError { get; private set; }

        public FallbackBrain(ILocalBrain primary, ILocalBrain backup) { _primary = primary; _backup = backup; Mode = primary.Mode; }

        public async Task AnswerAsync(LocalRequest req, Action<string, int, bool> onSentence, CancellationToken ct)
        {
            var any = false;
            try
            {
                Mode = _primary.Mode;
                await _primary.AnswerAsync(req, (t, i, last) => { any = true; onSentence(t, i, last); }, ct);
                if (any) return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                if (any) return; // part of the answer was said: don't start another
                LastError = e;
                Debug.LogWarning($"Hypa Stories: {_primary.Mode} couldn't answer ({e.Message}); the offline answers take over.");
            }
            Mode = _primary.Mode + "→" + _backup.Mode;
            await _backup.AnswerAsync(req, onSentence, ct);
        }
    }

    /// <summary>
    /// Which brain the locals use: one setting, a Hypa "provider/model" id (Muse by default), or "stub". Change it from
    /// Hypa > Stories > Local Brain in the editor, <see cref="Model"/> in code, or the Brain field on a Local; no code change.
    /// </summary>
    public static class LocalBrains
    {
        public const string Muse = "meta/muse-spark-1.3";
        public const string OpenAi = "openai/gpt-5.6-luna";
        public const string Gemini = "google/gemini-3.5-flash";
        public const string Hypa = "hypaai/Hypa-Llama3.1-8b-SFT";
        public const string Stub = "stub";
        const string PrefKey = "hypa.stories.localBrain";

        /// <summary>The brains offered in the menu, with what they are.</summary>
        public static readonly (string id, string name)[] Choices =
        {
            (Muse, "Muse (Meta)"), (OpenAi, "OpenAI"), (Gemini, "Gemini (Google)"), (Hypa, "Hypa"), (Stub, "Offline answers (no key needed)"),
        };

        /// <summary>The setting: a "provider/model" id or "stub". Default: Muse.</summary>
        public static string Model
        {
            get => PlayerPrefs.GetString(PrefKey, Muse);
            set { PlayerPrefs.SetString(PrefKey, string.IsNullOrWhiteSpace(value) ? Muse : value.Trim()); PlayerPrefs.Save(); }
        }

        /// <summary>"meta/muse-spark-1.3" → ("meta", "muse-spark-1.3"). The model part may contain more slashes. Pure.</summary>
        public static bool TryParse(string id, out string provider, out string model)
        {
            provider = model = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            var cut = id.IndexOf('/');
            if (cut <= 0 || cut == id.Length - 1) return false;
            provider = id.Substring(0, cut).Trim(); model = id.Substring(cut + 1).Trim();
            return provider.Length > 0 && model.Length > 0;
        }

        /// <summary>
        /// The brain for an id: chat (with the offline answers behind it) when there's a key and the id is a provider/model, else
        /// the offline brain. Pure apart from the network client, unit tested.
        /// </summary>
        public static ILocalBrain Create(string id, bool hasKey, string stubMode = "stub")
        {
            var stub = new StubLocalBrain { Mode = string.IsNullOrEmpty(stubMode) ? "stub" : stubMode };
            if (!hasKey || id == Stub || !TryParse(id, out var provider, out var model)) return stub;
            return new FallbackBrain(new HypaChatBrain(provider, model), stub);
        }

        /// <summary>The brain for the current setting (or <paramref name="overrideId"/>), with the configured key or proxy.</summary>
        public static ILocalBrain Create(string overrideId = null)
            => Create(string.IsNullOrWhiteSpace(overrideId) ? Model : overrideId, HypaConfig.Current.CanCallPaidTools);
    }
}
