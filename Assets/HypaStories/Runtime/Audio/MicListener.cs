using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace HypaStories
{
    /// <summary>
    /// Microphone + speech-to-text (the prototype's listen()): records at 16 kHz until the user goes quiet
    /// (<see cref="SilenceDetector"/>) or <see cref="IListenSession.Stop"/> is called, then sends the WAV to Hypa speech_to_text.
    /// If nothing louder than the noise floor was heard, no call is made and the transcript is empty ("could you say that again?").
    /// </summary>
    public sealed class MicListener : MonoBehaviour, IListener
    {
        const int Rate = 16000, ClipSeconds = 14;

        /// <summary>Speech to text. Default: Hypa's speech_to_text tool with the story language as the source.</summary>
        public Func<byte[], StoryLanguage, CancellationToken, Task<string>> Transcribe;

        readonly CancellationTokenSource _life = new CancellationTokenSource();
        Session _active;

        /// <summary>Ask for the microphone early (entering a story), so the first question doesn't stall on the prompt.</summary>
        public static void RequestPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) Permission.RequestUserPermission(Permission.Microphone);
#endif
        }

        public IListenSession Listen(StoryLanguage language, Action<float> onLevel)
        {
            if (!HypaConfig.Current.HasKey)
                throw new HypaException(HypaErrorKind.MissingKey, "speech_to_text needs a Hypa API key.");
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                throw new InvalidOperationException("Microphone permission not granted.");
            }
#endif
            if (Microphone.devices.Length == 0) throw new InvalidOperationException("No microphone.");
            _active?.Cancel();
            var transcribe = Transcribe ?? ((wav, lang, ct) => new HypaMcpClient().SpeechToTextAsync(wav, lang.HypaLabel, ct));
            var clip = Microphone.Start(null, true, ClipSeconds, Rate);
            if (clip == null) throw new InvalidOperationException("Microphone could not start.");
            return _active = new Session(clip, language, onLevel, transcribe, _life.Token);
        }

        void Update() => _active?.Tick(Time.unscaledDeltaTime);

        void OnDestroy()
        {
            _active?.Cancel();
            _life.Cancel();
            _life.Dispose();
        }

        sealed class Session : IListenSession
        {
            readonly AudioClip _clip;
            readonly StoryLanguage _lang;
            readonly Action<float> _onLevel;
            readonly Func<byte[], StoryLanguage, CancellationToken, Task<string>> _transcribe;
            readonly CancellationToken _ct;
            readonly TaskCompletionSource<ListenResult> _result = new TaskCompletionSource<ListenResult>();
            readonly SilenceDetector _silence = new SilenceDetector();
            readonly List<float> _samples = new List<float>(Rate * 6);
            int _read;
            bool _done;

            public Task<ListenResult> Result => _result.Task;

            public Session(AudioClip clip, StoryLanguage lang, Action<float> onLevel,
                Func<byte[], StoryLanguage, CancellationToken, Task<string>> transcribe, CancellationToken ct)
            {
                _clip = clip; _lang = lang; _onLevel = onLevel; _transcribe = transcribe; _ct = ct;
            }

            static double Now => Time.realtimeSinceStartupAsDouble * 1000.0;

            public void Tick(float dt)
            {
                if (_done) return;
                var pos = Microphone.GetPosition(null);
                if (pos < 0) return;
                var n = (pos - _read + _clip.samples) % _clip.samples;
                if (n == 0) return;
                var buf = new float[n]; // GetData reads exactly buf.Length samples
                _clip.GetData(buf, _read); // wraps around the looping clip
                _read = pos;
                var sum = 0f;
                for (var i = 0; i < n; i++) { _samples.Add(buf[i]); sum += buf[i] * buf[i]; }
                var rms = Mathf.Sqrt(sum / n);
                _onLevel?.Invoke(Mathf.Min(1f, rms * 6f));
                if (_silence.Update(rms, n * 1000f / Rate)) Stop();
            }

            public void Stop()
            {
                if (_done) return;
                _done = true;
                var tEnd = Now;
                Microphone.End(null);
                _onLevel?.Invoke(0f);
                if (!_silence.Spoke) { _result.TrySetResult(new ListenResult { TEnd = tEnd, TTranscript = tEnd }); return; }
                _ = TranscribeAsync(tEnd);
            }

            async Task TranscribeAsync(double tEnd)
            {
                try
                {
                    var wav = WavEncoder.Encode16(_samples.ToArray(), _samples.Count, 1, Rate);
                    var text = await _transcribe(wav, _lang, _ct);
                    _result.TrySetResult(new ListenResult { Transcript = text ?? "", TEnd = tEnd, TTranscript = Now, Spoke = true });
                }
                catch (Exception e) { _result.TrySetException(e); }
            }

            /// <summary>Drops the recording. Result never completes (a picked question has already won).</summary>
            public void Cancel()
            {
                if (_done) return;
                _done = true;
                Microphone.End(null);
                _onLevel?.Invoke(0f);
            }
        }
    }
}
