using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The story clock and narration (the prototype's flow.js play / seek / jump / ttsRun). Everything on screen and in the weather is
    /// derived from one number, T. The storyteller reads each line with TTS in the picked language, and T is pinned to the line being
    /// spoken (it waits at the line's start until its audio starts, and never runs past the next line's start).
    /// Pause freezes both. Interrupt / resume (a question) restarts the interrupted line from its beginning.
    /// </summary>
    public sealed class StoryPlayer : INarration
    {
        readonly StoryVoice _voice;
        /// <summary>
        /// A line whose speech takes longer than this is read silently, so a slow TTS call never holds the story. Hypa's own voices
        /// take 5-10 s a line (the next line is fetched while this one plays, so that is mostly hidden).
        /// </summary>
        /// <summary>How long a line may take to arrive before it plays silently. The words wait at the line's start meanwhile.</summary>
        const int LineTimeoutMs = 25000;
        /// <summary>Lines fetched ahead of the one playing: slow voices (Yoruba ~4-9 s a line) are then ready in time.</summary>
        const int Ahead = 3;
        /// <summary>
        /// Previous / next wait this long before the line is fetched, so tapping through lines asks only for the one you stop on.
        /// Without it each tap starts a fetch that runs on (to be cached), and the line you land on queues behind them all.
        /// </summary>
        const int SettleMs = 400;
        CancellationTokenSource _tts = new CancellationTokenSource();
        int _ttsToken;

        public StoryInfo Story { get; private set; }
        /// <summary>The language selected when this story opened.</summary>
        public StoryLanguage Language { get; private set; }
        public StoryDirector Director { get; private set; }
        public float T { get; private set; }
        public float Duration => Director?.Duration ?? 0f;
        public bool Live, Running, Paused, Asking, Ended;
        public int CaptionIndex { get; private set; } = -1;

        // ---------- for the debug panel ----------
        /// <summary>The line whose audio the story is waiting for (-1: not waiting), since when (<see cref="HypaTrace.Now"/>).</summary>
        public int WaitingFor { get; private set; } = -1;
        public double WaitingSince { get; private set; }
        /// <summary>A tap on previous / next is settling: no line asked for yet.</summary>
        public bool Settling { get; private set; }
        public int LineCount => Story?.Timeline.Captions.Length ?? 0;
        /// <summary>Where line <paramref name="i"/>'s audio is (debug panel).</summary>
        public string LineVoiceState(int i) => Story != null && i >= 0 && i < Caps.Length ? _voice.CacheState(Text(i), Spoken(i)) : "";
        public StoryLanguage LineLanguage(int i) => Story != null && i >= 0 && i < Caps.Length ? Spoken(i) : null;
        public string LineText(int i) => Story != null && i >= 0 && i < Caps.Length ? Text(i) : "";
        float _hold = float.PositiveInfinity, _lastT;

        /// <summary>Thunder claps to play this frame (lightning flashes crossed by the clock).</summary>
        public event Action Thunder;
        /// <summary>Story sounds to play this frame (a roar, a door, a hiss...), by name: cues crossed by the clock.</summary>
        public event Action<string> Sound;
        readonly System.Collections.Generic.List<string> _sounds = new System.Collections.Generic.List<string>();

        public StoryPlayer(StoryVoice voice) { _voice = voice; }

        Caption[] Caps => Story.Timeline.Captions;
        string Text(int i) => Caps[i].TextFor(Language.Code);

        /// <summary>
        /// The language a line is spoken in. A line with no translation is told in English, so a Yoruba (or any other) listener
        /// hears English words in an English voice instead of those words forced through the other language's voice.
        /// </summary>
        StoryLanguage Spoken(int i) => Caps[i].Has(Language.Code) ? Language : StoryLanguages.Find("en");
        int IdxAt(float t) => Mathf.Max(StoryDirector.IndexAt(Caps, t + 0.001f), 0);

        public void Load(StoryInfo story, StoryLanguage language)
        {
            StopTts();
            Story = story;
            Language = language;
            _voice.Voices = story.VoicesFor;
            _voice.NewSession();
            Director = new StoryDirector(story.Timeline);
            Live = Running = Paused = Asking = Ended = false;
            T = _lastT = 0f;
            CaptionIndex = -1;
        }

        /// <summary>Fetch the opening and warm the narration and question clips in the loaded story's voice.</summary>
        public Task PrepareAsync(CancellationToken ct)
        {
            // the opening line asks Auto first, so the voice locked for the story is the one that speaks it; the next line then
            // asks for that same voice instead of waiting on a filler
            var first = _voice.PrepareAsync(Text(0), Spoken(0), ct);
            for (var k = 1; k <= Ahead && k < Caps.Length; k++) _voice.Prefetch(Text(k), Spoken(k)); // they wait for the opening line's voice
            var lines = StoryLanguages.Lines(Language.Code);
            foreach (var text in lines.Filler) _voice.Prefetch(text, Language);
            foreach (var text in lines.Resume) _voice.Prefetch(text, Language);
            _voice.Prefetch(lines.Again, Language);
            _voice.Prefetch(lines.Offline, Language);
            return first;
        }

        public void Start(float from = 0f)
        {
            T = _lastT = from;
            Running = true; Paused = Asking = Ended = false;
            _hold = float.PositiveInfinity;
            _ = TtsRun(IdxAt(from));
        }

        async Task TtsRun(int idx, int settleMs = 0)
        {
            var token = ++_ttsToken;
            var ct = _tts.Token;
            _hold = Caps[idx].At; // held at the line's start while it settles, not at the last line's end
            try
            {
                if (settleMs > 0)
                {
                    Settling = true;
                    await Task.Delay(settleMs, ct);
                    if (token == _ttsToken) Settling = false;
                }
                for (var i = idx; i < Caps.Length; i++)
                {
                    if (token != _ttsToken) return;
                    T = _lastT = Caps[i].At;
                    _hold = Caps[i].At;                       // wait at the line's start until its audio starts
                    WaitingFor = i; WaitingSince = HypaTrace.Now;
                    var line = await PrepareLine(Text(i), Spoken(i), ct);
                    if (token != _ttsToken) return;
                    WaitingFor = -1;
                    if (HypaTrace.Enabled)
                        HypaTrace.Add("player", $"line {i + 1} {(line.Clip != null ? $"ready, {line.Clip.length:0.0}s clip" : "SILENT")} after {HypaTrace.Now - WaitingSince:0.0}s wait");
                    for (var k = i + 1; k <= i + Ahead && k < Caps.Length; k++) _voice.Prefetch(Text(k), Spoken(k));
                    var next = i + 1 < Caps.Length ? Caps[i + 1].At : Caps[i].Until + 1f;
                    await _voice.PlayAsync(line, _ => _hold = next - 0.01f, ct);
                    if (token != _ttsToken) return;
                    await Task.Delay(300, ct);
                }
                if (token != _ttsToken) return;
                _hold = float.PositiveInfinity;
                T = Duration;
            }
            catch (OperationCanceledException) { }
        }

        // The fetch carries on after a timeout, so the line's audio is cached for the next time it is read.
        async Task<SpeechLine> PrepareLine(string text, StoryLanguage lang, CancellationToken ct)
        {
            var prepared = _voice.PrepareAsync(text, lang, ct);
            if (await Task.WhenAny(prepared, Task.Delay(LineTimeoutMs, ct)) == prepared) return await prepared;
            ct.ThrowIfCancellationRequested();
            if (HypaTrace.Enabled) HypaTrace.Add("player", $"line timed out after {LineTimeoutMs / 1000}s: plays silently");
            return StoryVoice.Silent(text);
        }

        void StopTts()
        {
            WaitingFor = -1; Settling = false;
            _ttsToken++;
            _tts.Cancel();
            _tts.Dispose();
            _tts = new CancellationTokenSource();
            _voice.StopAll();
        }

        public void Seek(float to) => Seek(to, 0);

        void Seek(float to, int settleMs)
        {
            T = _lastT = Mathf.Clamp(to, 0f, Mathf.Max(Duration - 0.01f, 0f));
            Ended = false;
            StopTts();
            var i = IdxAt(T);
            T = _lastT = Caps[i].At;
            if (!Paused) _ = TtsRun(i, settleMs);
        }

        /// <summary>Previous / next line. Previous restarts the current line if it has been playing for a moment.</summary>
        public void Jump(int dir)
        {
            if (Story == null) return;
            var i = CaptionIndex;
            var to = dir > 0 ? Mathf.Min(i + 1, Caps.Length - 1)
                : (i >= 0 && T - Caps[i].At > 1.2f) || i <= 0 ? Mathf.Max(i, 0) : i - 1;
            if (HypaTrace.Enabled) HypaTrace.Add("player", $"{(dir > 0 ? "next" : "previous")} → line {to + 1}");
            Seek(Caps[to].At + 0.02f, SettleMs);
        }

        public void TogglePause()
        {
            if (Asking || !Running) return;
            if (Ended) { Paused = false; Seek(0f); return; }
            if (!Paused) Pause();
            else
            {
                Paused = false;
                _ = TtsRun(Mathf.Max(CaptionIndex, 0));
            }
        }

        /// <summary>Pause (also when the headset sleeps or the system menu opens). Never resumes on its own.</summary>
        public void Pause()
        {
            if (!Live || Paused || Asking) return;
            Paused = true;
            StopTts();
        }

        public void Stop()
        {
            StopTts();
            Running = Live = false;
        }

        /// <summary>Advances the clock. Returns the scene sample for this frame.</summary>
        public StorySample Update(float dt)
        {
            if (Running && !Paused && !Ended)
            {
                T = Mathf.Min(T + dt, _hold);
                if (Duration > 0f && T >= Duration) { T = Duration; Ended = true; }
                for (var k = Director.ThunderBetween(_lastT, T); k > 0; k--) Thunder?.Invoke();
                _sounds.Clear();
                if (T - _lastT < 1f) Director.SoundsBetween(_lastT, T, _sounds); // not on a seek: jumping a line skips its sounds
                foreach (var name in _sounds) Sound?.Invoke(name);
                _lastT = T;
            }
            var s = Director.Sample(T);
            CaptionIndex = s.Index;
            return s;
        }

        // ---------- INarration (questions) ----------

        public bool CanAsk => Live && Running && !Ended;
        public int LineIndex => Mathf.Max(CaptionIndex, 0);
        public string StoryId => Story?.Id;
        public string StorySoFar => string.Join(" ", Caps.Take(LineIndex + 1).Select(c => c.TextFor(Language.Code)));
        public string[] Suggestions => Story?.Suggestions(LineIndex, Language.Code) ?? new string[0];

        public void Interrupt()
        {
            Asking = true; Paused = true;
            StopTts();
        }

        public void Resume(int lineIndex)
        {
            Asking = false; Paused = false;
            Seek(Caps[Mathf.Clamp(lineIndex, 0, Caps.Length - 1)].At + 0.02f);
        }
    }
}
