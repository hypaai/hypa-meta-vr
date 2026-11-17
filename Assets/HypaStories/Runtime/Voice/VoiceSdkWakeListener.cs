using System;
using HypaCore;
using Meta.WitAi.Configuration;
using Meta.WitAi.Data;
using Meta.WitAi.Data.Configuration;
using Meta.WitAi.Lib;
using Oculus.Voice.Dictation;
using Oculus.Voice.Dictation.Configuration;
using UnityEngine;

namespace HypaStories.Voice
{
    /// <summary>
    /// The locals' wake word on the Meta Voice SDK (ticket 3I, stretch): its dictation (<see cref="AppDictationExperience"/>) runs
    /// while a <see cref="Local"/> is near and idle, and each finished phrase goes to <see cref="WakeWord"/> to check for a name.
    /// Dictation streams the microphone to Wit.ai, so it needs a Wit client token: WIT_CLIENT_TOKEN in the git-ignored .env (or
    /// "witToken" in hypa.local.json), read by <see cref="HypaConfig"/>, never stored in an asset. Without one there's no wake
    /// word and raise-a-hand / Talk work as before. The question itself still goes through Hypa speech_to_text.
    /// </summary>
    public sealed class VoiceSdkWakeListener : MonoBehaviour, IWakeListener
    {
        const int MaxErrors = 3;
        const float RetryAfter = 1.5f;

        AppDictationExperience _dictation;
        string _token;
        bool _initialized, _want;
        int _errors;
        float _retryAt, _micCheckAt;
        string _problem;

        public bool Available => _token != null && _errors < MaxErrors;
        public string Problem => _problem;
        public bool Listening => _want;
        public event Action<string> Heard;
        public event Action<string> HeardSoFar;
        public event Action<float> Level;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (WakeWord.Listener != null) return;
            var go = new GameObject("Hypa Wake Word");
            DontDestroyOnLoad(go);
            var listener = go.AddComponent<VoiceSdkWakeListener>();
            var token = HypaConfig.Current.WitToken;
            if (string.IsNullOrEmpty(token)) listener._problem = "no WIT_CLIENT_TOKEN in .env";
            else listener._token = token;
            WakeWord.Listener = listener;
        }

        /// <summary>
        /// The dictation is made the first time a local wants it, not at startup: its microphone gives up if it finds no device within
        /// 5 s of wall-clock time, and loading the first scene takes longer than that (seen in the editor: "No mics found after 5 seconds",
        /// then every request timed out with no audio).
        /// </summary>
        void CreateDictation()
        {
            var host = new GameObject("Dictation");
            host.SetActive(false); // the dictation component starts in OnEnable: configure it first
            host.transform.SetParent(transform, false);

            var config = ScriptableObject.CreateInstance<WitConfiguration>();
            config.name = "Hypa Wake Word (runtime, not saved)";
            config.SetClientAccessToken(_token);

            _dictation = host.AddComponent<AppDictationExperience>();
            _dictation.RuntimeDictationConfiguration = new WitDictationRuntimeConfiguration
            {
                witConfiguration = config,
                // Keep going phrase after phrase while active (the SDK reactivates after each one).
                dictationConfiguration = new DictationConfiguration { multiPhrase = true },
            };
            _dictation.OnInitialized += () => { _initialized = true; if (_want) Activate(); };
            _dictation.DictationEvents.OnFullTranscription.AddListener(OnPhrase);
            _dictation.DictationEvents.OnPartialTranscription.AddListener(t => { if (_want && !string.IsNullOrWhiteSpace(t)) HeardSoFar?.Invoke(t); });
            _dictation.DictationEvents.OnMicLevelChanged.AddListener(v => { if (_want) Level?.Invoke(Mathf.Clamp01(v * 4f)); });
            _dictation.DictationEvents.OnError.AddListener(OnError);
            host.SetActive(true);
        }

        void OnPhrase(string text)
        {
            _errors = 0;
            if (!_want || string.IsNullOrWhiteSpace(text)) return;
            Heard?.Invoke(text);
        }

        void OnError(string error, string message)
        {
            // A request with nobody speaking times out after 10 s (seen in the editor): that's quiet, not a fault. Start again.
            if ((message ?? "").IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0) return;
            _errors++;
            _retryAt = Time.unscaledTime + RetryAfter * _errors;
            if (_errors < MaxErrors) { Debug.Log($"Hypa Stories: wake word paused ({error}: {message}); retrying."); return; }
            _problem = $"{error}: {message}";
            Debug.LogWarning($"Hypa Stories: wake word off after {MaxErrors} errors ({_problem}). Raise a hand or pinch Talk instead.");
            End();
        }

        public void Begin()
        {
            if (!Available) return;
            _want = true;
            if (_dictation == null) CreateDictation();
            ReviveMic();
            Activate();
        }

        public void End()
        {
            _want = false;
            if (_dictation != null && _initialized && (_dictation.Active || _dictation.MicActive)) _dictation.Cancel(); // lets go of the mic now
        }

        void Activate()
        {
            if (!_initialized || !_want || _dictation == null || _dictation.Active || Time.unscaledTime < _retryAt) return;
            _retryAt = Time.unscaledTime + 0.5f; // not every frame while it starts up
            _dictation.Activate();
        }

        // Safety net: if dictation stops on its own (timeout, network) while wanted, start it again.
        void Update()
        {
            if (!_want || !Available) return;
            if (Time.unscaledTime >= _micCheckAt) { _micCheckAt = Time.unscaledTime + 1f; ReviveMic(); }
            Activate();
        }

        /// <summary>
        /// The Voice SDK keeps its microphone running between requests. A local's own listener (MicListener) records the question
        /// from the same device and ends it when done, which stops the SDK's recording underneath it while the SDK still thinks it's
        /// on (seen in the editor: no phrases after the first answer). Restart it when that happened.
        /// </summary>
        void ReviveMic()
        {
            if (_dictation == null || !(AudioBuffer.Instance?.MicInput is Mic mic) || !mic.IsRecording) return;
            var device = mic.CurrentDeviceName;
            if (string.IsNullOrEmpty(device) || Microphone.IsRecording(device)) return;
            Debug.Log("Hypa Stories: wake word: restarting the Voice SDK microphone (a local's listener had closed it).");
            mic.ChangeMicDevice(mic.CurrentDeviceIndex);
        }
    }
}
