using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The storyteller's voice, from his mouth: the recorded narration (its position is the story clock) and every other line
    /// (fillers, answers, TTS narration) through Hypa text_to_speech. TTS clips are cached for the session and can be prefetched,
    /// so the next line is usually ready before it is needed. Without a key (or offline) a line is "spoken" silently for about as
    /// long as it would take, so the flow and subtitles still run. <see cref="Level"/> drives the lip sync.
    /// </summary>
    public sealed class StoryVoice : MonoBehaviour, IStorySpeech
    {
        AudioSource _narration, _speech;
        readonly Dictionary<string, Task<AudioClip>> _cache = new Dictionary<string, Task<AudioClip>>();
        readonly CancellationTokenSource _life = new CancellationTokenSource();
        readonly float[] _buf = new float[256];
        HypaMcpClient _mcp;
        bool _warned, _fake, _skip, _narrationOn, _narrationPaused;
        int _playToken;
        Action _onNarrationEnd;
        Coroutine _fade;

        /// <summary>Loudness of whatever the storyteller is saying, 0..1.</summary>
        public float Level { get; private set; }
        public bool IsSpeaking => _fake || (_speech != null && _speech.isPlaying) || (_narration != null && _narration.isPlaying);
        public bool NarrationPlaying => _narration != null && _narration.isPlaying;
        public float NarrationTime => _narration != null && _narration.clip != null ? _narration.time : 0f;
        public bool TtsAvailable => HypaConfig.Current.HasKey;

        public void Init(Transform mouth)
        {
            _mcp = new HypaMcpClient();
            _narration = NewSource("Narration", mouth);
            _speech = NewSource("Speech", mouth);
        }

        static AudioSource NewSource(string name, Transform parent)
        {
            var go = new GameObject("Voice " + name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0.6f; // from the storyteller, but always clear
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 2f; s.maxDistance = 30f;
            return s;
        }

        void Update()
        {
            var rms = Mathf.Max(Rms(_narration), Rms(_speech));
            var flutter = _fake ? 0.3f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 9.3f) * Mathf.Sin(Time.time * 5.1f + 1f)) : 0f;
            Level = Mathf.Max(Mathf.Min(1f, rms * 5f), flutter);

            if (_narrationOn && !_narrationPaused && _fade == null && !_narration.isPlaying)
            {
                _narrationOn = false;
                var end = _onNarrationEnd; _onNarrationEnd = null;
                end?.Invoke();
            }
        }

        float Rms(AudioSource s)
        {
            if (s == null || !s.isPlaying) return 0f;
            s.GetOutputData(_buf, 0);
            var sum = 0f;
            for (var i = 0; i < _buf.Length; i++) sum += _buf[i] * _buf[i];
            return Mathf.Sqrt(sum / _buf.Length);
        }

        // ---------- Recorded narration ----------

        public void PlayNarration(AudioClip clip, float from, Action onEnd)
        {
            StopNarration(0f);
            if (clip == null) { onEnd?.Invoke(); return; }
            _narration.clip = clip;
            _narration.volume = 1f;
            _narration.time = Mathf.Clamp(from, 0f, clip.length - 0.01f);
            _narration.Play();
            _narrationOn = true; _narrationPaused = false;
            _onNarrationEnd = onEnd;
        }

        public void PauseNarration()
        {
            if (!_narrationOn) return;
            _narrationPaused = true;
            _narration.Pause();
        }

        public void ResumeNarration()
        {
            if (!_narrationOn || !_narrationPaused) return;
            _narrationPaused = false;
            _narration.volume = 1f;
            _narration.UnPause();
        }

        /// <summary>Interrupt: fade out over about 200 ms and pause, so a question never cuts the voice mid-word.</summary>
        public void FadeOutNarration(float seconds = 0.2f)
        {
            if (!_narrationOn || _narrationPaused) return;
            _narrationPaused = true;
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(Fade(seconds, () => _narration.Pause()));
        }

        public void StopNarration(float seconds = 0.6f)
        {
            _narrationOn = false; _onNarrationEnd = null;
            if (_fade != null) { StopCoroutine(_fade); _fade = null; }
            if (!_narration.isPlaying || seconds <= 0f) { _narration.Stop(); return; }
            _fade = StartCoroutine(Fade(seconds, () => _narration.Stop()));
        }

        IEnumerator Fade(float seconds, Action then)
        {
            var v0 = _narration.volume;
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _narration.volume = Mathf.Lerp(v0, 0f, t / seconds);
                yield return null;
            }
            then();
            _narration.volume = 1f;
            _fade = null;
        }

        // ---------- TTS lines ----------

        /// <summary>Warm the cache for a line that will be needed soon (next narration line, filler lines).</summary>
        public void Prefetch(string text, StoryLanguage language)
        {
            if (TtsAvailable && !string.IsNullOrWhiteSpace(text)) _ = Fetch(text, language);
        }

        Task<AudioClip> Fetch(string text, StoryLanguage language)
        {
            var key = language.HypaLabel + "|" + text;
            if (_cache.TryGetValue(key, out var t) && !t.IsFaulted && !t.IsCanceled) return t;
            return _cache[key] = FetchAsync(text, language.HypaLabel);
        }

        async Task<AudioClip> FetchAsync(string text, string label)
        {
            var speech = await _mcp.TextToSpeechAsync(text, label, _life.Token);
            return await HypaAudioPlayer.LoadClipAsync(speech, _life.Token); // main thread: we were called from it
        }

        static SpeechLine Silent(string text) => new SpeechLine { Text = text, Seconds = 0.25f + text.Length * 0.028f };

        public async Task<SpeechLine> PrepareAsync(string text, StoryLanguage language, CancellationToken ct)
        {
            if (!TtsAvailable) return Silent(text);
            try
            {
                var clip = await WithCancellation(Fetch(text, language), ct);
                return clip != null ? new SpeechLine { Text = text, Clip = clip, Seconds = clip.length } : Silent(text);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                if (!_warned) Debug.LogWarning("Hypa Stories: text to speech unavailable, lines play silently: " + e.Message);
                _warned = true;
                return Silent(text);
            }
        }

        static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled || task.IsCompleted) return await task;
            var cancelled = new TaskCompletionSource<bool>();
            using (ct.Register(() => cancelled.TrySetResult(true)))
                if (await Task.WhenAny(task, cancelled.Task) != task) throw new OperationCanceledException(ct);
            return await task;
        }

        public async Task PlayAsync(SpeechLine line, Action<double> onStart, CancellationToken ct)
        {
            var token = ++_playToken;
            _skip = false;
            if (line.Clip != null) { _speech.clip = line.Clip; _speech.volume = 1f; _speech.Play(); }
            else _fake = true;
            onStart?.Invoke(Time.realtimeSinceStartupAsDouble * 1000.0);
            var end = Time.realtimeSinceStartup + (line.Clip != null ? line.Clip.length + 0.5f : line.Seconds);
            try
            {
                while (token == _playToken && !_skip && Time.realtimeSinceStartup < end && (line.Clip == null || _speech.isPlaying))
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
            }
            finally
            {
                if (token == _playToken)
                {
                    _fake = false;
                    if (_speech.isPlaying) _speech.Stop();
                }
            }
        }

        public void SkipCurrent() => _skip = true;

        public void StopAll()
        {
            _playToken++;
            _fake = false;
            if (_speech != null) _speech.Stop();
        }

        void OnDestroy()
        {
            _life.Cancel();
            _life.Dispose();
        }
    }
}
