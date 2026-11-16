using System;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Hypa Translate inside the story panel, in the same glass: the story's language, a language to translate into,
    /// three words from the story (tap one, as you tap a suggested question), what was said, and the translation.
    /// Speaking and hearing use the buttons; this only draws <see cref="TryHypaFlow"/>.
    /// </summary>
    public sealed class TryHypaView
    {
        public readonly Transform Root;
        public readonly KitGroup Fade;

        readonly TryHypaFlow _f;
        readonly Action _close;
        readonly KitButton[] _words = new KitButton[3];
        readonly string[] _shown = new string[3];
        readonly TextMeshPro _from, _heard, _out;
        readonly KitButton _to, _hear, _closeBtn;
        readonly KitPanel _level;
        Color _accent = KitTheme.Ember;

        public TryHypaView(Transform playerRoot, TryHypaFlow flow, Action close)
        {
            _f = flow; _close = close;
            Root = new GameObject("Try Hypa").transform;
            Root.SetParent(playerRoot, false);

            _from = Text(Root, "", 0.014f, new Vector3(-0.16f, 0.086f, -0.01f), new Vector2(0.28f, 0.028f), KitTheme.Dim, TextAlignmentOptions.Left);
            _to = Kit.Button(Root, new Vector3(0.1f, 0.086f, -0.01f), new Vector2(0.24f, 0.038f), "To Yoruba", null, KitStyle.Ghost, () => _f.CycleTarget(), null, 0.015f);
            _closeBtn = Kit.Button(Root, new Vector3(0.28f, 0.086f, -0.01f), new Vector2(0.04f, 0.038f), null, "x", KitStyle.Quiet, () => _close());
            for (var i = 0; i < 3; i++)
            {
                var n = i;
                // a placeholder label: Kit makes an empty-label button round, with no text to fill in later
                var b = Kit.Button(Root, new Vector3((i - 1) * 0.21f, 0.036f, -0.01f), new Vector2(0.2f, 0.042f), "word", null, KitStyle.Ghost,
                    () => { if (!string.IsNullOrEmpty(_shown[n])) _ = _f.UseWordAsync(_shown[n]); }, null, 0.014f);
                b.Label.textWrappingMode = TextWrappingModes.Normal;
                b.Label.enableAutoSizing = true;
                b.Label.fontSizeMax = b.Label.fontSize;
                b.Label.fontSizeMin = b.Label.fontSize * 0.72f;
                b.Label.overflowMode = TextOverflowModes.Ellipsis;
                Face(b.Label); // the story's own words: ẹ, ọ, ụ, ɗ need the content font
                _words[i] = b;
            }
            _heard = Text(Root, "", 0.015f, new Vector3(0f, -0.008f, -0.01f), new Vector2(0.58f, 0.022f), KitTheme.Dim, TextAlignmentOptions.Center);
            _out = Text(Root, "", 0.026f, new Vector3(-0.02f, -0.048f, -0.01f), new Vector2(0.5f, 0.04f), KitTheme.Ember, TextAlignmentOptions.Center, true);
            _hear = Kit.Button(Root, new Vector3(0.27f, -0.048f, -0.01f), new Vector2(0.04f, 0.04f), null, "volume-2", KitStyle.Ghost, () => _ = _f.SpeakOutputAsync());
            _level = Kit.Panel(Root, "Level", new Vector3(0f, -0.084f, -0.008f), new Vector2(0.004f, 0.004f), 0.002f, KitTheme.Ember, KitTheme.Ember, KitTheme.Clear, 12, 0f);

            Face(_from); Face(_heard); Face(_out); Face(_to.Label);
            Fade = Root.gameObject.AddComponent<KitGroup>();
            Fade.Refresh();
            _f.Changed += Refresh;
            Refresh();
        }

        static TextMeshPro Text(Transform p, string s, float size, Vector3 at, Vector2 box, Color c, TextAlignmentOptions align, bool wrap = false)
            => Kit.Text(p, s, size, at, box, c, align, KitFont.Ui, 12, wrap);

        static void Face(TextMeshPro t)
        {
            var f = TryFonts.Content;
            if (f != null && t != null) t.font = f;
        }

        /// <summary>Call when the panel opens. <paramref name="accent"/> is the story's colour, used for the translation.</summary>
        public void Show(Color accent)
        {
            _accent = accent;
            _out.color = accent;
            Refresh();
        }

        public void Update(float dt)
        {
            _f.Tick(dt);
            if (_f.Listening)
                _level.Resize(new Vector2(Mathf.Max(0.004f, 0.4f * Mathf.Clamp01(_f.Level)), 0.004f));
        }

        static string Shown(string hypa)
        {
            var s = StoryLanguages.Find(hypa);
            return s != null ? s.Name : hypa ?? "";
        }

        void Refresh()
        {
            var listening = _f.Listening;
            var working = _f.Status == TryStatus.Working || _f.Status == TryStatus.Transcribing;
            _from.text = "From " + Shown(_f.Source?.Name);
            _to.SetLabel("To " + Shown(_f.Target?.Name));
            var words = _f.Suggestions ?? new string[0];
            for (var i = 0; i < _words.Length; i++)
            {
                var on = i < words.Length && !string.IsNullOrEmpty(words[i]);
                _words[i].gameObject.SetActive(on);
                _shown[i] = on ? words[i] : "";
                if (on) _words[i].SetLabel(words[i]);
            }
            var err = _f.Message;
            if (!string.IsNullOrEmpty(err)) { _heard.text = err; _heard.color = new Color(1f, 0.52f, 0.46f); }
            else if (listening) { _heard.text = string.IsNullOrEmpty(_f.Input) ? "Listening…" : _f.Input; _heard.color = KitTheme.Paper; }
            else if (working) { _heard.text = "Translating…"; _heard.color = KitTheme.Dim; }
            else if (!string.IsNullOrEmpty(_f.Input)) { _heard.text = _f.Input; _heard.color = KitTheme.Dim; }
            else { _heard.text = "Tap a word from the story, or speak."; _heard.color = KitTheme.Faint; }
            _out.text = _f.Output ?? "";
            _out.color = _accent;
            var canHear = !string.IsNullOrEmpty(_f.Output);
            _hear.gameObject.SetActive(canHear);
            _hear.SetIcon(_f.ActiveAudio == "output" ? "square" : "volume-2");
            _level.gameObject.SetActive(listening);
            _closeBtn.gameObject.SetActive(true);
        }
    }
}
