using System;
using System.Linq;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>A story language: code for the script translations, the Hypa label for TTS / ASR, and the display name.</summary>
    public sealed class StoryLanguage
    {
        public readonly string Code, Name, HypaLabel;
        public StoryLanguage(string code, string name, string hypaLabel) { Code = code; Name = name; HypaLabel = hypaLabel; }
        public override string ToString() => Name;
    }

    /// <summary>What the storyteller says around a question, per language (filler lines are cached at story start).</summary>
    public sealed class QaLines
    {
        public string Listening, Thinking, Again, Offline, Resume, You;
        public string[] Filler;
    }

    /// <summary>
    /// Languages offered for narration, questions and answers: the ones with speech-to-text, translation and text-to-speech
    /// and a translated script (the prototype's public/languages.json). Add a language here and in each caption's Tr.
    /// </summary>
    public static class StoryLanguages
    {
        const string PrefKey = "hypa.stories.lang";

        public static readonly StoryLanguage[] All =
        {
            new StoryLanguage("en", "English", "English"),
            new StoryLanguage("fr", "Français", "French"),
            new StoryLanguage("es", "Español", "Spanish"),
        };

        public static StoryLanguage Find(string codeOrLabel)
            => All.FirstOrDefault(l => string.Equals(l.Code, codeOrLabel, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(l.HypaLabel, codeOrLabel, StringComparison.OrdinalIgnoreCase));

        /// <summary>The last story language picked, else the app's target language if it is one of these, else English.</summary>
        public static StoryLanguage Current
        {
            get => Find(PlayerPrefs.GetString(PrefKey, "")) ?? Find(HypaSettings.TargetLanguage) ?? All[0];
            set { PlayerPrefs.SetString(PrefKey, value.Code); PlayerPrefs.Save(); }
        }

        static readonly QaLines En = new QaLines
        {
            Listening = "Listening…", Thinking = "Thinking…", Filler = new[] { "Hmm, good question…", "Let me think about that…" },
            Again = "Sorry, could you say that again?", Offline = "Let's keep going with the story.", Resume = "Now, where was I…", You = "You asked",
        };

        static readonly QaLines Fr = new QaLines
        {
            Listening = "Je t’écoute…", Thinking = "Je réfléchis…", Filler = new[] { "Hmm, bonne question…", "Laisse-moi y réfléchir…" },
            Again = "Désolé, peux-tu répéter ?", Offline = "Continuons l’histoire.", Resume = "Alors, où en étais-je…", You = "Tu as demandé",
        };

        static readonly QaLines Es = new QaLines
        {
            Listening = "Te escucho…", Thinking = "Pensando…", Filler = new[] { "Mmm, buena pregunta…", "Déjame pensarlo…" },
            Again = "Perdona, ¿puedes repetirlo?", Offline = "Sigamos con la historia.", Resume = "A ver, ¿por dónde iba…", You = "Preguntaste",
        };

        public static QaLines Lines(string code) => code == "fr" ? Fr : code == "es" ? Es : En;
    }
}
