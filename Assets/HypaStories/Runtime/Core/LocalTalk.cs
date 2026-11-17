using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// What a local says around a question, per story language (ticket 3I: "speak in the chosen story language"). English, French
    /// and Spanish are written here; the other story languages are translated from English once with Hypa's translate tool
    /// (<see cref="EnsureAsync"/>) and kept for the session and on the device, so the panel, fillers and greeting are in the language
    /// the story is told in. Until a translation arrives (or without a key) the English lines are used.
    /// </summary>
    public sealed class LocalLines
    {
        public string Prompt, Listening, Thinking, Again, Lost, You, NoVoice, Talk = "Talk", Done = "Done", Skip = "Skip";
        public string[] Filler;

        static readonly LocalLines En = new LocalLines
        {
            Prompt = "Raise a hand or pinch Talk", Listening = "Listening… speak now", Thinking = "Got it. Thinking…",
            Filler = new[] { "Hmm, good question…", "Let me think about that…" },
            Again = "Sorry, could you say that again?", Lost = "Forgive me, I lost my thread. Ask me again?", You = "You said",
            NoVoice = "I didn't hear anything. Is your microphone on?",
        };
        static readonly LocalLines Fr = new LocalLines
        {
            Prompt = "Lève la main ou pince Parler", Listening = "Je t’écoute… parle maintenant", Thinking = "Compris. Je réfléchis…",
            Filler = new[] { "Hmm, bonne question…", "Laisse-moi y réfléchir…" },
            Again = "Désolé, peux-tu répéter ?", Lost = "Pardon, j’ai perdu le fil. Redemande-moi ?", You = "Tu as dit",
            NoVoice = "Je n’ai rien entendu. Ton micro est-il allumé ?", Talk = "Parler", Done = "Fini", Skip = "Passer",
        };
        static readonly LocalLines Es = new LocalLines
        {
            Prompt = "Levanta la mano o pellizca Hablar", Listening = "Te escucho… habla ahora", Thinking = "Entendido. Pensando…",
            Filler = new[] { "Mmm, buena pregunta…", "Déjame pensarlo…" },
            Again = "Perdona, ¿puedes repetirlo?", Lost = "Perdona, perdí el hilo. ¿Me lo preguntas otra vez?", You = "Dijiste",
            NoVoice = "No te oí. ¿Está encendido tu micrófono?", Talk = "Hablar", Done = "Listo", Skip = "Saltar",
        };
        static readonly Dictionary<string, LocalLines> Translated = new Dictionary<string, LocalLines>();
        static readonly Dictionary<string, string> Texts = new Dictionary<string, string>(); // "code|English" → translation

        /// <summary>The lines for a language code: written, translated, or English until a translation is ready.</summary>
        public static LocalLines For(string code) =>
            code == "fr" ? Fr : code == "es" ? Es : code != null && Translated.TryGetValue(code, out var t) ? t : En;

        /// <summary>True when the lines for this language are ready (written, or already translated).</summary>
        public static bool Ready(string code) => code == "en" || code == "fr" || code == "es" || (code != null && Translated.ContainsKey(code));

        /// <summary>One English line in another story language (cached; on device too). English back on any failure.</summary>
        public static async Task<string> TranslateAsync(string english, StoryLanguage lang)
        {
            if (string.IsNullOrWhiteSpace(english) || lang == null || lang.Code == "en") return english;
            var key = lang.Code + "|" + english;
            if (Texts.TryGetValue(key, out var hit)) return hit;
            var pref = "hypa.stories.localLine." + StableHash(key);
            var saved = PlayerPrefs.GetString(pref, "");
            if (saved.Length > 0) return Texts[key] = saved;
            if (!HypaConfig.Current.CanCallPaidTools) return english;
            try
            {
                var t = (await new HypaMcpClient().TranslateAsync(english, lang.HypaLabel, null, CancellationToken.None))?.Text?.Trim();
                if (string.IsNullOrEmpty(t)) return english;
                PlayerPrefs.SetString(pref, t);
                return Texts[key] = t;
            }
            catch (Exception e) { Debug.LogWarning($"Hypa Stories: couldn't translate a local's line to {lang.HypaLabel}: {e.Message}"); return english; }
        }

        /// <summary>
        /// Several lines, one request at a time (many at once failed on the translate tool, and one request with all the lines timed
        /// out after 30 s; one at a time took ~20 s for twelve). Cached like <see cref="TranslateAsync"/>, so only the first visit in a
        /// language waits.
        /// </summary>
        public static async Task<string[]> TranslateManyAsync(string[] english, StoryLanguage lang)
        {
            var result = (string[])english.Clone();
            if (lang == null || lang.Code == "en") return result;
            var todo = new List<int>();
            for (var i = 0; i < english.Length; i++)
            {
                var key = lang.Code + "|" + english[i];
                var saved = PlayerPrefs.GetString("hypa.stories.localLine." + StableHash(key), "");
                if (Texts.TryGetValue(key, out var hit)) result[i] = hit;
                else if (saved.Length > 0) result[i] = Texts[key] = saved;
                else if (!string.IsNullOrWhiteSpace(english[i])) todo.Add(i);
            }
            if (todo.Count == 0 || !HypaConfig.Current.CanCallPaidTools) return result;
            foreach (var i in todo)
            {
                result[i] = await TranslateAsync(english[i], lang);
                if (result[i] == english[i]) { await Task.Delay(500); result[i] = await TranslateAsync(english[i], lang); } // once more
            }
            PlayerPrefs.Save();
            return result;
        }

        /// <summary>FNV-1a: the same on every run and device (string.GetHashCode isn't guaranteed to be).</summary>
        static string StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var c in s) { h ^= c; h *= 16777619; }
                return h.ToString("x8");
            }
        }

        /// <summary>Makes the lines for <paramref name="lang"/> ready (translating the English ones when needed). Main thread.</summary>
        public static async Task<LocalLines> EnsureAsync(StoryLanguage lang)
        {
            if (lang == null || Ready(lang.Code)) return For(lang?.Code);
            var parts = new[] { En.Prompt, En.Listening, En.Thinking, En.Again, En.Lost, En.You, En.NoVoice, En.Talk, En.Done, En.Skip, En.Filler[0], En.Filler[1] };
            var done = await TranslateManyAsync(parts, lang);
            var l = new LocalLines
            {
                Prompt = done[0], Listening = done[1], Thinking = done[2], Again = done[3], Lost = done[4], You = done[5], NoVoice = done[6],
                Talk = done[7], Done = done[8], Skip = done[9], Filler = new[] { done[10], done[11] },
            };
            PlayerPrefs.Save();
            return Translated[lang.Code] = l;
        }
    }

    /// <summary>
    /// Talking to a local (ticket 3I), the campfire's ask loop (<see cref="AskFlow"/>) without a story to pause: listen → think (a
    /// filler at once, a second after ~2 s) → answer sentence by sentence, then back to idle with the exchange remembered for the
    /// visit. Empty transcript: "could you say that again?" and listen again (three times, then stop). A failed answer: an
    /// in-character "I lost my thread". No microphone or key: the suggested questions still work. Every question writes a
    /// <see cref="QaLogEntry"/> (ASR, chat, first sentence, first audio, playback; targets: filler under 300 ms, playback under 3 s).
    /// </summary>
    public sealed class LocalTalk
    {
        readonly IStorySpeech _speech;
        readonly IListener _listener;
        readonly ILocalBrain _brain;
        readonly IAskView _view;
        readonly Func<StoryLanguage> _language;
        readonly Func<LocalPersona> _persona;
        readonly Action<bool> _onEngaged;
        readonly Func<double> _clockMs;

        public readonly List<LocalTurn> History = new List<LocalTurn>();
        public readonly List<QaLogEntry> Log = new List<QaLogEntry>();
        public event Action<QaLogEntry> Logged;
        public AskState State { get; private set; }
        public float SecondFillerSeconds = 2f;
        public int MemoryTurns = LocalPrompt.MemoryTurns;
        public string BrainMode => _brain.Mode;
        /// <summary>True while the greeting is being said (talking over it starts a question).</summary>
        public bool Greeting { get; private set; }
        /// <summary>
        /// The wake word has just been heard ("Hey Ama…") and the rest of the phrase is still coming: the panel shows "Listening"
        /// at once (not after the phrase is transcribed), then <see cref="AskAsync"/> or <see cref="BeginAsync"/> takes over.
        /// </summary>
        public bool WakeOpen { get; private set; }
        /// <summary>The greeting in the story language (set by the Local once translated); empty = the persona's own.</summary>
        public string GreetingText;

        sealed class Session
        {
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public bool Cancelled;
            public int Unclear;
            public IListenSession Rec;
            public TaskCompletionSource<ListenResult> Typed;
            /// <summary>The filler already started when the recording stopped (before speech-to-text came back).</summary>
            public bool FillerSaid;
            public double FillerAt;
        }

        Session _s;
        Task _queue = Task.CompletedTask;

        /// <param name="onEngaged">true when a question starts (the local turns to you), false when the answer is done.</param>
        public LocalTalk(IStorySpeech speech, IListener listener, ILocalBrain brain, IAskView view, Func<StoryLanguage> language,
            Func<LocalPersona> persona, Action<bool> onEngaged, Func<double> clockMs = null)
        {
            _speech = speech; _listener = listener; _brain = brain; _view = view; _language = language; _persona = persona; _onEngaged = onEngaged;
            _clockMs = clockMs ?? (() => Time.realtimeSinceStartupAsDouble * 1000.0);
        }

        LocalLines L => LocalLines.For(_language().Code);

        void Set(AskState s, string text = "")
        {
            State = s;
            _view.SetState(s, text);
        }

        Task Say(Session s, string text, Action<double> onStart = null, bool answer = false, Action<double> onReady = null)
        {
            var prepared = _speech.PrepareAsync(text, _language(), s.Cts.Token);
            var prev = _queue;
            return _queue = Run();

            async Task Run()
            {
                try { await prev; } catch { /* an earlier line failing never blocks this one */ }
                if (s.Cancelled) return;
                try
                {
                    var line = await prepared;
                    onReady?.Invoke(_clockMs());
                    if (s.Cancelled) return;
                    await _speech.PlayAsync(line, t => { if (answer) _view.SetAnswer(text); onStart?.Invoke(t); }, s.Cts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { Debug.LogWarning("Hypa Stories: the local couldn't speak a line: " + e.Message); }
            }
        }

        /// <summary>The local's greeting (when you come close). Ignored while a question is open.</summary>
        public async Task GreetAsync()
        {
            var p = _persona();
            var greeting = string.IsNullOrWhiteSpace(GreetingText) ? p?.Greeting : GreetingText;
            if (State != AskState.Idle || p == null || string.IsNullOrWhiteSpace(greeting)) return;
            var s = _s = new Session();
            Greeting = true;
            Set(AskState.Answering);
            _onEngaged?.Invoke(true);
            await Say(s, greeting, answer: true);
            Greeting = false;
            if (s.Cancelled) return;
            Finish();
        }

        /// <summary>Raise a hand / pinch Talk: start listening. No-op while a question is already open.</summary>
        public async Task BeginAsync()
        {
            if (State != AskState.Idle && !Greeting && !WakeOpen) return;
            if (Greeting) { Greeting = false; Cancel(); } // talking over the greeting: listen now
            WakeOpen = false;
            var s = _s = new Session();
            _onEngaged?.Invoke(true);
            _view.SetQuestion("");
            await Loop(s);
        }

        /// <summary>
        /// A question already heard (the wake word: "Ama, what do you weave?"): straight to the answer, no listening first.
        /// Talking over the greeting is fine; ignored while another question is open.
        /// </summary>
        public async Task AskAsync(string question)
        {
            question = question?.Trim();
            if (string.IsNullOrEmpty(question) || (State != AskState.Idle && !Greeting && !WakeOpen)) return;
            if (Greeting) { Greeting = false; Cancel(); }
            WakeOpen = false;
            var s = _s = new Session();
            _onEngaged?.Invoke(true);
            var now = _clockMs();
            await Answer(s, new ListenResult { Transcript = question, TEnd = now, TTranscript = now, Spoke = true });
        }

        /// <summary>The wake word was heard: show "Listening" now, while the rest of the phrase arrives. Ignored mid-question.</summary>
        public void OpenFromWake()
        {
            if (WakeOpen || (State != AskState.Idle && !Greeting)) return;
            if (Greeting) { Greeting = false; Cancel(); }
            WakeOpen = true;
            _onEngaged?.Invoke(true);
            _view.SetQuestion("");
            Set(AskState.Listening, L.Listening);
        }

        /// <summary>The wake phrase came to nothing (no question, timed out): back to idle.</summary>
        public void CloseWake()
        {
            if (!WakeOpen) return;
            WakeOpen = false;
            if (_s == null) Finish();
        }

        async Task Loop(Session s)
        {
            while (!s.Cancelled)
            {
                Set(AskState.Listening, L.Listening);
                var typed = s.Typed = new TaskCompletionSource<ListenResult>();
                ListenResult r;
                try
                {
                    s.Rec = _listener.Listen(_language(), _view.SetLevel);
                    s.FillerSaid = false;
                    // The moment you stop talking: "Got it. Thinking…" and the filler, while speech-to-text runs (~4 s on a Quest).
                    s.Rec.Recorded += () =>
                    {
                        if (s.Cancelled || s.Typed == null) return;
                        Set(AskState.Thinking, L.Thinking);
                        s.FillerSaid = true;
                        _ = Say(s, L.Filler[0], t => s.FillerAt = t);
                    };
                    r = await await Task.WhenAny(s.Rec.Result, typed.Task);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e)
                {
                    if (s.Cancelled) return;
                    Debug.LogWarning("Hypa Stories: the local couldn't listen: " + e.Message);
                    _view.SetState(State, Unavailable(e)); // a suggested question still works
                    r = await typed.Task;
                }
                if (s.Cancelled) return;
                s.Typed = null;
                if (string.IsNullOrWhiteSpace(r.Transcript))
                {
                    if (++s.Unclear > 2) { Finish(); if (!r.Spoke) _view.SetState(AskState.Idle, L.NoVoice); return; }
                    if (!r.Spoke) _view.SetQuestion(L.NoVoice); // nothing above the noise floor: likely the microphone
                    await Say(s, L.Again);
                    continue;
                }
                await Answer(s, r);
                return;
            }
        }

        static string Unavailable(Exception e)
        {
            if (e is HypaException h && h.Kind == HypaErrorKind.MissingKey) return "Speech needs a Hypa API key: pick a question";
            if (e is HypaException) return "Couldn't hear that: pick a question";
            return "Microphone unavailable: pick a question";
        }

        async Task Answer(Session s, ListenResult r)
        {
            var lang = _language();
            var entry = new QaLogEntry
            {
                At = DateTime.UtcNow.ToString("o"), Lang = lang.Code, Typed = r.Typed, Question = r.Transcript, Mode = _brain.Mode,
                Transcript = r.Typed ? 0 : r.TTranscript - r.TEnd,
            };
            _view.SetQuestion($"{L.You}: “{r.Transcript}”");
            _view.SetAnswer("");
            Set(AskState.Thinking, L.Thinking);
            var answered = false;
            var said = new List<string>();
            if (s.FillerSaid) entry.Filler = s.FillerAt > 0 ? s.FillerAt - r.TEnd : 0; // already started when the recording stopped
            else _ = Say(s, L.Filler[0], t => entry.Filler = t - r.TEnd); // at once, before any network work (cached)
            var timer = CancellationTokenSource.CreateLinkedTokenSource(s.Cts.Token);
            _ = SecondFiller();

            async Task SecondFiller()
            {
                try { await Task.Delay(TimeSpan.FromSeconds(SecondFillerSeconds), timer.Token); }
                catch (OperationCanceledException) { return; }
                if (answered || s.Cancelled) return;
                entry.Filler2 = true;
                _ = Say(s, L.Filler[1]);
            }

            try
            {
                var req = new LocalRequest { Question = r.Transcript, Persona = _persona(), Language = lang, History = History.ToArray() };
                await _brain.AnswerAsync(req, (text, i, last) =>
                {
                    if (s.Cancelled) return;
                    if (i == 0)
                    {
                        entry.FirstSentence = _clockMs() - r.TEnd;
                        answered = true; timer.Cancel();
                        Set(AskState.Answering);
                    }
                    said.Add(text);
                    _ = Say(s, text, i == 0 ? t => entry.Playback = t - r.TEnd : (Action<double>)null, true,
                        i == 0 ? t => entry.FirstAudio = t - r.TEnd : (Action<double>)null);
                }, s.Cts.Token);
                timer.Cancel();
                entry.Mode = _brain.Mode; // a fallback brain reports which one answered
                if (s.Cancelled) return;
                if (!answered) { await Say(s, L.Again); await Loop(s); return; }
                Remember(r.Transcript, string.Join(" ", said));
                await _queue;
                entry.Ok = true;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                timer.Cancel();
                if (s.Cancelled) return;
                entry.Error = e.Message;
                await Say(s, L.Lost);
            }
            if (s.Cancelled) return;
            Finish();
            Log.Add(entry);
            Logged?.Invoke(entry);
        }

        void Remember(string question, string answer)
        {
            History.Add(new LocalTurn { Question = question, Answer = answer });
            while (History.Count > MemoryTurns) History.RemoveAt(0);
        }

        /// <summary>Back to idle. The last answer stays on screen until the next question.</summary>
        void Finish()
        {
            _onEngaged?.Invoke(false);
            _s = null;
            Set(AskState.Idle, L.Prompt);
        }

        /// <summary>Abort (walked away, left the place). The visit's memory is kept.</summary>
        public void Cancel()
        {
            var s = _s;
            if (s == null) { if (WakeOpen) { WakeOpen = false; Finish(); } return; }
            WakeOpen = false;
            s.Cancelled = true;
            s.Cts.Cancel();
            s.Rec?.Cancel();
            s.Typed?.TrySetResult(new ListenResult());
            _speech.StopAll();
            _queue = Task.CompletedTask;
            Finish();
        }

        /// <summary>A new visit: forget the conversation.</summary>
        public void ForgetVisit() => History.Clear();

        /// <summary>Skip the line being said.</summary>
        public void Skip()
        {
            if (State == AskState.Idle || State == AskState.Listening) return;
            _speech.SkipCurrent();
        }

        /// <summary>End the recording now (release of a held Talk) and transcribe it.</summary>
        public void StopListening() => _s?.Rec?.Stop();

        /// <summary>A picked suggested question instead of speech.</summary>
        public void SubmitText(string question)
        {
            var s = _s;
            question = question?.Trim();
            if (string.IsNullOrEmpty(question) || s?.Typed == null) return;
            var now = _clockMs();
            s.Rec?.Cancel();
            s.Typed.TrySetResult(new ListenResult { Transcript = question, TEnd = now, TTranscript = now, Spoke = true, Typed = true });
        }
    }
}
