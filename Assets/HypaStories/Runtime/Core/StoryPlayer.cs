using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        readonly Func<StoryLanguage> _lang;
        /// <summary>A line whose speech takes longer than this is read silently, so a slow TTS call never holds the story.</summary>
        const int LineTimeoutMs = 5000;
        CancellationTokenSource _tts = new CancellationTokenSource();
        int _ttsToken;

        public StoryInfo Story { get; private set; }
        public StoryDirector Director { get; private set; }
        public float T { get; private set; }
        public float Duration => Director?.Duration ?? 0f;
        public bool Live, Running, Paused, Asking, Ended;
        public int CaptionIndex { get; private set; } = -1;
        float _hold = float.PositiveInfinity, _lastT;

        /// <summary>Thunder claps to play this frame (lightning flashes crossed by the clock).</summary>
        public event Action Thunder;

        public StoryPlayer(StoryVoice voice, Func<StoryLanguage> language) { _voice = voice; _lang = language; }

        Caption[] Caps => Story.Timeline.Captions;
        string Text(int i) => Caps[i].TextFor(_lang().Code);
        int IdxAt(float t) => Mathf.Max(StoryDirector.IndexAt(Caps, t + 0.001f), 0);

        public void Load(StoryInfo story)
        {
            StopTts();
            Story = story;
            Director = new StoryDirector(story.Timeline);
            Live = Running = Paused = Asking = Ended = false;
            T = _lastT = 0f;
            CaptionIndex = -1;
        }

        /// <summary>The first lines to fetch before the story starts, so it doesn't open on a pause.</summary>
        public Task PrepareAsync(CancellationToken ct)
        {
            _voice.Prefetch(Text(1), _lang());
            return _voice.PrepareAsync(Text(0), _lang(), ct);
        }

        public void Start(float from = 0f)
        {
            T = _lastT = from;
            Running = true; Paused = Asking = Ended = false;
            _hold = float.PositiveInfinity;
            _ = TtsRun(IdxAt(from));
        }

        async Task TtsRun(int idx)
        {
            var token = ++_ttsToken;
            var ct = _tts.Token;
            var lang = _lang();
            try
            {
                for (var i = idx; i < Caps.Length; i++)
                {
                    if (token != _ttsToken) return;
                    T = _lastT = Caps[i].At;
                    _hold = Caps[i].At;                       // wait at the line's start until its audio starts
                    var line = await PrepareLine(Text(i), lang, ct);
                    if (token != _ttsToken) return;
                    if (i + 1 < Caps.Length) _voice.Prefetch(Text(i + 1), lang);
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
            return StoryVoice.Silent(text);
        }

        void StopTts()
        {
            _ttsToken++;
            _tts.Cancel();
            _tts.Dispose();
            _tts = new CancellationTokenSource();
            _voice.StopAll();
        }

        public void Seek(float to)
        {
            T = _lastT = Mathf.Clamp(to, 0f, Mathf.Max(Duration - 0.01f, 0f));
            Ended = false;
            StopTts();
            var i = IdxAt(T);
            T = _lastT = Caps[i].At;
            if (!Paused) _ = TtsRun(i);
        }

        /// <summary>Previous / next line. Previous restarts the current line if it has been playing for a moment.</summary>
        public void Jump(int dir)
        {
            if (Story == null) return;
            var i = CaptionIndex;
            var to = dir > 0 ? Mathf.Min(i + 1, Caps.Length - 1)
                : (i >= 0 && T - Caps[i].At > 1.2f) || i <= 0 ? Mathf.Max(i, 0) : i - 1;
            Seek(Caps[to].At + 0.02f);
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
        public string StorySoFar => string.Join(" ", Caps.Take(LineIndex + 1).Select(c => c.TextFor(_lang().Code)));
        public string[] Suggestions => Story?.Suggestions(LineIndex, _lang().Code) ?? new string[0];

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
