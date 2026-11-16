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
