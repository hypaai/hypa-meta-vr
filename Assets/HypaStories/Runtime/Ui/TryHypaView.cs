using System;
using System.Collections.Generic;
using HypaCore;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// "Try Hypa": Hypa Translate (translate.hypaintelligence.com) in the story player's dark glass and ember, as a 3D screen at the pulpit:
    /// two stacked cards that lean in toward you, with wells, buttons and text floating at their own depth (and buttons that lift toward you on hover).
    /// Same layout as the web app: logo header, language pills with a round swap button, an input card (Voice selected, a record zone,
    /// mic and Translate buttons) and a translation card (Hypa icon, "Translated to X", the speaker, the dictionary icon, the voice
    /// menu), and the dictionary panel with a tab per word. Voice only: no keyboard, no image translation.
    /// Laid out in virtual pixels (1000 x 640) scaled to metres. The logic is <see cref="TryHypaFlow"/>; this only draws it.
    /// </summary>
    public sealed class TryHypaView
    {
        public const float S = 0.0011f; // metres per virtual pixel
        const float PW = 800f, PH = 790f;
        const int PageSize = 15;
        enum Pick { Source, Target, Voice }

        public readonly Transform Root;
        public readonly KitGroup Fade;
        public readonly FollowView Follow;

        readonly TryHypaFlow _f;
        readonly Action _close;
        readonly Transform _main, _in, _result, _dict, _picker;
        readonly Typewriter _typeIn = new Typewriter { CharsPerSecond = 55f }, _typeOut = new Typewriter { CharsPerSecond = 110f };
        readonly List<KitButton> _grid = new List<KitButton>();
        readonly Section[] _sections = new Section[6];
        readonly List<string> _labels = new List<string>();
        readonly List<HypaLanguage> _langs = new List<HypaLanguage>();

        KitButton _speakIn, _copyIn, _copyOut, _think;
        KitIcon _thinkIcon;
        TextMeshPro _thinkTitle, _thinkText;
        KitButton _srcPill, _swap, _dstPill, _mic, _clear, _run, _speakOut, _dictBtn, _voicePill, _prevPage, _nextPage;
        KitButton _tabS, _tabT, _dictClose, _dictSpeak, _retry;
        TextMeshPro _inText, _zoneText, _count, _note, _outTitle, _outText, _outHint, _pickTitle, _pageText;
        TextMeshPro _dWord, _dMeta, _dState;
        KitIcon _zoneIcon;
        TextMeshPro _zoneSub, _timer;
        float _listenStart;
        bool _wasListening;
        KitPanel _zone, _level;
        Pick _pick;
        int _page;

        sealed class Section
        {
            public TextMeshPro Title, Body;
            public KitButton Speak;
        }

        static KitIcon Dark(KitIcon i) { i.Color = KitTheme.Ember; i.Apply(); return i; } // the Hypa marks in ember, as on the story player

        // the story player's glass: cards float above the panel, wells sink into the cards, buttons and text float above those
        static readonly Color CardTop = new Color(1f, 0.95f, 0.88f, 0.07f), CardBottom = new Color(1f, 0.95f, 0.88f, 0.035f);
        static readonly Color WellTop = new Color(0f, 0f, 0f, 0.30f), WellBottom = new Color(0f, 0f, 0f, 0.20f), WellEdge = new Color(1f, 0.9f, 0.8f, 0.07f);
        static readonly Color Rec = new Color(1f, 0.38f, 0.36f), Err = new Color(1f, 0.52f, 0.46f);
        const float ZCard = -0.010f, ZWell = -0.006f, ZBtn = -0.018f, ZTxt = -0.016f; // metres toward you
        const float Lean = 6f; // degrees the top card leans down and the bottom card leans up toward you
        static readonly Vector3 InC = V(400, 296), OutC = V(400, 592), DictC = V(400, 444);

        static Vector3 V(float cx, float cy, float z = 0f) => new Vector3((cx - PW / 2f) * S, (PH / 2f - cy) * S, z);
        static Vector2 Z(float w, float h) => new Vector2(w * S, h * S);
        static float F(float px) => px * S;

        public TryHypaView(Transform uiRoot, TryHypaFlow flow, Action close)
        {
            _f = flow; _close = close;
            Root = new GameObject("Try Hypa").transform;
            Root.SetParent(uiRoot, false);
            Follow = Root.gameObject.AddComponent<FollowView>();
            // at the pulpit, where the story player sits, but closer and a little higher so the whole card is in view
            Follow.Distance = 1.05f; Follow.Height = -0.12f; Follow.Threshold = 40f;

            Kit.OrderBoost = 40;
            var shell = Kit.Panel(Root, "Panel", new Vector3(0f, 0f, 0.006f), Z(PW, PH), F(30), KitTheme.GlassTop, KitTheme.GlassBottom, KitTheme.Hairline, 9, 0.8f);
            shell.Glow = new Color(KitTheme.Ember.r, KitTheme.Ember.g, KitTheme.Ember.b, 0.10f); shell.Apply();
            var block = new GameObject("Blocker").transform; // the player's buttons behind must not catch the pinch
            block.SetParent(Root, false);
            block.localPosition = new Vector3(0f, 0f, 0.014f);
            block.gameObject.AddComponent<BoxCollider>().size = new Vector3(PW * S, PH * S, 0.01f);

            _main = new GameObject("Main").transform; _main.SetParent(Root, false);
            _in = new GameObject("Input card").transform; _in.SetParent(_main, false);
            _result = new GameObject("Result").transform; _result.SetParent(_main, false);
            _dict = new GameObject("Dictionary").transform; _dict.SetParent(_main, false);
            _picker = new GameObject("Picker").transform; _picker.SetParent(Root, false);
            Kit.OrderBoost = 40; // draw over the story player behind
            try { BuildMain(); BuildDictionary(); BuildPicker(); }
            finally { Kit.OrderBoost = 0; }
            // 3D: the two cards turn in toward you like a lectern's wings, and every layer floats at its own depth
            Lift(_main); Lift(_in); Lift(_result); Lift(_dict); Lift(_picker);
            Turn(_in, InC, -Lean); Turn(_result, OutC, Lean); Turn(_dict, DictC, 0f);
            _picker.gameObject.SetActive(false);

            Fade = Root.gameObject.AddComponent<KitGroup>();
            Fade.Refresh();
            _f.Changed += Refresh;
            Refresh();
        }

        // ---------- depth ----------

        /// <summary>Gives each direct child its depth by kind: card panels, wells, buttons, then text and icons on top.</summary>
        static void Lift(Transform g)
        {
            foreach (Transform c in g)
            {
                float z;
                if (c.GetComponent<KitButton>() != null) z = ZBtn;
                else if (c.GetComponent<KitPanel>() != null)
                {
                    var n = c.name.ToLowerInvariant();
                    z = n.Contains("panel") || n.Contains("card") ? ZCard : n.Contains("line") ? 0f : ZWell;
                }
                else if (c.GetComponent<KitIcon>() != null || c.GetComponent<TMP_Text>() != null) z = ZTxt;
                else continue;
                var p = c.localPosition; p.z += z; c.localPosition = p;
            }
        }

        /// <summary>Re-centres a group on its card and tilts it about the horizontal axis (negative = the top comes toward you).</summary>
        static void Turn(Transform g, Vector3 pivot, float pitch)
        {
            foreach (Transform c in g) c.localPosition -= pivot;
            g.localPosition = pivot;
            g.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // ---------- build ----------

        static TextMeshPro T(Transform p, string s, float px, float cx, float cy, float w, float h, Color c, TextAlignmentOptions a, KitFont font = KitFont.Ui, bool wrap = false)
            => Kit.Text(p, s, F(px), V(cx, cy), Z(w, h), c, a, font, 12, wrap);


        /// <summary>A select-style button: label left, chevron right (shadcn outline button with a chevron).</summary>
        KitButton Select(Transform p, float cx, float cy, float w, float h, string label, Action press, string icon = null, int px = 16)
        {
            var b = Kit.Button(p, V(cx, cy), Z(w, h), label, icon, KitStyle.Ghost, press, null, F(px), F(12));
            Kit.Icon(b.transform, "chevron-down", new Vector3((w / 2f - 20f) * S, 0f, 0f), F(16), KitTheme.Dim);
            return b;
        }

        void BuildMain()
        {
            var m = _main;
            // header: logo and name, close
            Dark(Kit.Image(m, Kit.IconTex("hypa-translate"), V(52, 36), Z(40, 37)));
            T(m, "Hypa Translate", 24, 215, 36, 260, 36, KitTheme.Paper, TextAlignmentOptions.Left, KitFont.Serif);
            Kit.Button(m, V(PW - 48, 36), Z(40, 40), null, "x", KitStyle.Quiet, () => _close(), null, 0.019f, F(12));
            Kit.Panel(m, "Header line", V(PW / 2f, 68), Z(PW, 1.5f), 0f, KitTheme.Hairline, KitTheme.Hairline, KitTheme.Clear, 11, 0f);

            // language row: two selects and the round swap button, centred as on the web
            _srcPill = Select(m, 282, 118, 200, 40, "Auto detect", () => OpenPicker(Pick.Source));
            _swap = Kit.Button(m, V(400, 118), Z(44, 44), null, "arrow-left-right", KitStyle.Ghost, () => _f.Swap());
            _dstPill = Select(m, 518, 118, 200, 40, "Yoruba", () => OpenPicker(Pick.Target));

            // input card (on top)
            Kit.Panel(_in, "Input panel", V(400, 296), Z(752, 280), F(20), CardTop, CardBottom, KitTheme.Hairline, 10, 0.5f);
            Kit.Button(_in, V(60, 190), Z(40, 40), null, "mic", KitStyle.Ghost, () => { }, null, 0.019f, F(12)).Selected = true; // Voice
            _zone = Kit.Panel(_in, "Record zone", V(400, 299), Z(720, 154), F(14), WellTop, WellBottom, WellEdge, 11, 0f);
            _zoneIcon = Kit.Icon(_in, "mic", V(400, 262), F(44), KitTheme.Dim);
            _zoneText = T(_in, "Tap the mic to record", 21, 400, 304, 600, 32, KitTheme.Paper, TextAlignmentOptions.Center, KitFont.UiBold);
            _zoneSub = T(_in, "Speak and watch it appear here", 15, 400, 336, 600, 26, KitTheme.Faint, TextAlignmentOptions.Center);
            _level = Kit.Panel(_in, "Level", V(400, 362), Z(6, 8), F(4), Rec, Rec, KitTheme.Clear, 12, 0f);
            _inText = T(_in, "", 22, 400, 299, 690, 140, KitTheme.Paper, TextAlignmentOptions.TopLeft, KitFont.Content, true);
            _inText.richText = false; _inText.enableAutoSizing = true; _inText.fontSizeMin = F(14) * 10f; _inText.fontSizeMax = F(26) * 10f;
            _clear = Kit.Button(_in, V(740, 190), Z(36, 36), null, "x", KitStyle.Quiet, () => _f.Clear(), null, 0.019f, F(12));
            _mic = Kit.Button(_in, V(60, 408), Z(40, 40), null, "mic", KitStyle.Ghost, () => { _ = _f.ToggleMicAsync(); }, null, 0.019f, F(12));
            _timer = T(_in, "", 15, 126, 408, 70, 26, Rec, TextAlignmentOptions.Left);
            _speakIn = Kit.Button(_in, V(150, 408), Z(40, 36), null, "volume-2", KitStyle.Quiet, () => { _ = _f.SpeakInputAsync(); }, null, 0.019f, F(12));
            _copyIn = Kit.Button(_in, V(196, 408), Z(40, 36), null, "copy", KitStyle.Quiet, () => _f.Copy(false), null, 0.019f, F(12));
            _count = T(_in, "0 / 500", 15, 560, 408, 120, 26, KitTheme.Dim, TextAlignmentOptions.Right);
            _run = Kit.Button(_in, V(690, 408), Z(120, 40), "Translate", null, KitStyle.Dim, () => { _ = _f.RunAsync(); }, null, F(17), F(12));

            // translation card (below)
            Kit.Panel(_result, "Output panel", V(400, 592), Z(752, 280), F(20), CardTop, CardBottom, KitTheme.Hairline, 10, 0.5f);
            var r = _result;
            Dark(Kit.Image(r, Kit.IconTex("hypa-intelligence"), V(60, 490), Z(24, 20)));
            _outTitle = T(r, "", 16, 257, 490, 360, 30, KitTheme.Dim, TextAlignmentOptions.Left, KitFont.UiBold);
            Kit.Panel(r, "Result box", V(400, 595), Z(720, 154), F(14), WellTop, WellBottom, WellEdge, 11, 0f);
            _outHint = T(r, "Your translation appears here", 18, 400, 595, 600, 34, KitTheme.Dim, TextAlignmentOptions.Center);
            _outText = T(r, "", 24, 400, 595, 690, 140, KitTheme.Paper, TextAlignmentOptions.TopLeft, KitFont.Content, true);
            _outText.richText = false; _outText.enableAutoSizing = true; _outText.fontSizeMin = F(15) * 10f; _outText.fontSizeMax = F(28) * 10f;
            _speakOut = Kit.Button(r, V(60, 708), Z(40, 36), null, "volume-2", KitStyle.Quiet, () => { _ = _f.SpeakOutputAsync(); }, null, 0.019f, F(12));
            _copyOut = Kit.Button(r, V(106, 708), Z(40, 36), null, "copy", KitStyle.Quiet, () => _f.Copy(true), null, 0.019f, F(12));
            _dictBtn = Kit.Button(r, V(152, 708), Z(40, 36), null, "hypa-dictionary", KitStyle.Quiet, () => _f.OpenDictionary(), null, 0.019f, F(12));
            _voicePill = Select(r, 620, 708, 130, 40, "Auto", () => OpenPicker(Pick.Voice), "volume-2", 15);
            _think = Kit.Button(r, V(740, 708), Z(40, 40), null, "brain", KitStyle.Ghost, () => { _ = _f.ToggleThinkingAsync(); }, null, 0.019f, F(12));
            _thinkIcon = Kit.Icon(r, "brain", V(66, 538), F(22), KitTheme.Paper);
            _thinkTitle = T(r, "Thinking Process", 18, 210, 538, 240, 28, KitTheme.Paper, TextAlignmentOptions.Left, KitFont.UiBold);
            _thinkText = T(r, "", 18, 400, 608, 690, 100, KitTheme.Dim, TextAlignmentOptions.TopLeft, KitFont.Content, true);
            _thinkText.richText = false; _thinkText.enableAutoSizing = true; _thinkText.fontSizeMin = F(12) * 10f; _thinkText.fontSizeMax = F(17) * 10f;

            _note = T(m, "", 16, 400, 758, 740, 26, KitTheme.Dim, TextAlignmentOptions.Center);
        }

        void BuildDictionary()
        {
            // the dictionary takes over the card area (both cards) while it is open
            var d = _dict;
            Kit.Panel(d, "Dictionary card", V(400, 444), Z(752, 576), F(20), CardTop, CardBottom, KitTheme.Hairline, 11, 0.5f);
            Dark(Kit.Image(d, Kit.IconTex("hypa-dictionary"), V(60, 190), Z(22, 26)));
            T(d, "Dictionary", 19, 158, 190, 180, 32, KitTheme.Paper, TextAlignmentOptions.Left, KitFont.UiBold);
            _dictClose = Kit.Button(d, V(740, 190), Z(38, 38), null, "x", KitStyle.Quiet, () => _f.CloseDictionary());
            _tabS = Kit.Button(d, V(262, 244), Z(260, 38), "…", null, KitStyle.Ghost, () => _f.SetTab(DictTab.Source), null, F(17));
            _tabT = Kit.Button(d, V(538, 244), Z(260, 38), "…", null, KitStyle.Ghost, () => _f.SetTab(DictTab.Target), null, F(17));
            _dWord = T(d, "", 26, 210, 304, 300, 40, KitTheme.Paper, TextAlignmentOptions.Left, KitFont.ContentBold);
            _dMeta = T(d, "", 16, 210, 336, 300, 26, KitTheme.Dim, TextAlignmentOptions.Left, KitFont.Content);
            _dictSpeak = Kit.Button(d, V(740, 318), Z(38, 38), null, "volume-2", KitStyle.Quiet, () => { _ = _f.SpeakAsync("word", DictWord(), _f.Find(_f.Side(_f.Tab).Language)); });
            _dState = T(d, "", 18, 400, 470, 500, 60, KitTheme.Dim, TextAlignmentOptions.Center, KitFont.Ui, true);
            _retry = Kit.Button(d, V(400, 540), Z(130, 42), "Retry", null, KitStyle.Ghost, () => { _ = _f.FetchDictionaryAsync(_f.Tab); }, null, F(17));
            string[] keys = { "definition", "usage", "synonyms", "antonyms", "cultural", "fun" };
            for (var i = 0; i < _sections.Length; i++)
            {
                var k = keys[i]; var idx = i;
                var s = new Section
                {
                    Title = T(d, "", 16, 210, 0, 300, 24, KitTheme.Paper, TextAlignmentOptions.Left, KitFont.UiBold),
                    Body = T(d, "", 16, 380, 0, 640, 60, KitTheme.Paper, TextAlignmentOptions.TopLeft, KitFont.Content, true),
                };
                s.Body.richText = false;
                s.Speak = Kit.Button(d, V(740, 0), Z(32, 32), null, "volume-2", KitStyle.Quiet, () => { _ = _f.SpeakAsync(k, SectionSpeech(idx), _f.Find(_f.Side(_f.Tab).Language)); });
                _sections[i] = s;
            }
        }

        void BuildPicker()
        {
            var p = _picker;
            Kit.Button(p, V(60, 44), Z(44, 44), null, "arrow-left", KitStyle.Quiet, ClosePicker);
            _pickTitle = T(p, "Choose a language", 26, 400, 44, 560, 44, KitTheme.Paper, TextAlignmentOptions.Center, KitFont.Serif);
            for (var i = 0; i < PageSize; i++)
            {
                var c = i;
                var b = Kit.Button(p, V(140 + (i % 3) * 260, 160 + (i / 3) * 88), Z(240, 64), "…", null, KitStyle.Ghost, () => Choose(c), null, F(19), F(14));
                b.Label.overflowMode = TextOverflowModes.Ellipsis;
                _grid.Add(b);
            }
            _prevPage = Kit.Button(p, V(300, 690), Z(46, 46), null, "chevron-left", KitStyle.Ghost, () => Page(-1));
            _nextPage = Kit.Button(p, V(500, 690), Z(46, 46), null, "chevron-right", KitStyle.Ghost, () => Page(1));
            _pageText = T(p, "", 17, 400, 690, 160, 30, KitTheme.Dim, TextAlignmentOptions.Center);
        }

        // ---------- opening and closing ----------

        /// <summary>Call when the panel opens: back to the main view, up to date.</summary>
        public void Show()
        {
            _wasListening = false;
            _picker.gameObject.SetActive(false);
            _main.gameObject.SetActive(true);
            _typeIn.Set(_f.Input, true); _typeOut.Set(_f.Output, true);
            Refresh();
        }

        /// <summary>Pop-in scale for the open / close tween (0..1).</summary>
        public void SetPop(float u) => Root.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, u);

        // ---------- the picker (languages, voices) ----------

        void OpenPicker(Pick kind)
        {
            _pick = kind; _page = 0;
            _f.LoadLanguagesAsync();
            _pickTitle.text = kind == Pick.Source ? "Translate from" : kind == Pick.Target ? "Translate to" : "Voice";
            BuildItems();
            var cur = kind == Pick.Voice ? Math.Max(0, _labels.IndexOf(_f.Voice)) : _langs.FindIndex(l => l?.Name == (kind == Pick.Source ? _f.Source : _f.Target)?.Name);
            if (cur >= 0) _page = cur / PageSize;
            _main.gameObject.SetActive(false);
            _picker.gameObject.SetActive(true);
            FillPicker();
        }

        void ClosePicker()
        {
            _picker.gameObject.SetActive(false);
            _main.gameObject.SetActive(true);
            Refresh();
        }

        void BuildItems()
        {
            _labels.Clear(); _langs.Clear();
            if (_pick == Pick.Voice) { _labels.AddRange(_f.VoiceOptions()); return; }
            if (_pick == Pick.Source) { _langs.Add(null); _labels.Add("Auto detect"); }
            foreach (var l in _f.Ordered()) { _langs.Add(l); _labels.Add(l.Name); }
        }

        void FillPicker()
        {
            BuildItems();
            var pages = Mathf.Max(1, Mathf.CeilToInt(_labels.Count / (float)PageSize));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            var curName = _pick == Pick.Voice ? _f.Voice : _pick == Pick.Source ? _f.Source?.Name ?? "Auto detect" : _f.Target?.Name;
            for (var i = 0; i < _grid.Count; i++)
            {
                var idx = _page * PageSize + i;
                var on = idx < _labels.Count;
                _grid[i].gameObject.SetActive(on);
                if (!on) continue;
                _grid[i].SetLabel(TryHypaText.Clip(_labels[idx], 20));
                _grid[i].Selected = _labels[idx] == curName;
            }
            _pageText.text = $"{_page + 1} / {pages}";
            _prevPage.gameObject.SetActive(_page > 0);
            _nextPage.gameObject.SetActive(_page < pages - 1);
        }

        void Page(int d) { _page += d; FillPicker(); }

        void Choose(int cell)
        {
            var idx = _page * PageSize + cell;
            if (idx >= _labels.Count) return;
            if (_pick == Pick.Voice) _f.SetVoice(_labels[idx]);
            else if (_pick == Pick.Source) _f.SetSource(_langs[idx]);
            else _f.SetTarget(_langs[idx]);
            ClosePicker();
        }

        // ---------- drawing the flow ----------

        string DictWord() => _f.Side(_f.Tab).Card?.Word ?? _f.Side(_f.Tab).Word;

        static string Join(List<HypaCardItem> items, int max, bool numbered, bool bullet)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < items.Count && i < max; i++)
            {
                if (i > 0) sb.Append('\n');
                if (numbered) sb.Append(i + 1).Append(". ");
                else if (bullet) sb.Append("• ");
                if (!string.IsNullOrEmpty(items[i].Type)) sb.Append('(').Append(items[i].Type).Append(") ");
                sb.Append(items[i].Text);
            }
            return sb.ToString();
        }

        static string Words(List<string> l, int max) => string.Join(", ", l.GetRange(0, Math.Min(max, l.Count)));

        string SectionText(int i, HypaDictionaryCard c)
        {
            switch (i)
            {
                case 0: return Join(c.Definitions, 3, true, false);
                case 1: return Join(c.Examples, 2, false, true);
                case 2: return Words(c.Synonyms, 8);
                case 3: return Words(c.Antonyms, 8);
                case 4: return Join(c.CulturalContext, 2, false, false);
                default: return Join(c.FunFacts, 2, false, false);
            }
        }

        static readonly string[] SectionTitles = { "Definition:", "Usage Example:", "Synonyms:", "Antonyms:", "Cultural Context:", "Fun Facts:" };
        // what the speaker says first, as Hypa Translate does
        static readonly string[] SectionSpoken = { "definition ", "usage example ", "synonyms ", "antonyms ", "cultural context ", "fun facts " };

        string SectionSpeech(int i)
        {
            var c = _f.Side(_f.Tab).Card;
            return c == null ? "" : SectionSpoken[i] + SectionText(i, c).Replace("\n", " ");
        }

        void Refresh()
        {
            if (_picker.gameObject.activeSelf) FillPicker(); // the full language list arrived
            var busy = _f.Status != TryStatus.Idle;
            var listening = _f.Status == TryStatus.Listening;
            var hasIn = !string.IsNullOrEmpty(_f.Input);
            var hasOut = !string.IsNullOrEmpty(_f.Output);
            var dict = _f.DictionaryOpen;
            _in.gameObject.SetActive(!dict);
            _result.gameObject.SetActive(!dict);
            _dict.gameObject.SetActive(dict);

            _srcPill.SetLabel(TryHypaText.Clip(_f.Source?.Name ?? "Auto detect", 18));
            _dstPill.SetLabel(TryHypaText.Clip(_f.Target?.Name ?? "", 18));

            // input
            _typeIn.Set(_f.Input, listening); // live words appear at once; a finished one is typed in
            _inText.text = _typeIn.Current;
            _inText.gameObject.SetActive(hasIn);
            _zoneIcon.gameObject.SetActive(!hasIn);
            _zoneText.gameObject.SetActive(!hasIn);
            _zoneSub.gameObject.SetActive(!hasIn);
            _zoneIcon.Color = listening ? Rec : KitTheme.Dim; _zoneIcon.Apply();
            _zoneText.text = listening ? "Recording..." : _f.Status == TryStatus.Transcribing ? "Typing it out…" : "Tap the mic to speak";
            _zoneSub.text = listening ? "Speak now, tap the red button to stop" : "Speak and watch it appear here";
            if (listening && !_wasListening) _listenStart = Time.unscaledTime;
            _wasListening = listening;
            _timer.gameObject.SetActive(listening);
            _level.gameObject.SetActive(listening);
            _clear.gameObject.SetActive(hasIn && !listening);
            _count.text = (_f.Input?.Length ?? 0) + " / 500";
            _mic.Style = listening ? KitStyle.Danger : KitStyle.Ghost;
            _mic.SetIcon(listening ? "square" : "mic");
            var canRun = hasIn && !busy;
            _run.Style = canRun ? KitStyle.Primary : KitStyle.Dim;
            _run.SetLabel("Translate");

            // translation
            var target = _f.Target?.Name ?? "";
            _outTitle.text = _f.Status == TryStatus.Working ? "Translating to " + target + "…" : hasOut ? "Translated to " + target : "Translate to " + target;
            _typeOut.Set(_f.Output);
            _outText.text = _typeOut.Current;
            _outHint.gameObject.SetActive(!hasOut);
            _speakOut.gameObject.SetActive(hasOut);
            _speakOut.SetIcon(_f.ActiveAudio == "output" ? "square" : "volume-2");
            _dictBtn.gameObject.SetActive(_f.CanShowDictionary);
            _copyOut.gameObject.SetActive(hasOut);
            _think.gameObject.SetActive(hasOut);
            _think.Selected = _f.ThinkingOpen;
            var thinking = _f.ThinkingOpen;
            // as on the web: Thinking Process replaces the translation in the box; press the brain again to go back
            _thinkIcon.gameObject.SetActive(thinking); _thinkTitle.gameObject.SetActive(thinking); _thinkText.gameObject.SetActive(thinking);
            _thinkTitle.text = _f.ThinkingLoading ? "Thinking…" : "Thinking Process";
            _thinkText.text = _f.Thinking;
            _outText.gameObject.SetActive(!thinking);
            _speakIn.gameObject.SetActive(hasIn);
            _copyIn.gameObject.SetActive(hasIn);
            _speakIn.SetIcon(_f.ActiveAudio == "input" ? "square" : "volume-2");
            _voicePill.gameObject.SetActive(hasOut);
            _voicePill.SetLabel(TryHypaText.Clip(_f.Voice, 9));

            var note = _f.Message;
            var isError = !string.IsNullOrEmpty(note);
            if (!isError) note = _f.Info;
            if (string.IsNullOrEmpty(note) && _f.Source == null && !string.IsNullOrEmpty(_f.DetectedLanguage) && hasOut) note = "Detected: " + _f.DetectedLanguage;
            _note.text = note ?? "";
            _note.color = isError ? Err : KitTheme.Dim;

            if (dict) RefreshDictionary();
        }

        void RefreshDictionary()
        {
            _tabS.SetLabel(TryHypaText.Clip(_f.TabLabel(DictTab.Source), 14));
            _tabT.SetLabel(TryHypaText.Clip(_f.TabLabel(DictTab.Target), 14));
            _tabS.Selected = _f.Tab == DictTab.Source; _tabT.Selected = _f.Tab == DictTab.Target;
            var side = _f.Side(_f.Tab);
            var c = side.Card;
            _dWord.text = c?.Word ?? side.Word;
            var meta = "";
            if (!string.IsNullOrEmpty(c?.Phonetic)) meta = "/" + c.Phonetic.Trim('/') + "/";
            _dMeta.text = meta;
            _dictSpeak.gameObject.SetActive(c != null);
            _dictSpeak.SetIcon(_f.ActiveAudio == "word" ? "square" : "volume-2");
            _dState.gameObject.SetActive(c == null);
            _dState.text = side.Loading ? "Looking up…" : side.Failed ? "Couldn't load this word." : "No definition found.";
            _retry.gameObject.SetActive(side.Failed && !side.Loading);

            var y = 385f;
            var keys = new[] { "definition", "usage", "synonyms", "antonyms", "cultural", "fun" };
            for (var i = 0; i < _sections.Length; i++)
            {
                var s = _sections[i];
                var text = c != null ? SectionText(i, c) : "";
                var lines = 0;
                foreach (var ln in text.Split('\n')) lines += Mathf.Max(1, Mathf.CeilToInt(ln.Length / 72f));
                var h = lines * 22f + 4f;
                var show = !string.IsNullOrEmpty(text) && y + 26f + h < 712f;
                s.Title.gameObject.SetActive(show); s.Body.gameObject.SetActive(show); s.Speak.gameObject.SetActive(show);
                if (!show) continue;
                s.Title.text = SectionTitles[i];
                s.Title.transform.localPosition = V(210, y, ZTxt - 0.003f) - DictC;
                s.Speak.transform.localPosition = V(740, y, ZBtn) - DictC;
                s.Speak.SetIcon(_f.ActiveAudio == keys[i] ? "square" : "volume-2");
                s.Body.text = text;
                s.Body.rectTransform.sizeDelta = Z(640, h);
                s.Body.transform.localPosition = V(380, y + 14f + h / 2f, ZTxt - 0.003f) - DictC;
                y += 30f + h + 6f;
            }
        }

        /// <summary>Per frame while open: types the text in, moves the mic level, notices when the speech ends.</summary>
        public void Update(float dt)
        {
            _f.Tick();
            if (_typeIn.Tick(dt)) _inText.text = _typeIn.Current;
            if (_typeOut.Tick(dt)) _outText.text = _typeOut.Current;
            if (_f.Status == TryStatus.Listening)
            {
                var secs = Mathf.FloorToInt(Time.unscaledTime - _listenStart);
                _timer.text = $"{secs / 60}:{secs % 60:00}";
            }
            if (_f.Status == TryStatus.Listening)
                _level.Resize(Z(Mathf.Max(6f, 300f * Mathf.Clamp01(_f.Level)), 8f));
        }
    }
}
