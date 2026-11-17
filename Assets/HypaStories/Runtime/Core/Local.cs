using System.Collections.Generic;
using System.Linq;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// A local you can talk to (ticket 3I). Drop it on a character and give it a <see cref="LocalPersona"/>; that's all.
    /// Come close and they greet you. Raise a hand (or pinch Talk) while near them and looking at them → they listen (Hypa
    /// speech_to_text) → think (Hypa chat in character, see <see cref="LocalBrains"/>) → answer sentence by sentence (Hypa
    /// text_to_speech) with lip sync and subtitles, looking at you. Walk away and the visit ends.
    /// Optional: <see cref="Figure"/> (a Storyteller-style figure: lip sync and head turn come free), or <see cref="Head"/>
    /// (turned to you) and <see cref="Mouth"/> (where the voice comes from). Editor: T = talk to the local you're facing.
    /// Wake word (with the Meta Voice SDK and a Wit token, see <see cref="WakeWord"/>): near them, say their name ("Ama?") to
    /// talk, or ask straight away ("Ama, what do you weave?").
    /// </summary>
    public sealed class Local : MonoBehaviour
    {
        [Tooltip("Who this local is.")]
        public LocalPersona Persona;
        [Tooltip("Assets/HypaStories/StoryAssets.asset (fonts and icons for the panel).")]
        public StoryAssets Assets;
        [Tooltip("A seated figure (Storyteller): drives its lip sync and turns its head to you. Optional.")]
        public Storyteller Figure;
        [Tooltip("Turned to face you while talking, when there's no Figure. Optional (defaults to nothing turning).")]
        public Transform Head;
        [Tooltip("Where the voice comes from. Optional (Figure's mouth, else Head, else this object).")]
        public Transform Mouth;
        [Tooltip("Hypa provider/model for this local, e.g. meta/muse-spark-1.3; empty = the Local Brain setting (Hypa > Stories > Local Brain).")]
        public string Brain = "";
        [Tooltip("Offline answers' mode for testing: stub, slow (exercises the second filler) or offline (every answer fails).")]
        public string StubMode = "stub";
        [Tooltip("Hypa speech-to-text model. gpt-4o-mini-transcribe took ~1.5 s (the default ~3.5 s); empty = Hypa picks.")]
        public string TranscribeModel = "openai/gpt-4o-mini-transcribe";
        [Tooltip("Hypa text-to-speech model; gpt-4o-mini-tts with inline audio took ~2.2 s for a sentence (the default ~3.9 s). Empty = Hypa picks.")]
        public string SpeechModel = "openai/gpt-4o-mini-tts";
        [Tooltip("How close you must be to talk, metres. 4.5: the camp's test local stands ~2.9 m from the seat, and on the headset a step or a lean took you past 3 m (she then never greeted or heard you).")]
        public float TalkDistance = 4.5f;
        [Tooltip("Height of the panel above this object's origin, metres.")]
        public float PanelHeight = 1.9f;
        public bool GreetOnApproach = true;
        [Tooltip("Say their name to talk to them (Meta Voice SDK + WIT_CLIENT_TOKEN). Raise a hand and Talk always work too.")]
        public bool WakeWordEnabled = true;
        [Tooltip("They greet you once you're within TalkDistance and looking within this many degrees of them.")]
        public float GreetAngle = 25f;

        /// <summary>Every local in the loaded places.</summary>
        public static readonly List<Local> All = new List<Local>();
        /// <summary>The local you're near and looking at (gets the raised hand), or null. The campfire storyteller yields to it.</summary>
        public static Local Focused { get; private set; }

        public LocalTalk Talk { get; private set; }

        StoryVoice _voice;
        MicListener _mic;
        LocalView _view;
        AudioSource _cues;
        AudioClip _chime, _tap;
        KitPanel _ring;
        AskState _was;
        // While listening, the world's sound is turned down: the camp's wind and fire from the speakers reach the microphone
        // (measured 0.03-0.05 RMS, over the 0.02 speech threshold), so it never heard you stop and transcribed the ambience.
        const float DuckTo = 0.15f, DuckAfter = 0.3f;
        float _listenedFor, _volumeBefore = -1f;
        readonly RaiseHandGesture _raise = new RaiseHandGesture();
        bool _greeted, _holdToTalk, _engaged, _ready, _wantWake;
        static int _wokeFrame = -1;
        float _headYaw;
        Quaternion _headRest;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        /// <summary>The world's sound back to where it was (also if the visit ends mid-question).</summary>
        void Unduck()
        {
            if (_volumeBefore < 0f) return;
            if (Mathf.Approximately(AudioListener.volume, Mathf.Min(_volumeBefore, DuckTo))) AudioListener.volume = _volumeBefore; // unless muted meanwhile
            _volumeBefore = -1f;
        }

        void OnDisable()
        {
            SetWantWake(false);
            if (WakeWord.Listener != null)
            {
                WakeWord.Listener.Heard -= OnHeard;
                WakeWord.Listener.HeardSoFar -= OnHeardSoFar;
                WakeWord.Listener.Level -= OnWakeLevel;
            }
            Unduck();
            All.Remove(this);
            if (Focused == this) Focused = null;
            Talk?.Cancel();
        }

        void Start()
        {
            if (Persona == null) { Debug.LogError($"Hypa Stories: {name} has a Local with no persona."); enabled = false; return; }
            var problem = Persona.Validate();
            if (problem != null) Debug.LogWarning("Hypa Stories: " + problem);
            if (!Kit.Ready)
            {
                if (Assets == null) { Debug.LogError($"Hypa Stories: {name}'s Local needs StoryAssets for its panel."); enabled = false; return; }
                Kit.Init(Assets);
            }
            PinchPointer.EnsureExists();
            MicListener.RequestPermission();

            _voice = gameObject.AddComponent<StoryVoice>();
            _voice.Init(Mouth != null ? Mouth : Figure != null && Figure.Mouth != null ? Figure.Mouth : Head != null ? Head : transform);
            _voice.Voice = Persona.Voice;
            _voice.SpeechModel = SpeechModel;
            _voice.InlineAudio = true; // no second download for the audio
            _mic = gameObject.AddComponent<MicListener>();
            _mic.Transcribe = TranscribeAsync;
            _mic.IgnoreFirstMs = 350f; // the listening cue plays from the speakers right then
            _mic.NoSpeechMs = 7000f;   // nobody spoke: ask again after 7 s, not 12
            _mic.SilenceMs = 1000f;    // you've finished after 1 s of quiet, not 1.3
            Debug.Log($"Hypa Stories: {Persona.DisplayName} listens on " +
                      (Microphone.devices.Length > 0 ? $"'{MicListener.Device(Microphone.devices) ?? "the default input"}'" : "no microphone (pick a suggested question)") +
                      (Application.isEditor ? ". In the editor, macOS must allow Unity to use the microphone (System Settings > Privacy > Microphone)." : "."));

            // Cues: a short soft ding when they start listening, a lower one when they've got your question. Short and quiet on
            // purpose: the microphone is open right after, and anything loud from the speakers would be heard as you speaking.
            _cues = gameObject.AddComponent<AudioSource>();
            _cues.playOnAwake = false; _cues.spatialBlend = 0f; _cues.volume = 0.5f;
            _chime = ProceduralClips.Clip("listen", ProceduralClips.TapSamples(1320f));
            _tap = ProceduralClips.Clip("heard", ProceduralClips.TapSamples(660f));

            // A soft ember ring on the ground under them, lit while you can talk to them.
            _ring = Kit.Panel(transform, "Talk Ring", new Vector3(0f, 0.02f, 0f), new Vector2(1.1f, 1.1f), 0.55f,
                new Color(1f, 0.66f, 0.32f, 0.10f), new Color(1f, 0.5f, 0.2f, 0.04f), KitTheme.Ember, 9, 0f);
            _ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _ring.BorderWidth = 0.01f;
            _ring.Alpha = 0f;
            if (Head != null) _headRest = Head.localRotation;

            var lang = StoryLanguages.Current;
            var lines = LocalLines.For(lang.Code);
            _view = new LocalView(transform, Persona, lines.Prompt, TalkTap, TalkHold, q => Talk.SubmitText(q));
            _view.Root.gameObject.SetActive(false);
            var brain = LocalBrains.Create(Brain);
            if (brain is StubLocalBrain stub) stub.Mode = string.IsNullOrEmpty(StubMode) ? "stub" : StubMode;
            Talk = new LocalTalk(_voice, _mic, brain, _view, () => StoryLanguages.Current, () => Persona, on => _engaged = on);
            Talk.Logged += e => Debug.Log($"Hypa Stories local {Persona.DisplayName} {e}");
            _view.SetState(AskState.Idle, lines.Prompt);
            _ = ApplyLanguageAsync(lang);
            if (WakeWordEnabled)
            {
                var l = WakeWord.Listener;
                if (l == null) Debug.Log($"Hypa Stories: no wake word for {Persona.DisplayName} in this build; raise a hand or pinch Talk.");
                else
                {
                    l.Heard += OnHeard;
                    l.HeardSoFar += OnHeardSoFar;
                    l.Level += OnWakeLevel;
                    Debug.Log(l.Available
                        ? $"Hypa Stories: say \"{Persona.DisplayName}\" near them to talk (wake word)."
                        : $"Hypa Stories: wake word off ({l.Problem}); raise a hand or pinch Talk.");
                }
            }
            _ready = true;
            if (Debug.isDebugBuild) StartCoroutine(DevSteps());
        }

        /// <summary>
        /// Development builds: test on the headset without hands. adb ... -e hypa_local "tap" presses Talk (as a pinch does),
        /// "ask:Who is Kiko?" asks a typed question, "lang:en" switches the story language; steps separated by ';', each after the previous one finishes (a few seconds).
        /// </summary>
        System.Collections.IEnumerator DevSteps()
        {
            var steps = HypaCore.DevTools.Extra("hypa_local");
            if (string.IsNullOrWhiteSpace(steps)) yield break;
            yield return new WaitForSeconds(8f); // the greeting and the first prefetches
            foreach (var raw in steps.Split(';'))
            {
                var step = raw.Trim();
                while (Talk.State != AskState.Idle) yield return null;
                yield return new WaitForSeconds(1.5f);
                Debug.Log($"Hypa dev: {Persona.DisplayName} step '{step}'");
                if (step.StartsWith("lang:")) { var l = StoryLanguages.Find(step.Substring(5)); if (l != null) StoryLanguages.Current = l; }
                else if (step == "tap") TalkTap();
                else if (step.StartsWith("ask:")) _ = Talk.AskAsync(step.Substring(4));
                yield return new WaitForSeconds(1f);
            }
        }

        float _devLogAt;
        void DevStatus(float distance, float angle, bool near)
        {
            if (!Debug.isDebugBuild || Time.unscaledTime < _devLogAt) return;
            _devLogAt = Time.unscaledTime + 3f;
            Debug.Log($"Hypa dev: {Persona.DisplayName} dist {distance:0.00} m angle {angle:0}° near={near} focused={Focused == this} " +
                      $"state={Talk.State} greeted={_greeted} wake={(WakeWord.Listener == null ? "none" : WakeWord.Listener.Listening ? "listening" : WakeWord.Listener.Available ? "idle" : "off: " + WakeWord.Listener.Problem)}");
        }

        string _langCode;
        bool _langReady;

        /// <summary>
        /// Ticket 3I: the local speaks the chosen story language. Puts the panel, the button labels, the suggested questions, the
        /// greeting and the fillers in it (translated once with Hypa when not written in), and fetches their speech. Runs at start and
        /// whenever the story language changes; the greeting waits for it.
        /// </summary>
        async System.Threading.Tasks.Task ApplyLanguageAsync(StoryLanguage lang)
        {
            _langCode = lang.Code;
            _langReady = false;
            var lines = await LocalLines.EnsureAsync(lang);
            var persona = await LocalLines.TranslateManyAsync((Persona.Suggested ?? new string[0]).Concat(new[] { Persona.Greeting ?? "" }).ToArray(), lang);
            var suggested = persona.Take(persona.Length - 1).ToArray();
            var greeting = persona[persona.Length - 1];
            if (this == null || _langCode != lang.Code) return; // changed again meanwhile
            _view.SetLanguage(lines.Prompt, lines.Talk, lines.Done, lines.Skip);
            _view.SetSuggestions(suggested);
            Talk.GreetingText = greeting;
            // Fillers and stock lines are spoken at once, so fetch their speech now; the greeting too.
            foreach (var f in lines.Filler) _voice.Prefetch(f, lang);
            _voice.Prefetch(lines.Again, lang);
            _voice.Prefetch(lines.Lost, lang);
            if (!string.IsNullOrWhiteSpace(greeting)) _voice.Prefetch(greeting, lang);
            _langReady = true;
            if (lang.Code != "en") Debug.Log($"Hypa Stories: {Persona.DisplayName} speaks {lang.HypaLabel} (\"{lines.Prompt}\").");
        }

        void SetWantWake(bool want)
        {
            if (want == _wantWake) return;
            _wantWake = want;
            WakeWord.Want(this, want);
        }

        /// <summary>A phrase from the wake listener: if it calls this local, talk (the question in it, or listen for one).</summary>
        float _wakeOpenedAt;

        /// <summary>
        /// The phrase so far: as soon as it calls this local ("Hey Emma…"), the panel shows "Listening" and the voice meter moves,
        /// while you go on talking. The finished phrase (<see cref="OnHeard"/>) then answers or listens. The wake listener keeps
        /// running meanwhile (it is hearing the rest of your question).
        /// </summary>
        void OnHeardSoFar(string phrase)
        {
            if (!_ready || !_wantWake || Talk.WakeOpen || Talk.State != AskState.Idle) return;
            if (!WakeWord.Match(phrase, Persona.CalledBy(), out _)) return;
            _wakeOpenedAt = Time.unscaledTime;
            Talk.OpenFromWake(); // the listening chime plays on the state change, below in Update
        }

        void OnWakeLevel(float level) { if (Talk.WakeOpen) _view.SetLevel(level); }

        void OnHeard(string phrase)
        {
            if (!_ready || !_wantWake || _wokeFrame == Time.frameCount) return;
            if (!WakeWord.Match(phrase, Persona.CalledBy(), out var rest))
            {
                if (!Talk.WakeOpen) return;
                rest = phrase; // the words so far called us, the final words came out differently: take it all as the question
            }
            _wokeFrame = Time.frameCount; // one local answers a call, even if two share a name
            _ = WakeAsync(phrase, rest);
        }

        async System.Threading.Tasks.Task WakeAsync(string phrase, string rest)
        {
            var ask = WakeWord.IsQuestion(rest);
            Debug.Log($"Hypa Stories: heard \"{phrase}\" → {Persona.DisplayName} {(ask ? "answers it" : "listens")} (wake word).");
            SetWantWake(false); // the wake listener lets go of the microphone...
            await System.Threading.Tasks.Task.Yield(); // ...before ours opens it
            if (Talk.State != AskState.Idle && !Talk.Greeting && !Talk.WakeOpen) return;
            if (ask) await Talk.AskAsync(rest);
            else await Talk.BeginAsync();
        }

        /// <summary>
        /// Speech to text with the fast model; if it fails for another reason, once more with Hypa's default. A recording with no
        /// words in it (room noise) comes back from the fast model as an "incomplete response" error: that is "didn't catch that".
        /// </summary>
        async System.Threading.Tasks.Task<string> TranscribeAsync(byte[] wav, StoryLanguage language, System.Threading.CancellationToken ct)
        {
            var mcp = new HypaMcpClient();
            if (!string.IsNullOrEmpty(TranscribeModel))
            {
                try { return await mcp.SpeechToTextAsync(wav, language.HypaLabel, ct, TranscribeModel); }
                // No words in it: believe that. (Hypa's default model invents words from room noise, e.g. a Zulu sentence.)
                catch (HypaException e) when (IsNoSpeech(e)) { return ""; }
                catch (HypaException e) { Debug.Log($"Hypa Stories: {TranscribeModel} couldn't transcribe ({e.Message}); trying Hypa's default."); }
            }
            try { return await mcp.SpeechToTextAsync(wav, language.HypaLabel, ct); }
            catch (HypaException e) when (IsNoSpeech(e)) { return ""; }
        }

        /// <summary>The transcription service's way of saying it heard no words. Pure.</summary>
        public static bool IsNoSpeech(System.Exception e) => e != null && (e.Message ?? "").IndexOf("incomplete response", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Tap Talk: start listening; while listening, done talking; while they speak, skip the line.</summary>
        public void TalkTap()
        {
            if (!_ready) return;
            if (Talk.State == AskState.Idle || Talk.Greeting) _ = Talk.BeginAsync(); // talking over the greeting is fine
            else if (Talk.State == AskState.Listening) Talk.StopListening();
            else Talk.Skip();
        }

        /// <summary>Hold Talk: push-to-talk, ends when the pinch is released.</summary>
        public void TalkHold()
        {
            if (!_ready || Talk.State != AskState.Idle) return;
            _holdToTalk = true;
            _ = Talk.BeginAsync();
        }

        void Update()
        {
            if (!_ready) return;
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return;
            var to = transform.position - cam.position; to.y = 0f;
            var distance = to.magnitude;
            var fwd = cam.forward; fwd.y = 0f;
            var angle = distance < 0.01f ? 0f : Vector3.Angle(fwd, to);
            var looking = angle < 40f;
            var near = distance < TalkDistance;
            DevStatus(distance, angle, near);

            // The local you're near and facing gets the raised hand (the nearest, if several).
            if (Focused == this && !(near && looking)) Focused = null;
            if (near && looking && (Focused == null || Focused.DistanceTo(cam) > distance)) Focused = this;

            _view.Root.gameObject.SetActive(distance < TalkDistance * 2.2f);
            if (distance > TalkDistance * 2.2f && (_greeted || Talk.History.Count > 0))
            {
                Talk.Cancel(); Talk.ForgetVisit(); _greeted = false; // walked away: the visit is over
            }
            if (StoryLanguages.Current.Code != _langCode) _ = ApplyLanguageAsync(StoryLanguages.Current);
            if (GreetOnApproach && !_greeted && _langReady && near && angle < GreetAngle && Talk.State == AskState.Idle)
            {
                _greeted = true;
                _ = Talk.GreetAsync();
            }

            // Listen for their name while you're near and nobody is talking (the listener would hear the local, or fight our microphone).
            SetWantWake(WakeWordEnabled && near && (Talk.State == AskState.Idle || Talk.WakeOpen));
            if (Talk.WakeOpen && Time.unscaledTime - _wakeOpenedAt > 8f) Talk.CloseWake(); // the call never became a phrase

            var raised = RaiseHand.Probe != null && RaiseHand.Probe();
            if (_raise.Update(raised, Time.deltaTime) && Focused == this && (Talk.State == AskState.Idle || Talk.Greeting))
                _ = Talk.BeginAsync();
            if (_holdToTalk && !HypaInput.Current.IsPinching)
            {
                _holdToTalk = false;
                if (Talk.State == AskState.Listening) Talk.StopListening();
            }
            if (Application.isEditor && Input.GetKeyDown(KeyCode.T) && Focused == this) TalkTap();

            // Cues for what they're doing, so you know they hear you.
            if (Talk.State != _was)
            {
                if (Talk.State == AskState.Listening) { _cues.PlayOneShot(_chime, 0.8f); _listenedFor = 0f; }
                else if (_was == AskState.Listening) { Unduck(); if (Talk.State == AskState.Thinking) _cues.PlayOneShot(_tap, 0.9f); }
                _was = Talk.State;
            }
            if (Talk.State == AskState.Listening)
            {
                _listenedFor += Time.unscaledDeltaTime;
                if (_listenedFor > DuckAfter && _volumeBefore < 0f) // after the cue (the microphone ignores that moment)
                {
                    _volumeBefore = AudioListener.volume;
                    AudioListener.volume = Mathf.Min(_volumeBefore, DuckTo);
                }
            }
            _view.Tick(Time.deltaTime);
            var lit = Focused == this ? (Talk.State == AskState.Listening ? 1f : 0.6f + 0.25f * Mathf.Sin(Time.time * 2.5f)) : near ? 0.25f : 0f;
            _ring.Alpha = Mathf.Lerp(_ring.Alpha, lit, 1f - Mathf.Exp(-6f * Time.deltaTime));

            // Face the visitor while talking or listening.
            var attend = _engaged || Talk.State != AskState.Idle;
            if (Figure != null) { Figure.Level = _voice.Level; Figure.Attend = attend ? 1f : 0f; }
            else if (Head != null)
            {
                var local = transform.InverseTransformDirection(cam.position - Head.position);
                var target = attend ? Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -70f, 70f) : 0f;
                _headYaw = Mathf.LerpAngle(_headYaw, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
                Head.localRotation = _headRest * Quaternion.Euler(0f, _headYaw, 0f);
            }
        }

        float DistanceTo(Transform cam)
        {
            var d = transform.position - cam.position; d.y = 0f;
            return d.magnitude;
        }

        // The panel floats above the local and turns to face you (upright).
        void LateUpdate()
        {
            if (!_ready || _view == null || Camera.main == null) return;
            var p = transform.position + Vector3.up * PanelHeight;
            var away = p - Camera.main.transform.position; away.y = 0f;
            _view.Root.SetPositionAndRotation(p, away.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(away.normalized) : _view.Root.rotation);
            // Readable from where you stand: the same size in view as at 1.3 m, never smaller than designed.
            _view.Root.localScale = Vector3.one * Mathf.Clamp(away.magnitude / 1.3f, 1f, 2.0f) / Mathf.Max(1e-4f, transform.lossyScale.x);
        }
    }
}
