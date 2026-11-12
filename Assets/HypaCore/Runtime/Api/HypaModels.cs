using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HypaCore
{
    /// <summary>One entry from list_languages. API calls take the <see cref="Name"/> label (e.g. "Yoruba").</summary>
    public sealed class HypaLanguage
    {
        public string Name;
        public string Code;
        public IReadOnlyList<string> Voices = new string[0];
        public bool HasTts => Voices != null && Voices.Count > 0;
        public override string ToString() => Name;
    }

    public sealed class HypaPairCheck
    {
        public bool Supported;
        public IReadOnlyList<string> Models = new string[0];
        public IReadOnlyList<string> Unknown = new string[0];
    }

    public sealed class HypaTranslation
    {
        public string Text;
        /// <summary>Detected source language label when auto-detected; may be null.</summary>
        public string DetectedLanguage;
        public string Model;
    }

    /// <summary>
    /// Speech from text_to_speech. Normally <see cref="AudioUrl"/> (signed, valid ~1 hour: play it, don't store it).
    /// <see cref="AudioBytes"/> is for local/test audio.
    /// </summary>
    public sealed class HypaSpeech
    {
        public string AudioUrl;
        public string AudioBase64;
        public byte[] AudioBytes;
        public string Mime;
        public int SampleRate;
        public string Voice;
    }

    /// <summary>look_up_term returns a free-form object. Render what we recognise, then fall back to a plain dump.</summary>
    public sealed class HypaDictionaryEntry
    {
        public JObject Raw;

        static readonly string[] Preferred =
            { "meaning", "definition", "definitions", "meanings", "partOfSpeech", "part_of_speech", "translation", "pronunciation", "example", "examples", "usage", "fun_facts", "cultural_context" };

        /// <summary>Human-readable text, or null when the entry has nothing to show (caller shows a note instead).</summary>
        public string ToDisplayText(int maxChars = 500)
        {
            if (Raw == null || !Raw.HasValues) return null;
            var sb = new StringBuilder();
            var used = new HashSet<string>();
            foreach (var k in Preferred)
            {
                var t = Raw[k];
                if (t == null) continue;
                used.Add(k);
                Append(sb, k, t);
            }
            foreach (var p in Raw.Properties())
                if (!used.Contains(p.Name) && p.Name != "word" && p.Name != "language") Append(sb, p.Name, p.Value);
            var s = sb.ToString().Trim();
            if (s.Length == 0) return null;
            return s.Length > maxChars ? s.Substring(0, maxChars) + "..." : s;
        }

        static void Append(StringBuilder sb, string key, JToken t)
        {
            var text = Flatten(t);
            if (string.IsNullOrWhiteSpace(text)) return;
            var label = key.Replace('_', ' ');
            label = char.ToUpperInvariant(label[0]) + label.Substring(1);
            sb.Append(label).Append(": ").Append(text).Append('\n');
        }

        static string Flatten(JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Null: case JTokenType.Undefined: return null;
                case JTokenType.Array:
                    var parts = new List<string>();
                    foreach (var c in t) { var f = Flatten(c); if (!string.IsNullOrWhiteSpace(f)) parts.Add(f); }
                    return string.Join("; ", parts);
                case JTokenType.Object:
                    var o = (JObject)t;
                    var bits = new List<string>();
                    foreach (var p in o.Properties()) { var f = Flatten(p.Value); if (!string.IsNullOrWhiteSpace(f)) bits.Add(f); }
                    return string.Join(", ", bits);
                default: return t.ToString();
            }
        }
    }
}
