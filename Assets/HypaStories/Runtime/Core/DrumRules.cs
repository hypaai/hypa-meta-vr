using System;

namespace HypaStories
{
    /// <summary>Where the drum game is (the playable scene after The King's Magic Drum).</summary>
    public enum DrumPhase
    {
        /// <summary>The drums are set before you; the camp settles.</summary>
        Intro,
        /// <summary>The bell counts a bar in, then the drums play the king's beat by themselves (each skin lights as it sounds). Listen.</summary>
        Call,
        /// <summary>Your turn: the bell keeps time; play the beat back.</summary>
        Answer,
        /// <summary>The beat was right: a dish of the feast appears by the fire.</summary>
        Feast,
        /// <summary>The beat was wrong: the guests laugh, and the drums play it again.</summary>
        Miss,
        /// <summary>Every dish is on the ground. Dawn, and the town comes to eat.</summary>
        Won,
    }

    /// <summary>Which drum a stroke is on: the big low drum (your left) or the small high one (your right).</summary>
    public enum Tone { Low, High }

    /// <summary>What a stroke did.</summary>
    public enum StrokeResult { Ignored, Good, Done, Wrong }

    /// <summary>Why a beat went wrong.</summary>
    public enum MissReason { None, WrongTone, Early, Late }

    /// <summary>
    /// The rules of the drum game, after the story. The king's drums remember his beats. For each dish the ogene bell counts a
    /// bar in, the drums play a one-bar beat by themselves, and the bell keeps going while you play it back. Only the gaps
    /// between your strokes count (come in whenever you like; a little slower or faster as a whole still counts), each within
    /// <see cref="Tolerance"/>. A stroke on the wrong drum, too soon or too late is a miss and the drums play the beat again
    /// (no lives); not starting at all just plays it again. Each beat played back brings a dish of the feast; the last brings dawn.
    /// Pure, driven by <see cref="Tick"/> and <see cref="Stroke"/>, unit tested.
    /// </summary>
    public sealed class DrumRules
    {
        /// <summary>One step of a beat (an eighth note at 96 beats a minute), and a bar of eight.</summary>
        public const float Step = 0.3125f;
        public const int StepsPerBar = 8;
        public const float Bar = Step * StepsPerBar;
        public const float IntroTime = 3.5f, CallTail = 0.15f, FeastTime = 3.2f, MissTime = 2.4f;
        /// <summary>How long your turn waits for a first stroke before the drums play the beat again: two bars.</summary>
        public const float AnswerWait = Bar * 2f;
        /// <summary>A second stroke this soon after the last is the same stroke (two hands landing together, a hand bouncing).</summary>
        public const float Flam = 0.12f;

        /// <summary>
        /// The king's beats, a bar of eighths each, easiest first: L = the low drum, H = the high drum, . = a rest. The early ones
        /// sit on the beat; then the 3+3+2 of the tresillo pulls against the bell, and the last ones fill the off-beats.
        /// </summary>
        public static readonly string[] Patterns = { "L.L.L.H.", "L..L..H.", "L.HL.HL.", "L..LH.H.", "LH.HL.L.", "L.HL.HLH" };

        /// <summary>The feast, in the story's own words: one dish for each beat.</summary>
        public static readonly string[] Dishes = { "Fish", "Fufu", "Yams", "Okra", "Palm-oil soup", "Palm wine" };

        public struct Beat { public float T; public Tone Tone; public int Slot; }

        public DrumPhase Phase { get; private set; } = DrumPhase.Intro;
        public float T { get; private set; }
        /// <summary>How many dishes are on the ground (= beats played back).</summary>
        public int Served { get; private set; }
        /// <summary>The strokes of the beat being answered that were right so far.</summary>
        public int Next { get; private set; }
        public int Misses { get; private set; }
        public MissReason Missed { get; private set; }

        readonly Beat[][] _beats;
        float _last;

        public event Action<DrumPhase> Changed;

        public DrumRules()
        {
            _beats = new Beat[Patterns.Length][];
            for (var i = 0; i < Patterns.Length; i++) _beats[i] = Parse(Patterns[i]);
        }

        /// <summary>A beat's strokes from its pattern: one step a character, rests skipped. Pure.</summary>
        public static Beat[] Parse(string pattern)
        {
            var list = new System.Collections.Generic.List<Beat>();
            for (var i = 0; i < pattern.Length; i++)
                if (pattern[i] == 'L' || pattern[i] == 'H') list.Add(new Beat { T = i * Step, Slot = i, Tone = pattern[i] == 'L' ? Tone.Low : Tone.High });
            return list.ToArray();
        }

        /// <summary>How far off a gap may be and still count: a fixed slack, or a share of a long gap.</summary>
        public static float Tolerance(float gap) => Math.Max(0.15f, 0.36f * gap);

        /// <summary>The beat being called or answered now (the last one once won).</summary>
        public Beat[] Current => _beats[Math.Min(Served, _beats.Length - 1)];

        /// <summary>When (seconds into <see cref="DrumPhase.Call"/>) the drums play stroke <paramref name="i"/>: after a bar of the bell.</summary>
        public float CallAt(int i) => Bar + Current[i].T;

        /// <summary>The call: the bell's bar, then the beat's bar. Whole bars, so the bell runs straight on into your turn.</summary>
        public float CallLength => Bar * 2f;

        public bool Over => Phase == DrumPhase.Won;

        /// <summary>The bell keeps time through the call and your turn (seconds on its grid: <see cref="T"/>, the call's bars carried on).</summary>
        public bool Keeping => Phase == DrumPhase.Call || Phase == DrumPhase.Answer;

        void Go(DrumPhase p) { Phase = p; T = 0f; Changed?.Invoke(p); }

        void Call() { Next = 0; Go(DrumPhase.Call); }

        void Miss(MissReason why) { Missed = why; Misses++; Next = 0; Go(DrumPhase.Miss); }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            T += dt;
            switch (Phase)
            {
                case DrumPhase.Intro: if (T >= IntroTime) Call(); break;
                case DrumPhase.Call:
                    if (T >= CallLength)
                    {
                        Missed = MissReason.None;
                        var over = T - CallLength; // keep the bell's grid: your turn starts on the bar line, not a frame late
                        Go(DrumPhase.Answer);
                        T = over;
                    }
                    break;
                case DrumPhase.Answer:
                    if (Next == 0) { if (T >= AnswerWait) Call(); } // nothing played yet: hear it again, no harm
                    else
                    {
                        var gap = Current[Next].T - Current[Next - 1].T;
                        if (T - _last > gap + Tolerance(gap)) Miss(MissReason.Late);
                    }
                    break;
                case DrumPhase.Feast:
                    if (T >= FeastTime) { if (Served >= _beats.Length) Go(DrumPhase.Won); else Call(); }
                    break;
                case DrumPhase.Miss: if (T >= MissTime) Call(); break;
            }
        }

        /// <summary>A stroke on a drum. Only strokes in <see cref="DrumPhase.Answer"/> count; the drums sound either way.</summary>
        public StrokeResult Stroke(Tone tone)
        {
            if (Phase != DrumPhase.Answer) return StrokeResult.Ignored;
            var beat = Current;
            if (Next > 0)
            {
                if (T - _last < Flam) return StrokeResult.Ignored;
                var gap = beat[Next].T - beat[Next - 1].T;
                var off = T - _last - gap;
                if (off < -Tolerance(gap)) { Miss(MissReason.Early); return StrokeResult.Wrong; }
                if (off > Tolerance(gap)) { Miss(MissReason.Late); return StrokeResult.Wrong; }
            }
            if (tone != beat[Next].Tone) { Miss(MissReason.WrongTone); return StrokeResult.Wrong; }
            _last = T;
            Next++;
            if (Next < beat.Length) return StrokeResult.Good;
            Served++;
            Go(DrumPhase.Feast);
            return StrokeResult.Done;
        }
    }
}
