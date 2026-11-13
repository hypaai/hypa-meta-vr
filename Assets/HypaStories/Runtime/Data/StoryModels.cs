using System.Collections.Generic;
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

        public string TextFor(string lang)
            => lang != "en" && Tr != null && Tr.TryGetValue(lang, out var s) && !string.IsNullOrEmpty(s) ? s : Text;
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

    /// <summary>
    /// A story card. Theme picks the card art (0 = lantern in the pines, 1 = noir crime scene). The storyteller reads the lines with TTS.
    /// </summary>
    public sealed class StoryInfo
    {
        public string Id;
        public int Theme;
        public Color Accent;
        public string Eyebrow, Title, Desc;
        public StoryTimeline Timeline;
    }
}
