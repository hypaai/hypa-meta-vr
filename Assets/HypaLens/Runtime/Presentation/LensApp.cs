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
        public const float ShutterRadius = 0.046f;
        public const float SmallIconRadius = 0.028f;
        public const float PillH = 0.05f;
        public const float PickerRowW = 0.18f;
        public const float PickerRowH = 0.05f;
        public const float PickerPitch = 0.058f;
    }

    /// <summary>
    /// Lens scene entry point. Builds the HUD, word labels and cards at runtime; input comes from HypaCore's
    /// PinchPointer (hands on Quest, mouse in the editor).
    ///
    /// The HUD follows the Hypa mobile Lens: a top bar (X, "Auto" and the target language, Saved), a hint pill, and a
    /// bottom row of round controls: Capture (reads the text in view on the headset; the words open in a popup to pinch),
    /// Caption (Hypa reads and translates the whole view; the photo leaves the headset), Speak and Clear. Results always
    /// show in one popup (see "Results popup"). X leaves Lens for the shell's Home card.
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
        PinchTarget _sourceBtn, _targetBtn, _captureBtn, _captionBtn, _speakBtn;
        GameObject _onboarding, _practice, _liveBadge;
        readonly List<GameObject> _bottomRow = new List<GameObject>();
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
            _api = UseSimulated(useSimulatedApi, Application.isEditor) ? (_simApi = new SimHypaApi()) : (IHypaLensApi)new HypaCoreLensApi();
            _lens = new LensController(_p.Frames, _p.Recognizer, _api,
                () => _simApi != null || Application.internetReachability != NetworkReachability.NotReachable)
            {
                TargetLanguage = HypaSettings.TargetLanguage,
                SourceLanguage = null, // always auto-detect (Hypa: sourceLang left out); never picked, never changed
            };

            BuildHud();
            _ = Loading(LoadLanguages, _targetBtn);
            if (AppRouter.Consume(AppRouter.LensSavedWords)) TogglePractice();
            else if (!LensPrefs.Onboarded) ShowOnboarding(true);
            SetStatus(DefaultHint);
        }

        /// <summary>
        /// The example camera, words and translations are for the editor only. On the headset Lens always reads the real
        /// view and calls the real Hypa API, whatever the scene's checkboxes say. Pure, unit tested.
        /// </summary>
        public static bool UseSimulated(bool setting, bool isEditor) => setting && isEditor;

        LensPlatform BuildPlatform()
        {
            LensPlatform p = null;
            _simCam = UseSimulated(useSimulatedCamera, Application.isEditor); // a device build never fakes the camera
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

        // ---------- HUD placement: follows where the head points (see HypaCore.HeadFollow) ----------

        /// <summary>The HUD was designed in metres at 0.6 m; it sits further out and is scaled up to the same angular size.</summary>
        public const float HudDesignDistance = 0.6f;
        // 1 m out (beyond the hands, so a hand ray crosses the HUD), centred on the line of sight and square to it, up or
        // down: looking up at the ceiling, the HUD still reads as if looking straight ahead. A small dead zone so a hand
        // ray can aim at a control; past it a soft spring brings it back to the centre.
        readonly HeadFollow _follow = new HeadFollow { Distance = 1.0f, Height = -0.22f, FollowPitch = true, Threshold = 8f, SmoothTime = 0.28f };

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
            var pose = _follow.Update(_cam.position, _cam.rotation, hold, Time.unscaledDeltaTime);
            _hud.SetPositionAndRotation(pose.position, pose.rotation);
        }

        // ---------- HUD: top bar, hint, bottom controls (the mobile Lens layout) ----------

        // More room between the top bar and the bottom row than before (they read as cramped). The HUD's centre,
        // (TopY + BottomY) / 2 = 0.13 at 1.67x scale, sits on the line of sight (HeadFollow Height -0.22).
        const float TopY = 0.33f, HintY = 0.25f, CardY = 0.12f, BottomY = -0.07f;

        void BuildHud()
        {
            _hud = new GameObject("HypaLens HUD").transform;
            _hud.SetParent(_world, false);
            _hud.localScale = Vector3.one * (_follow.Distance / HudDesignDistance); // same angular size as at 0.6 m
            FollowHead(); // first placement: straight in front of the eyes

            // Top bar: X | Auto-detect → Target ▾ | Saved. The source is always auto-detected: its pill is a fixed label.
            Tooltip(Ui.IconButton("x", _hud, new Vector3(-0.32f, TopY, 0), LensTouch.SmallIconRadius, GoHome), "Leave Lens", below: true);
            _sourceBtn = Ui.Button("Auto-detect", _hud, new Vector3(-0.11f, TopY, 0), new Vector2(SourcePillW, LensTouch.PillH), Ui.Muted, null);
            Tooltip(_sourceBtn, "Hypa detects the language of the text.", below: true);
            Ui.Text(_hud, "→", 0.02f, new Vector3(0.0f, TopY, 0), new Color(1f, 1f, 1f, 0.7f));
            _targetBtn = LanguagePill(new Vector3(0.125f, TopY, 0), TargetPillW, () => OpenPicker(false));
            var savedBtn = Ui.IconButton("bookmark", _hud, new Vector3(0.32f, TopY, 0), LensTouch.SmallIconRadius, TogglePractice);
            Tooltip(savedBtn, "Your saved words, as practice cards.", below: true);

            // Hint pill (the status), centred under the top bar.
            _status = Ui.Text(_hud, "", StatusLineH, new Vector3(0f, HintY, 0), new Color(1f, 1f, 1f, 0.95f));

            // Bottom row: Capture big in the middle (the shutter), the rest spread either side, each with a tooltip.
            const float r = LensTouch.IconRadius, pitch = 0.19f;
            // Clear is off for now (the popup's X closes results). Kept for when it comes back.
            // var clear = Ui.IconButton("eraser", _hud, new Vector3(-2f * pitch, BottomY, 0), r, ClearAll, "Clear");
            // Tooltip(clear, "Clear the results.");
            Tooltip(_captionBtn = Ui.IconButton("languages", _hud, new Vector3(-pitch, BottomY, 0), r, () => _ = CaptionAsync(), "Caption"),
                "Sends a photo of your view to Hypa to read\nand translate it as a whole. Not stored.");
            Tooltip(_captureBtn = Ui.IconButton("camera", _hud, new Vector3(0f, BottomY, 0), LensTouch.ShutterRadius, () => _ = CaptureAsync(), "Capture", () => _ = CaptureAsync(), Ui.Primary),
                "Reads the text in view on the headset.\nThe words open in a popup: pinch one.");
            Tooltip(_speakBtn = Ui.IconButton("volume-2", _hud, new Vector3(pitch, BottomY, 0), r, SpeakLast, "Speak"), "Hear the last translation.");
            foreach (var b in new[] { _captionBtn, _captureBtn, _speakBtn }) _bottomRow.Add(b.transform.parent.gameObject);

            // Live-mode badge (dev-only toggle for now; the mode itself is not in the UI yet).
            _liveBadge = Ui.Quad("live badge", _hud, new Vector3(0f, BottomY + r * 1.9f, 0f), new Vector2(0.075f, 0.026f), Ui.Accent, false, 0.013f);
            Ui.Text(_liveBadge.transform, "● LIVE", 0.012f, Vector3.zero, Ui.TextColorFor(Ui.Accent));
            _liveBadge.SetActive(false);

            HypaShell.Instance?.SetMenuButtonVisible(false); // the X replaces the shell's Home button here
            UpdateLanguageText();
        }

        /// <summary>A small note that appears above (or below) a control while the pointer rests on it.</summary>
        static void Tooltip(PinchTarget btn, string text, bool below = false)
        {
            var root = btn.transform.parent;
            var lines = text.Split('\n');
            var longest = 0;
            foreach (var l in lines) longest = Mathf.Max(longest, l.Length);
            const float lineH = 0.013f, pad = 0.012f;
            var size = new Vector2(Ui.TextWidth(new string('x', longest), lineH) + pad * 2f, lines.Length * lineH * 1.1f + pad * 1.3f);
            var reach = btn.transform.lossyScale.x / Mathf.Max(1e-4f, root.lossyScale.x); // the button's radius in root units
            var y = (below ? -1f : 1f) * (reach * 1.25f + size.y * 0.5f + 0.01f);
            var tip = Ui.Quad("tooltip", root, new Vector3(0f, y, -0.004f), size, Ui.Panel, false);
            Ui.Text(tip.transform, text, lineH, Vector3.zero, new Color(1f, 1f, 1f, 0.95f));
            tip.SetActive(false);
            var hover = btn.OnHover;
            btn.OnHover = on => { hover?.Invoke(on); tip.SetActive(on); };
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
            _picker?.Close(); ShowOnboarding(false); ClosePopup();
            if (_practice != null) { Destroy(_practice); _practice = null; }
            if (HypaShell.Instance != null) HypaShell.Instance.ExitToHome(); // leaves Lens for the entry screen
            else SetStatus("Home needs the Hypa shell. Play from the HypaShell scene.");
        }

        bool OtherViewOpen => (_picker != null && _picker.IsOpen) || _onboarding != null || _practice != null || _popup != null
                              || (HypaShell.Instance != null && HypaShell.Instance.IsMenuOpen);

        /// <summary>Onboarding, practice, the picker, the caption and the consent card share the spot above the bar: one at a time.</summary>
        void CloseCards()
        {
            _picker?.Close();
            ShowOnboarding(false);
            ClosePopup();
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
            Ui.IconButton("x", go.transform, new Vector3(size.x * 0.5f - 0.045f, top, -0.001f), 0.022f, onClose); // inside the card's inner edge
            return go;
        }

        const float StatusLineH = 0.016f, StatusPad = 0.014f;
        const int StatusWrap = 44;

        const string DefaultHint = "Point at text, then pinch Capture.";
        const float ErrorSeconds = 5f;
        float _errorUntil = -1f;

        /// <summary>An error or warning in the hint pill for <see cref="ErrorSeconds"/>, then back to the default hint.</summary>
        void ShowError(string s)
        {
            SetStatus(s);
            _errorUntil = Time.unscaledTime + ErrorSeconds;
        }

        /// <summary>
        /// Runs work that calls Hypa or the camera with every given button in its loading state (spinner, pinches
        /// ignored) until it finishes, whatever the outcome.
        /// </summary>
        static async Task Loading(Func<Task> work, params PinchTarget[] buttons)
        {
            foreach (var b in buttons) Ui.SetBusy(b, true);
            try { await work(); }
            finally { foreach (var b in buttons) Ui.SetBusy(b, false); }
        }

        void SetStatus(string s)
        {
            _errorUntil = -1f; // a new message replaces a pending error
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
            _statusBg.SetActive(_popup == null);
        }

        const float SourcePillW = 0.15f, TargetPillW = 0.20f;

        void UpdateLanguageText()
        {
            var lineH = LensTouch.PillH * 0.42f * 0.75f; // the smallest the label is allowed to shrink to
            Ui.SetLabel(_sourceBtn, "Auto-detect");
            Ui.SetLabel(_targetBtn, LanguageLabel("", _lens.TargetLanguage, lineH, TargetPillW - 0.05f) + "  ");
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
            catch (Exception e) { ShowError(LensException.From(e).UserMessage()); }
        }

        void OpenPicker(bool source)
        {
            CloseCards();
            if (_langs.Count == 0) { SetStatus("Loading languages..."); _ = Loading(LoadLanguages, _sourceBtn, _targetBtn); return; }
            _picker = new LanguagePicker(_hud, new Vector3(0f, CardY, -0.01f), source ? "Translate from" : "Translate into",
                _langs, source ? _lens.SourceLanguage : _lens.TargetLanguage, source, name => _ = Loading(() => SetLanguage(source, name), source ? _sourceBtn : _targetBtn));
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
                ShowError(new LensException(LensErrorKind.UnsupportedPair).UserMessage(_lens.SourceLanguage, _lens.TargetLanguage));
            }
            catch (Exception e) { ShowError(LensException.From(e).UserMessage()); }
        }

        void Apply(bool source, string name)
        {
            if (source) return; // the source is always auto-detect: it is never set
            if (name != null) { _lens.TargetLanguage = name; HypaSettings.TargetLanguage = name; } // remembered
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
                "1. Look at a sign, menu or book and pinch Capture.\n2. The words open in a popup. Pinch one to translate, hear and\n    define it; hold the pinch for the whole line.\n3. Caption reads the whole view at once. Speak reads the last result aloud.\n\nPick the language to translate into at the top. Save words with the bookmark.\nHypa only sends the text you pinch (Caption sends the photo).",
                0.016f, new Vector3(-0.23f, -0.02f, 0), new Color(1f, 1f, 1f, 0.92f), TextAnchor.MiddleLeft);
        }

        // ---------- Capture (photo mode: pinchable labels, recognised on the headset) ----------

        Task CaptureAsync() => _busy ? Task.CompletedTask : Loading(CaptureWorkAsync, _captureBtn, _captionBtn);

        async Task CaptureWorkAsync()
        {
            if (_busy) return;
            _busy = true;
            CloseCards();
            ClearLabels();
            SetStatus("Reading text...");
            try
            {
                var ocr = await _lens.CaptureAsync(_cts.Token);
                var count = 0;
                foreach (var l in ocr.Lines) count += l.Words.Count;
                if (count == 0) ShowError("No text found. Point at a sign, menu or page and try again.");
                else { ShowWords(ocr); SetStatus($"{count} words found."); }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { ShowError(LensException.From(e).UserMessage()); }
            finally { _busy = false; }
        }

        // ---------- Caption (Hypa reads and translates the whole view; the photo leaves the headset, as the tooltip says) ----------

        Task CaptionAsync() => _busy ? Task.CompletedTask : Loading(CaptionWorkAsync, _captionBtn, _captureBtn);

        async Task CaptionWorkAsync()
        {
            if (_busy) return;
            _busy = true;
            CloseCards();
            SetStatus("Sending the view to Hypa...");
            try
            {
                // The simulator's fake camera has no pixels: send a snapshot of the rendered view instead, so Caption
                // can be tried end to end in the editor (it reads whatever text is in the virtual room).
                var r = await _lens.CaptionAsync(f => FrameEncoder.ToJpeg(f) ?? (Application.isEditor ? FrameEncoder.ViewSnapshotJpeg(_hud, PinchPointer.Instance != null ? PinchPointer.Instance.transform : null, HypaShell.Instance != null ? HypaShell.Instance.UiRoot : null) : null), _cts.Token);
                ShowCaption(r);
                SetStatus("Caption ready. Pinch Speak to hear it.");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { ShowError(LensException.From(e).UserMessage(_lens.SourceLanguage, _lens.TargetLanguage)); }
            finally { _busy = false; }
        }

        void ShowCaption(HypaImageTranslation r)
        {
            if (r == null) { ClosePopup(); return; }
            CloseCards();
            _popupOcr = null; // a caption has no words to go back to
            _lastSpeech = null; _lastSpeechText = r.Translation; _lastSpeechLang = _lens.TargetLanguage;
            var c = PopupContent("Caption");
            var y = PopupTop - 0.05f;
            RenderResult(c, ref y, r.Text, r.Language ?? _lens.SourceLanguage, r.Translation, _lens.TargetLanguage, null);
            var row = new List<(string, Color, Action<PinchTarget>)>
            {
                ("Speak", Ui.Accent, b => _ = Loading(SpeakLastAsync, b, _speakBtn)),
                (_saved.Contains(r.Text, _lens.TargetLanguage) ? "Saved" : "Save", Ui.Muted, b =>
                {
                    _saved.Add(new LensResult
                    {
                        Selection = new Selection { Kind = SelectionKind.Sentence, Text = r.Text },
                        SourceLanguage = r.Language ?? _lens.SourceLanguage, TargetLanguage = _lens.TargetLanguage, Translation = r.Translation,
                    });
                    Ui.SetLabel(b, "Saved");
                }),
            };
            ButtonRow(c, row);
        }

        /// <summary>Speak: the last translation heard (a pinched word's audio, or a caption, fetched fresh as audio links expire).</summary>
        void SpeakLast() => _ = Loading(SpeakLastAsync, _speakBtn);

        async Task SpeakLastAsync()
        {
            if (_lastSpeech != null) { await PlayAsync(_lastSpeech); return; }
            if (string.IsNullOrEmpty(_lastSpeechText)) { ShowError("Pinch a word or take a caption first, then Speak."); return; }
            try { await PlayAsync(await _api.SpeakAsync(_lastSpeechText, _lastSpeechLang, _cts.Token)); }
            catch (OperationCanceledException) { }
            catch (Exception e) { ShowError("Couldn't play audio: " + LensException.From(e).UserMessage()); }
        }

        void ClearAll()
        {
            ClearLabels(); ClosePopup();
            _lastSpeech = null; _lastSpeechText = null;
            SetStatus(DefaultHint);
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
            if (on) { CloseCards(); ClearLabels(); }
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
                ShowError(LensException.From(e).UserMessage());
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
            _popupOcr = null; // live-mode labels: no word list to go back to
            _ = ShowTranslation(sel);
        }

        void Play(HypaSpeech speech) => _ = Loading(() => PlayAsync(speech), _speakBtn);

        /// <summary>Downloads (if needed) and starts the audio; returns once it is playing.</summary>
        async Task PlayAsync(HypaSpeech speech)
        {
            if (speech == null) return;
            try { await HypaAudioPlayer.Instance.PlayNowAsync(speech, _cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { ShowError("Couldn't play audio: " + LensException.From(e).UserMessage()); }
        }

        // ---------- Results popup: every result (captured words, a translation, a caption) in one card ----------
        //
        // Modal, in the middle of the HUD: while it is up the bottom row and the hint step aside, so nothing overlaps.
        // "Text found" lists the captured words as chips, line by line; pinching one switches the popup to its
        // translation (hold the pinch for the whole line), with Back to the words.

        const float PopupW = 0.62f, PopupH = 0.40f, PopupY = 0.075f, PopupPad = 0.03f; // clear of the top bar (its pills end at 0.305)
        const float PopupTop = PopupH / 2f - PopupPad - 0.012f, PopupLeft = -PopupW / 2f + PopupPad;
        const float ChipH = 0.042f, ChipGap = 0.009f, ChipRowGap = 0.011f;
        static readonly Color PopupMuted = new Color(1f, 1f, 1f, 0.62f);
        static readonly Color ChipBg = new Color(0.21f, 0.21f, 0.21f, 1f); // lighter than the panel, so every chip reads as a button

        GameObject _popup; Transform _popupContent; TextMesh _popupTitle;
        OcrResult _popupOcr;
        readonly List<(OcrWord word, PinchTarget chip)> _chips = new List<(OcrWord, PinchTarget)>();

        /// <summary>Opens the popup (or reuses it) with a fresh, empty content area under <paramref name="title"/>.</summary>
        Transform PopupContent(string title)
        {
            if (_popup == null)
            {
                _popup = new GameObject("Results Popup");
                _popup.transform.SetParent(_hud, false);
                _popup.transform.localPosition = new Vector3(0f, PopupY, -0.012f);
                Ui.Quad("bg", _popup.transform, new Vector3(0, 0, 0.003f), new Vector2(PopupW, PopupH), Ui.Panel, true).AddComponent<PinchTarget>();
                _popupTitle = Ui.Text(_popup.transform, "", 0.022f, new Vector3(PopupLeft, PopupTop, 0), Color.white, TextAnchor.MiddleLeft);
                // Inside the card's inner edge, level with the title: the corner was too tight before.
                Ui.IconButton("x", _popup.transform, new Vector3(PopupW / 2f - PopupPad - 0.022f, PopupTop, -0.001f), 0.022f, ClosePopup);
                _popup.AddComponent<LabelPop>(); // eases in
                SetPopupMode(true);
            }
            _popupTitle.text = title;
            if (_popupContent != null) Destroy(_popupContent.gameObject);
            _popupContent = new GameObject("content").transform;
            _popupContent.SetParent(_popup.transform, false);
            _chips.Clear();
            return _popupContent;
        }

        void ClosePopup()
        {
            if (_popup == null) return;
            ++_resultVersion; // a translation still in flight no longer has anywhere to go
            Destroy(_popup);
            _popup = null; _popupContent = null; _chips.Clear();
            foreach (var s in _selected) if (s != null) s.Target.SetColor(null);
            _selected.Clear();
            SetPopupMode(false);
        }

        /// <summary>The bottom row and the hint step aside while the popup is up.</summary>
        void SetPopupMode(bool open)
        {
            foreach (var go in _bottomRow) if (go != null) go.SetActive(!open);
            if (_status != null) _status.gameObject.SetActive(!open);
            if (_statusBg != null) _statusBg.SetActive(!open);
        }

        /// <summary>"Text found": the captured words as pinchable chips, one row (or more) per line of text.</summary>
        void ShowWords(OcrResult ocr)
        {
            _popupOcr = ocr;
            var c = PopupContent("Text found");
            var y = PopupTop - 0.042f;
            Ui.Text(c, "Pinch a word to translate it. Hold the pinch for the whole line.", 0.0135f, new Vector3(PopupLeft, y, 0), PopupMuted, TextAnchor.MiddleLeft);
            y -= 0.03f;

            var lines = new List<IList<string>>();
            foreach (var l in ocr.Lines) { var ws = new List<string>(); foreach (var w in l.Words) ws.Add(w.Text); lines.Add(ws); }
            var bottom = -PopupH / 2f + PopupPad;
            var chips = LayoutChips(lines, PopupW - PopupPad * 2f, y - bottom, out _);
            foreach (var chip in chips)
            {
                var word = ocr.Lines[chip.Line].Words[chip.Index];
                var pos = new Vector3(PopupLeft + chip.Center.x, y - chip.Center.y, -0.001f);
                var pt = Ui.Button(word.Text, c, pos, chip.Size, ChipBg, null);
                pt.OnPinch = k =>
                {
                    if (k == PinchKind.None) return;
                    foreach (var (_, other) in _chips) other.SetColor(null);
                    var sel = k == PinchKind.Long ? LensController.SelectSentence(word) : LensController.SelectWord(word);
                    _ = ShowTranslation(sel);
                };
                _chips.Add((word, pt));
            }
        }

        public struct ChipSlot { public int Line, Index; public Vector2 Center, Size; }

        /// <summary>
        /// Lays the words out as chips, left to right, a new row for each line of text and wherever a row runs out of
        /// <paramref name="width"/>. Shrinks everything evenly (to no less than 55%) so it fits <paramref name="height"/>.
        /// Centres are relative to the top-left of the area, y downwards. Pure, unit tested.
        /// </summary>
        public static List<ChipSlot> LayoutChips(IList<IList<string>> lines, float width, float height, out float scale)
        {
            scale = 1f;
            List<ChipSlot> slots = null;
            for (var pass = 0; pass < 6; pass++)
            {
                slots = new List<ChipSlot>();
                float h = ChipH * scale, gap = ChipGap * scale, rowGap = ChipRowGap * scale, textH = h * 0.42f;
                float x = 0f, rowTop = 0f;
                for (var li = 0; li < lines.Count; li++)
                {
                    if (li > 0) { x = 0f; rowTop += h + rowGap; }
                    for (var wi = 0; wi < lines[li].Count; wi++)
                    {
                        var w = Mathf.Min(width, Ui.TextWidth(lines[li][wi], textH) * 1.08f + h * 0.8f);
                        if (x > 0f && x + w > width) { x = 0f; rowTop += h + rowGap; }
                        slots.Add(new ChipSlot { Line = li, Index = wi, Center = new Vector2(x + w / 2f, rowTop + h / 2f), Size = new Vector2(w, h) });
                        x += w + gap;
                    }
                }
                var total = slots.Count == 0 ? 0f : rowTop + h;
                if (total <= height || scale <= 0.55f) break;
                scale = Mathf.Max(0.55f, scale * Mathf.Sqrt(height / total));
            }
            return slots;
        }

        /// <summary>The popup's translation view for a word or a line: fills in as Hypa answers, then offers Hear and Save.</summary>
        async Task ShowTranslation(Selection sel)
        {
            var version = ++_resultVersion;
            var back = _popupOcr;
            void Render(LensResult r, string error)
            {
                if (version != _resultVersion) return;
                var c = PopupContent(sel.Kind == SelectionKind.Sentence ? "Translation · line" : "Translation");
                var y = PopupTop - 0.05f;
                var translation = error ?? r?.Translation ?? $"Translating to {_lens.TargetLanguage}...";
                RenderResult(c, ref y, sel.Text, r?.SourceLanguage ?? _lens.SourceLanguage, translation, _lens.TargetLanguage,
                    error == null ? (r?.Meaning ?? r?.MeaningNote) : null);
                var row = new List<(string, Color, Action<PinchTarget>)>();
                if (back != null) row.Add(("Back", Ui.Muted, _ => ShowWords(back)));
                if (r != null && error == null && r.Translation != null)
                {
                    if (r.SourceAudio != null) row.Add(("Hear", Ui.Accent, b => _ = Loading(() => PlayAsync(r.SourceAudio), b)));
                    if (r.TargetAudio != null) row.Add(("Hear →", Ui.Accent, b => _ = Loading(() => PlayAsync(r.TargetAudio), b)));
                    row.Add((_saved.Contains(r.Selection.Text, r.TargetLanguage) ? "Saved" : "Save", Ui.Muted, b => { _saved.Add(r); Ui.SetLabel(b, "Saved"); }));
                }
                ButtonRow(c, row);
            }
            Render(null, null);
            try
            {
                var r = await _lens.ResolveAsync(sel, _cts.Token, partial => Render(partial, null), audio =>
                {
                    if (version == _resultVersion) { _lastSpeech = audio; Play(audio); }
                });
                if (version != _resultVersion) return; // closed, or a newer selection replaced it
                _lastSpeech = r.TargetAudio; _lastSpeechText = r.Translation; _lastSpeechLang = r.TargetLanguage;
                Render(r, null);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Render(null, LensException.From(e).UserMessage(_lens.SourceLanguage, _lens.TargetLanguage)); }
        }

        /// <summary>Source text (small, quoted), the translation (large), the language pair, and the meaning if any.</summary>
        void RenderResult(Transform c, ref float y, string source, string sourceLang, string translation, string targetLang, string meaning)
        {
            Block(c, $"\"{source}\"", 0.016f, PopupMuted, ref y, 2);
            Block(c, translation, 0.026f, Color.white, ref y, 3);
            Block(c, $"{sourceLang ?? "Auto"}  →  {targetLang}", 0.0125f, PopupMuted, ref y, 1);
            if (!string.IsNullOrEmpty(meaning)) Block(c, meaning, 0.0135f, new Color(1f, 1f, 1f, 0.8f), ref y, 4);
        }

        /// <summary>Wrapped text at the left of the popup, at most <paramref name="maxLines"/> lines, moving <paramref name="y"/> down.</summary>
        static void Block(Transform c, string text, float lineH, Color color, ref float y, int maxLines)
        {
            if (string.IsNullOrEmpty(text)) return;
            var chars = Mathf.Max(8, Mathf.FloorToInt((PopupW - PopupPad * 2f) / (lineH * Ui.CharWidth)) - 4);
            var lines = Ui.Wrap(text, chars).Split('\n');
            if (lines.Length > maxLines)
            {
                Array.Resize(ref lines, maxLines);
                lines[maxLines - 1] = lines[maxLines - 1].TrimEnd() + "…";
            }
            Ui.Text(c, string.Join("\n", lines), lineH, new Vector3(PopupLeft, y, 0), color, TextAnchor.UpperLeft);
            y -= lines.Length * lineH * 1.28f + 0.014f; // a TextMesh line (Inter, 1.08 spacing) is ~1.28 line heights tall
        }

        /// <summary>Buttons spread along the bottom of the popup.</summary>
        static void ButtonRow(Transform c, List<(string label, Color color, Action<PinchTarget> onPress)> row)
        {
            if (row.Count == 0) return;
            const float h = 0.048f, gap = 0.014f;
            var w = Mathf.Min(0.14f, (PopupW - PopupPad * 2f - gap * (row.Count - 1)) / row.Count);
            var x = -(w * row.Count + gap * (row.Count - 1)) / 2f + w / 2f;
            var y = -PopupH / 2f + PopupPad + h / 2f;
            foreach (var (label, color, onPress) in row)
            {
                PinchTarget b = null;
                b = Ui.Button(label, c, new Vector3(x, y, -0.001f), new Vector2(w, h), color, () => onPress(b));
                x += w + gap;
            }
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
            PinchTarget hear = null;
            hear = Ui.Button("Hear", t, new Vector3(0f, -0.11f, -0.001f), new Vector2(0.10f, 0.045f), Ui.Accent, () => _ = Loading(PlayDeckAudio, hear));
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
            try { await PlayAsync(await _api.SpeakAsync(_deck.Face, lang, _cts.Token)); } // audio links expire, so fetch fresh
            catch (OperationCanceledException) { }
            catch (Exception e) { _deckText.text = LensException.From(e).UserMessage(); }
        }

        // ---------- Per frame ----------

        // The flat "Sim Wall" backdrop is only for mouse testing in the editor. On a headset it would sit in front of
        // the real room, and Quest's OpenXR path often leaves XRSettings.isDeviceActive false, so a device build hides
        // the wall outright; in the editor it goes once the simulator (or Link) is running.
        GameObject _simWall;

        // After the camera rig has taken this frame's head pose, so the HUD doesn't trail it by a frame.
        void LateUpdate()
        {
            var home = HypaShell.Instance != null && HypaShell.Instance.IsMenuOpen;
            if (home) _follow.Reset(); // when Home closes the HUD comes back straight in front
            else FollowHead();
        }

        void Update()
        {
            if (_simWall != null && _simWall.activeSelf && (!Application.isEditor || UnityEngine.XR.XRSettings.isDeviceActive)) _simWall.SetActive(false);
            // Home (the shell's card) is modal: the Lens HUD steps aside while it is up.
            var home = HypaShell.Instance != null && HypaShell.Instance.IsMenuOpen;
            if (_hud != null && _hud.gameObject.activeSelf == home) _hud.gameObject.SetActive(!home);
            if (_errorUntil > 0f && Time.unscaledTime >= _errorUntil) SetStatus(DefaultHint);
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
