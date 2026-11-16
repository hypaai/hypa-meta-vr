using System;
using System.Collections.Generic;
using System.Linq;

namespace HypaStories
{
    /// <summary>Text helpers for the Try Hypa panel. Pure, unit tested.</summary>
    public static class TryHypaText
    {
        /// <summary>A label cut to <paramref name="max"/> characters with an ellipsis.</summary>
        public static string Clip(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s.Substring(0, Math.Max(max - 1, 1)).TrimEnd() + "…";

        static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "of", "to", "in", "on", "for", "with", "his", "her", "their", "they", "them",
            "he", "she", "it", "was", "were", "is", "are", "be", "been", "that", "this", "from", "as", "at", "by", "but",
            "not", "you", "your", "we", "our", "me", "my", "so", "if", "then", "when", "there", "here", "into", "over",
            "after", "before", "about", "what", "who", "how", "one", "out", "all", "just", "said", "says", "came", "come",
            "went", "like", "its", "had", "have", "has", "him", "did", "do", "does", "would", "could", "should",
        };

        /// <summary>
        /// Up to <paramref name="count"/> words from the story, in the order they are told, skipping the little words.
        /// These are the chips you can tap to translate, the way a story offers questions.
        /// </summary>
        public static string[] Suggest(IEnumerable<string> lines, int count = 3)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (lines == null || count <= 0) return new string[0];
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                var word = new System.Text.StringBuilder();
                void Take()
                {
                    if (word.Length == 0) return;
                    var s = word.ToString();
                    word.Clear();
                    if (s.Length < 4 || s.Length > 18 || Stop.Contains(s) || !seen.Add(s)) return;
                    found.Add(s);
                }
                foreach (var ch in line)
                {
                    if (char.IsLetter(ch) || (ch == '\'' && word.Length > 0)) word.Append(ch);
                    else Take();
                    if (found.Count >= count) return found.ToArray();
                }
                Take();
                if (found.Count >= count) break;
            }
            return found.ToArray();
        }
    }

    /// <summary>
    /// Types a text in, a few characters per second of the frame, so what you said appears in the input as if you were typing.
    /// Setting a text that continues the shown one keeps going; a different one starts again. Never splits a surrogate pair.
    /// </summary>
    public sealed class Typewriter
    {
        public float CharsPerSecond = 70f;
        string _target = "";
        float _shown;

        public string Target => _target;
        public bool Done => (int)_shown >= _target.Length;

        public string Current
        {
            get
            {
                var n = Math.Min((int)_shown, _target.Length);
                if (n > 0 && n < _target.Length && char.IsHighSurrogate(_target[n - 1])) n++;
                return _target.Substring(0, Math.Min(n, _target.Length));
            }
        }

        public void Set(string text, bool instant = false)
        {
            text = text ?? "";
            if (text == _target) return;
            if (instant || !text.StartsWith(Current, StringComparison.Ordinal)) _shown = instant ? text.Length : 0f;
            _target = text;
        }

        /// <summary>Advance by <paramref name="dt"/> seconds. Returns true if the visible text changed.</summary>
        public bool Tick(float dt)
        {
            if (Done) return false;
            var before = (int)_shown;
            _shown = Math.Min(_shown + CharsPerSecond * dt, _target.Length);
            return (int)_shown != before;
        }
    }
}
