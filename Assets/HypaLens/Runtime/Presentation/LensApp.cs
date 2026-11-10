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

    /// <summary>
    /// Lens scene entry point. Builds the HUD, word labels and result card at runtime; input comes from HypaCore's
    /// PinchPointer (hands on Quest, mouse in the editor).
    /// Editor keys: Space = capture, hold left mouse = pinch (&gt; 0.6 s = sentence), O = offline, N = no text, H = help.
    /// </summary>
    public sealed class LensApp : MonoBehaviour
    {
        [Tooltip("Fake camera frame, fake text recognition and a fixed wall. Turn off on device.")]
        [SerializeField] bool useSimulatedCamera = true;
        [Tooltip("Canned translations. Turn off to call the real Hypa API (needs a key in hypa.local.json).")]
        [SerializeField] bool useSimulatedApi = true;
        [SerializeField] float simWallDistance = 1.0f;

        static readonly Color LabelCol = new Color(1f, 0.85f, 0.2f, 0.28f);
        static readonly Color LabelSel = new Color(0.2f, 0.9f, 0.5f, 0.5f);

        LensController _lens; LensPlatform _p; IHypaLensApi _api;
        SimHypaApi _simApi; SimFrameSource _simFrames; SimPlanePlacer _simPlacer;
        SavedListStore _saved;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        Transform _cam, _hud, _world;
        TextMesh _status, _fromText, _toText;
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
            SetStatus("Look at some text, then pinch Capture.");
        }

        LensPlatform BuildPlatform()
        {
            LensPlatform p = null;
            if (useSimulatedCamera)
            {
                _simFrames = new SimFrameSource(); _simPlacer = new SimPlanePlacer();
                p = new LensPlatform { Frames = _simFrames, Recognizer = new SimRecognizer(), Placer = _simPlacer };
            }
            else if (LensPlatform.Factory != null) p = LensPlatform.Factory();
            p = p ?? new LensPlatform();
            p.Frames = p.Frames ?? new DeviceFrameSource();
            p.Recognizer = p.Recognizer ?? new UnwiredRecognizer();
            p.Placer = p.Placer ?? new UnwiredPlacer();
            return p;
        }

        // ---------- HUD: head-locked, low in a narrow field of view, within arm's reach ----------

        void BuildHud()
        {
            _hud = new GameObject("HypaLens HUD").transform;
            _hud.SetParent(_cam, false);
            _hud.localPosition = new Vector3(0f, -0.17f, 0.6f);
            Ui.Button("Capture", _hud, new Vector3(-0.21f, 0, 0), new Vector2(0.13f, 0.055f), Ui.Accent, () => _ = CaptureAsync());
            _fromText = Ui.LabelOf(Ui.Button("From", _hud, new Vector3(-0.065f, 0, 0), new Vector2(0.15f, 0.055f), Ui.Muted, () => OpenPicker(true)));
            _toText = Ui.LabelOf(Ui.Button("To", _hud, new Vector3(0.095f, 0, 0), new Vector2(0.15f, 0.055f), Ui.Muted, () => OpenPicker(false)));
            Ui.Button("Saved", _hud, new Vector3(0.23f, 0, 0), new Vector2(0.10f, 0.055f), Ui.Muted, TogglePractice);
            _status = Ui.Text(_hud, "", 0.018f, new Vector3(0f, 0.055f, 0), Color.white);
            UpdateLanguageText();
        }

        void SetStatus(string s) { if (_status != null) _status.text = Ui.Wrap(s, 60); }

        void UpdateLanguageText()
        {
            _fromText.text = "From: " + (_lens.SourceLanguage ?? "Auto");
            _toText.text = "To: " + _lens.TargetLanguage;
        }

        async Task LoadLanguages()
        {
            try { _langs = await _api.ListLanguagesAsync(_cts.Token); }
            catch (Exception e) { SetStatus(LensException.From(e).UserMessage()); }
        }

        void OpenPicker(bool source)
        {
            _picker?.Close();
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
            var bg = Ui.Quad("bg", _onboarding.transform, Vector3.zero, new Vector2(0.48f, 0.24f), Ui.Panel, true);
            Ui.Text(_onboarding.transform,
                "Welcome to Hypa Lens\n\n1. Look at a sign, menu or book\n2. Pinch  Capture\n3. Pinch a word to translate, hear and define it\n    Hold the pinch to pick the whole sentence\n\nHypa only sends the text you pinch. Photos never leave the headset.\n(pinch here to start)",
                0.017f, Vector3.zero, Color.white);
            var pt = bg.AddComponent<PinchTarget>();
            pt.OnPinch = k => { if (k == PinchKind.Short) ShowOnboarding(false); };
        }

        // ---------- Capture ----------

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

        int PlaceLabels(OcrResult ocr)
        {
            var meta = _lens.LastFrameMeta;
            if (useSimulatedCamera) _simPlacer.SetWallFromPose(meta.CameraPose, simWallDistance);
            var placed = 0;
            foreach (var line in ocr.Lines)
                foreach (var w in line.Words)
                {
                    // Ray from the camera pose AT CAPTURE TIME, so labels stay on the text after the head moves.
                    var ray = FrameProjector.PixelToWorldRay(meta, w.Box.Center);
                    if (!_p.Placer.TryPlace(ray, out var pos, out var normal)) continue;
                    var dist = Vector3.Distance(ray.origin, pos);
                    if (dist < 0.1f || dist > 4f) continue; // implausible hit: drop it rather than float it
                    if (Vector3.Dot(normal, -ray.direction) < 0) normal = -normal;
                    var size = FrameProjector.PixelSizeToWorld(meta.Intrinsics, w.Box, dist) * 1.1f;
                    size = new Vector2(Mathf.Max(size.x, 0.03f), Mathf.Max(size.y, 0.035f)); // small print stays pinchable

                    var go = Ui.Quad("Word " + w.Text, _world, Vector3.zero, size, LabelCol, true);
                    go.transform.position = pos + normal * 0.003f;
                    go.transform.rotation = FrameProjector.FaceViewer(normal, Vector3.up);
                    var label = go.AddComponent<WordLabel>();
                    label.Word = w;
                    label.Target = go.AddComponent<PinchTarget>();
                    label.Target.OnPinch = k => OnWordPinch(label, k);
                    label.Target.OnHover = on => { if (!_selected.Contains(label)) label.Target.SetColor(on ? Ui.Hover : (Color?)null); };
                    _labels.Add(label);
                    placed++;
                }
            return placed;
        }

        void ClearLabels()
        {
            foreach (var l in _labels) if (l != null) Destroy(l.gameObject);
            _labels.Clear(); _selected.Clear();
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
                });
                if (version != _resultVersion || body == null) return; // a newer selection replaced this card
                body.text = CardText(r);
                AddCardButtons(r);
                Play(r.TargetAudio);
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
            var save = Ui.Button(_saved.Contains(r.Selection.Text, r.TargetLanguage) ? "Saved" : "Save", t, new Vector3(0.05f, y, z), w, new Color(.1f, .6f, .35f), null);
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

        void Update()
        {
            if (!Application.isEditor) return;
            if (Input.GetKeyDown(KeyCode.Space)) _ = CaptureAsync();
            if (Input.GetKeyDown(KeyCode.H)) ShowOnboarding(_onboarding == null);
            if (Input.GetKeyDown(KeyCode.O) && _simApi != null) { _simApi.Offline = !_simApi.Offline; SetStatus("Sim offline: " + _simApi.Offline); }
            if (Input.GetKeyDown(KeyCode.N) && _simFrames != null) { _simFrames.ReturnBlankFrame = !_simFrames.ReturnBlankFrame; SetStatus("Sim blank frame: " + _simFrames.ReturnBlankFrame); }
        }

        void OnDestroy()
        {
            _cts.Cancel();
            _cts.Dispose();
            if (_hud != null) Destroy(_hud.gameObject);    // the HUD hangs off the camera, which the shell keeps alive
            if (_world != null) Destroy(_world.gameObject);
        }
    }
}
