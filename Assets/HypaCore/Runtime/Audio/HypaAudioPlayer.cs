using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace HypaCore
{
    /// <summary>
    /// Plays Hypa TTS. Queues clips, stops with a fade, and reports <see cref="Volume"/> (RMS, 0..1) every frame for
    /// lip sync. Audio links are fetched off the main thread; decoding to an AudioClip happens on the main thread.
    /// WAV is decoded in memory; MP3/OGG go through UnityWebRequestMultimedia via a temp file (audio only, never frames).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class HypaAudioPlayer : MonoBehaviour
    {
        static HypaAudioPlayer _instance;
        public static HypaAudioPlayer Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("Hypa Audio");
                DontDestroyOnLoad(go);
                return _instance = go.AddComponent<HypaAudioPlayer>();
            }
        }

        readonly Queue<AudioClip> _queue = new Queue<AudioClip>();
        readonly float[] _buf = new float[256];
        AudioSource _src;
        Coroutine _fade;

        /// <summary>Current output loudness (RMS of the last ~256 samples), 0..1. Drive mouth shapes from this.</summary>
        public float Volume { get; private set; }
        public bool IsPlaying => _src != null && _src.isPlaying;
        public int Queued => _queue.Count;
        public event Action<AudioClip> ClipStarted;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _src = GetComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
        }

        void Update()
        {
            if (_fade == null && !_src.isPlaying && _queue.Count > 0)
            {
                _src.clip = _queue.Dequeue();
                _src.volume = 1f;
                _src.Play();
                ClipStarted?.Invoke(_src.clip);
            }
            if (_src.isPlaying)
            {
                _src.GetOutputData(_buf, 0);
                var sum = 0f;
                for (var i = 0; i < _buf.Length; i++) sum += _buf[i] * _buf[i];
                Volume = Mathf.Sqrt(sum / _buf.Length);
            }
            else Volume = 0f;
        }

        /// <summary>Fetch, decode and add to the end of the queue.</summary>
        public async Task EnqueueAsync(HypaSpeech speech, CancellationToken ct)
        {
            var clip = await LoadClipAsync(speech, ct);
            if (clip != null) _queue.Enqueue(clip);
        }

        /// <summary>Stop what's playing (with a short fade), clear the queue, then play this.</summary>
        public async Task PlayNowAsync(HypaSpeech speech, CancellationToken ct)
        {
            var clip = await LoadClipAsync(speech, ct);
            Stop(0.08f);
            if (clip != null) _queue.Enqueue(clip);
        }

        public void Enqueue(AudioClip clip) { if (clip != null) _queue.Enqueue(clip); }

        /// <summary>Clear the queue and fade the current clip out over <paramref name="fadeSeconds"/>.</summary>
        public void Stop(float fadeSeconds = 0.25f)
        {
            _queue.Clear();
            if (!_src.isPlaying) return;
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeOut(fadeSeconds));
        }

        IEnumerator FadeOut(float seconds)
        {
            var start = _src.volume;
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _src.volume = Mathf.Lerp(start, 0f, t / seconds);
                yield return null;
            }
            _src.Stop();
            _src.volume = 1f;
            _fade = null;
        }

        // ---------- Loading ----------

        public static async Task<AudioClip> LoadClipAsync(HypaSpeech s, CancellationToken ct)
        {
            if (s == null) return null;
            byte[] bytes = s.AudioBytes;
            if (bytes == null && !string.IsNullOrEmpty(s.AudioBase64)) bytes = Convert.FromBase64String(s.AudioBase64);
            if (bytes == null && !string.IsNullOrEmpty(s.AudioUrl)) bytes = await HypaHttp.GetBytesAsync(s.AudioUrl, ct); // off main thread
            if (bytes == null || bytes.Length == 0) return null;

            if (WavDecoder.TryDecode(bytes, out var samples, out var ch, out var rate))
            {
                var clip = AudioClip.Create("hypa-tts", samples.Length / ch, ch, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            return await DecodeCompressedAsync(bytes, s.Mime, ct);
        }

        static async Task<AudioClip> DecodeCompressedAsync(byte[] bytes, string mime, CancellationToken ct)
        {
            mime = mime ?? "";
            var type = mime.Contains("ogg") ? AudioType.OGGVORBIS : AudioType.MPEG;
            var path = Path.Combine(Application.temporaryCachePath, "hypa_tts_" + Guid.NewGuid().ToString("N") + (type == AudioType.MPEG ? ".mp3" : ".ogg"));
            File.WriteAllBytes(path, bytes);
            try
            {
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file://" + path, type))
                {
                    var op = req.SendWebRequest();
                    while (!op.isDone) { ct.ThrowIfCancellationRequested(); await Task.Yield(); }
                    if (req.result != UnityWebRequest.Result.Success)
                        throw new HypaException(HypaErrorKind.InvalidResponse, "Could not decode audio (" + mime + "): " + req.error);
                    return DownloadHandlerAudioClip.GetContent(req);
                }
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
