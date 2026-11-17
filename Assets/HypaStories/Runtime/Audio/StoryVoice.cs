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
    /// Without a key (or offline) a line is "spoken" silently for about as long as it would take, so the flow and subtitles still run. <see cref="Level"/> drives the
    /// lip sync.
    /// </summary>
    public sealed class StoryVoice : MonoBehaviour, IStorySpeech
    {
        AudioSource _speech;
        readonly Dictionary<string, Task<AudioClip>> _cache = new Dictionary<string, Task<AudioClip>>();
        readonly CancellationTokenSource _life = new CancellationTokenSource();
        readonly float[] _buf = new float[256];
        HypaMcpClient _mcp;
        /// <summary>Failures already logged (voice / language / error), so each shows once instead of once per line.</summary>
        readonly HashSet<string> _logged = new HashSet<string>();
        bool _fake, _skip;
        int _playToken;

        /// <summary>Loudness of whatever the storyteller is saying, 0..1.</summary>
        public float Level { get; private set; }
        public bool IsSpeaking => _fake || (_speech != null && _speech.isPlaying);
        public bool TtsAvailable => HypaConfig.Current.CanCallPaidTools; // the dev key, or the judges' proxy
        /// <summary>The story's voices for a language code, best first (see <see cref="StoryInfo.VoicesFor"/>). Null or empty = Auto.</summary>
        public Func<string, VoiceOption[]> Voices { get; set; }
        /// <summary>Hypa voice for this speaker (a local's persona sets it), tried before <see cref="Voices"/>; empty = none.</summary>
        public string Voice;
        /// <summary>Hypa speech model (provider/model) for <see cref="Voice"/>; empty = Hypa picks. Locals use a fast one.</summary>
        public string SpeechModel;
        /// <summary>Audio in the reply instead of a link to download: about a second sooner.</summary>
        public bool InlineAudio;

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

        /// <summary>Warm the cache for a line that will be needed soon (next narration line, filler lines).</summary>
        public void Prefetch(string text, StoryLanguage language)
        {
            if (TtsAvailable && !string.IsNullOrWhiteSpace(text)) _ = Fetch(text, language);
        }

        Task<AudioClip> Fetch(string text, StoryLanguage language)
        {
            var voices = Voices?.Invoke(language.Code) ?? new VoiceOption[0];
            if (!string.IsNullOrEmpty(Voice) || !string.IsNullOrEmpty(SpeechModel))
                voices = new[] { new VoiceOption { Voice = Voice, Model = SpeechModel } }.Concat(voices).ToArray();
            var key = voices.FirstOrDefault() + "|" + language.HypaLabel + "|" + text;
            if (_cache.TryGetValue(key, out var t) && !t.IsFaulted && !t.IsCanceled) return t;
            return _cache[key] = FetchAsync(text, language.HypaLabel, voices);
        }

        /// <summary>The voice that spoke each language (by Hypa label); every later line in that language uses it.</summary>
        readonly Dictionary<string, VoiceOption> _chosen = new Dictionary<string, VoiceOption>();
        static readonly VoiceOption Auto = new VoiceOption();

        /// <summary>Network and service hiccups: worth trying the same voice again rather than switching to another one.</summary>
        static bool Transient(HypaException e) => e.Kind == HypaErrorKind.Offline || e.Kind == HypaErrorKind.Timeout ||
            e.Kind == HypaErrorKind.Unavailable || e.Kind == HypaErrorKind.RateLimited || e.Kind == HypaErrorKind.Server ||
            e.Kind == HypaErrorKind.InvalidResponse;

        /// <summary>
        /// One voice per language. The first voice that can speak the language (this speaker's, then the story's, then Hypa's
        /// Auto) is kept for every later line, and a network hiccup retries that same voice instead of falling through to another:
        /// on the headset a failed request once switched Ama to a different voice mid-answer.
        /// </summary>
        async Task<AudioClip> FetchAsync(string text, string label, VoiceOption[] voices)
        {
            var order = new List<VoiceOption>();
            if (_chosen.TryGetValue(label, out var chosen)) order.Add(chosen);
            order.AddRange(voices.Where(v => v != chosen));
            if (chosen != Auto) order.Add(Auto);
            HypaException last = null;
            foreach (var voice in order)
            {
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        var speech = await _mcp.TextToSpeechAsync(text, label, _life.Token, NullIfEmpty(voice.Voice), NullIfEmpty(voice.Model), InlineAudio);
                        if (!_chosen.TryGetValue(label, out var had) || had != voice)
                        {
                            _chosen[label] = voice;
                            Debug.Log($"Hypa Stories: {(voice == Auto ? "Hypa's Auto voice" : "voice " + voice)} speaks {label} from now on.");
                        }
                        return await HypaAudioPlayer.LoadClipAsync(speech, _life.Token); // main thread: we were called from it
                    }
                    catch (HypaException e) when (Transient(e) && attempt < 2)
                    {
                        Warn($"voice {voice} hiccup for {label} ({e.Message}); trying the same voice again.");
                        await Task.Delay(400 * (attempt + 1), _life.Token);
                    }
                    catch (HypaException e) when (Transient(e))
                    {
                        throw; // still failing: this line plays silently rather than in another voice
                    }
                    catch (HypaException e) when (e.Kind != HypaErrorKind.MissingKey && e.Kind != HypaErrorKind.Unauthorized)
                    {
                        last = e;
                        if (_chosen.TryGetValue(label, out var c) && c == voice) _chosen.Remove(label);
                        Warn($"voice {voice} can't speak {label} ({e.Message}); trying the next voice.");
                        break;
                    }
                }
            }
            throw last ?? new HypaException(HypaErrorKind.BadRequest, "No voice could speak " + label);
        }

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

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
            catch (Exception e)
            {
                Warn($"no voice could speak {language.HypaLabel}, lines play silently: {e.Message}");
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
}
