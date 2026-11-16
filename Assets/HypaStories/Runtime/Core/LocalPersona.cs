using System;
using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>One canned answer for the offline brain: any of the comma-separated keywords in a question picks it.</summary>
    [Serializable]
    public sealed class CannedAnswer
    {
        [Tooltip("Comma-separated words or phrases, e.g. \"weave, loom, cloth\".")]
        public string Keywords = "";
        [TextArea(2, 4)] public string Answer = "";
    }

    /// <summary>
    /// Who a local is (ticket 3I): one asset per local (Assets > Create > Hypa > Stories > Local Persona). The chat brain is
    /// told to be this person, to talk only about their own world and life, and to say so when asked about what they don't
    /// know. The offline brain answers from <see cref="Canned"/> and <see cref="Fallback"/>, so the demo never stalls.
    /// Drop a <see cref="Local"/> on a character, give it one of these, and it can be talked to.
    /// </summary>
    [CreateAssetMenu(menuName = "Hypa/Stories/Local Persona", fileName = "NewLocal")]
    public sealed class LocalPersona : ScriptableObject
    {
        [Header("Who")]
        public string DisplayName = "Ama";
        [Tooltip("What they do, e.g. \"weaver\".")]
        public string Role = "weaver";
        [Tooltip("Where they live, e.g. \"a small hut at the edge of the village, by the forest\".")]
        public string Home = "";
        [Tooltip("Their own world and life: family, work, the village, its customs. The brain only talks about this.")]
        [TextArea(3, 8)] public string Knows = "";
        [Tooltip("What they don't know or won't talk about (phones, cities far away, the story's ending...).")]
        [TextArea(2, 5)] public string DoesNotKnow = "";

        [Header("Voice")]
        [Tooltip("The language they grew up speaking (flavour for the brain). They answer in the story language the user picked.")]
        public string NativeLanguage = "English";
        [Tooltip("Hypa voice id for their speech (see list_voices); empty = Hypa's default for the language.")]
        public string Voice = "";
        [Tooltip("What they say when you come close.")]
        [TextArea(2, 4)] public string Greeting = "";
        [Tooltip("Three questions to pick when there's no microphone or key (also shown while listening).")]
        public string[] Suggested = new string[3];
        [Tooltip("Other names they answer to when called (the wake word), e.g. \"Auntie Ama\". Their name always works.")]
        public string[] WakeNames = new string[0];

        [Header("Offline answers")]
        public CannedAnswer[] Canned = new CannedAnswer[0];
        [Tooltip("The offline answer when no keyword matches: in character, inviting another question.")]
        [TextArea(2, 4)] public string Fallback = "";

        /// <summary>What's missing for this persona to work, or null when it's ready. Pure, unit tested.</summary>
        public string Validate()
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(DisplayName)) missing.Add("a name");
            if (string.IsNullOrWhiteSpace(Role)) missing.Add("a role");
            if (string.IsNullOrWhiteSpace(Knows)) missing.Add("what they know");
            if (string.IsNullOrWhiteSpace(Greeting)) missing.Add("a greeting");
            if (Suggested == null || Suggested.Length < 3 || Array.Exists(Suggested, string.IsNullOrWhiteSpace)) missing.Add("3 suggested questions");
            if (string.IsNullOrWhiteSpace(Fallback)) missing.Add("an offline fallback answer");
            return missing.Count == 0 ? null : $"{(string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName)} needs {string.Join(", ", missing)}.";
        }

        /// <summary>The names that wake them: their name, then <see cref="WakeNames"/>. Pure.</summary>
        public IEnumerable<string> CalledBy()
        {
            if (!string.IsNullOrWhiteSpace(DisplayName)) yield return DisplayName;
            foreach (var n in WakeNames ?? new string[0]) if (!string.IsNullOrWhiteSpace(n)) yield return n;
        }

        /// <summary>"Ama, the weaver". Pure.</summary>
        public string Title => string.IsNullOrWhiteSpace(Role) ? DisplayName : $"{DisplayName}, the {Role}";

        /// <summary>A persona from JSON with the same field names (e.g. content written outside Unity). Unit tested.</summary>
        public static LocalPersona FromJson(string json)
        {
            var p = CreateInstance<LocalPersona>();
            JsonUtility.FromJsonOverwrite(json, p);
            return p;
        }
    }
}
