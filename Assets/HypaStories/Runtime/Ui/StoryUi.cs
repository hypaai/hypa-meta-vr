using System.Text;
using UnityEngine;

namespace HypaStories
{
    /// <summary>Text helpers for the story UI.</summary>
    public static class StoryUi
    {
        /// <summary>
        /// A caption with its first <paramref name="lit"/> words bright and the rest dim (rich-text colour tags), so the words light
        /// up in step with the voice. maxChars &gt; 0 also wraps it; 0 leaves wrapping to the text box.
        /// </summary>
        public static string Reveal(string text, int lit, int maxChars, Color on, Color off)
        {
            var words = (text ?? "").Split(' ');
            var sb = new StringBuilder();
            string onHex = ColorUtility.ToHtmlStringRGBA(on), offHex = ColorUtility.ToHtmlStringRGBA(off);
            var len = 0;
            var open = -1; // 1 = in the "on" span, 0 = in the "off" span
            for (var i = 0; i < words.Length; i++)
            {
                var w = words[i];
                var state = i < lit ? 1 : 0;
                if (state != open)
                {
                    if (open >= 0) sb.Append("</color>");
                    sb.Append("<color=#").Append(state == 1 ? onHex : offHex).Append('>');
                    open = state;
                }
                if (maxChars > 0 && len > 0 && len + 1 + w.Length > maxChars) { sb.Append('\n'); len = 0; }
                else if (len > 0) { sb.Append(' '); len++; }
                sb.Append(w); len += w.Length;
            }
            if (open >= 0) sb.Append("</color>");
            return sb.ToString();
        }

        public static int WordCount(string text) => string.IsNullOrEmpty(text) ? 0 : text.Split(' ').Length;

        /// <summary>Turns a transform so its readable side (-Z) faces <paramref name="viewer"/>, upright.</summary>
        public static void Face(Transform t, Vector3 viewer)
        {
            var d = t.position - viewer; d.y = 0f;
            if (d.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(d, Vector3.up);
        }
    }
}
