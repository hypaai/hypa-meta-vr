using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Records from the system's default input unless that is a virtual device (see <see cref="Device"/>).
    /// </summary>
    public sealed class MicListener : MonoBehaviour, IListener, IPartialListener
    {
        Action<string> _partial;
        /// <summary>With a partial handler set the recording runs until Stop (up to 30 s) instead of ending on silence.</summary>
        bool Manual => _partial != null;
        /// <summary>Called with what has been heard so far while recording (a speech_to_text call every ~1.5 s), so it can be shown as you speak.</summary>
        public void SetPartialHandler(Action<string> handler) => _partial = handler;

        const int Rate = 16000, ClipSeconds = 14, ManualSeconds = 30;

        /// <summary>Virtual inputs (loopback, voice changers, meeting apps) give silence unless something is routed into them.</summary>
        static readonly string[] VirtualInputs = { "virtual", "blackhole", "loopback", "soundflower", "aggregate", "zoom", "teams", "altered", "krisp" };

        /// <summary>Speech to text. Default: Hypa's speech_to_text tool with the story language as the source.</summary>
        public Func<byte[], StoryLanguage, CancellationToken, Task<string>> Transcribe;

        readonly CancellationTokenSource _life = new CancellationTokenSource();
        Session _active;

        /// <summary>Ask for the microphone early (entering a story), so the first question doesn't stall on the prompt.</summary>
        public static void RequestPermission()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            // macOS only shows its microphone prompt (and lists the app in Privacy > Microphone) once this is asked for
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone)) Application.RequestUserAuthorization(UserAuthorization.Microphone);
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) Permission.RequestUserPermission(Permission.Microphone);
#endif
        }

        /// <summary>
        /// The microphone to record from: the first real one, else the default (null). Unity can't tell which input the system
        /// uses by default, so a virtual one set as the default (common on Macs) would record silence.
        /// </summary>
        public static string Device(string[] devices)
            => devices.FirstOrDefault(d => !VirtualInputs.Any(v => d.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0));

        public IListenSession Listen(StoryLanguage language, Action<float> onLevel)
        {
            if (!HypaConfig.Current.HasKey)
                throw new HypaException(HypaErrorKind.MissingKey, "speech_to_text needs a Hypa API key.");
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                throw new InvalidOperationException("Allow the microphone when asked, then tap the mic again.");
            }
#endif
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Application.RequestUserAuthorization(UserAuthorization.Microphone);
#if UNITY_EDITOR
                // The Mac Editor often can't get the microphone (macOS never prompts for Unity). So the flow can still be tried,
                // the Editor answers with a test phrase instead. A real microphone is used as soon as macOS allows it.
                Debug.LogWarning("Hypa Stories: macOS is not letting Unity use the microphone; using a test phrase. Launch Unity from Terminal to use your voice.");
                return new TestSession(_partial);
#else
                throw new InvalidOperationException("Allow the microphone in the macOS prompt, then tap the mic again.");
#endif
            }
#endif
            if (Microphone.devices.Length == 0) throw new InvalidOperationException("No microphone.");
            _active?.Cancel();
            var transcribe = Transcribe ?? ((wav, lang, ct) => new HypaMcpClient().SpeechToTextAsync(wav, lang.HypaLabel, ct));
            var device = Device(Microphone.devices);
            var clip = Microphone.Start(device, true, Manual ? ManualSeconds : ClipSeconds, Rate);
            if (clip == null) throw new InvalidOperationException("Microphone could not start.");
            return _active = new Session(device, clip, language, onLevel, transcribe, _life.Token, _partial, Manual);
        }

        void Update() => _active?.Tick(Time.unscaledDeltaTime);

        void OnDestroy()
        {
            _active?.Cancel();
            _life.Cancel();
            _life.Dispose();
        }

#if UNITY_EDITOR
        static int _testPhrase;
        static readonly string[] TestPhrases = { "hello", "Good morning, how are you today?", "water", "Thank you very much." };

        /// <summary>Editor only: pretends to listen, then "hears" a test phrase, typed in word by word.</summary>
        sealed class TestSession : IListenSession
        {
            readonly TaskCompletionSource<ListenResult> _r = new TaskCompletionSource<ListenResult>();
            public Task<ListenResult> Result => _r.Task;
            bool _stopped;
            public TestSession(Action<string> partial) { _ = Run(partial); }
            async Task Run(Action<string> partial)
            {
                var phrase = TestPhrases[_testPhrase++ % TestPhrases.Length];
                var words = phrase.Split(' ');
                for (var i = 1; i <= words.Length && !_stopped; i++)
                {
                    await Task.Delay(450);
                    if (_stopped) break;
                    partial?.Invoke(string.Join(" ", words, 0, i));
                }
                _r.TrySetResult(new ListenResult { Transcript = phrase, Spoke = true, Typed = true, Peak = 1f });
            }
            public void Stop() { _stopped = true; }
            public void Cancel() { _stopped = true; }
        }
#endif

        sealed class Session : IListenSession
        {
            readonly string _device;
            readonly AudioClip _clip;
            readonly StoryLanguage _lang;
            readonly Action<float> _onLevel;
            readonly Func<byte[], StoryLanguage, CancellationToken, Task<string>> _transcribe;
            readonly CancellationToken _ct;
            readonly TaskCompletionSource<ListenResult> _result = new TaskCompletionSource<ListenResult>();
            readonly SilenceDetector _silence = new SilenceDetector();
            readonly List<float> _samples = new List<float>(Rate * 6);
            int _read;
            float _peak;
            bool _done;

            public Task<ListenResult> Result => _result.Task;

            public Session(string device, AudioClip clip, StoryLanguage lang, Action<float> onLevel,
                Func<byte[], StoryLanguage, CancellationToken, Task<string>> transcribe, CancellationToken ct, Action<string> partial, bool manual)
            {
                if (manual) { _silence.SilenceMs = float.MaxValue; _silence.MaxMs = ManualSeconds * 1000f - 500f; }
                _device = device; _clip = clip; _lang = lang; _onLevel = onLevel; _transcribe = transcribe; _ct = ct; _partialCb = partial;
                if (manual && partial != null && HypaConfig.Current.HasKey)
                {
                    // true streaming: words arrive as they are said (Hypa's live WebSocket). If it can't connect, the
                    // periodic speech_to_text calls below take over.
                    _live = new HypaLiveAsr();
                    _ = _live.StartAsync();
                }
            }

            HypaLiveAsr _live;
            bool Streaming => _live != null && !_live.Failed;

            void Pump()
            {
                if (_live == null) return;
                while (_live.TryDequeue(out var u))
                {
                    if (!_done && !string.IsNullOrWhiteSpace(u.Text) && !u.Final) _partialCb?.Invoke(u.Text.Trim());
                }
            }

            readonly Action<string> _partialCb;
            float _sincePartial;
            bool _partialBusy;

            async Task PartialAsync()
            {
                _partialBusy = true;
                try
                {
                    var snap = _samples.ToArray();
                    var wav = WavEncoder.Encode16(snap, snap.Length, 1, Rate);
                    var text = await _transcribe(wav, _lang, _ct);
                    if (!_done && !string.IsNullOrWhiteSpace(text)) _partialCb(text.Trim());
                }
                catch (Exception) { } // the final transcription reports any real error
                finally { _partialBusy = false; _sincePartial = 0f; }
            }

            string Name => _device ?? "default input";

            static double Now => Time.realtimeSinceStartupAsDouble * 1000.0;

            public void Tick(float dt)
            {
                Pump();
                if (_done) return;
                var pos = Microphone.GetPosition(_device);
                if (pos < 0) return;
                var n = (pos - _read + _clip.samples) % _clip.samples;
                if (n == 0) return;
                var buf = new float[n]; // GetData reads exactly buf.Length samples
                _clip.GetData(buf, _read); // wraps around the looping clip
                _read = pos;
                var sum = 0f;
                for (var i = 0; i < n; i++) { _samples.Add(buf[i]); sum += buf[i] * buf[i]; }
                if (_live != null && !_live.Failed) _live.Send(buf, n);
                var rms = Mathf.Sqrt(sum / n);
                _peak = Mathf.Max(_peak, rms);
                _onLevel?.Invoke(Mathf.Min(1f, rms * 6f));
                if (_silence.Update(rms, n * 1000f / Rate)) { Stop(); return; }
                _sincePartial += dt;
                if (_partialCb != null && !Streaming && !_partialBusy && _silence.Spoke && _sincePartial >= 1.5f && _samples.Count > Rate * 0.8f) _ = PartialAsync();
            }

            public void Stop()
            {
                if (_done) return;
                _done = true;
                var tEnd = Now;
                Microphone.End(_device);
                _onLevel?.Invoke(0f);
                Debug.Log($"Hypa Stories: mic '{Name}' heard {_samples.Count / (float)Rate:0.0}s, peak level {_peak:0.000} (speech threshold {_silence.Threshold})");
                // a quiet voice may never cross the speech threshold: still transcribe when something other than silence arrived
                if (!_silence.Spoke && _peak < 0.004f)
                {
                    Debug.LogWarning($"Hypa Stories: heard nothing from {Name} ({_samples.Count / (float)Rate:0.0} s, loudest {_peak:0.000}, " +
                                     $"speech starts at {_silence.Threshold}). Is it the right input, and does Unity have microphone access?");
                    _result.TrySetResult(new ListenResult { TEnd = tEnd, TTranscript = tEnd, Peak = _peak });
                    return;
                }
                _ = TranscribeAsync(tEnd);
            }


            async Task TranscribeAsync(double tEnd)
            {
                try
                {
                    // streaming: the server's final text is already there, no second call needed
                    if (_live != null && _live.Connected && !_live.Failed)
                    {
                        _live.Finish();
                        var until = Time.realtimeSinceStartup + 6f;
                        while (!_live.Done && !_live.Failed && Time.realtimeSinceStartup < until && !_ct.IsCancellationRequested) await Task.Delay(60);
                        var heard = _live.Text;
                        _live.Dispose(); _live = null;
                        if (!string.IsNullOrWhiteSpace(heard))
                        {
                            _result.TrySetResult(new ListenResult { Transcript = heard.Trim(), TEnd = tEnd, TTranscript = Now, Spoke = true, Peak = _peak });
                            return;
                        }
                    }
                    else if (_live != null) { _live.Dispose(); _live = null; }
                    var wav = WavEncoder.Encode16(_samples.ToArray(), _samples.Count, 1, Rate);
                    var text = await _transcribe(wav, _lang, _ct);
                    Debug.Log($"Hypa Stories: heard “{text}” from {Name} ({_samples.Count / (float)Rate:0.0} s, {Now - tEnd:0} ms to transcribe)");
                    _result.TrySetResult(new ListenResult { Transcript = text ?? "", TEnd = tEnd, TTranscript = Now, Spoke = true, Peak = _peak });
                }
                catch (Exception e) { _result.TrySetException(e); }
            }

            /// <summary>Drops the recording. Result never completes (a picked question has already won).</summary>
            public void Cancel()
            {
                if (_done) return;
                _done = true;
                Microphone.End(_device);
                _onLevel?.Invoke(0f);
                _live?.Dispose(); _live = null;
            }
        }
    }
}
