using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The story clock and narration (the prototype's flow.js play / seek / jump / ttsRun). Everything on screen and in the weather is
    /// derived from one number, T:
    ///  - with the recording (English and a voice clip): T is the voice-over position, then wall-clock for any silent tail;
    ///  - otherwise the storyteller reads each line with TTS in the picked language, and T is pinned to the line being spoken
    ///    (it waits at the line's start until its audio starts, and never runs past the next line's start).
    /// Pause freezes both. Interrupt / resume (a question) restarts the interrupted line from its beginning.
    /// </summary>
    public sealed class StoryPlayer : INarration
    {
        readonly StoryVoice _voice;
        readonly Func<StoryLanguage> _lang;
        CancellationTokenSource _tts = new CancellationTokenSource();
        int _ttsToken;
        AudioClip _clip;

        public StoryInfo Story { get; private set; }
        public StoryDirector Director { get; private set; }
        public float T { get; private set; }
        public float Duration => Director?.Duration ?? 0f;
        public bool Live, Running, Paused, Asking, Ended;
        public bool TtsMode { get; private set; }
        public int CaptionIndex { get; private set; } = -1;
        bool _voiceDone;
        float _hold = float.PositiveInfinity, _lastT;

        /// <summary>Thunder claps to play this frame (lightning flashes crossed by the clock).</summary>
        public event Action Thunder;

        public StoryPlayer(StoryVoice voice, Func<StoryLanguage> language) { _voice = voice; _lang = language; }

        Caption[] Caps => Story.Timeline.Captions;
        string Text(int i) => Caps[i].TextFor(_lang().Code);
        int IdxAt(float t) => Mathf.Max(StoryDirector.IndexAt(Caps, t + 0.001f), 0);

        public void Load(StoryInfo story, AudioClip voiceClip)
        {
            StopTts();
            Story = story;
            Director = new StoryDirector(story.Timeline);
            _clip = voiceClip;
            Live = Running = Paused = Asking = Ended = false;
            T = _lastT = 0f;
            CaptionIndex = -1;
        }

        /// <summary>The first lines to fetch before the story starts, so it doesn't open on a pause.</summary>
        public Task PrepareAsync(CancellationToken ct)
        {
            if (!WillUseTts()) return Task.CompletedTask;
            _voice.Prefetch(Text(1), _lang());
            return _voice.PrepareAsync(Text(0), _lang(), ct);
        }

        bool WillUseTts() => _clip == null || _lang().Code != "en";

        public void Start(float from = 0f)
        {
            T = _lastT = from;
            Running = true; Paused = Asking = Ended = false;
            _hold = float.PositiveInfinity;
            TtsMode = WillUseTts();
            _voiceDone = TtsMode;
            if (TtsMode) _ = TtsRun(IdxAt(from));
            else _voice.PlayNarration(_clip, from, () => _voiceDone = true);
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
                    var line = await _voice.PrepareAsync(Text(i), lang, ct);
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
            if (TtsMode)
            {
                StopTts();
                var i = IdxAt(T);
                T = _lastT = Caps[i].At;
                if (!Paused) _ = TtsRun(i);
                return;
            }
            if (_clip != null && T < _clip.length)
            {
                _voiceDone = false;
                _voice.PlayNarration(_clip, T, () => _voiceDone = true);
                if (Paused) _voice.PauseNarration();
            }
            else { _voice.StopNarration(0.2f); _voiceDone = true; }
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
                if (TtsMode) _ = TtsRun(Mathf.Max(CaptionIndex, 0));
                else _voice.ResumeNarration();
            }
        }

        /// <summary>Pause (also when the headset sleeps or the system menu opens). Never resumes on its own.</summary>
        public void Pause()
        {
            if (!Live || Paused || Asking) return;
            Paused = true;
            if (TtsMode) StopTts(); else _voice.PauseNarration();
        }

        public void Stop()
        {
            StopTts();
            _voice.StopNarration(0.8f);
            Running = Live = false;
        }

        /// <summary>Advances the clock. Returns the scene sample for this frame.</summary>
        public StorySample Update(float dt)
        {
            if (Running && !Paused && !Ended)
            {
                if (TtsMode) T = Mathf.Min(T + dt, _hold);
                else if (_voice.NarrationPlaying) T = _voice.NarrationTime;
                else if (_voiceDone) T += dt;
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

        public void Interrupt()
        {
            Asking = true; Paused = true;
            if (TtsMode) StopTts(); else _voice.FadeOutNarration(0.2f);
        }

        public void Resume(int lineIndex)
        {
            Asking = false; Paused = false;
            Seek(Caps[Mathf.Clamp(lineIndex, 0, Caps.Length - 1)].At + 0.02f);
        }
    }
}
