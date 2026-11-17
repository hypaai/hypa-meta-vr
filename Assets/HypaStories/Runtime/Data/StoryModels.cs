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
            => Has(lang) && lang != "en" ? Tr[lang] : Text;

        /// <summary>This line has its own words in <paramref name="lang"/>. English always does.</summary>
        public bool Has(string lang)
            => lang == "en" || (lang != null && Tr != null && Tr.TryGetValue(lang, out var s) && !string.IsNullOrEmpty(s));

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
        /// <summary>The campfire's strength: 1 as it is, below 1 it shrinks and gutters, above 1 it roars up.</summary>
        public Vector2[] Fire = { new Vector2(0f, 1f) };
        /// <summary>How much deeper the night gets (0..1): the sky, the moonlight and the ambient fall away.</summary>
        public Vector2[] Dark = { Vector2.zero };
        /// <summary>Daybreak (0..1): the sky warms from the horizon up, the stars fade, the fog turns to morning haze.</summary>
        public Vector2[] Dawn = { Vector2.zero };
        /// <summary>Police lights (0..1): red and blue pulsing over the camp.</summary>
        public Vector2[] Police = { Vector2.zero };
        /// <summary>A god's light (0..1): the fire's glow turns slowly from deep red to pale white and back.</summary>
        public Vector2[] Spirit = { Vector2.zero };
        /// <summary>One-off sounds (and their sparks or flicker) at story times: a roar, footsteps, a door, a hiss...</summary>
        public SoundCue[] Sounds = new SoundCue[0];
    }

    /// <summary>A named sound at a story time (seconds on the story clock). See <c>StoryAudio.Effects</c> for the names.</summary>
    public struct SoundCue
    {
        public float T;
        public string Name;
    }

    /// <summary>
    /// A text-to-speech model ("provider/model") and its voice, as list_voices names them: one <see cref="Voice"/> for every language,
    /// and/or one per language code in <see cref="ByLanguage"/> (Hypa's voices are per language).
    /// </summary>
    public sealed class VoiceOption
    {
        public string Model, Voice;
        public Dictionary<string, string> ByLanguage = new Dictionary<string, string>();

        /// <summary>The voice for a language, or null when this model has none for it.</summary>
        public string For(string lang) => lang != null && ByLanguage.TryGetValue(lang, out var v) ? v : Voice;

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
        /// <summary>
        /// The chat models that answer questions, "provider/model", in order: the first that answers wins (see <c>ChatStoryQa</c>).
        /// </summary>
        public string[] Models = new string[0];
        /// <summary>The first choice of <see cref="Models"/>.</summary>
        public string Model => Models.Length > 0 ? Models[0] : StoryLibrary.DefaultModel;
        /// <summary>The storyteller's voices for this story, in order of preference.</summary>
        public VoiceOption[] Voices = new VoiceOption[0];
        /// <summary>Words from the story to try in Hypa Translate at the end ("words" in story.json), in English.</summary>
        public string[] Words = new string[0];
        /// <summary>The same words in each translation's own wording (language code → words), from each language file's "words".</summary>
        public readonly Dictionary<string, string[]> WordsTr = new Dictionary<string, string[]>();

        /// <summary>The story's words in the language it was told in; empty when that language file has none. Pure.</summary>
        public string[] WordsFor(string lang)
            => lang == "en" ? Words : WordsTr.TryGetValue(lang ?? "", out var w) ? w : new string[0];

        /// <summary>
        /// The voices that can tell the story in a language, best first: each entry with a voice for it, as (model, voice).
        /// The first is used; the next ones are the fallbacks. Empty = Hypa's Auto voice for the language. Pure.
        /// </summary>
        public VoiceOption[] VoicesFor(string lang)
            => Voices.Where(v => !string.IsNullOrEmpty(v.For(lang))).Select(v => new VoiceOption { Model = v.Model, Voice = v.For(lang) }).ToArray();
        public StoryTimeline Timeline;

        /// <summary>
        /// Questions to suggest when the listener stops at <paramref name="lineIndex"/>: those of the lines told so far, newest first,
        /// so they follow the story and never ask about what hasn't happened yet. Pure.
        /// </summary>
        public string[] Suggestions(int lineIndex, string lang, int max = 3)
            => Timeline.Captions.Take(lineIndex + 1).Reverse().SelectMany(c => c.AskFor(lang)).Distinct().Take(max).ToArray();
    }
}
