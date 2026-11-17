using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// A local's panel (ticket 3I), above their head, turned to you. Compact: their name, what's happening, then either your question
    /// and their answer (subtitles of the sentence being said) or, while they listen, a full-width wave that moves with your voice.
    /// Talk below (tap: talk / done / skip; hold: push-to-talk). The persona's three suggested questions appear only when they
    /// can't hear you (no microphone or no key, as the ticket asks); the panel grows downward to fit them.
    /// </summary>
    public sealed class LocalView : IAskView
    {
        const float W = 0.56f, H = 0.30f, Inner = W - 0.06f;
        const float RowH = 0.044f, RowGap = 0.008f, Rows = 3 * (RowH + RowGap);
        public readonly Transform Root;
        readonly TextMeshPro _state, _question, _answer;
        readonly KitPanel _bg;
        readonly KitButton _talk;
        readonly List<KitButton> _suggestions = new List<KitButton>();
        string[] _suggested;
        string _prompt;
        string _talkLabel = "Talk", _doneLabel = "Done", _skipLabel = "Skip";
        bool _idle = true, _listening, _wantSuggestions;

        // The wave: thin rounded bars across the panel, mirrored about the middle line; your voice enters on the right and
        // scrolls left, so you see the last second and a half of it.
        const int WaveBars = 44;
        const float WaveH = 0.075f, WaveY = 0.005f, BarW = 0.0055f;
        readonly List<KitPanel> _wave = new List<KitPanel>();
        readonly float[] _history = new float[WaveBars];
        float _level, _shown, _step;

        public LocalView(Transform parent, LocalPersona persona, string prompt, Action onTalk, Action onTalkHold, Action<string> onSuggestion)
        {
            _suggested = persona.Suggested ?? new string[0];
            _prompt = prompt;
            Root = new GameObject("Local Panel").transform;
            Root.SetParent(parent, false);
            _bg = Kit.Glass(Root, "bg", Vector3.zero, new Vector2(W, H), 0.03f);
            Kit.Text(Root, persona.Title, 0.024f, new Vector3(0f, 0.118f, 0f), new Vector2(Inner, 0.036f), KitTheme.Paper, TextAlignmentOptions.Left, KitFont.UiBold);
            _state = Fit(Kit.Text(Root, prompt, 0.017f, new Vector3(0f, 0.084f, 0f), new Vector2(Inner, 0.028f), KitTheme.Ember, TextAlignmentOptions.Left, KitFont.Ui, 12, false), 0.6f);
            _question = Fit(Kit.Text(Root, "", 0.015f, new Vector3(0f, 0.052f, 0f), new Vector2(Inner, 0.028f), KitTheme.Dim, TextAlignmentOptions.Left, KitFont.Ui, 12, false), 0.7f);
            _answer = Fit(Kit.Text(Root, "", 0.022f, new Vector3(0f, -0.005f, 0f), new Vector2(Inner, 0.08f), KitTheme.Paper, TextAlignmentOptions.TopLeft, KitFont.Serif), 0.65f);

            var gap = (Inner - WaveBars * BarW) / (WaveBars - 1);
            for (var i = 0; i < WaveBars; i++)
            {
                var bar = Kit.Panel(Root, "wave", new Vector3(-Inner / 2f + BarW / 2f + i * (BarW + gap), WaveY, 0f), new Vector2(BarW, BarW), BarW / 2f,
                    KitTheme.Ember, KitTheme.EmberDeep, KitTheme.Clear, 12, 0f);
                bar.gameObject.SetActive(false);
                _wave.Add(bar);
            }

            for (var i = 0; i < 3; i++)
            {
                var k = i;
                // Created with its text: a Kit button made with no label is an icon-only button, and a label can't be added later.
                var text = i < _suggested.Length && !string.IsNullOrWhiteSpace(_suggested[i]) ? _suggested[i] : "…";
                var b = Kit.Button(Root, Vector3.zero, new Vector2(Inner, RowH), text, null, KitStyle.Ghost,
                    () => { if (k < _suggested.Length) onSuggestion(_suggested[k]); }, null, 0.016f);
                b.gameObject.SetActive(false);
                _suggestions.Add(b);
            }
            _talk = Kit.Button(Root, Vector3.zero, new Vector2(0.19f, 0.052f), "Talk", "mic", KitStyle.Primary, onTalk, onTalkHold);
            Layout();
        }

        /// <summary>Shrinks the text to fit its box (down to <paramref name="min"/> of its size) instead of spilling past the panel.</summary>
        static TextMeshPro Fit(TextMeshPro t, float min)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = t.fontSize * min;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        bool ShowingSuggestions => _listening && _wantSuggestions;

        /// <summary>Sizes the panel: compact, or grown downward for the suggested questions.</summary>
        void Layout()
        {
            var extra = ShowingSuggestions ? Rows : 0f;
            _bg.transform.localPosition = new Vector3(0f, -extra / 2f, 0f);
            _bg.Resize(new Vector2(W, H + extra));
            for (var i = 0; i < _suggestions.Count; i++)
            {
                var on = ShowingSuggestions && i < _suggested.Length && !string.IsNullOrWhiteSpace(_suggested[i]);
                _suggestions[i].transform.localPosition = new Vector3(0f, -0.078f - i * (RowH + RowGap), 0f);
                _suggestions[i].gameObject.SetActive(on);
                if (on) _suggestions[i].SetLabel(_suggested[i]);
            }
            _talk.transform.localPosition = new Vector3(0f, -0.108f - extra, 0f);
        }

        /// <summary>The idle prompt and the button labels in another language (the story language changed).</summary>
        public void SetLanguage(string prompt, string talk, string done, string skip)
        {
            _prompt = prompt; _talkLabel = talk; _doneLabel = done; _skipLabel = skip;
            if (_idle) _state.text = prompt;
            _talk.SetLabel(_listening ? done : _idle ? talk : skip);
        }

        /// <summary>
        /// Show the suggested questions while listening: only when the local can't hear you (no microphone, no permission, no key),
        /// so a spoken question is the way in otherwise.
        /// </summary>
        public bool WantSuggestions
        {
            get => _wantSuggestions;
            set { if (_wantSuggestions == value) return; _wantSuggestions = value; Layout(); }
        }

        public void SetState(AskState state, string text)
        {
            _state.text = string.IsNullOrEmpty(text) ? (state == AskState.Idle ? _prompt : "") : text;
            var listening = _listening = state == AskState.Listening;
            // Listening: the wave in place of the question and answer, and the panel lights up in ember.
            foreach (var b in _wave) b.gameObject.SetActive(listening);
            _question.gameObject.SetActive(!listening);
            _answer.gameObject.SetActive(!listening);
            _bg.Glow = listening ? new Color(KitTheme.Ember.r, KitTheme.Ember.g, KitTheme.Ember.b, 0.55f) : KitTheme.Clear;
            _bg.Border = listening ? KitTheme.Ember : KitTheme.Hairline;
            _bg.Apply();
            _idle = state == AskState.Idle;
            _talk.SetLabel(listening ? _doneLabel : _idle ? _talkLabel : _skipLabel);
            _talk.SetIcon(listening ? "check" : _idle ? "mic" : "skip-forward");
            if (listening) Array.Clear(_history, 0, _history.Length);
            else SetLevel(0f);
            Layout();
        }

        public void SetLevel(float level01) => _level = Mathf.Clamp01(level01);

        /// <summary>
        /// Animates the wave (call every frame): the loudness is eased, written into the history every 35 ms and scrolled left;
        /// each bar is the history at its place, rounded off toward both ends, with a faint shimmer when it's quiet.
        /// </summary>
        public void Tick(float dt)
        {
            if (!_listening) return;
            _shown = Mathf.Lerp(_shown, _level, 1f - Mathf.Exp(-22f * dt));
            _step += dt;
            while (_step >= 0.035f)
            {
                _step -= 0.035f;
                Array.Copy(_history, 1, _history, 0, WaveBars - 1);
                _history[WaveBars - 1] = Mathf.Min(1f, _shown * 2.2f);
            }
            var t = Time.time;
            for (var i = 0; i < WaveBars; i++)
            {
                var x = i / (WaveBars - 1f);
                var ends = Mathf.Sin(x * Mathf.PI);                            // softer at both ends
                var neighbours = (_history[Mathf.Max(0, i - 1)] + _history[i] * 2f + _history[Mathf.Min(WaveBars - 1, i + 1)]) / 4f;
                var shimmer = 0.06f + 0.05f * (0.5f + 0.5f * Mathf.Sin(t * 4f + i * 0.55f));
                var h = Mathf.Max(shimmer, neighbours) * (0.35f + 0.65f * ends) * WaveH;
                _wave[i].Resize(new Vector2(BarW, Mathf.Max(BarW, h)));
            }
        }

        /// <summary>Replaces the persona's suggested questions (empty or null keeps them).</summary>
        public void SetSuggestions(string[] questions) { if (questions != null && questions.Length > 0) { _suggested = questions; Layout(); } }
        public void SetQuestion(string text) => _question.text = text ?? "";
        public void SetAnswer(string text) { if (text != null) _answer.text = text; }
    }
}
