using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HypaStories
{
    /// <summary>One line of the script on the story clock (seconds, estimated by <see cref="StoryLibrary"/>), with its translations.</summary>
    public sealed class Caption
    {
        /// <summary>Stable id from story.json: translation files are keyed by it.</summary>
        public string Id;
        public float At, Until;
        public string Text;
        /// <summary>Translated line per language code ("fr", "es"). Missing = the English line.</summary>
        public Dictionary<string, string> Tr = new Dictionary<string, string>();

        /// <summary>Questions a listener may ask once this line has been told (English), and their translations per language code.</summary>
        public string[] Ask = new string[0];
        public Dictionary<string, string[]> AskTr = new Dictionary<string, string[]>();

        public string TextFor(string lang)
            => lang != "en" && Tr != null && Tr.TryGetValue(lang, out var s) && !string.IsNullOrEmpty(s) ? s : Text;

        public string[] AskFor(string lang)
            => lang != "en" && AskTr != null && AskTr.TryGetValue(lang, out var a) && a.Length > 0 ? a : Ask;
    }

    /// <summary>
    /// Captions and weather over time. Tracks are (time, value) keys eased with smoothstep (see <see cref="StoryDirector"/>);
    /// flashes are lightning times, with thunder 0.35 s later.
    /// </summary>
    public sealed class StoryTimeline
    {
        public float Duration;
        public float[] Flashes = new float[0];
        public Caption[] Captions = new Caption[0];
        public Vector2[] Rain = { Vector2.zero };
        public Vector2[] Wind = { Vector2.zero };
    }

    /// <summary>A text-to-speech voice: the model ("provider/model") and one of its voices, as list_voices names them.</summary>
    public sealed class VoiceOption
    {
        public string Model, Voice;
        public override string ToString() => $"{Model} {Voice}";
    }

    /// <summary>
    /// A story card. Theme picks the card art (0 = lantern in the pines, 1 = noir crime scene). The storyteller reads the lines with TTS
    /// and answers questions with <see cref="Model"/>.
    /// </summary>
    public sealed class StoryInfo
    {
        public string Id;
        public int Theme;
        public Color Accent;
        public string Eyebrow, Title, Desc;
        /// <summary>The chat model that answers questions, "provider/model" (e.g. "meta/muse-wonder-1.0").</summary>
        public string Model;
        /// <summary>The storyteller's voices for this story. The first is used (the rest are for a voice picker later).</summary>
        public VoiceOption[] Voices = new VoiceOption[0];
        /// <summary>The voice the story is told in. Null = Hypa's Auto voice for the language.</summary>
        public VoiceOption Voice => Voices.FirstOrDefault();
        public StoryTimeline Timeline;

        /// <summary>
        /// Questions to suggest when the listener stops at <paramref name="lineIndex"/>: those of the lines told so far, newest first,
        /// so they follow the story and never ask about what hasn't happened yet. Pure.
        /// </summary>
        public string[] Suggestions(int lineIndex, string lang, int max = 3)
            => Timeline.Captions.Take(lineIndex + 1).Reverse().SelectMany(c => c.AskFor(lang)).Distinct().Take(max).ToArray();
    }
}
