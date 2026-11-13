using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The story player (the prototype's #play panel): a glass panel just below eye level, in front of you, so the storyteller stays in
    /// view above it. The line being spoken is large, its words lighting up with the voice, with the lines before and after faded.
    /// Progress, previous / play-pause / next and Ask along the bottom. While a question is open the script gives way to the Q&amp;A:
    /// state and mic level, "You asked", the answer in the story's accent colour, and suggested questions (no microphone or no key).
    /// </summary>
    public sealed class PlayerView : IAskView
    {
        const float W = 0.68f, H = 0.42f;

        public readonly Transform Root;
        public readonly KitGroup Fade;
        public readonly FollowView Follow;
        readonly TextMeshPro _kicker, _title, _prev, _line, _next, _time, _hint, _state, _question, _answer;
        readonly Transform _lines, _qa;
        readonly KitPanel _fill, _level;
        readonly KitButton _pause, _ask, _sound;
        readonly KitIcon _hintIcon;
        readonly List<KitButton> _suggestions = new List<KitButton>();
        readonly Action<string> _pick;
        StoryInfo _story;
        StoryLanguage _lang;
        string _pauseState;
        int _shownWords = -1, _shownLine = -2;
        static readonly Color Unlit = new Color(1f, 0.97f, 0.92f, 0.55f);

        public PlayerView(Transform uiRoot, Action prev, Action pause, Action next, Action askTap, Action askHold,
            Action back, Action toggleSound, Action<string> pickQuestion)
        {
            _pick = pickQuestion;
            Root = new GameObject("Player").transform;
            Root.SetParent(uiRoot, false);
            Follow = Root.gameObject.AddComponent<FollowView>();
            // about 24° below the eyes: its top edge stays under the storyteller's head whether you sit or stand
            Follow.Distance = 0.9f; Follow.Height = -0.4f; Follow.Threshold = 30f;
            Kit.Glass(Root, "Panel", new Vector3(0f, 0f, 0.006f), new Vector2(W, H), 0.04f);

            // header: back, title, sound
            Kit.Button(Root, new Vector3(-W / 2f + 0.05f, H / 2f - 0.05f, 0f), new Vector2(0.056f, 0.056f), null, "arrow-left", KitStyle.Ghost, back);
            _sound = Kit.Button(Root, new Vector3(W / 2f - 0.05f, H / 2f - 0.05f, 0f), new Vector2(0.056f, 0.056f), null, "volume-2", KitStyle.Ghost, toggleSound);
            _kicker = Kit.Text(Root, "", 0.0115f, new Vector3(0f, H / 2f - 0.036f, 0f), new Vector2(0.46f, 0.02f), KitTheme.Ember, TextAlignmentOptions.Center, KitFont.UiBold);
            _kicker.characterSpacing = 14f;
            _title = Kit.Text(Root, "", 0.026f, new Vector3(0f, H / 2f - 0.064f, 0f), new Vector2(0.46f, 0.035f), KitTheme.Paper, TextAlignmentOptions.Center, KitFont.Serif, 12, false);
            Kit.Panel(Root, "Rule", new Vector3(0f, H / 2f - 0.095f, 0.002f), new Vector2(W - 0.08f, 0.0012f), 0.0006f,
                KitTheme.Hairline, KitTheme.Hairline, KitTheme.Clear, 11, 0f);

            // the script
            _lines = new GameObject("Lines").transform;
            _lines.SetParent(Root, false);
            _prev = Kit.Text(_lines, "", 0.019f, new Vector3(0f, 0.065f, 0f), new Vector2(0.58f, 0.03f), KitTheme.Faint, TextAlignmentOptions.Bottom, KitFont.SerifItalic);
            _line = Kit.Text(_lines, "", 0.034f, new Vector3(0f, 0.005f, 0f), new Vector2(0.6f, 0.09f), KitTheme.Paper, TextAlignmentOptions.Center, KitFont.Serif);
            _next = Kit.Text(_lines, "", 0.019f, new Vector3(0f, -0.058f, 0f), new Vector2(0.58f, 0.03f), KitTheme.Faint, TextAlignmentOptions.Top, KitFont.SerifItalic);

            // the question
            _qa = new GameObject("Q&A").transform;
            _qa.SetParent(Root, false);
            Kit.Icon(_qa, "mic", new Vector3(0f, 0.1f, 0f), 0.022f, KitTheme.Ember);
            _state = Kit.Text(_qa, "", 0.016f, new Vector3(0f, 0.074f, 0f), new Vector2(0.58f, 0.025f), KitTheme.Dim, TextAlignmentOptions.Center, KitFont.UiBold);
            _level = Kit.Panel(_qa, "Level", new Vector3(0f, 0.058f, 0f), new Vector2(0.002f, 0.004f), 0.002f, KitTheme.Ember, KitTheme.Ember, KitTheme.Clear, 12, 0f);
            _question = Kit.Text(_qa, "", 0.016f, new Vector3(0f, 0.036f, 0f), new Vector2(0.58f, 0.03f), KitTheme.Dim, TextAlignmentOptions.Center, KitFont.Ui);
            _answer = Kit.Text(_qa, "", 0.03f, new Vector3(0f, -0.01f, 0f), new Vector2(0.6f, 0.07f), KitTheme.Ember, TextAlignmentOptions.Center, KitFont.Serif);
            for (var i = 0; i < 3; i++)
            {
                KitButton b = null;
                b = Kit.Button(_qa, new Vector3((i - 1) * 0.212f, -0.012f, 0f), new Vector2(0.204f, 0.048f), "…", null, KitStyle.Ghost,
                    () => { if (!string.IsNullOrEmpty(b.Label.text)) _pick(b.Label.text); }, null, 0.0122f);
                _suggestions.Add(b);
            }

            // progress
            var y = -H / 2f + 0.105f;
            Kit.Panel(Root, "Track", new Vector3(0f, y, 0.002f), new Vector2(W - 0.1f, 0.006f), 0.003f,
                new Color(1f, 1f, 1f, 0.12f), new Color(1f, 1f, 1f, 0.12f), KitTheme.Clear, 11, 0f);
            _fill = Kit.Panel(Root, "Fill", new Vector3(0f, y, 0f), new Vector2(0.01f, 0.006f), 0.003f, KitTheme.Ember, KitTheme.EmberDeep, KitTheme.Clear, 12, 0f);
            _time = Kit.Text(Root, "", 0.0115f, new Vector3(W / 2f - 0.1f, y - 0.017f, 0f), new Vector2(0.15f, 0.02f), KitTheme.Dim, TextAlignmentOptions.Right);

            // transport, Ask, and the raise-a-hand hint
            var cy = -H / 2f + 0.052f;
            Kit.Button(Root, new Vector3(-0.075f, cy, 0f), new Vector2(0.056f, 0.056f), null, "skip-back", KitStyle.Ghost, prev);
            _pause = Kit.Button(Root, new Vector3(0f, cy, 0f), new Vector2(0.07f, 0.07f), null, "pause", KitStyle.Primary, pause);
            Kit.Button(Root, new Vector3(0.075f, cy, 0f), new Vector2(0.056f, 0.056f), null, "skip-forward", KitStyle.Ghost, next);
            _ask = Kit.Button(Root, new Vector3(W / 2f - 0.1f, cy, 0f), new Vector2(0.14f, 0.056f), "Ask", "mic", KitStyle.Primary, askTap, askHold, 0.02f);
            _hintIcon = Kit.Icon(Root, "hand", new Vector3(-W / 2f + 0.05f, cy, 0f), 0.024f, KitTheme.Dim);
            _hint = Kit.Text(Root, "", 0.0145f, new Vector3(-W / 2f + 0.145f, cy, 0f), new Vector2(0.16f, 0.05f), KitTheme.Dim, TextAlignmentOptions.Left);

            Fade = Root.gameObject.AddComponent<KitGroup>();
            SetState(AskState.Idle, "");
            Fade.Refresh();
        }

        public void Load(StoryInfo story, StoryLanguage lang)
        {
            _story = story; _lang = lang;
            _kicker.text = story.Eyebrow.ToUpperInvariant(); _kicker.color = story.Accent;
            _title.text = story.Title.Replace('\n', ' ');
            _answer.color = story.Accent;
            _prev.text = _next.text = _line.text = "";
            _shownLine = -2; _shownWords = -1;
            SetProgress(0f, 0f);
            SetPauseLabel("Pause");
            SetNote("");
            Fade.Refresh();
        }

        string Line(int i)
        {
            var caps = _story?.Timeline?.Captions;
            return caps != null && i >= 0 && i < caps.Length ? caps[i].TextFor(_lang.Code) : "";
        }

        /// <summary>The words of line <paramref name="index"/> light up in step with the story clock (a pure function of it).</summary>
        public void ShowLine(int index, float T)
        {
            var caps = _story.Timeline.Captions;
            var text = Line(Mathf.Max(index, 0));
            var lit = index < 0 ? 0 : StoryDirector.WordsShown(caps[index], T, StoryUi.WordCount(text));
            if (index == _shownLine && lit == _shownWords) return;
            if (index != _shownLine)
            {
                _prev.text = Line(index - 1);
                _next.text = Line(Mathf.Max(index, 0) + 1);
            }
            _shownLine = index; _shownWords = lit;
            _line.text = StoryUi.Reveal(text, lit, 0, KitTheme.Paper, Unlit);
        }

        public void SetProgress(float T, float duration)
        {
            var k = duration > 0f ? Mathf.Clamp01(T / duration) : 0f;
            var full = W - 0.1f;
            var w = Mathf.Max(full * k, 0.006f);
            _fill.transform.localPosition = new Vector3(-full / 2f + w / 2f, _fill.transform.localPosition.y, 0f);
            _fill.Resize(new Vector2(w, 0.006f));
            _time.text = duration > 0f ? $"{Fmt(T)} / {Fmt(duration)}" : "";
        }

        static string Fmt(float s) => $"{Mathf.FloorToInt(s / 60f)}:{Mathf.FloorToInt(s % 60f):00}";

        /// <summary>"Pause" / "Resume" / "Replay": the big round button shows pause, play or replay.</summary>
        public void SetPauseLabel(string s)
        {
            if (s == _pauseState) return;
            _pauseState = s;
            _pause.SetIcon(s == "Pause" ? "pause" : s == "Replay" ? "rotate-ccw" : "play");
        }

        public void SetSound(bool muted) => _sound.SetIcon(muted ? "volume-x" : "volume-2");

        /// <summary>The hint beside the transport (default: raise a hand to ask). Empty restores the default.</summary>
        public void SetNote(string s)
        {
            _hint.text = string.IsNullOrEmpty(s) ? "Raise a hand to ask" : s;
            _hintIcon.SetIcon(Kit.IconTex(string.IsNullOrEmpty(s) ? "hand" : "message-circle-question-mark"));
        }

        // ---------- IAskView ----------

        public void SetState(AskState state, string text)
        {
            var asking = state != AskState.Idle;
            _lines.gameObject.SetActive(!asking);
            _qa.gameObject.SetActive(asking);
            _state.text = text ?? "";
            var listening = state == AskState.Listening || state == AskState.Interrupt;
            _level.gameObject.SetActive(state == AskState.Listening);
            var qs = listening && _story != null ? StubStoryQa.Suggestions(_story.Id, _lang?.Code) : new string[0];
            for (var i = 0; i < _suggestions.Count; i++)
            {
                var on = i < qs.Length;
                _suggestions[i].gameObject.SetActive(on);
                if (on) _suggestions[i].SetLabel(qs[i]);
            }
            _answer.gameObject.SetActive(!listening);
            _ask.SetLabel(state == AskState.Listening ? "Done" : asking ? "Skip" : "Ask");
            _ask.SetIcon(state == AskState.Listening ? "check" : asking ? "skip-forward" : "mic");
            if (!asking) SetLevel(0f);
        }

        public void SetLevel(float level01)
        {
            var w = Mathf.Max(0.002f, 0.16f * Mathf.Clamp01(level01));
            _level.Resize(new Vector2(w, 0.004f));
        }

        public void SetQuestion(string text) => _question.text = text;
        public void SetAnswer(string text) => _answer.text = text;
    }
}
