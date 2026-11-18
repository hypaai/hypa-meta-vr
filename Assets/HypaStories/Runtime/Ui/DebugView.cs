using System;
using System.Collections.Generic;
using System.Text;
using HypaCore;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The debug panel (only with DEBUG=true in .env, see <see cref="HypaConfig.DebugMode"/>; otherwise it is never made and nothing
    /// is recorded): beside the player at eye level, turned in towards you. What the story is doing (line, waiting for which line's
    /// voice and for how long, settling after a skip), the voice (the story's voices, what Auto locked, what is playing), where the
    /// next lines' audio is, every Hypa call running (API, text, language, model, voice, how long) and the recent ones (what came
    /// back: provider, model, voice, or the error), and notes (skips, timeouts, lines played silently). Redrawn four times a second.
    /// It goes where the player goes, except while you read it (look at it, or lean in): then it and the player hold still, so
    /// turning or leaning towards it doesn't send it away.
    /// </summary>
    public sealed class DebugView
    {
        const float W = 0.8f, H = 0.9f, Gap = 0.04f, Turn = 30f, Em = 0.013f, Every = 0.25f;
        /// <summary>How far past its edge a look still counts as reading it; how close counts as leaning in; how long it stays put after.</summary>
        const float LookMargin = 0.12f, LeanIn = 0.75f, Linger = 0.8f;
        const int Recent = 8, ShownNotes = 8, LinesAhead = 4;
        const string Hot = "#FFA852", Ok = "#9BE39B", Bad = "#FF7A6B", Dim = "#A89C8E";

        public readonly Transform Root;
        readonly TextMeshPro _body;
        readonly PlayerView _player;
        readonly StoryPlayer _story;
        readonly StoryVoice _voice;
        readonly Func<StoryLanguage> _lang;
        readonly List<HypaTrace.Call> _calls = new List<HypaTrace.Call>();
        readonly List<HypaTrace.Note> _notes = new List<HypaTrace.Note>();
        readonly StringBuilder _sb = new StringBuilder(4096);
        float _next, _holdUntil;

        public DebugView(Transform uiRoot, PlayerView player, StoryPlayer story, StoryVoice voice, Func<StoryLanguage> lang)
        {
            _player = player; _story = story; _voice = voice; _lang = lang;
            HypaTrace.Enabled = true;

            Root = new GameObject("Debug").transform;
            Root.SetParent(uiRoot, false);
            Kit.Glass(Root, "Panel", new Vector3(0f, 0f, 0.006f), new Vector2(W, H), 0.03f);
            var title = Kit.Text(Root, "DEBUG", 0.015f, new Vector3(-W / 2f + 0.14f, H / 2f - 0.035f, 0f), new Vector2(0.24f, 0.026f), KitTheme.Ember,
                TextAlignmentOptions.Left, KitFont.UiBold);
            title.characterSpacing = 14f;
            Kit.Button(Root, new Vector3(W / 2f - 0.08f, H / 2f - 0.035f, 0f), new Vector2(0.12f, 0.042f), "Clear", null, KitStyle.Ghost,
                HypaTrace.Clear, null, 0.015f);
            _body = Kit.Text(Root, "", Em, new Vector3(0f, -0.025f, 0f), new Vector2(W - 0.05f, H - 0.1f), KitTheme.Paper,
                TextAlignmentOptions.TopLeft, KitFont.Ui);
            _body.overflowMode = TextOverflowModes.Truncate;
            _body.lineSpacing = -8f;
            Place();
        }

        /// <summary>Every frame: shown with the player, placed beside it unless you're reading it, redrawn now and then.</summary>
        public void Update()
        {
            var show = _player.Root.gameObject.activeInHierarchy;
            if (Root.gameObject.activeSelf != show) Root.gameObject.SetActive(show);
            if (!show) { _player.Follow.Hold = false; return; }

            var head = Camera.main != null ? Camera.main.transform : null;
            if (head != null && Reading(head)) _holdUntil = Time.unscaledTime + Linger;
            var hold = Time.unscaledTime < _holdUntil;
            _player.Follow.Hold = hold;
            if (!hold) Place();

            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Every;
            _body.text = Render();
        }

        /// <summary>Just past the player's right edge, centred at eye level (the player sits below the eyes), turned in to face you.</summary>
        void Place()
        {
            var p = _player.Root;
            var yaw = Quaternion.Euler(0f, p.eulerAngles.y, 0f);
            var turn = Turn * Mathf.Deg2Rad;
            var at = new Vector3(PlayerView.Width / 2f + Gap + W / 2f * Mathf.Cos(turn), -_player.Follow.Height, -W / 2f * Mathf.Sin(turn));
            Root.SetPositionAndRotation(p.position + yaw * at, yaw * Quaternion.Euler(0f, Turn, 0f));
        }

        /// <summary>You're looking at it (your gaze lands on it, give or take a margin) or leaning in close in front of it.</summary>
        bool Reading(Transform head)
        {
            var ray = new Ray(head.position, head.forward);
            if (new Plane(Root.forward, Root.position).Raycast(ray, out var d))
            {
                var hit = Root.InverseTransformPoint(ray.GetPoint(d));
                if (Mathf.Abs(hit.x) < W / 2f + LookMargin && Mathf.Abs(hit.y) < H / 2f + LookMargin) return true;
            }
            var me = Root.InverseTransformPoint(head.position); // the panel faces -z
            return me.z < 0f && -me.z < LeanIn && Mathf.Abs(me.x) < W / 2f && Mathf.Abs(me.y) < H / 2f + 0.3f;
        }

        string Render()
        {
            var now = HypaTrace.Now;
            var lang = _lang();
            HypaTrace.Snapshot(_calls, _notes, out var total, out var failed);
            var sb = _sb.Clear();

            // the story
            var idx = _story.CaptionIndex;
            Head(sb, "STORY");
            sb.Append(_story.Story?.Id ?? "none").Append("  ·  ").Append(lang?.Name).Append(" (").Append(lang?.Code).Append(")  ·  line ")
                .Append(idx + 1).Append('/').Append(_story.LineCount).Append("  ·  T ").Append(_story.T.ToString("0.0")).Append("s\n");
            Head(sb, "STATE");
            if (!_story.Live || !_story.Running) sb.Append("not playing");
            else if (_story.Asking) sb.Append("asking (story paused)");
            else if (_story.Paused) sb.Append("paused");
            else if (_story.Ended) sb.Append("ended");
            else if (_story.Settling) sb.Append(Col(Hot, "settling after a skip (nothing asked yet)"));
            else if (_story.WaitingFor >= 0)
            {
                var w = now - _story.WaitingSince;
                sb.Append(Col(w > 3 ? Bad : Hot, $"WAITING for line {_story.WaitingFor + 1}'s voice: {w:0.0}s (gives up at 25s, then silent)"));
            }
            else sb.Append(Col(Ok, "speaking"));
            sb.Append('\n');

            // the voice
            Head(sb, "SPEECH"); sb.Append(_voice.Speaking).Append('\n');
            if (lang != null) { Head(sb, "VOICE"); sb.Append(Esc(_voice.VoiceState(lang))).Append('\n'); }
            Head(sb, "LINES");
            if (_story.Story != null)
                for (var i = Mathf.Max(idx, 0); i < Mathf.Min(Mathf.Max(idx, 0) + LinesAhead, _story.LineCount); i++)
                {
                    var st = _story.LineVoiceState(i);
                    var c = st.StartsWith("ready") ? Ok : st.StartsWith("fetching") ? Hot : st.StartsWith("not") ? Dim : Bad;
                    sb.Append("\n   ").Append(i + 1).Append(' ').Append(_story.LineLanguage(i)?.Code).Append("  ").Append(Col(c, st))
                        .Append(Col(Dim, "  \"" + Esc(HypaTrace.Clip(_story.LineText(i), 48)) + "\""));
                }
            sb.Append('\n');

            // the calls
            var running = 0;
            foreach (var c in _calls) if (c.Running) running++;
            Head(sb, "CALLS");
            sb.Append(Col(running > 2 ? Bad : running > 0 ? Hot : Ok, running + " running")).Append("  ·  ").Append(total).Append(" total  ·  ")
                .Append(Col(failed > 0 ? Bad : Dim, failed + " failed")).Append('\n');
            foreach (var c in _calls)
                if (c.Running)
                    sb.Append("   ").Append(Col(Hot, $"#{c.Id} {c.Api}  {c.Seconds(now):0.0}s")).Append('\n')
                        .Append(Col(Dim, "      " + Esc(c.Sent))).Append('\n');
            Head(sb, "RECENT");
            sb.Append('\n');
            var shown = 0;
            for (var i = _calls.Count - 1; i >= 0 && shown < Recent; i--)
            {
                var c = _calls[i];
                if (c.Running) continue;
                shown++;
                var col = c.Status == "ok" ? Ok : c.Status == "cancelled" ? Dim : Bad;
                sb.Append("   ").Append(Col(col, $"#{c.Id} {c.Api}  {c.Status}  {c.Seconds(now):0.0}s")).Append(Col(Dim, $"  {now - c.End:0}s ago")).Append('\n')
                    .Append(Col(Dim, "      → " + Esc(HypaTrace.Clip(c.Sent, 110)))).Append('\n');
                if (!string.IsNullOrEmpty(c.Got)) sb.Append("      ← ").Append(Esc(HypaTrace.Clip(c.Got, 110))).Append('\n');
            }

            // what the app did
            Head(sb, "NOTES");
            sb.Append('\n');
            for (var i = _notes.Count - 1; i >= 0 && i >= _notes.Count - ShownNotes; i--)
            {
                var n = _notes[i];
                sb.Append("   ").Append(Col(Dim, $"{now - n.At:0.0}s ago  {n.Area}  ")).Append(Esc(n.Text)).Append('\n');
            }
            return sb.ToString();
        }

        static void Head(StringBuilder sb, string label) => sb.Append("<b><color=").Append(Hot).Append('>').Append(label).Append("</color></b>  ");
        static string Col(string hex, string s) => $"<color={hex}>{s}</color>";
        /// <summary>Text from outside (story lines, replies) can't open rich-text tags.</summary>
        static string Esc(string s) => string.IsNullOrEmpty(s) ? s : "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";
    }
}
