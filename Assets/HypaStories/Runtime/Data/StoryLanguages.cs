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
        public string Listening, Thinking, Again, Offline, You;
        /// <summary>Said at once, and again if the answer is still coming. One is picked at random.</summary>
        public string[] Filler;
        /// <summary>Said when the story starts again. One is picked at random.</summary>
        public string[] Resume;
    }

    /// <summary>
    /// Languages offered for narration, questions and answers: the ones with speech-to-text, translation and text-to-speech
    /// and a translated script (the prototype's public/languages.json). Add a language here and a {code}.json to each story
    /// (see <see cref="StoryLibrary"/>).
    /// </summary>
    public static class StoryLanguages
    {
        const string PrefKey = "hypa.stories.lang";

        public static readonly StoryLanguage[] All =
        {
            new StoryLanguage("en", "English", "English"),
            new StoryLanguage("fr", "Français", "French"),
            new StoryLanguage("es", "Español", "Spanish"),
            new StoryLanguage("ha", "Hausa", "Hausa"),
            new StoryLanguage("ig", "Igbo", "Igbo"),
            new StoryLanguage("pcm", "Pidgin", "Pidgin"),
            new StoryLanguage("sw", "Kiswahili", "Swahili"),
            new StoryLanguage("yo", "Yorùbá", "Yoruba"),
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
            Listening = "Listening…", Thinking = "Thinking…",
            Filler = new[] { "Hmm, good question…", "Let me think about that…", "Ah, that is a good one…", "Hmm. One moment…", "Good question. Let me think…" },
            Again = "Sorry, could you say that again?", Offline = "Let's keep going with the story.",
            Resume = new[] { "Now, where was I…", "Right. Where was I…", "So, back to the story…", "Now, what was I saying…" },
            You = "You asked",
        };

        static readonly QaLines Fr = new QaLines
        {
            Listening = "Je t’écoute…", Thinking = "Je réfléchis…",
            Filler = new[] { "Hmm, bonne question…", "Laisse-moi y réfléchir…", "Ah, celle-là est bonne…", "Hmm. Un instant…", "Bonne question. Laisse-moi réfléchir…" },
            Again = "Désolé, peux-tu répéter ?", Offline = "Continuons l’histoire.",
            Resume = new[] { "Alors, où en étais-je…", "Bon. Où en étais-je…", "Revenons à l’histoire…", "Alors, que disais-je…" },
            You = "Tu as demandé",
        };

        static readonly QaLines Es = new QaLines
        {
            Listening = "Te escucho…", Thinking = "Pensando…",
            Filler = new[] { "Mmm, buena pregunta…", "Déjame pensarlo…", "Ah, esa está buena…", "Mmm. Un momento…", "Buena pregunta. Déjame pensar…" },
            Again = "Perdona, ¿puedes repetirlo?", Offline = "Sigamos con la historia.",
            Resume = new[] { "A ver, ¿por dónde iba…", "Bien. ¿Por dónde iba…", "Volvamos a la historia…", "A ver, qué estaba diciendo…" },
            You = "Preguntaste",
        };

        static readonly QaLines Ha = new QaLines
        {
            Listening = "Ina sauraro…", Thinking = "Ina tunani…",
            Filler = new[] { "Hmm, tambaya mai kyau…", "Bari in ɗan yi tunani a kai…", "Ah, wannan tambaya ce mai kyau…", "Hmm. Ɗan jira…", "Tambaya mai kyau. Bari in yi tunani…" },
            Again = "Yi haƙuri, za ka iya sake faɗa mini?", Offline = "Mu ci gaba da labarin.",
            Resume = new[] { "To, ina na tsaya ne…", "To, mu ci gaba…", "Mu koma labarin…", "To, me nake faɗa ne…" },
            You = "Ka tambaya",
        };

        static readonly QaLines Ig = new QaLines
        {
            Listening = "Ana m ege ntị…", Thinking = "Ana m eche echiche…",
            Filler = new[] { "Hmm, ọ bụ ezigbo ajụjụ…", "Ka m chee echiche banyere nke ahụ…", "Ah, nke ahụ dị mma…", "Hmm. Chere ntakịrị…", "Ezigbo ajụjụ. Ka m chee echiche…" },
            Again = "Ndo, ị nwere ike ikwu nke ahụ ọzọ?", Offline = "Ka anyị gaa n’ihu n’akụkọ ahụ.",
            Resume = new[] { "Ugbu a, olee ebe m kwụsịrị…", "Ọ dị mma. Olee ebe m kwụsịrị…", "Ka anyị laghachi n’akụkọ…", "Ugbu a, gịnị ka m na-ekwu…" },
            You = "Ị jụrụ",
        };

        static readonly QaLines Pcm = new QaLines
        {
            Listening = "I dey listen…", Thinking = "I dey think…",
            Filler = new[] { "Hmm, na good question be that…", "Make I think about that one…", "Ah, that one sweet…", "Hmm. Wait small…", "Good question. Make I think…" },
            Again = "Abeg, you fit talk am again?", Offline = "Make we continue the tori.",
            Resume = new[] { "Ehen, where I stop sef…", "Okay. Where I stop…", "Make we go back to the tori…", "Ehen, wetin I dey talk…" },
            You = "You ask",
        };

        static readonly QaLines Sw = new QaLines
        {
            Listening = "Nasikiliza…", Thinking = "Ninafikiria…",
            Filler = new[] { "Hmm, swali zuri…", "Hebu nifikirie hilo…", "Ah, hilo ni zuri…", "Hmm. Subiri kidogo…", "Swali zuri. Hebu nifikiri…" },
            Again = "Samahani, unaweza kusema tena?", Offline = "Tuendelee na hadithi.",
            Resume = new[] { "Sasa, nilikuwa wapi…", "Sawa. Nilikuwa wapi…", "Turudi kwenye hadithi…", "Sasa, nilikuwa nasema nini…" },
            You = "Uliuliza",
        };

        static readonly QaLines Yo = new QaLines
        {
            Listening = "Mò ń tẹ́tí sí ọ…", Thinking = "Mò ń ronú…",
            Filler = new[] { "Hmmm, ìbéèrè tó dára nìyẹn…", "Jẹ́ kí n ronú nípa ìyẹn…", "Ah, ìyẹn dára…", "Hmmm. Jẹ́ kí n dúró díẹ̀…", "Ìbéèrè tó dára. Jẹ́ kí n ronú…" },
            Again = "Má bínú, ṣé o lè tún un sọ?", Offline = "Ẹ jẹ́ ká máa bá ìtàn wa lọ.",
            Resume = new[] { "Ó dáa, níbo ni mo dé nínú ìtàn náà…", "Ó dáa. Níbo ni mo wà…", "Ẹ jẹ́ ká padà sí ìtàn…", "Ó dáa, kí ni mo ń sọ…" },
            You = "O béèrè",
        };

        static readonly System.Collections.Generic.Dictionary<string, QaLines> ByCode = new System.Collections.Generic.Dictionary<string, QaLines>
        {
            ["en"] = En, ["fr"] = Fr, ["es"] = Es, ["ha"] = Ha, ["ig"] = Ig, ["pcm"] = Pcm, ["sw"] = Sw, ["yo"] = Yo,
        };

        public static QaLines Lines(string code) => code != null && ByCode.TryGetValue(code, out var l) ? l : En;
    }
}
