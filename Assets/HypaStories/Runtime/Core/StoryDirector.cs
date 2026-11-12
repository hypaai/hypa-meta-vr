using UnityEngine;

namespace HypaStories
{
    /// <summary>Scene parameters at one moment of a story.</summary>
    public struct StorySample
    {
        public float T, Rain, Wind, Flash;
        /// <summary>The line being (or last) spoken; -1 before the first line.</summary>
        public int Index;
    }

    /// <summary>
    /// Turns a timeline into scene parameters. Everything is a pure function of the story clock T (the voice-over position),
    /// so pause, resume, seek and replay can never drift out of sync: there is no separate animation clock. Pure, unit tested.
    /// </summary>
    public sealed class StoryDirector
    {
        /// <summary>Thunder arrives this long after its lightning flash.</summary>
        public const float ThunderDelay = 0.35f;

        readonly StoryTimeline _tl;
        public StoryDirector(StoryTimeline timeline) { _tl = timeline; }
        public float Duration => _tl.Duration;
        public StoryTimeline Timeline => _tl;

        static float Smooth(float u) => u * u * (3f - 2f * u);

        public static float Track(Vector2[] keys, float t)
        {
            if (keys == null || keys.Length == 0) return 0f;
            if (t <= keys[0].x) return keys[0].y;
            for (var i = 1; i < keys.Length; i++)
            {
                if (t > keys[i].x) continue;
                var a = keys[i - 1]; var b = keys[i];
                var span = b.x - a.x;
                return span <= 0f ? b.y : a.y + (b.y - a.y) * Smooth((t - a.x) / span);
            }
            return keys[keys.Length - 1].y;
        }

        public StorySample Sample(float T)
        {
            var flash = 0f;
            foreach (var f in _tl.Flashes)
            {
                if (T < f) continue;
                var e = T - f;
                flash += Mathf.Exp(-e * 7f) * (0.7f + 0.3f * Mathf.Sin(e * 60f)) + (e > 0.22f ? 0.6f * Mathf.Exp(-(e - 0.22f) * 9f) : 0f);
            }
            return new StorySample
            {
                T = T, Rain = Track(_tl.Rain, T), Wind = Track(_tl.Wind, T), Flash = Mathf.Min(flash, 1f),
                Index = IndexAt(_tl.Captions, T),
            };
        }

        /// <summary>The line being (or last) spoken at T, or -1 before the first.</summary>
        public static int IndexAt(Caption[] caps, float T)
        {
            var index = -1;
            for (var i = 0; i < caps.Length; i++) if (T >= caps[i].At) index = i;
            return index;
        }

        /// <summary>Thunder claps whose time (flash + delay) falls in (a, b]: between two clock readings.</summary>
        public int ThunderBetween(float a, float b)
        {
            var n = 0;
            foreach (var f in _tl.Flashes) if (f + ThunderDelay > a && f + ThunderDelay <= b) n++;
            return n;
        }

        /// <summary>How many of a line's <paramref name="words"/> are lit at T: they switch on in step with the voice.</summary>
        public static int WordsShown(Caption c, float T, int words)
        {
            var u = Mathf.Clamp01((T - c.At) / Mathf.Max(c.Until - c.At - 0.25f, 0.4f));
            return Mathf.CeilToInt(u * words);
        }
    }
}
