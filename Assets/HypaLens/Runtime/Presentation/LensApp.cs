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
    /// Minimum touch-target sizes for simulator hands (metres). Pinching with mouse-driven
    /// simulator hands is imprecise, so menu targets stay well above the 0.04 m pinchable floor.
    /// </summary>
    public static class LensTouch
    {
        public const float MenuButtonW = 0.20f;
        public const float MenuButtonH = 0.056f;
        public const float MenuGap = 0.01f;
        public const int MenuColumns = 2;
        public const float LauncherRadius = 0.042f;
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
    /// Grid cells for the launcher menu: <paramref name="cols"/> across, and when the last row holds a single item
    /// it spans the full width so the panel reads as a block rather than a column with a stub. Pure, unit tested.
    /// </summary>
    public static class MenuLayout
    {
        /// <summary>Centre (x, y) and width of item <paramref name="index"/>, relative to the panel centre.</summary>
        public static Rect Cell(int index, int items, int cols, float w, float h, float gap, float pad)
        {
            var rows = (items + cols - 1) / cols;
            var width = cols * w + (cols - 1) * gap;
            var height = rows * h + (rows - 1) * gap;
            var row = index / cols;
            var y = height * 0.5f - h * 0.5f - row * (h + gap);
            var lastRowAlone = row == rows - 1 && items - row * cols == 1;
            if (lastRowAlone) return new Rect(0f, y, width, h);
            var x = -width * 0.5f + w * 0.5f + (index % cols) * (w + gap);
            return new Rect(x, y, w, h);
        }

        public static Vector2 PanelSize(int items, int cols, float w, float h, float gap, float pad)
        {
            var rows = (items + cols - 1) / cols;
            return new Vector2(cols * w + (cols - 1) * gap + pad * 2f, rows * h + (rows - 1) * gap + pad * 2f);
        }
    }

    /// <summary>
    /// Keeps the launcher menu open while the pointer is on the launcher or the menu and closes it a moment after the
    /// pointer has left (never mid-pinch, and a brief wander off the menu is forgiven). Pure, unit tested.
    /// </summary>
    public sealed class MenuLinger
    {
        public float CloseAfterSeconds = 1.5f;
        double _leftAt = -1;

        /// <summary>Returns true on the frame the menu should close.</summary>
        public bool Update(bool open, bool pointerInside, bool pinching, double now)
        {
            if (!open || pointerInside || pinching) { _leftAt = -1; return false; }
            if (_leftAt < 0) { _leftAt = now; return false; }
            if (now - _leftAt < CloseAfterSeconds) return false;
            _leftAt = -1;
            return true;
        }
    }

    /// <summary>
    /// Lens scene entry point. Builds the HUD, word labels and result card at runtime; input comes from HypaCore's
    /// PinchPointer (hands on Quest, mouse in the editor).
    /// Live mode (menu toggle, or L in the editor): rest the hand pointer or head on text for a second and that part of
    /// the view is recognised (one run a second at most); labels are reused while the text stays in view.
    /// Editor keys: Space = capture, hold left mouse = pinch (&gt; 0.6 s = sentence), O = offline, N = no text, H = help.
    /// With the Meta XR Simulator running, the hand aims and pinches as on the headset; the left mouse button also
    /// pinches, and P switches aiming to the mouse (for webcam hands, which pinch well but aim badly).
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
        PinchTarget _fromBtn, _toBtn, _liveBtn;
        GameObject _onboarding, _card, _practice;
        LanguagePicker _picker;
        PracticeDeck _deck; TextMesh _deckText;
        readonly List<WordLabel> _labels = new List<WordLabel>();
        readonly HashSet<WordLabel> _selected = new HashSet<WordLabel>();
        IReadOnlyList<HypaLanguage> _langs = new HypaLanguage[0];
        bool _busy;
        int _resultVersion;

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
            SetStatus("Point at the round Hypa Lens button for the menu. Hold a pinch on it to translate text.");
        }

        LensPlatform BuildPlatform()
        {
            LensPlatform p = null;
            _simCam = useSimulatedCamera && Application.isEditor; // a device build never fakes the camera
            if (_simCam)
            {
                _simFrames = new SimFrameSource(); _simPlacer = new SimPlanePlacer();
                p = new LensPlatform { Frames = _simFrames, Recognizer = new SimRecognizer(), Placer = _simPlacer };
            }
            else if (LensPlatform.Factory != null) p = LensPlatform.Factory();
            p = p ?? new LensPlatform();
            p.Frames = p.Frames ?? new UnavailableFrameSource();
            p.Recognizer = p.Recognizer ?? new UnavailableRecognizer();
            p.Placer = p.Placer ?? new UnwiredPlacer();
            return p;
        }

        // ---------- HUD: lazy-follow (see LazyFollow), low in a narrow field of view, within arm's reach ----------

        readonly LazyFollow _follow = new LazyFollow();

        /// <summary>Is the pointer on anything in the HUD (launcher, menu, picker, cards)?</summary>
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
            var hold = (_launcherMenu != null && _launcherMenu.gameObject.activeSelf) || PointerOnHud || PinchingHud;
            var pose = _follow.Update(_cam.position, _cam.rotation, new Pose(_hud.position, _hud.rotation), hold, Time.unscaledDeltaTime);
            _hud.SetPositionAndRotation(pose.position, pose.rotation);
        }

        // ---------- Launcher: one round button opens everything (seated, within reach, out of the way) ----------
        // Hovering the button opens the menu; pinch an item, or pinch the button and slide onto an item and release.
        // The menu closes itself once the pointer has left it. A short pinch on the button still toggles it, and a
        // long pinch translates straight away.

        Transform _launcher, _launcherMenu;
        TextMesh _launcherLabel;
        GameObject _launcherIcon;
        readonly MenuLinger _linger = new MenuLinger();
        /// <summary>The shared Hypa mark: Assets/HypaCore/Resources/HypaLogo.png (square, transparent, white on dark).</summary>
        const string LogoResource = "HypaLogo";

        void BuildHud()
        {
            _hud = new GameObject("HypaLens HUD").transform;
            _hud.SetParent(_world, false);
            _hud.localScale = Vector3.one * (_follow.Distance / LazyFollow.DesignDistance); // same angular size as at 0.6 m
            FollowHead(); // first placement: in front of the eyes, then it stays put until the head turns away
            // Status: a fixed-width card just left of the launcher with left-aligned text, so every message reads the same way.
            _status = Ui.Text(_hud, "", StatusLineH, new Vector3(StatusRight - StatusW + StatusPad, -0.04f, 0), new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleLeft);

            // Low right, inside arm's reach. Hover = open the menu; hold the pinch = translate text straight away.
            var launcher = _launcher = new GameObject("Hypa Launcher").transform;
            launcher.SetParent(_hud, false);
            launcher.localPosition = new Vector3(0.24f, -0.04f, 0f);
            var logo = Resources.Load<Texture2D>(LogoResource);
            var launcherBtn = Ui.RoundButton("Hypa", launcher, Vector3.zero, LensTouch.LauncherRadius, Ui.Panel,
                ToggleLauncher, () => { ShowLauncher(false); _ = CaptureAsync(); }, logo);
            _launcherLabel = Ui.LabelOf(launcherBtn);
            _launcherIcon = launcherBtn.transform.parent.Find("icon")?.gameObject;
            var launcherHover = launcherBtn.OnHover;
            launcherBtn.OnHover = on =>
            {
                launcherHover(on);
                // Open on hover, but never on top of a picker or card the user is in the middle of (a pinch still can).
                if (on && !OtherViewOpen) ShowLauncher(true, closeOthers: false);
            };

            // A compact grid above-left of the button, so every item is one small hand movement away.
            _launcherMenu = new GameObject("Lens Menu").transform;
            _launcherMenu.SetParent(launcher, false);
            var items = HypaShell.Instance != null ? 7 : 6;
            const float w = LensTouch.MenuButtonW, h = LensTouch.MenuButtonH, gap = LensTouch.MenuGap, pad = 0.012f;
            const int cols = LensTouch.MenuColumns;
            var size = MenuLayout.PanelSize(items, cols, w, h, gap, pad);
            // Right edge near the button's right edge, bottom edge just above it.
            _launcherMenu.localPosition = new Vector3(LensTouch.LauncherRadius * 1.12f - size.x * 0.5f, 0.045f + size.y * 0.5f, 0f);
            // The panel is a (no-op) target so a pinch that wanders onto it counts as "still in the menu".
            Ui.Quad("bg", _launcherMenu, new Vector3(0, 0, 0.003f), size, Ui.Panel, true).AddComponent<PinchTarget>();
            var n = 0;
            void Item(string label, Color c, Action a, out PinchTarget btn)
            {
                var cell = MenuLayout.Cell(n, items, cols, w, h, gap, pad);
                btn = Ui.Button(label, _launcherMenu, new Vector3(cell.x, cell.y, 0), new Vector2(cell.width, cell.height), c, () => { ShowLauncher(false); a(); });
                btn.AcceptsSlide = true; // pinch the button, slide here, release
                n++;
            }
            Item("Translate text", Ui.Accent, () => _ = CaptureAsync(), out _);
            Item("Live: off", Ui.Muted, ToggleLive, out _liveBtn);
            Item("From", Ui.Muted, () => OpenPicker(true), out _fromBtn);
            Item("To", Ui.Muted, () => OpenPicker(false), out _toBtn);
            Item("Saved words", Ui.Muted, TogglePractice, out _);
            Item("How it works", Ui.Muted, () => ShowOnboarding(true), out _);
            if (HypaShell.Instance != null) Item("Apps", Ui.Muted, () => HypaShell.Instance.ToggleMenu(), out _);
            _launcherMenu.gameObject.SetActive(false);

            HypaShell.Instance?.SetMenuButtonVisible(false); // the launcher's "Apps" replaces the shell's Menu button here
            UpdateLanguageText();
        }

        void ToggleLauncher() => ShowLauncher(!_launcherMenu.gameObject.activeSelf);

        bool OtherViewOpen => (_picker != null && _picker.IsOpen) || _onboarding != null || _practice != null;

        /// <summary>Is the pointer on the launcher button or its menu?</summary>
        bool PointerOnLauncher
        {
            get
            {
                var hover = PinchPointer.Instance != null ? PinchPointer.Instance.Hover : null;
                return hover != null && _launcher != null && hover.transform.IsChildOf(_launcher);
            }
        }

        void ShowLauncher(bool open, bool closeOthers = true)
        {
            if (_launcherMenu == null || _launcherMenu.gameObject.activeSelf == open) return;
            if (open && closeOthers) { _picker?.Close(); ShowOnboarding(false); if (_practice != null) { Destroy(_practice); _practice = null; } }
            _launcherMenu.gameObject.SetActive(open);
            // Open: the button reads "Close". Closed: the Hypa Lens logo (or "Hypa" if the logo isn't in the project).
            if (_launcherIcon != null) _launcherIcon.SetActive(!open);
            _launcherLabel.text = open ? "Close" : _launcherIcon != null ? "" : "Hypa";
        }

        const float StatusLineH = 0.016f, StatusRight = 0.18f, StatusW = 0.34f, StatusPad = 0.014f;
        /// <summary>Characters per line that fit the card's width at <see cref="StatusLineH"/>.</summary>
        static readonly int StatusWrap = Mathf.FloorToInt((StatusW - StatusPad * 2f) / (StatusLineH * Ui.CharWidth));

        void SetStatus(string s)
        {
            if (_status == null) return;
            var text = Ui.Wrap(s, StatusWrap);
            _status.text = text;
            if (_statusBg != null) Destroy(_statusBg);
            _statusBg = null;
            if (string.IsNullOrEmpty(text)) return;
            var lines = text.Split('\n').Length;
            var size = new Vector2(StatusW, lines * StatusLineH * 1.08f + StatusPad * 1.6f);
            _statusBg = Ui.Quad("status bg", _hud, new Vector3(StatusRight - StatusW * 0.5f, -0.04f, 0.003f), size, Ui.Panel, false);
        }

        void UpdateLanguageText()
        {
            var maxW = LensTouch.MenuButtonW - 0.028f;
            var lineH = LensTouch.MenuButtonH * 0.42f * 0.75f; // the smallest the label is allowed to shrink to
            Ui.SetLabel(_fromBtn, LanguageLabel("From: ", _lens.SourceLanguage ?? "Auto", lineH, maxW));
            Ui.SetLabel(_toBtn, LanguageLabel("To: ", _lens.TargetLanguage, lineH, maxW));
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
            _picker?.Close();
            // Onboarding, practice and the picker share the spot above the HUD: show one at a time.
            ShowOnboarding(false);
            if (_practice != null) { Destroy(_practice); _practice = null; }
            if (_langs.Count == 0) { SetStatus("Loading languages... try again in a moment."); _ = LoadLanguages(); return; }
            _picker = new LanguagePicker(_hud, new Vector3(0f, 0.22f, -0.01f), source ? "Translate from" : "Translate to",
                _langs, source ? _lens.SourceLanguage : _lens.TargetLanguage, source, name => _ = SetLanguage(source, name));
        }

        async Task SetLanguage(bool source, string name)
        {
            if (source) { _lens.SourceLanguage = name; LensPrefs.Source = name; }
            else if (name != null) { _lens.TargetLanguage = name; HypaSettings.TargetLanguage = name; } // remembered
            UpdateLanguageText();
            try
            {
                if (!await _lens.IsPairSupportedAsync(_lens.SourceLanguage, _lens.TargetLanguage, _cts.Token))
                    SetStatus(new LensException(LensErrorKind.UnsupportedPair).UserMessage(_lens.SourceLanguage, _lens.TargetLanguage));
                else SetStatus($"{_lens.SourceLanguage ?? "Auto"} -> {_lens.TargetLanguage}. Pinch a word.");
            }
            catch (Exception e) { SetStatus(LensException.From(e).UserMessage()); }
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
            _onboarding = new GameObject("Onboarding");
            _onboarding.transform.SetParent(_hud, false);
            _onboarding.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            var bg = Ui.Quad("bg", _onboarding.transform, Vector3.zero, new Vector2(0.48f, 0.27f), Ui.Panel, true);
            Ui.Text(_onboarding.transform,
                "Welcome to Hypa Lens\n\n1. Look at a sign, menu or book\n2. Point at the round Hypa Lens button, then pinch Translate text\n    (or hold the pinch on the button to capture straight away)\n3. Pinch a word to translate, hear and define it\n    Hold the pinch to pick the whole sentence\n\nLive mode (in the menu): rest your pointer on text for a second instead.\nLanguages and saved words are in the same menu.\nHypa only sends the text you pinch. Photos never leave the headset.\n(pinch here to start)",
                0.017f, Vector3.zero, Color.white);
            var pt = bg.AddComponent<PinchTarget>();
            pt.OnPinch = k => { if (k == PinchKind.Short) ShowOnboarding(false); };
        }

        // ---------- Capture (photo mode: one pinch, the whole view) ----------

        async Task CaptureAsync()
        {
            if (_busy) return;
            _busy = true;
            ShowOnboarding(false);
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

        // ---------- Live mode: rest the pointer (or head) on text and it becomes pinchable ----------

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
                : "Live mode off. Pinch Translate text to capture.");
        }

        void SetLive(bool on)
        {
            _live = on;
            _dwell.Reset();
            if (_liveBtn != null) Ui.SetLabel(_liveBtn, on ? "Live: on" : "Live: off");
            if (on) { ShowOnboarding(false); ClearLabels(); CloseCard(); }
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
                    if (liveRegion != null) _liveLabels.Add(label, w.Text, pos, now);
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
                    if (version == _resultVersion) Play(audio);
                });
                if (version != _resultVersion || body == null) return; // a newer selection replaced this card
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
            sb.Append("-> ").Append(r.Translation).Append("  (").Append(r.TargetLanguage).Append(")\n\n");
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
            return Ui.Text(_card.transform, "", 0.021f, new Vector3(-0.18f, 0.13f, 0), Color.white, TextAnchor.UpperLeft);
        }

        void AddCardButtons(LensResult r)
        {
            var t = _card.transform;
            const float y = -0.12f, z = -0.001f;
            var w = new Vector2(0.09f, 0.045f);
            if (r.SourceAudio != null) Ui.Button("Hear", t, new Vector3(-0.15f, y, z), w, Ui.Accent, () => Play(r.SourceAudio));
            if (r.TargetAudio != null) Ui.Button("Hear ->", t, new Vector3(-0.05f, y, z), w, Ui.Accent, () => Play(r.TargetAudio));
            var save = Ui.Button(_saved.Contains(r.Selection.Text, r.TargetLanguage) ? "Saved" : "Save", t, new Vector3(0.05f, y, z), w, Ui.Accent, null);
            var saveLabel = Ui.LabelOf(save);
            save.OnPinch = k => { if (k == PinchKind.Short) { _saved.Add(r); saveLabel.text = "Saved"; } };
            Ui.Button("Close", t, new Vector3(0.15f, y, z), w, Ui.Muted, CloseCard);
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
            ShowOnboarding(false);
            _picker?.Close();
            _deck = new PracticeDeck(_saved.Items);
            _practice = new GameObject("Practice");
            _practice.transform.SetParent(_hud, false);
            _practice.transform.localPosition = new Vector3(0f, 0.21f, 0f);
            var bg = Ui.Quad("bg", _practice.transform, Vector3.zero, new Vector2(0.42f, 0.26f), Ui.Panel, true);
            bg.AddComponent<PinchTarget>().OnPinch = k => { if (k == PinchKind.Short) { _deck.Flip(); RefreshDeck(); } };
            _deckText = Ui.Text(_practice.transform, "", 0.03f, new Vector3(0f, 0.03f, 0), Color.white);
            var t = _practice.transform;
            Ui.Button("<", t, new Vector3(-0.15f, -0.095f, -0.001f), new Vector2(0.07f, 0.045f), Ui.Accent, () => { _deck.Prev(); RefreshDeck(); });
            Ui.Button("Hear", t, new Vector3(0f, -0.095f, -0.001f), new Vector2(0.10f, 0.045f), Ui.Accent, () => _ = PlayDeckAudio());
            Ui.Button(">", t, new Vector3(0.15f, -0.095f, -0.001f), new Vector2(0.07f, 0.045f), Ui.Accent, () => { _deck.Next(); RefreshDeck(); });
            RefreshDeck();
        }

        void RefreshDeck()
        {
            _deckText.text = _deck.IsEmpty
                ? "No saved words yet.\nPinch a word, then Save."
                : Ui.Wrap(_deck.Face, 20) + "\n\n" + $"({(_deck.Flipped ? "pinch card to flip back" : "pinch card to reveal")})  {_deck.Index + 1}/{_saved.Items.Count}";
        }

        async Task PlayDeckAudio()
        {
            if (_deck == null || _deck.IsEmpty) return;
            var lang = _deck.FaceLanguage ?? _lens.TargetLanguage;
            try { Play(await _api.SpeakAsync(_deck.Face, lang, _cts.Token)); } // audio links expire, so fetch fresh
            catch (OperationCanceledException) { }
            catch (Exception e) { _deckText.text = LensException.From(e).UserMessage(); }
        }

        // ---------- Editor shortcuts ----------

        // The flat "Sim Wall" backdrop is only for mouse testing in the editor. On a headset it would sit in front of
        // the real room, and Quest's OpenXR path often leaves XRSettings.isDeviceActive false, so a device build hides
        // the wall outright; in the editor it goes once the simulator (or Link) is running.
        GameObject _simWall;

        void Update()
        {
            if (_simWall != null && _simWall.activeSelf && (!Application.isEditor || UnityEngine.XR.XRSettings.isDeviceActive)) _simWall.SetActive(false);
            // A hand merely held in a pinch pose (common with simulator hands) must not pin the menu open or the HUD in place:
            // only a pinch that started on something counts.
            var pointer = PinchPointer.Instance;
            var pinchingSomething = pointer != null && pointer.Pressed != null;
            if (_launcherMenu != null && _linger.Update(_launcherMenu.gameObject.activeSelf, PointerOnLauncher, pinchingSomething, Time.realtimeSinceStartupAsDouble))
                ShowLauncher(false);
            FollowHead();
            if (_live) LiveTick();
            if (!Application.isEditor) return;
            if (Input.GetKeyDown(KeyCode.Space)) _ = CaptureAsync();
            if (Input.GetKeyDown(KeyCode.L)) ToggleLive();
            if (Input.GetKeyDown(KeyCode.H)) ShowOnboarding(_onboarding == null);
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
