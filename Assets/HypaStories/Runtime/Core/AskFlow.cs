using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    public enum AskState { Idle, Interrupt, Listening, Thinking, Answering, Resuming }

    /// <summary>The story side of a question: what was being told, and how to stop and restart it.</summary>
    public interface INarration
    {
        bool CanAsk { get; }
        /// <summary>The interrupted line (it is restarted from its beginning afterwards).</summary>
        int LineIndex { get; }
        string StoryId { get; }
        /// <summary>The script up to and including the interrupted line, in the current language (the answer's context).</summary>
        string StorySoFar { get; }
        /// <summary>Fade the narration out (about 200 ms, never mid-word cut) and pause the clock.</summary>
        void Interrupt();
        void Resume(int lineIndex);
    }

    /// <summary>A spoken line, ready to play. No clip = speech unavailable (no key, offline): it is "spoken" silently for Seconds.</summary>
    public sealed class SpeechLine
    {
        public string Text;
        public AudioClip Clip;
        public float Seconds;
    }

    /// <summary>The storyteller's voice for lines that are not the recorded narration: fillers, answers, TTS narration.</summary>
    public interface IStorySpeech
    {
        /// <summary>Starts fetching speech now, so a line queued behind another is ready when its turn comes. Never throws but for cancellation.</summary>
        Task<SpeechLine> PrepareAsync(string text, StoryLanguage language, CancellationToken ct);
        /// <summary>Resolves when the line has finished or was skipped. onStart(ms) fires when audio actually starts.</summary>
        Task PlayAsync(SpeechLine line, Action<double> onStart, CancellationToken ct);
        void SkipCurrent();
        void StopAll();
    }

    public interface IAskView
    {
        void SetState(AskState state, string text);
        void SetLevel(float level01);
        void SetQuestion(string text);
        void SetAnswer(string text);
    }

    /// <summary>Timing for one question, in ms from the end of the question. Targets: filler under 300, playback under 3000.</summary>
    public sealed class QaLogEntry
    {
        public string At, Lang, Question, Mode, Error;
        public int Line;
        public bool Typed, Filler2, Ok;
        public double? Filler, Transcript, FirstSentence, FirstAudio, Playback;
        public bool MeetsTargets => Filler < 300 && Playback < 3000;

        static string Ms(double? v) => v.HasValue ? Math.Round(v.Value).ToString(CultureInfo.InvariantCulture) : "-";

        public override string ToString() =>
            $"Q&A {Lang} line {Line}{(Typed ? " picked" : "")}: filler {Ms(Filler)} · asr {Ms(Transcript)} · 1st sentence {Ms(FirstSentence)}" +
            $" · 1st audio {Ms(FirstAudio)} · playback {Ms(Playback)} ms{(MeetsTargets ? " ✓" : "")}{(Filler2 ? " · 2nd filler" : "")}" +
            $"{(Error != null ? " ERR " + Error : "")}";
    }

    /// <summary>
    /// The interrupt-and-ask loop (tickets 3B + 3C), ported from the prototype's ask.js.
    /// States: idle → interrupt → listening → thinking (filler now, second filler after 2 s) → answering → resuming → idle.
    /// Empty transcript → "could you say that again?" and keep listening (three times, then back to the story);
    /// error / offline → "Let's keep going with the story" and resume. The narration restarts at the interrupted line.
    /// Every question writes a <see cref="QaLogEntry"/>.
    /// </summary>
    public sealed class AskFlow
    {
        readonly INarration _narration;
        readonly IStorySpeech _speech;
        readonly IListener _listener;
        readonly IStoryQa _qa;
        readonly IAskView _view;
        readonly Func<StoryLanguage> _language;
        readonly Action<bool> _onAsking;
        readonly Func<double> _clockMs;

        public readonly List<QaLogEntry> Log = new List<QaLogEntry>();
        public event Action<QaLogEntry> Logged;
        public AskState State { get; private set; }
        public float SecondFillerSeconds = 2f;

        sealed class Session
        {
            public int Line;
            public string Context;
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public bool Cancelled;
            public int Unclear;
            public IListenSession Rec;
            public TaskCompletionSource<ListenResult> Typed;
        }

        Session _s;
        Task _queue = Task.CompletedTask;

        /// <param name="onAsking">true when a question starts (duck the ambience, fireflies gather, storyteller turns), false when it ends.</param>
        public AskFlow(INarration narration, IStorySpeech speech, IListener listener, IStoryQa qa, IAskView view,
            Func<StoryLanguage> language, Action<bool> onAsking, Func<double> clockMs = null)
        {
            _narration = narration; _speech = speech; _listener = listener; _qa = qa; _view = view;
            _language = language; _onAsking = onAsking;
            _clockMs = clockMs ?? (() => Time.realtimeSinceStartupAsDouble * 1000.0);
        }

        QaLines L => StoryLanguages.Lines(_language().Code);

        void Set(AskState s, string text = "")
        {
            State = s;
            _view.SetState(s, text);
        }

        // Lines are spoken one after another. Speech for a line is fetched as soon as it is queued, not when its turn comes.
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
                catch (Exception e) { Debug.LogWarning("Hypa Stories: couldn't speak a line: " + e.Message); }
            }
        }

        /// <summary>Raise a hand / Ask. No-op unless the story is playing and nothing is being asked.</summary>
        public async Task BeginAsync()
        {
            if (State != AskState.Idle || !_narration.CanAsk) return;
            var s = _s = new Session { Line = _narration.LineIndex, Context = _narration.StorySoFar };
            Set(AskState.Interrupt, L.Listening);
            _onAsking?.Invoke(true);
            _view.SetQuestion(""); _view.SetAnswer("");
            _narration.Interrupt();
            await Loop(s);
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
                    r = await await Task.WhenAny(s.Rec.Result, typed.Task);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e)
                {
                    // No microphone or permission, or speech-to-text unavailable: a suggested question still works.
                    if (s.Cancelled) return;
                    _view.SetState(State, UnavailableMessage(e));
                    r = await typed.Task;
                }
                if (s.Cancelled) return;
                s.Typed = null;
                if (string.IsNullOrWhiteSpace(r.Transcript))
                {
                    if (++s.Unclear > 2) { await End(s, null, true); return; }
                    await Say(s, L.Again);
                    continue;
                }
                await Answer(s, r);
                return;
            }
        }

        static string UnavailableMessage(Exception e)
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
                At = DateTime.UtcNow.ToString("o"), Lang = lang.Code, Line = s.Line, Typed = r.Typed, Question = r.Transcript, Mode = _qa.Mode,
                Transcript = r.Typed ? 0 : r.TTranscript - r.TEnd,
            };
            _view.SetQuestion($"{L.You}: “{r.Transcript}”");
            Set(AskState.Thinking, L.Thinking);
            var answered = false;
            // The filler starts at once, before any network work (its speech is cached when the story starts).
            _ = Say(s, L.Filler[0], t => entry.Filler = t - r.TEnd);
            var timer = CancellationTokenSource.CreateLinkedTokenSource(s.Cts.Token);
            _ = SecondFiller();

            async Task SecondFiller() // still nothing after ~2 s → "Let me think about that…"
            {
                try { await Task.Delay(TimeSpan.FromSeconds(SecondFillerSeconds), timer.Token); }
                catch (OperationCanceledException) { return; }
                if (answered || s.Cancelled) return;
                entry.Filler2 = true;
                _ = Say(s, L.Filler[1]);
            }

            try
            {
                var req = new QaRequest { Question = r.Transcript, StoryId = _narration.StoryId, StorySoFar = s.Context, Language = lang, LineIndex = s.Line };
                await _qa.AnswerAsync(req, (text, i, last) =>
                {
                    if (s.Cancelled) return;
                    if (i == 0)
                    {
                        entry.FirstSentence = _clockMs() - r.TEnd;
                        answered = true; timer.Cancel();
                        Set(AskState.Answering);
                    }
                    _ = Say(s, text, i == 0 ? t => entry.Playback = t - r.TEnd : (Action<double>)null, true,
                        i == 0 ? t => entry.FirstAudio = t - r.TEnd : (Action<double>)null);
                }, s.Cts.Token);
                timer.Cancel();
                if (s.Cancelled) return;
                if (!answered) { await Say(s, L.Again); await Loop(s); return; } // the service produced no answer
                await _queue;
                entry.Ok = true;
                await End(s, entry, false);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                timer.Cancel();
                if (s.Cancelled) return;
                entry.Error = e.Message;
                await End(s, entry, true);
            }
        }

        // Back to the story: "Now, where was I…" then restart the interrupted line (or the offline line if something failed).
        async Task End(Session s, QaLogEntry entry, bool failed)
        {
            if (s.Cancelled) return;
            Set(AskState.Resuming);
            await Say(s, failed ? L.Offline : L.Resume);
            if (s.Cancelled) return;
            Cleanup();
            _narration.Resume(s.Line);
            if (entry == null) return;
            Log.Add(entry);
            Logged?.Invoke(entry);
        }

        void Cleanup()
        {
            _speech.StopAll();
            _view.SetAnswer(""); _view.SetQuestion("");
            _onAsking?.Invoke(false);
            _s = null;
            Set(AskState.Idle);
        }

        /// <summary>Abort everything (leaving the story). Does not resume the narration.</summary>
        public void Cancel()
        {
            var s = _s;
            if (State == AskState.Idle || s == null) return;
            s.Cancelled = true;
            s.Cts.Cancel();
            s.Rec?.Cancel();
            s.Typed?.TrySetResult(new ListenResult());
            Cleanup();
            _queue = Task.CompletedTask;
        }

        /// <summary>"Stop talking" while the storyteller is mid-line: skips the current line.</summary>
        public void Skip()
        {
            if (State == AskState.Idle || State == AskState.Listening) return;
            _speech.SkipCurrent();
        }

        /// <summary>End the recording now (release of a held Ask) and transcribe it.</summary>
        public void StopListening() => _s?.Rec?.Stop();

        /// <summary>A picked (or, in the editor, typed) question instead of speech.</summary>
        public void SubmitText(string question)
        {
            var s = _s;
            question = question?.Trim();
            if (string.IsNullOrEmpty(question) || State == AskState.Idle || s?.Typed == null) return;
            var now = _clockMs();
            s.Rec?.Cancel(); // first, so the recording can't win the race with an empty transcript
            s.Typed.TrySetResult(new ListenResult { Transcript = question, TEnd = now, TTranscript = now, Spoke = true, Typed = true });
        }
    }
}
