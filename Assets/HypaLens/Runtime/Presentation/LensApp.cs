using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using HypaLens.Sim;
using UnityEngine;

namespace HypaLens
{
    /// <summary>Marks a pinned word label.</summary>
    public sealed class WordLabel : MonoBehaviour
    {
        public OcrWord Word;
        public PinchTarget Target;
    }

    /// <summary>A new label eases in (scale 0.6 to 1 over a few frames) instead of popping, so live mode never flickers.</summary>
    public sealed class LabelPop : MonoBehaviour
    {
        public float Seconds = 0.18f;
        float _t;
        void OnEnable() { _t = 0f; transform.localScale = Vector3.one * 0.6f; }
        void Update()
        {
            _t += Time.unscaledDeltaTime;
            var k = Mathf.Clamp01(_t / Seconds);
            transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, 1f - (1f - k) * (1f - k)); // ease out
            if (k >= 1f) enabled = false;
        }
    }

    /// <summary>
    /// Touch-target sizes (metres at the 0.6 m design distance). Pinching with simulator hands is imprecise, so
    /// targets stay well above the 0.04 m pinchable floor.
    /// </summary>
    public static class LensTouch
    {
        public const float IconRadius = 0.034f;
        public const float SmallIconRadius = 0.028f;
        public const float PillH = 0.05f;
        public const float PickerRowW = 0.18f;
        public const float PickerRowH = 0.05f;
        public const float PickerPitch = 0.058f;
    }

    /// <summary>
    /// Where the HUD sits. Head-locked UI drags along with every head turn, so a hand pointing at the menu loses it
    /// the moment the user looks at the menu (worse with webcam head tracking). Instead the HUD stays where it is in
    /// the room and only glides back in front of the user once the head has turned or moved well away from it, and
    /// never while the HUD is in use. Yaw only: looking up or down never moves it. Pure, unit tested.
    /// </summary>
    public sealed class LazyFollow
    {
        public float RecentreAngle = 30f;      // degrees of yaw between where the head looks and where the HUD is
        public float RecentreDistance = 0.4f;  // metres the head has moved since the HUD was placed
        // From the eyes. Beyond the hands (0.3-0.6 m out), so a hand ray crosses the HUD instead of running level
        // with it. The UI was designed in metres at 0.6 m; LensApp scales the HUD by Distance / DesignDistance.
        public const float DesignDistance = 0.6f;
        public float Distance = 1.0f, Height = -0.28f;
        public float Smoothing = 5f;           // per second; higher = snappier glide
        bool _placed, _gliding;
        Vector3 _headAtPlace, _targetPos;

        public bool IsGliding => _gliding;

        /// <summary>Forget the placement: the next update snaps the HUD in front of the eyes again.</summary>
        public void Reset() { _placed = false; _gliding = false; }

        /// <summary>
        /// Returns the pose the HUD should have this frame. <paramref name="hold"/> = in use, don't move. The position
        /// is lazy; the rotation is not: the HUD always turns in place to square up with the way the head is facing
        /// (yaw) while tilting to meet the eyes (pitch), so it never reads as a panel seen from the side.
        /// </summary>
        public Pose Update(Vector3 headPos, Quaternion headRot, Pose current, bool hold, float dt)
        {
            var fwd = Flat(headRot * Vector3.forward);
            if (!_placed)
            {
                _placed = true;
                SetTarget(headPos, fwd);
                _gliding = false;
                return new Pose(_targetPos, Facing(_targetPos, headPos, fwd)); // first placement snaps
            }
            var t = 1f - Mathf.Exp(-Smoothing * dt);
            if (hold) _gliding = false;
            else if (!_gliding)
            {
                var toHud = Flat(current.position - headPos);
                var turned = toHud != Vector3.zero && Vector3.Angle(fwd, toHud) > RecentreAngle;
                var moved = Vector3.Distance(headPos, _headAtPlace) > RecentreDistance;
                if (turned || moved) SetTarget(headPos, fwd);
            }
            var pos = current.position;
            if (_gliding)
            {
                pos = Vector3.Lerp(current.position, _targetPos, t);
                if (Vector3.Distance(pos, _targetPos) < 0.002f) { pos = _targetPos; _gliding = false; }
            }
            var rot = Quaternion.Slerp(current.rotation, Facing(pos, headPos, fwd), t);
            return new Pose(pos, rot);
        }

        void SetTarget(Vector3 headPos, Vector3 fwd)
        {
            _gliding = true;
            _headAtPlace = headPos;
            _targetPos = headPos + fwd * Distance + Vector3.up * Height;
        }

        /// <summary>Square to the head's yaw, pitched to meet the eyes (UI reads from -Z, so +Z points away from the viewer).</summary>
        public static Quaternion Facing(Vector3 hudPos, Vector3 headPos, Vector3 headFwdFlat)
        {
            if (headFwdFlat == Vector3.zero) headFwdFlat = Vector3.forward;
            var horizontal = Mathf.Max(0.05f, Vector3.Distance(Flat0(hudPos), Flat0(headPos)));
            var rise = (hudPos.y - headPos.y) / horizontal;
            return Quaternion.LookRotation(new Vector3(headFwdFlat.x, rise, headFwdFlat.z).normalized, Vector3.up);
        }

        static Vector3 Flat0(Vector3 v) { v.y = 0f; return v; }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
        }
    }

    /// <summary>
    /// Lens scene entry point. Builds the HUD, word labels and cards at runtime; input comes from HypaCore's
    /// PinchPointer (hands on Quest, mouse in the editor).
    ///
    /// The HUD follows the Hypa mobile Lens: a top bar (X, "Auto" and the target language, Saved), a hint pill, and a
    /// bottom row of round controls: Capture (pinchable labels on the real text, recognised on the headset), Caption
    /// (Hypa reads and translates the whole view; the photo leaves the headset, so the user is asked once), Speak and
    /// Clear. X returns to the shell's Home card.
    ///
    /// Editor keys: Space = capture, C = caption, hold left mouse = pinch (&gt; 0.6 s = sentence), O = offline,
    /// N = no text, H = help, L = live mode (dev only, not in the UI yet), P = mouse aims instead of the hand.
    /// </summary>
    public sealed class LensApp : MonoBehaviour
    {
        [Tooltip("Editor only: fake camera frame, fake text and a fixed wall. Off = real passthrough camera (Link or the " +
                 "Meta XR Simulator in the editor). Quest builds always use the real camera.")]
        [SerializeField] bool useSimulatedCamera = true;
        [Tooltip("Canned translations. Turn off to call the real Hypa API (needs a key in .env).")]
        [SerializeField] bool useSimulatedApi = true;
        [SerializeField] float simWallDistance = 1.0f;

        // A dark pill with the word printed on it, so the label is readable whatever is behind it; the selected
        // word lifts to a lighter neutral (Hypa's theme has no loud accent).
        static readonly Color LabelBg = new Color(0.09f, 0.09f, 0.09f, 0.95f);
        static readonly Color LabelSel = new Color(0.40f, 0.40f, 0.40f, 0.95f);

        LensController _lens; LensPlatform _p; IHypaLensApi _api;
        SimHypaApi _simApi; SimFrameSource _simFrames; SimPlanePlacer _simPlacer;
        bool _simCam;
        SavedListStore _saved;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        Transform _cam, _hud, _world;
        TextMesh _status; GameObject _statusBg;
        PinchTarget _sourceBtn, _targetBtn;
        GameObject _onboarding, _card, _practice, _caption, _consent, _liveBadge;
        LanguagePicker _picker;
        PracticeDeck _deck; TextMesh _deckText;
        readonly List<WordLabel> _labels = new List<WordLabel>();
        readonly HashSet<WordLabel> _selected = new HashSet<WordLabel>();
        IReadOnlyList<HypaLanguage> _langs = new HypaLanguage[0];
        bool _busy;
        int _resultVersion;
        HypaSpeech _lastSpeech; string _lastSpeechText, _lastSpeechLang;

        void Start()
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("HypaLens: no MainCamera."); enabled = false; return; }
            _cam = cam.transform;
            _world = new GameObject("HypaLens World").transform;
            PinchPointer.EnsureExists();
            _simWall = GameObject.Find("Sim Wall");
            if (_simWall != null && !Application.isEditor) _simWall.SetActive(false);
            _saved = new SavedListStore();

            _p = BuildPlatform();
            _api = useSimulatedApi ? (_simApi = new SimHypaApi()) : (IHypaLensApi)new HypaCoreLensApi();
            _lens = new LensController(_p.Frames, _p.Recognizer, _api,
                () => useSimulatedApi || Application.internetReachability != NetworkReachability.NotReachable)
            {
                TargetLanguage = HypaSettings.TargetLanguage,
                SourceLanguage = string.IsNullOrEmpty(LensPrefs.Source) ? null : LensPrefs.Source,
            };

            BuildHud();
            _ = LoadLanguages();
            if (AppRouter.Consume(AppRouter.LensSavedWords)) TogglePractice();
            else if (!LensPrefs.Onboarded) ShowOnboarding(true);
            SetStatus("Point at text, then pinch Capture.");
        }

        LensPlatform BuildPlatform()
        {
            LensPlatform p = null;
            _simCam = useSimulatedCamera && Application.isEditor; // a device build never fakes the camera
            if (_simCam)
            {
                _simFrames = new SimFrameSource(); _simPlacer = new SimPlanePlacer();
                p = new LensPlatform { Frames = _simFrames, Recognizer = new SimRecognizer { Wall = _simPlacer }, Placer = _simPlacer };
            }
            else if (LensPlatform.Factory != null) p = LensPlatform.Factory();
            p = p ?? new LensPlatform();
            p.Frames = p.Frames ?? new UnavailableFrameSource();
            p.Recognizer = p.Recognizer ?? new UnavailableRecognizer();
            p.Placer = p.Placer ?? new UnwiredPlacer();
            return p;
        }

        // ---------- HUD placement: lazy-follow (see LazyFollow) ----------

        readonly LazyFollow _follow = new LazyFollow();

        /// <summary>Is the pointer on anything in the HUD?</summary>
        bool PointerOnHud
        {
            get
            {
                var hover = PinchPointer.Instance != null ? PinchPointer.Instance.Hover : null;
                return hover != null && _hud != null && hover.transform.IsChildOf(_hud);
            }
        }

        /// <summary>Is a pinch in progress that started on the HUD? (A hand merely held in a pinch pose never holds the HUD.)</summary>
        bool PinchingHud
        {
            get
            {
                var pressed = PinchPointer.Instance != null ? PinchPointer.Instance.Pressed : null;
                return pressed != null && _hud != null && pressed.transform.IsChildOf(_hud);
            }
        }

        bool _xrWasActive;

        void FollowHead()
        {
            if (_hud == null || _cam == null) return;
            // The XR session can start a few frames in, moving the camera to eye height: re-place the HUD instantly then.
            var xrActive = UnityEngine.XR.XRSettings.isDeviceActive;
            if (xrActive && !_xrWasActive) _follow.Reset();
            _xrWasActive = xrActive;
            var hold = PointerOnHud || PinchingHud;
            var pose = _follow.Update(_cam.position, _cam.rotation, new Pose(_hud.position, _hud.rotation), hold, Time.unscaledDeltaTime);
            _hud.SetPositionAndRotation(pose.position, pose.rotation);
        }

        // ---------- HUD: top bar, hint, bottom controls (the mobile Lens layout) ----------

        const float TopY = 0.30f, HintY = 0.215f, CardY = 0.085f, BottomY = -0.02f;

        void BuildHud()
        {
            _hud = new GameObject("HypaLens HUD").transform;
            _hud.SetParent(_world, false);
            _hud.localScale = Vector3.one * (_follow.Distance / LazyFollow.DesignDistance); // same angular size as at 0.6 m
            FollowHead(); // first placement: in front of the eyes, then it stays put until the head turns away

            // Top bar: X | Auto ▾ | → Target ▾ | Saved
            Ui.IconButton("x", _hud, new Vector3(-0.30f, TopY, 0), LensTouch.SmallIconRadius, GoHome);
            _sourceBtn = LanguagePill(new Vector3(-0.105f, TopY, 0), 0.13f, () => OpenPicker(true));
            Ui.Text(_hud, "→", 0.02f, new Vector3(-0.018f, TopY, 0), new Color(1f, 1f, 1f, 0.7f));
            _targetBtn = LanguagePill(new Vector3(0.105f, TopY, 0), 0.20f, () => OpenPicker(false));
            Ui.IconButton("bookmark", _hud, new Vector3(0.30f, TopY, 0), LensTouch.SmallIconRadius, TogglePractice);

            // Hint pill (the status), centred under the top bar.
            _status = Ui.Text(_hud, "", StatusLineH, new Vector3(0f, HintY, 0), new Color(1f, 1f, 1f, 0.95f));

            // Bottom row: the camera controls.
            const float r = LensTouch.IconRadius, pitch = 0.135f;
            Ui.IconButton("camera", _hud, new Vector3(-1.5f * pitch, BottomY, 0), r, () => _ = CaptureAsync(), "Capture", () => _ = CaptureAsync(), Ui.Primary);
            Ui.IconButton("languages", _hud, new Vector3(-0.5f * pitch, BottomY, 0), r, () => _ = CaptionAsync(), "Caption");
            Ui.IconButton("volume-2", _hud, new Vector3(0.5f * pitch, BottomY, 0), r, SpeakLast, "Speak");
            Ui.IconButton("eraser", _hud, new Vector3(1.5f * pitch, BottomY, 0), r, ClearAll, "Clear");

            // Live-mode badge (dev-only toggle for now; the mode itself is not in the UI yet).
            _liveBadge = Ui.Quad("live badge", _hud, new Vector3(0f, BottomY + r * 1.9f, 0f), new Vector2(0.075f, 0.026f), Ui.Accent, false, 0.013f);
            Ui.Text(_liveBadge.transform, "● LIVE", 0.012f, Vector3.zero, Ui.TextColorFor(Ui.Accent));
            _liveBadge.SetActive(false);

            HypaShell.Instance?.SetMenuButtonVisible(false); // the X replaces the shell's Home button here
            UpdateLanguageText();
        }

        /// <summary>A dark pill with a language name and a small chevron, like the mobile app's language selector.</summary>
        PinchTarget LanguagePill(Vector3 pos, float width, Action onPress)
        {
            var pill = Ui.Button("", _hud, pos, new Vector2(width, LensTouch.PillH), Ui.Muted, onPress);
            var chevron = Ui.Icon("chevron-down");
            if (chevron != null)
            {
                var q = Ui.Quad("chevron", pill.transform.parent, new Vector3(width * 0.5f - 0.02f, 0, -0.001f), Vector2.one * 0.02f, Color.white, false, rounded: false);
                q.GetComponent<Renderer>().material.mainTexture = chevron;
            }
            return pill;
        }

        void GoHome()
        {
            ClearAll();
            _picker?.Close(); ShowOnboarding(false); ShowCaption(null); ShowConsent(false);
            if (_practice != null) { Destroy(_practice); _practice = null; }
            if (HypaShell.Instance != null) HypaShell.Instance.ShowMenu(true);
            else SetStatus("Home needs the Hypa shell. Play from the HypaShell scene.");
        }

        bool OtherViewOpen => (_picker != null && _picker.IsOpen) || _onboarding != null || _practice != null || _caption != null || _consent != null
                              || (HypaShell.Instance != null && HypaShell.Instance.IsMenuOpen);

        /// <summary>Onboarding, practice, the picker, the caption and the consent card share the spot above the bar: one at a time.</summary>
        void CloseCards()
        {
            _picker?.Close();
            ShowOnboarding(false);
            ShowCaption(null);
            ShowConsent(false);
            if (_practice != null) { Destroy(_practice); _practice = null; }
        }

        /// <summary>A card above the controls with a title and an X, sized to hold <paramref name="size"/>.</summary>
        GameObject Card(string name, string title, Vector2 size, Action onClose)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_hud, false);
            go.transform.localPosition = new Vector3(0f, CardY, -0.01f);
            Ui.Quad("bg", go.transform, new Vector3(0, 0, 0.003f), size, Ui.Panel, true).AddComponent<PinchTarget>();
            var top = size.y * 0.5f - 0.03f;
            Ui.Text(go.transform, title, 0.022f, new Vector3(-size.x * 0.5f + 0.02f, top, 0), Color.white, TextAnchor.MiddleLeft);
            Ui.IconButton("x", go.transform, new Vector3(size.x * 0.5f - 0.03f, top, -0.001f), 0.022f, onClose);
            return go;
        }

        const float StatusLineH = 0.016f, StatusPad = 0.014f;
        const int StatusWrap = 44;

        void SetStatus(string s)
        {
            if (_status == null) return;
            var text = Ui.Wrap(s, StatusWrap);
            _status.text = text;
            if (_statusBg != null) Destroy(_statusBg);
            _statusBg = null;
            if (string.IsNullOrEmpty(text)) return;
            var lines = text.Split('\n');
            var longest = 0;
            foreach (var l in lines) longest = Mathf.Max(longest, l.Length);
            var size = new Vector2(Ui.TextWidth(new string('x', longest), StatusLineH) + StatusPad * 2f, lines.Length * StatusLineH * 1.08f + StatusPad * 1.4f);
            _statusBg = Ui.Quad("status bg", _hud, new Vector3(0f, HintY, 0.003f), size, Ui.Panel, false);
        }

        void UpdateLanguageText()
        {
            var lineH = LensTouch.PillH * 0.42f * 0.75f; // the smallest the label is allowed to shrink to
            Ui.SetLabel(_sourceBtn, LanguageLabel("", _lens.SourceLanguage ?? "Auto", lineH, 0.13f - 0.05f) + "  ");
            Ui.SetLabel(_targetBtn, LanguageLabel("", _lens.TargetLanguage, lineH, 0.20f - 0.05f) + "  ");
        }

        /// <summary>
        /// "To: Arabic (Libyan)" when it fits, else "To: Arabic": the bracketed variant goes before any ellipsis.
        /// Pure, unit tested.
        /// </summary>
        public static string LanguageLabel(string prefix, string name, float lineHeight, float maxWidth)
        {
            var full = prefix + name;
            if (Ui.TextWidth(full, lineHeight) <= maxWidth) return full;
            var bracket = name.IndexOf(" (", StringComparison.Ordinal);
            return bracket > 0 ? prefix + name.Substring(0, bracket) : full;
        }

        async Task LoadLanguages()
        {
            try { _langs = await _api.ListLanguagesAsync(_cts.Token); }
            catch (Exception e) { SetStatus(LensException.From(e).UserMessage()); }
        }

        void OpenPicker(bool source)
        {
            CloseCards();
            if (_langs.Count == 0) { SetStatus("Loading languages... try again in a moment."); _ = LoadLanguages(); return; }
            _picker = new LanguagePicker(_hud, new Vector3(0f, CardY, -0.01f), source ? "Translate from" : "Translate into",
                _langs, source ? _lens.SourceLanguage : _lens.TargetLanguage, source, name => _ = SetLanguage(source, name));
        }

        async Task SetLanguage(bool source, string name)
        {
            Apply(source, name);
            try
            {
                if (await _lens.IsPairSupportedAsync(_lens.SourceLanguage, _lens.TargetLanguage, _cts.Token))
                {
                    SetStatus($"{_lens.SourceLanguage ?? "Auto"} → {_lens.TargetLanguage}. Point at text, then pinch Capture.");
                    return;
                }
                // Hypa has no model for some regional variants (e.g. Amharic -> Arabic (Libyan)) where the base language
                // works. Fall back to the base rather than leaving the user stuck with an error.
                var baseName = BaseLanguage(name, _langs);
                if (baseName != null)
                {
                    Apply(source, baseName);
                    if (await _lens.IsPairSupportedAsync(_lens.SourceLanguage, _lens.TargetLanguage, _cts.Token))
                    {
                        SetStatus($"Hypa doesn't have {name} for this pair yet, so it's using {baseName}.");
                        return;
                    }
                    Apply(source, name);
                }
                SetStatus(new LensException(LensErrorKind.UnsupportedPair).UserMessage(_lens.SourceLanguage, _lens.TargetLanguage));
            }
            catch (Exception e) { SetStatus(LensException.From(e).UserMessage()); }
        }

        void Apply(bool source, string name)
        {
            if (source) { _lens.SourceLanguage = name; LensPrefs.Source = name; }
            else if (name != null) { _lens.TargetLanguage = name; HypaSettings.TargetLanguage = name; } // remembered
            UpdateLanguageText();
        }

        /// <summary>"Arabic (Libyan)" -> "Arabic" when that language is in the list; null when there is no variant to drop. Pure, unit tested.</summary>
        public static string BaseLanguage(string name, IReadOnlyList<HypaLanguage> langs)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var bracket = name.IndexOf(" (", StringComparison.Ordinal);
            if (bracket <= 0) return null;
            var baseName = name.Substring(0, bracket);
            return HypaLanguages.Find(langs, baseName) != null ? baseName : null;
        }

        // ---------- Onboarding ----------

        void ShowOnboarding(bool on)
        {
            if (!on)
            {
                if (_onboarding != null) Destroy(_onboarding);
                _onboarding = null;
                LensPrefs.Onboarded = true;
                return;
            }
            if (_onboarding != null) return;
            _onboarding = Card("Onboarding", "Welcome to Hypa Lens", new Vector2(0.50f, 0.30f), () => ShowOnboarding(false));
            Ui.Text(_onboarding.transform,
                "1. Look at a sign, menu or book and pinch Capture.\n2. Each word becomes a label on the real surface. Pinch one to translate,\n    hear and define it; hold the pinch for the whole sentence.\n3. Caption reads the whole view at once. Speak reads the last result aloud.\n\nPick languages at the top. Save words with the bookmark to practise later.\nHypa only sends the text you pinch (Caption sends the photo, and asks first).",
                0.016f, new Vector3(-0.23f, -0.02f, 0), new Color(1f, 1f, 1f, 0.92f), TextAnchor.MiddleLeft);
        }

        // ---------- Capture (photo mode: pinchable labels, recognised on the headset) ----------

        async Task CaptureAsync()
        {
            if (_busy) return;
            _busy = true;
            CloseCards();
            ClearLabels(); CloseCard();
            SetStatus("Reading text...");
            try
            {
                var ocr = await _lens.CaptureAsync(_cts.Token);
                var placed = PlaceLabels(ocr);
                SetStatus(placed == 0
                    ? "Couldn't pin labels to a surface. Face a wall, sign or table and try again."
                    : $"{placed} words. Pinch a word; hold the pinch for the sentence.");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { SetStatus(LensException.From(e).UserMessage()); }
            finally { _busy = false; }
        }

        // ---------- Caption (Hypa reads and translates the whole view; the photo leaves the headset) ----------

        async Task CaptionAsync()
        {
            if (_busy) return;
            if (!LensPrefs.CaptionAllowed) { ShowConsent(true); return; }
            _busy = true;
            CloseCards();
            SetStatus("Sending the view to Hypa...");
            try
            {
                var r = await _lens.CaptionAsync(f => FrameEncoder.ToJpeg(f), _cts.Token);
                ShowCaption(r);
                SetStatus("Caption ready. Pinch Speak to hear it.");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { SetStatus(LensException.From(e).UserMessage(_lens.SourceLanguage, _lens.TargetLanguage)); }
            finally { _busy = false; }
        }

        void ShowConsent(bool on)
        {
            if (_consent != null) Destroy(_consent);
            _consent = null;
            if (!on) return;
            CloseCards();
            _consent = Card("Caption consent", "Caption sends a photo", new Vector2(0.46f, 0.24f), () => ShowConsent(false));
            Ui.Text(_consent.transform,
                "To caption, Hypa reads a photo of your view and translates it.\nThe photo is sent once, used for that caption, and not kept.\nCapture, on the other hand, never sends photos.",
                0.016f, new Vector3(-0.21f, 0.005f, 0), new Color(1f, 1f, 1f, 0.92f), TextAnchor.MiddleLeft);
            Ui.Button("Allow", _consent.transform, new Vector3(-0.07f, -0.08f, -0.001f), new Vector2(0.12f, 0.046f), Ui.Accent, () =>
            {
                LensPrefs.CaptionAllowed = true;
                ShowConsent(false);
                _ = CaptionAsync();
            });
            Ui.Button("Not now", _consent.transform, new Vector3(0.07f, -0.08f, -0.001f), new Vector2(0.12f, 0.046f), Ui.Muted, () => ShowConsent(false));
        }

        void ShowCaption(HypaImageTranslation r)
        {
            if (_caption != null) Destroy(_caption);
            _caption = null;
            if (r == null) return;
            CloseCards();
            _caption = Card("Caption", "Caption", new Vector2(0.50f, 0.32f), () => ShowCaption(null));
            var body = $"\"{r.Text}\"  ({r.Language ?? _lens.SourceLanguage ?? "source?"})\n\n→ {r.Translation}  ({_lens.TargetLanguage})";
            Ui.Text(_caption.transform, Ui.Wrap(body, 48), 0.017f, new Vector3(-0.23f, 0.02f, 0), Color.white, TextAnchor.MiddleLeft);
            _lastSpeech = null; _lastSpeechText = r.Translation; _lastSpeechLang = _lens.TargetLanguage;
            Ui.Button("Speak", _caption.transform, new Vector3(-0.07f, -0.125f, -0.001f), new Vector2(0.12f, 0.046f), Ui.Accent, SpeakLast);
            var save = Ui.Button(_saved.Contains(r.Text, _lens.TargetLanguage) ? "Saved" : "Save", _caption.transform, new Vector3(0.07f, -0.125f, -0.001f), new Vector2(0.12f, 0.046f), Ui.Muted, null);
            save.OnPinch = k =>
            {
                if (k != PinchKind.Short) return;
                _saved.Add(new LensResult
                {
                    Selection = new Selection { Kind = SelectionKind.Sentence, Text = r.Text },
                    SourceLanguage = r.Language ?? _lens.SourceLanguage, TargetLanguage = _lens.TargetLanguage, Translation = r.Translation,
                });
                Ui.SetLabel(save, "Saved");
            };
        }

        /// <summary>Speak: the last translation heard (a pinched word's audio, or a caption, fetched fresh as audio links expire).</summary>
        async void SpeakLast()
        {
            if (_lastSpeech != null) { Play(_lastSpeech); return; }
            if (string.IsNullOrEmpty(_lastSpeechText)) { SetStatus("Pinch a word or take a caption first, then Speak."); return; }
            try { Play(await _api.SpeakAsync(_lastSpeechText, _lastSpeechLang, _cts.Token)); }
            catch (OperationCanceledException) { }
            catch (Exception e) { SetStatus("Couldn't play audio: " + LensException.From(e).UserMessage()); }
        }

        void ClearAll()
        {
            ClearLabels(); CloseCard(); ShowCaption(null);
            _lastSpeech = null; _lastSpeechText = null;
            SetStatus("Point at text, then pinch Capture.");
        }

        // ---------- Live mode (dev-only toggle for now: L in the editor) ----------

        const float LiveRegionFraction = 0.4f; // of the frame, around the aim: smaller image, faster recognition
        bool _live;
        readonly DwellTrigger _dwell = new DwellTrigger();
        readonly LiveLabelSet<WordLabel> _liveLabels = new LiveLabelSet<WordLabel>();
        Vector2Int _frameSize = new Vector2Int(1280, 960); // until the first frame tells us

        public bool IsLive => _live;

        void ToggleLive()
        {
            SetLive(!_live);
            SetStatus(_live
                ? "Live mode: rest your hand pointer (or just look) on text for a second and it becomes pinchable."
                : "Live mode off. Pinch Capture to read text.");
        }

        void SetLive(bool on)
        {
            _live = on;
            _dwell.Reset();
            if (_liveBadge != null) _liveBadge.SetActive(on);
            if (on) { CloseCards(); ClearLabels(); CloseCard(); }
            else _liveLabels.Clear(); // labels stay on the walls as photo-mode labels
        }

        void LiveTick()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            var region = LiveRegion(out var aim);
            if (_dwell.Update(region != null ? aim : (Vector2?)null, _busy, now) && region != null) _ = LiveCaptureAsync(region.Value);
            foreach (var gone in _liveLabels.Tick(InView, now)) RemoveLabel(gone);
        }

        bool InView(Vector3 p)
        {
            var d = p - _cam.position;
            return d.sqrMagnitude < 25f && Vector3.Angle(_cam.forward, d) < 50f;
        }

        /// <summary>The part of the next frame to read: around where the hand pointer (or the head, with no hand) rests.</summary>
        FrameRect? LiveRegion(out Vector2 aimPx)
        {
            aimPx = default;
            if (!HypaInput.Current.TryGetRay(out var aim)) aim = new Ray(_cam.position, _cam.forward);
            var head = new Pose(_cam.position, _cam.rotation);
            if (_simCam && !_simPlacer.HasWall) _simPlacer.SetWallFromPose(head, simWallDistance);
            if (!_p.Placer.TryPlace(aim, out var hit, out _)) hit = aim.GetPoint(1.5f);
            var k = _lens.LastFrameMeta != null ? _lens.LastFrameMeta.Intrinsics : FrameCrop.ApproxIntrinsics(_frameSize);
            if (!FrameProjector.WorldToPixel(k, head, hit, out aimPx)) return null;
            return FrameCrop.RegionAround(aimPx, _frameSize.x, _frameSize.y, LiveRegionFraction);
        }

        async Task LiveCaptureAsync(FrameRect region)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var ocr = await _lens.CaptureAsync(_cts.Token, region);
                PlaceLabels(ocr, region);
            }
            catch (OperationCanceledException) { }
            catch (LensException e) when (e.Kind == LensErrorKind.NoTextFound) { } // nothing in that patch: normal in live mode
            catch (Exception e)
            {
                SetLive(false); // camera or permission trouble: stop rather than repeat the error every second
                SetStatus(LensException.From(e).UserMessage());
            }
            finally { _busy = false; }
        }

        // ---------- Labels ----------

        /// <summary>
        /// Pins one label per recognised word. In live mode (<paramref name="liveRegion"/> set) a word already labelled
        /// where it stands reuses its label, and labels the run looked at but did not find take a miss.
        /// </summary>
        int PlaceLabels(OcrResult ocr, FrameRect? liveRegion = null)
        {
            var meta = _lens.LastFrameMeta;
            _frameSize = new Vector2Int(meta.Width, meta.Height);
            // The simulator's wall: photo mode puts it in front of each capture; live mode keeps one wall so labels line up between runs.
            if (_simCam && (liveRegion == null || !_simPlacer.HasWall)) _simPlacer.SetWallFromPose(meta.CameraPose, simWallDistance);
            var now = Time.realtimeSinceStartupAsDouble;
            var placed = 0;
            if (liveRegion != null) _liveLabels.BeginRun();

            foreach (var line in ocr.Lines)
                foreach (var w in line.Words)
                {
                    // Ray from the camera pose AT CAPTURE TIME, so labels stay on the text after the head moves.
                    var ray = FrameProjector.PixelToWorldRay(meta, w.Box.Center);
                    if (!_p.Placer.TryPlace(ray, out var pos, out var normal)) continue;
                    var dist = Vector3.Distance(ray.origin, pos);
                    if (dist < 0.1f || dist > 4f) continue; // implausible hit: drop it rather than float it
                    if (Vector3.Dot(normal, -ray.direction) < 0) normal = -normal;

                    if (liveRegion != null)
                    {
                        var existing = _liveLabels.Match(w.Text, pos, now);
                        if (existing != null) { existing.Word = w; placed++; continue; } // same word, same place: keep it still
                    }
                    var size = FrameProjector.PixelSizeToWorld(meta.Intrinsics, w.Box, dist) * 1.1f;
                    size = new Vector2(Mathf.Max(size.x, 0.03f), Mathf.Max(size.y, 0.035f)); // small print stays pinchable

                    var label = NewWordLabel(w, size, pos + normal * 0.003f, FrameProjector.FaceViewer(normal, Vector3.up), _world);
                    label.Target.OnPinch = k => OnWordPinch(label, k);
                    label.Target.OnHover = on => { if (!_selected.Contains(label)) label.Target.SetColor(on ? Ui.Hover : (Color?)null); };
                    _labels.Add(label);
                    if (liveRegion != null) RemoveLabel(_liveLabels.Add(label, w.Text, pos, now)); // over the cap: the stalest goes
                    placed++;
                }

            if (liveRegion != null)
            {
                var r = liveRegion.Value;
                foreach (var gone in _liveLabels.EndRun(p => FrameProjector.WorldToPixel(meta.Intrinsics, meta.CameraPose, p, out var px) && FrameCrop.Contains(r, px)))
                    RemoveLabel(gone);
            }
            return placed;
        }

        void RemoveLabel(WordLabel l)
        {
            if (l == null) return;
            _labels.Remove(l); _selected.Remove(l);
            Destroy(l.gameObject);
        }

        /// <summary>Widens the pill so the printed word fits inside it. Pure, unit tested.</summary>
        public static Vector2 FitPillToWord(string text, Vector2 size, float lineHeight)
        {
            var need = (text ?? "").Length * lineHeight * 0.62f + lineHeight * 0.8f;
            if (need > size.x) size.x = need;
            return size;
        }

        /// <summary>
        /// One readable word label: an unscaled root at the surface point, a dark pill (collider + pinch
        /// target) and the word printed on top in white. The root stays unscaled so the text is undistorted.
        /// </summary>
        public static WordLabel NewWordLabel(OcrWord word, Vector2 size, Vector3 pos, Quaternion rot, Transform parent)
        {
            var lineH = size.y * 0.5f;
            size = FitPillToWord(word.Text, size, lineH);
            var root = new GameObject("Word " + word.Text);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = rot;
            var pill = Ui.Quad("pill", root.transform, Vector3.zero, size, LabelBg, true);
            Ui.Text(root.transform, word.Text, lineH, Vector3.zero, Color.white);
            var label = root.AddComponent<WordLabel>();
            label.Word = word;
            label.Target = pill.AddComponent<PinchTarget>();
            root.AddComponent<LabelPop>();
            return label;
        }

        void ClearLabels()
        {
            foreach (var l in _labels) if (l != null) Destroy(l.gameObject);
            _labels.Clear(); _selected.Clear(); _liveLabels.Clear();
        }

        // ---------- Selection + result card ----------

        void OnWordPinch(WordLabel label, PinchKind k)
        {
            foreach (var s in _selected) if (s != null) s.Target.SetColor(null);
            _selected.Clear();
            var sentence = k == PinchKind.Long;
            foreach (var l in _labels)
                if (l == label || (sentence && l.Word.Line == label.Word.Line)) { _selected.Add(l); l.Target.SetColor(LabelSel); }
            var sel = sentence ? LensController.SelectSentence(label.Word) : LensController.SelectWord(label.Word);
            _ = ShowResult(sel, label.transform);
        }

        async Task ShowResult(Selection sel, Transform anchor)
        {
            var version = ++_resultVersion;
            CloseCard();
            var body = BuildCard(anchor);
            body.text = Ui.Wrap($"\"{sel.Text}\"\n\nTranslating to {_lens.TargetLanguage}...", 32);
            try
            {
                var r = await _lens.ResolveAsync(sel, _cts.Token, partial =>
                {
                    if (version == _resultVersion && body != null) body.text = CardText(partial);
                }, audio =>
                {
                    if (version == _resultVersion) { _lastSpeech = audio; Play(audio); }
                });
                if (version != _resultVersion || body == null) return; // a newer selection replaced this card
                _lastSpeech = r.TargetAudio; _lastSpeechText = r.Translation; _lastSpeechLang = r.TargetLanguage;
                body.text = CardText(r);
                AddCardButtons(r);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (version != _resultVersion || body == null) return;
                body.text = Ui.Wrap($"\"{sel.Text}\"\n\n" + LensException.From(e).UserMessage(_lens.SourceLanguage, _lens.TargetLanguage), 32);
                Ui.Button("Close", _card.transform, new Vector3(0, -0.125f, -0.001f), new Vector2(0.12f, 0.045f), Ui.Muted, CloseCard);
            }
        }

        static string CardText(LensResult r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('"').Append(r.Selection.Text).Append("\"  (").Append(r.SourceLanguage ?? "source?").Append(")\n");
            sb.Append("→ ").Append(r.Translation).Append("  (").Append(r.TargetLanguage).Append(")\n\n");
            sb.Append(r.Meaning ?? r.MeaningNote ?? "");
            if (r.AudioNote != null) sb.Append('\n').Append(r.AudioNote);
            return Ui.Wrap(sb.ToString(), 32);
        }

        TextMesh BuildCard(Transform anchor)
        {
            // Beside the selection, on the side nearer the centre of view so it stays inside a narrow FOV.
            var local = _cam.InverseTransformPoint(anchor.position);
            var side = local.x > 0 ? -1f : 1f;
            _card = new GameObject("Result Card");
            _card.transform.SetParent(_world, false);
            _card.transform.position = anchor.position + _cam.right * (0.27f * side) - anchor.forward * 0.02f;
            _card.transform.rotation = anchor.rotation;
            Ui.Quad("bg", _card.transform, Vector3.zero, new Vector2(0.40f, 0.30f), Ui.Panel, true);
            Ui.IconButton("x", _card.transform, new Vector3(0.17f, 0.12f, -0.001f), 0.02f, CloseCard);
            return Ui.Text(_card.transform, "", 0.021f, new Vector3(-0.18f, 0.13f, 0), Color.white, TextAnchor.UpperLeft);
        }

        void AddCardButtons(LensResult r)
        {
            var t = _card.transform;
            const float y = -0.12f, z = -0.001f;
            var w = new Vector2(0.10f, 0.045f);
            if (r.SourceAudio != null) Ui.Button("Hear", t, new Vector3(-0.13f, y, z), w, Ui.Accent, () => Play(r.SourceAudio));
            if (r.TargetAudio != null) Ui.Button("Hear →", t, new Vector3(0f, y, z), w, Ui.Accent, () => Play(r.TargetAudio));
            var save = Ui.Button(_saved.Contains(r.Selection.Text, r.TargetLanguage) ? "Saved" : "Save", t, new Vector3(0.13f, y, z), w, Ui.Muted, null);
            save.OnPinch = k => { if (k == PinchKind.Short) { _saved.Add(r); Ui.SetLabel(save, "Saved"); } };
        }

        void CloseCard()
        {
            if (_card != null) Destroy(_card);
            _card = null;
        }

        async void Play(HypaSpeech speech)
        {
            if (speech == null) return;
            try { await HypaAudioPlayer.Instance.PlayNowAsync(speech, _cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { SetStatus("Couldn't play audio: " + LensException.From(e).UserMessage()); }
        }

        // ---------- Practice: saved list as flip cards with audio ----------

        void TogglePractice()
        {
            if (_practice != null) { Destroy(_practice); _practice = null; return; }
            CloseCards();
            _deck = new PracticeDeck(_saved.Items);
            _practice = Card("Practice", "Saved words", new Vector2(0.44f, 0.30f), () => { Destroy(_practice); _practice = null; });
            var face = Ui.Quad("face", _practice.transform, new Vector3(0, 0.005f, -0.001f), new Vector2(0.40f, 0.15f), Ui.Muted, true);
            face.AddComponent<PinchTarget>().OnPinch = k => { if (k == PinchKind.Short) { _deck.Flip(); RefreshDeck(); } };
            _deckText = Ui.Text(_practice.transform, "", 0.024f, new Vector3(0f, 0.005f, 0), Color.white);
            var t = _practice.transform;
            Ui.Button("<", t, new Vector3(-0.15f, -0.11f, -0.001f), new Vector2(0.07f, 0.045f), Ui.Muted, () => { _deck.Prev(); RefreshDeck(); });
            Ui.Button("Hear", t, new Vector3(0f, -0.11f, -0.001f), new Vector2(0.10f, 0.045f), Ui.Accent, () => _ = PlayDeckAudio());
            Ui.Button(">", t, new Vector3(0.15f, -0.11f, -0.001f), new Vector2(0.07f, 0.045f), Ui.Muted, () => { _deck.Next(); RefreshDeck(); });
            RefreshDeck();
        }

        void RefreshDeck()
        {
            _deckText.text = _deck.IsEmpty
                ? "No saved words yet.\nPinch a word, then Save."
                : Ui.Wrap(_deck.Face, 24) + "\n" + $"({(_deck.Flipped ? "pinch to flip back" : "pinch to reveal")})  {_deck.Index + 1}/{_saved.Items.Count}";
        }

        async Task PlayDeckAudio()
        {
            if (_deck == null || _deck.IsEmpty) return;
            var lang = _deck.FaceLanguage ?? _lens.TargetLanguage;
            try { Play(await _api.SpeakAsync(_deck.Face, lang, _cts.Token)); } // audio links expire, so fetch fresh
            catch (OperationCanceledException) { }
            catch (Exception e) { _deckText.text = LensException.From(e).UserMessage(); }
        }

        // ---------- Per frame ----------

        // The flat "Sim Wall" backdrop is only for mouse testing in the editor. On a headset it would sit in front of
        // the real room, and Quest's OpenXR path often leaves XRSettings.isDeviceActive false, so a device build hides
        // the wall outright; in the editor it goes once the simulator (or Link) is running.
        GameObject _simWall;

        void Update()
        {
            if (_simWall != null && _simWall.activeSelf && (!Application.isEditor || UnityEngine.XR.XRSettings.isDeviceActive)) _simWall.SetActive(false);
            // Home (the shell's card) is modal: the Lens HUD steps aside while it is up.
            var home = HypaShell.Instance != null && HypaShell.Instance.IsMenuOpen;
            if (_hud != null && _hud.gameObject.activeSelf == home) _hud.gameObject.SetActive(!home);
            if (!home) FollowHead();
            if (_live && !home) LiveTick();
            if (!Application.isEditor) return;
            if (Input.GetKeyDown(KeyCode.Space)) _ = CaptureAsync();
            if (Input.GetKeyDown(KeyCode.C)) _ = CaptionAsync();
            if (Input.GetKeyDown(KeyCode.H)) ShowOnboarding(_onboarding == null);
            if (Input.GetKeyDown(KeyCode.L)) ToggleLive();
            if (Input.GetKeyDown(KeyCode.P)) { EditorXrInput.HandAims = !EditorXrInput.HandAims; SetStatus(EditorXrInput.HandAims ? "Hand aims (P: mouse aims)." : "Mouse aims, hand or button pinches (P: hand aims)."); }
            if (Input.GetKeyDown(KeyCode.O) && _simApi != null) { _simApi.Offline = !_simApi.Offline; SetStatus("Sim offline: " + _simApi.Offline); }
            if (Input.GetKeyDown(KeyCode.N) && _simFrames != null) { _simFrames.ReturnBlankFrame = !_simFrames.ReturnBlankFrame; SetStatus("Sim blank frame: " + _simFrames.ReturnBlankFrame); }
        }

        void OnDestroy()
        {
            _cts.Cancel();
            _cts.Dispose();
            HypaShell.Instance?.SetMenuButtonVisible(true);
            if (_world != null) Destroy(_world.gameObject); // takes the HUD with it
        }
    }
}
