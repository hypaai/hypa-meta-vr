using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// A local's panel (ticket 3I), above their head, turned to you: their name, what's happening ("Listening…"), your question,
    /// their answer as subtitles (the sentence being said), the microphone level, Talk (tap: talk / done / skip; hold:
    /// push-to-talk) and the persona's three suggested questions while listening (for no microphone or no key).
    /// </summary>
    public sealed class LocalView : IAskView
    {
        // Taller than first built: the suggested questions are full-width rows (side by side they overflowed on the headset).
        const float W = 0.6f, H = 0.54f;
        public readonly Transform Root;
        readonly TextMeshPro _state, _question, _answer;
        readonly KitPanel _bg;
        readonly List<KitPanel> _bars = new List<KitPanel>();
        const int Bars = 9;
        const float BarW = 0.012f, BarGap = 0.008f, BarMax = 0.04f;
        float _level, _shown;
        bool _listening;
        readonly KitButton _talk;
        readonly List<KitButton> _suggestions = new List<KitButton>();
        string[] _suggested;
        string _prompt;

        public LocalView(Transform parent, LocalPersona persona, string prompt, Action onTalk, Action onTalkHold, Action<string> onSuggestion)
        {
            _suggested = persona.Suggested ?? new string[0];
            _prompt = prompt;
            Root = new GameObject("Local Panel").transform;
            Root.SetParent(parent, false);
            const float inner = W - 0.06f;
            _bg = Kit.Glass(Root, "bg", Vector3.zero, new Vector2(W, H), 0.03f);
            Kit.Text(Root, persona.Title, 0.026f, new Vector3(0f, 0.225f, 0f), new Vector2(inner, 0.04f), KitTheme.Paper, TextAlignmentOptions.Left, KitFont.UiBold);
            _state = Fit(Kit.Text(Root, prompt, 0.019f, new Vector3(0f, 0.188f, 0f), new Vector2(inner, 0.032f), KitTheme.Ember, TextAlignmentOptions.Left, KitFont.Ui, 12, false), 0.6f);
            _question = Fit(Kit.Text(Root, "", 0.017f, new Vector3(0f, 0.146f, 0f), new Vector2(inner, 0.046f), KitTheme.Dim, TextAlignmentOptions.TopLeft, KitFont.Ui), 0.7f);
            _answer = Fit(Kit.Text(Root, "", 0.025f, new Vector3(0f, 0.065f, 0f), new Vector2(inner, 0.11f), KitTheme.Paper, TextAlignmentOptions.TopLeft, KitFont.Serif), 0.7f);

            // The voice meter: bars that bounce with your voice while they listen, so you can see they hear you.
            var x0 = -(Bars * BarW + (Bars - 1) * BarGap) / 2f + BarW / 2f;
            for (var i = 0; i < Bars; i++)
            {
                var bar = Kit.Panel(Root, "level", new Vector3(x0 + i * (BarW + BarGap), -0.012f, 0f), new Vector2(BarW, 0.006f), BarW / 2f,
                    KitTheme.Ember, KitTheme.EmberDeep, KitTheme.Clear, 12, 0f);
                bar.gameObject.SetActive(false);
                _bars.Add(bar);
            }
            const float rowH = 0.044f, rowGap = 0.008f;
            for (var i = 0; i < 3; i++)
            {
                var k = i;
                // Created with its text: a Kit button made with no label is an icon-only button, and a label can't be added later.
                var text = i < _suggested.Length && !string.IsNullOrWhiteSpace(_suggested[i]) ? _suggested[i] : "…";
                var b = Kit.Button(Root, new Vector3(0f, -0.058f - i * (rowH + rowGap), 0f), new Vector2(inner, rowH), text, null, KitStyle.Ghost,
                    () => { if (k < _suggested.Length) onSuggestion(_suggested[k]); }, null, 0.017f);
                b.gameObject.SetActive(false);
                _suggestions.Add(b);
            }
            _talk = Kit.Button(Root, new Vector3(0f, -0.225f, 0f), new Vector2(0.2f, 0.056f), "Talk", "mic", KitStyle.Primary, onTalk, onTalkHold);
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

        /// <summary>The idle prompt and the button labels in another language (the story language changed).</summary>
        public void SetLanguage(string prompt, string talk, string done, string skip)
        {
            _prompt = prompt; _talkLabel = talk; _doneLabel = done; _skipLabel = skip;
            if (_idle) _state.text = prompt;
            _talk.SetLabel(_listening ? done : _idle ? talk : skip);
        }
        string _talkLabel = "Talk", _doneLabel = "Done", _skipLabel = "Skip";
        bool _idle = true;

        public void SetState(AskState state, string text)
        {
            _state.text = string.IsNullOrEmpty(text) ? (state == AskState.Idle ? _prompt : "") : text;
            var listening = _listening = state == AskState.Listening;
            foreach (var b in _bars) b.gameObject.SetActive(listening);
            // Listening: the panel lights up in ember, like the storyteller's when you raise a hand.
            _bg.Glow = listening ? new Color(KitTheme.Ember.r, KitTheme.Ember.g, KitTheme.Ember.b, 0.55f) : KitTheme.Clear;
            _bg.Border = listening ? KitTheme.Ember : KitTheme.Hairline;
            _bg.Apply();
            for (var i = 0; i < _suggestions.Count; i++)
            {
                var on = listening && i < _suggested.Length && !string.IsNullOrWhiteSpace(_suggested[i]);
                _suggestions[i].gameObject.SetActive(on);
                if (on) _suggestions[i].SetLabel(_suggested[i]);
            }
            _idle = state == AskState.Idle;
            _talk.SetLabel(listening ? _doneLabel : _idle ? _talkLabel : _skipLabel);
            _talk.SetIcon(listening ? "check" : state == AskState.Idle ? "mic" : "skip-forward");
            if (!listening) SetLevel(0f);
        }

        public void SetLevel(float level01) => _level = Mathf.Clamp01(level01);

        /// <summary>Animates the voice meter (call every frame): bars rise with your voice, a small idle pulse when quiet.</summary>
        public void Tick(float dt)
        {
            if (!_listening) return;
            _shown = Mathf.Lerp(_shown, _level, 1f - Mathf.Exp(-18f * dt));
            var t = Time.time;
            for (var i = 0; i < _bars.Count; i++)
            {
                var centre = 1f - Mathf.Abs(i - (Bars - 1) / 2f) / ((Bars - 1) / 2f) * 0.55f; // taller in the middle
                var wobble = 0.65f + 0.35f * Mathf.Sin(t * 13f + i * 1.7f);
                var idle = 0.006f + 0.003f * (0.5f + 0.5f * Mathf.Sin(t * 3f + i * 0.6f));
                _bars[i].Resize(new Vector2(BarW, Mathf.Max(idle, BarMax * Mathf.Min(1f, _shown * 1.8f) * centre * wobble)));
            }
        }
        /// <summary>Replaces the persona's suggested questions (empty or null keeps them); shown the next time they listen.</summary>
        public void SetSuggestions(string[] questions) { if (questions != null && questions.Length > 0) _suggested = questions; }
        public void SetQuestion(string text) => _question.text = text ?? "";
        public void SetAnswer(string text) { if (text != null) _answer.text = text; }
    }
}
