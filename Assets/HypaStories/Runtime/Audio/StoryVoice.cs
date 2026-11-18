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
    /// The storyteller's voice, from his mouth: every line (narration, fillers, answers) through Hypa text_to_speech, in the story's
    /// voice for the language (<see cref="Voices"/>). Clips are cached for the session and can be prefetched, so the next line is usually
    /// ready before it is needed. If that voice fails, the line tries the story's next voice for the language, then Hypa's Auto voice.
    /// Auto's first pick in a language is kept for the rest of the story (<see cref="VoiceLock"/>), so the voice never changes mid-story.
    /// Without a key (or offline) a line is "spoken" silently for about as long as it would take, so the flow and subtitles still run. <see cref="Level"/> drives the
    /// lip sync.
    /// </summary>
    public sealed class StoryVoice : MonoBehaviour, IStorySpeech
    {
        AudioSource _speech;
        readonly Dictionary<string, Task<AudioClip>> _cache = new Dictionary<string, Task<AudioClip>>();
        readonly VoiceLock _lock = new VoiceLock();
        readonly CancellationTokenSource _life = new CancellationTokenSource();
        readonly float[] _buf = new float[256];
        HypaMcpClient _mcp;
        /// <summary>Failures already logged (voice / language / error), so each shows once instead of once per line.</summary>
        readonly HashSet<string> _logged = new HashSet<string>();
        /// <summary>Voice locks already logged this story, so each shows once.</summary>
        readonly HashSet<string> _locked = new HashSet<string>();
        bool _fake, _skip;
        int _playToken;

        /// <summary>Loudness of whatever the storyteller is saying, 0..1.</summary>
        public float Level { get; private set; }
        public bool IsSpeaking => _fake || (_speech != null && _speech.isPlaying);
        public bool TtsAvailable => HypaConfig.Current.HasKey;
        /// <summary>The story's voices for a language code, best first (see <see cref="StoryInfo.VoicesFor"/>). Null or empty = Auto.</summary>
        public Func<string, VoiceOption[]> Voices { get; set; }

        public void Init(Transform mouth)
        {
            _mcp = new HypaMcpClient();
            var go = new GameObject("Voice");
            go.transform.SetParent(mouth, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0.6f; // from the storyteller, but always clear
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 2f; s.maxDistance = 30f;
            _speech = s;
        }

        /// <summary>Move only the audio object; keep the current clip, playback position and voice settings.</summary>
        public void SetOrigin(Transform mouth)
        {
            if (_speech == null || mouth == null || _speech.transform.parent == mouth) return;
            _speech.transform.SetParent(mouth, false);
            _speech.transform.localPosition = Vector3.zero;
        }

        void Update()
        {
            var flutter = _fake ? 0.3f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 9.3f) * Mathf.Sin(Time.time * 5.1f + 1f)) : 0f;
            Level = Mathf.Max(Mathf.Min(1f, Rms(_speech) * 5f), flutter);
        }

        float Rms(AudioSource s)
        {
            if (s == null || !s.isPlaying) return 0f;
            s.GetOutputData(_buf, 0);
            var sum = 0f;
            for (var i = 0; i < _buf.Length; i++) sum += _buf[i] * _buf[i];
            return Mathf.Sqrt(sum / _buf.Length);
        }

        // ---------- TTS lines ----------

        /// <summary>A story was opened: Auto picks the voice again, and lines cached in the last pick's voice are dropped.</summary>
        public void NewSession()
        {
            _lock.Reset();
            _cache.Clear();
            _locked.Clear();
        }

        /// <summary>Warm the cache for a line that will be needed soon (next narration line, filler lines).</summary>
        public void Prefetch(string text, StoryLanguage language)
        {
            if (TtsAvailable && !string.IsNullOrWhiteSpace(text)) _ = Fetch(text, language);
        }

        Task<AudioClip> Fetch(string text, StoryLanguage language)
        {
            var voices = Voices?.Invoke(language.Code) ?? new VoiceOption[0];
            var key = voices.FirstOrDefault() + "|" + language.HypaLabel + "|" + text;
            // a fetch that failed, or came back with no sound, is asked again
            if (_cache.TryGetValue(key, out var t) && !t.IsFaulted && !t.IsCanceled && !(t.IsCompleted && t.Result == null)) return t;
            return _cache[key] = FetchAsync(text, language, voices);
        }

        async Task<AudioClip> FetchAsync(string text, StoryLanguage language, VoiceOption[] voices)
        {
            var label = language.HypaLabel;
            HypaSpeech speech = null;
            foreach (var voice in voices)
            {
                try { speech = await _mcp.TextToSpeechAsync(text, label, _life.Token, voice.Voice, voice.Model); break; }
                catch (HypaException e) when (e.Kind != HypaErrorKind.MissingKey && e.Kind != HypaErrorKind.Unauthorized)
                {
                    Warn($"voice {voice} failed for {label} ({e.Message}); trying the next voice.");
                }
            }
            if (speech == null)
            {
                // Auto (pick = null) for the first line in the language, then the voice it picked.
                speech = await _lock.SpeakAsync(language.Code, pick => _mcp.TextToSpeechAsync(text, label, _life.Token, pick?.Voice, pick?.Model));
                var locked = _lock.PickFor(language.Code);
                if (locked != null && _locked.Add(language.Code + "|" + locked)) Debug.Log($"Hypa Stories: {label} voice locked to {locked} for this story.");
            }
            return await HypaAudioPlayer.LoadClipAsync(speech, _life.Token); // main thread: we were called from it
        }

        /// <summary>A line with no audio, "spoken" for about as long as it would take to say.</summary>
        public static SpeechLine Silent(string text) => new SpeechLine { Text = text, Seconds = 0.25f + text.Length * 0.028f };

        public async Task<SpeechLine> PrepareAsync(string text, StoryLanguage language, CancellationToken ct)
        {
            if (!TtsAvailable) return Silent(text);
            try
            {
                var clip = await WithCancellation(Fetch(text, language), ct);
                return clip != null ? new SpeechLine { Text = text, Clip = clip, Seconds = clip.length } : Silent(text);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception first)
            {
                // once more (a failed fetch isn't kept in the cache, so this asks again) before the line plays silently
                try
                {
                    var clip = await WithCancellation(Fetch(text, language), ct);
                    if (clip != null) return new SpeechLine { Text = text, Clip = clip, Seconds = clip.length };
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception e) { first = e; }
                Warn($"no voice could speak this {language.HypaLabel} line, it plays silently: {first.Message}");
                return Silent(text);
            }
        }

        void Warn(string message)
        {
            if (_logged.Add(message)) Debug.LogWarning("Hypa Stories: " + message);
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

    /// <summary>
    /// Keeps the storyteller on one voice for a story. Hypa's Auto routes every request on its own (and fails over to another model,
    /// with another voice, when one fails), so line by line the voice could change mid-story. The first Auto line in a language picks;
    /// every later line in that language asks for that provider/model and voice. <see cref="Reset"/> (a story opened) lets Auto pick
    /// again. A locked voice the service then refuses (it spoke, but will not take that voice by name) is dropped, and this line
    /// and the rest of that language go back to Auto, so the line is never left silent. Pure, unit tested.
    /// </summary>
    public sealed class VoiceLock
    {
        Dictionary<string, Task<VoiceOption>> _picks = new Dictionary<string, Task<VoiceOption>>();

        /// <summary>The voice locked for a language code, or null before Auto has picked.</summary>
        public VoiceOption PickFor(string lang)
            => _picks.TryGetValue(lang, out var t) && t.Status == TaskStatus.RanToCompletion ? t.Result : null;

        public void Reset() => _picks = new Dictionary<string, Task<VoiceOption>>();

        /// <summary>
        /// Speaks a line in the language's locked voice. <paramref name="speak"/>(null) is Auto. The first call for a language makes the
        /// pick; calls that come while it runs wait for it instead of asking Auto too. If the first call fails, the next one picks.
        /// A reply that doesn't name its provider, model and voice can't be locked, so that language stays on Auto.
        /// </summary>
        public Task<HypaSpeech> SpeakAsync(string lang, Func<VoiceOption, Task<HypaSpeech>> speak) => SpeakAsync(_picks, lang, speak);

        static async Task<HypaSpeech> SpeakAsync(Dictionary<string, Task<VoiceOption>> picks, string lang, Func<VoiceOption, Task<HypaSpeech>> speak)
        {
            if (picks.TryGetValue(lang, out var pending))
            {
                VoiceOption pick;
                try { pick = await pending; }
                catch (OperationCanceledException) { return await SpeakAsync(picks, lang, speak); } // the first call failed: pick again
                if (pick == null) return await speak(null);
                try { return await speak(pick); }
                catch (HypaException e) when (Refused(e))
                {
                    // the voice Auto used can't be asked for by name in this language (seen 2026-11-17: Auto speaks Spanish with
                    // google/gemini-2.5-pro-tts, then refuses it by name, "cannot speak Spanish"). Never fall silent for it: this
                    // line and the rest of the story in this language go to Auto.
                    picks[lang] = Task.FromResult<VoiceOption>(null);
                    return await speak(null);
                }
                catch (HypaException e) when (Transient(e))
                {
                    try { return await speak(pick); }                                  // a blip: the same voice once more
                    catch (HypaException e2) when (Refused(e2))
                    {
                        picks[lang] = Task.FromResult<VoiceOption>(null);
                        return await speak(null);
                    }
                    catch (HypaException e2) when (Transient(e2)) { return await speak(null); } // then Auto, this line only
                }
            }
            var made = new TaskCompletionSource<VoiceOption>();
            picks[lang] = made.Task;
            try
            {
                var speech = await speak(null);
                made.SetResult(Pick(speech));
                return speech;
            }
            catch
            {
                picks.Remove(lang);
                made.SetCanceled();
                throw;
            }
        }

        /// <summary>The service turned the request down (that model or voice can't be used for this language).</summary>
        static bool Refused(HypaException e) => e.Kind == HypaErrorKind.BadRequest || e.Kind == HypaErrorKind.ToolError;

        /// <summary>A passing failure worth one more try.</summary>
        static bool Transient(HypaException e) => e.Kind == HypaErrorKind.Timeout || e.Kind == HypaErrorKind.Offline
            || e.Kind == HypaErrorKind.Unavailable || e.Kind == HypaErrorKind.Server || e.Kind == HypaErrorKind.RateLimited
            || e.Kind == HypaErrorKind.InvalidResponse;

        /// <summary>The voice Auto used, as a model and voice to ask for again. Null when the reply doesn't name them.</summary>
        public static VoiceOption Pick(HypaSpeech speech)
            => speech == null || string.IsNullOrEmpty(speech.Provider) || string.IsNullOrEmpty(speech.Model) || string.IsNullOrEmpty(speech.Voice)
                ? null
                : new VoiceOption { Model = speech.Model.Contains("/") ? speech.Model : speech.Provider + "/" + speech.Model, Voice = speech.Voice };
    }
}
