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

    /// <summary>translate_image: the text read from a picture and its translation (text only, no positions).</summary>
    public sealed class HypaImageTranslation
    {
        public string Text;
        public string Translation;
        /// <summary>Detected source language label; may be null.</summary>
        public string Language;
        public bool NoTextFound;
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
        /// <summary>The provider and model that spoke (with model "auto", the one Auto picked), e.g. "hypaai" + "hypa-orpheus-3b-tts-vc-flt".</summary>
        public string Provider, Model;
    }

    /// <summary>A line of a dictionary entry with its label, e.g. type "noun", text "an animal that lives in water".</summary>
    public sealed class HypaCardItem
    {
        public string Type, Text;
        public HypaCardItem(string text, string type = null) { Text = text; Type = type; }
    }

    /// <summary>
    /// A dictionary entry in the shape Hypa Translate's dictionary sidebar shows: word, phonetic and language, definitions (each with its
    /// part of speech), usage examples, synonyms and antonyms, cultural context and fun facts. Built from the free-form look_up_term
    /// result by <see cref="HypaDictionaryEntry.ToCard"/>.
    /// </summary>
    public sealed class HypaDictionaryCard
    {
        public string Word, Phonetic, Language;
        public readonly List<HypaCardItem> Definitions = new List<HypaCardItem>();
        public readonly List<HypaCardItem> Examples = new List<HypaCardItem>();
        public readonly List<string> Synonyms = new List<string>();
        public readonly List<string> Antonyms = new List<string>();
        public readonly List<HypaCardItem> CulturalContext = new List<HypaCardItem>();
        public readonly List<HypaCardItem> FunFacts = new List<HypaCardItem>();

        public bool IsEmpty => Definitions.Count == 0 && Examples.Count == 0 && Synonyms.Count == 0 && Antonyms.Count == 0
                               && CulturalContext.Count == 0 && FunFacts.Count == 0;
    }

    /// <summary>look_up_term returns a free-form object. Render what we recognise, then fall back to a plain dump.</summary>
    public sealed class HypaDictionaryEntry
    {
        public JObject Raw;

        /// <summary>
        /// The entry as a <see cref="HypaDictionaryCard"/>, reading the fields Hypa Translate's dictionary reads: definitions,
        /// usageExamples / usage (else exampleSentences), synonyms / syms, antonyms / antm, cultural_context / culturalContext(s),
        /// fun_facts / funFact(s), phonetic, language. Tolerates strings, arrays and {text|definition|meaning|example} objects.
        /// Never throws; an unrecognised entry comes back empty.
        /// </summary>
        public HypaDictionaryCard ToCard(string word = null)
        {
            var c = new HypaDictionaryCard { Word = (string)Raw?["word"] ?? word };
            if (Raw == null) return c;
            c.Phonetic = First(Raw, "phonetic", "pronunciation");
            c.Language = First(Raw, "language");

            foreach (var key in new[] { "definitions", "definition", "meanings", "meaning" })
            {
                AddItems(c.Definitions, Raw[key]);
                if (c.Definitions.Count > 0) break;
            }

            AddItems(c.Examples, Raw["usageExamples"]); AddItems(c.Examples, Raw["usage"]);
            if (c.Examples.Count == 0) { AddItems(c.Examples, Raw["exampleSentences"]); AddItems(c.Examples, Raw["examples"]); AddItems(c.Examples, Raw["example"]); }

            var syn = new List<HypaCardItem>(); AddItems(syn, Raw["synonyms"]); if (syn.Count == 0) AddItems(syn, Raw["syms"]);
            foreach (var i in syn) c.Synonyms.Add(i.Text);
            var ant = new List<HypaCardItem>(); AddItems(ant, Raw["antonyms"]); if (ant.Count == 0) AddItems(ant, Raw["antm"]);
            foreach (var i in ant) c.Antonyms.Add(i.Text);

            AddItems(c.CulturalContext, Raw["cultural_context"]); AddItems(c.CulturalContext, Raw["culturalContext"]); AddItems(c.CulturalContext, Raw["culturalContexts"]);
            AddItems(c.FunFacts, Raw["fun_facts"]); AddItems(c.FunFacts, Raw["funFact"]); AddItems(c.FunFacts, Raw["funFacts"]);
            return c;
        }

        static readonly string[] TextKeys = { "text", "definition", "meaning", "example", "value" };

        /// <summary>Adds the text lines in a token: a string, an object with a text field (type = partOfSpeech or type), or an array of them.</summary>
        static void AddItems(List<HypaCardItem> into, JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return;
            if (t is JArray arr) { foreach (var i in arr) AddItems(into, i); return; }
            string text = null, type = null;
            if (t.Type == JTokenType.String) text = ((string)t)?.Trim();
            else if (t is JObject o)
            {
                foreach (var k in TextKeys) { var v = o[k]; if (v != null && v.Type == JTokenType.String) { text = ((string)v).Trim(); break; } }
                if (text == null) text = Flatten(o);
                type = First(o, "partOfSpeech", "type");
            }
            else text = Flatten(t);
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!into.Exists(x => x.Text == text && x.Type == type)) into.Add(new HypaCardItem(text, type));
        }

        static string First(JObject o, params string[] keys)
        {
            foreach (var k in keys) { var v = o[k]; if (v != null && v.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)v)) return (string)v; }
            return null;
        }

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
