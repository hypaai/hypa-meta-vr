using System;

namespace HypaStories
{
    /// <summary>Where the Dodo game is (the playable scene after Seven Hot Stones).</summary>
    public enum DodoPhase
    {
        /// <summary>Dodo bursts out of the trees.</summary>
        Intro,
        /// <summary>Put the seven stones in the fire; each heats until it glows red.</summary>
        Heat,
        /// <summary>Dodo roars its challenge: "Who is able to fight with me?"</summary>
        Roar,
        /// <summary>Waiting for you to answer "I am!"</summary>
        Answer,
        /// <summary>Dodo comes to the fire and starts to stoop.</summary>
        Approach,
        /// <summary>Dodo's mouth is open over the fire: throw a hot stone now.</summary>
        Window,
        /// <summary>A stone went in: Dodo staggers and swallows.</summary>
        Swallow,
        /// <summary>A miss (or too slow): Dodo steps closer and roars. One life gone.</summary>
        Missed,
        /// <summary>Dodo got you. Game over.</summary>
        Lost,
        /// <summary>All seven stones swallowed: Dodo falls.</summary>
        Won,
    }

    /// <summary>
    /// The rules of the Dodo game, as the story tells it: heat seven stones; each time Dodo roars its challenge you answer
    /// "I am!", it stoops at the fire, and you throw a hot stone into its mouth. Three misses (a stone that misses, or not
    /// throwing in time, or not answering twice) and Dodo wins. A missed stone rolls back into the fire to be thrown again.
    /// Pure: driven by <see cref="Tick"/> and the player's actions, unit tested.
    /// </summary>
    public sealed class DodoRules
    {
        public const int Stones = 7, Lives = 3;
        public const float IntroTime = 4.5f, HeatTime = 2.2f, RoarTime = 2.8f, AnswerTime = 7f, ApproachTime = 2.6f,
            WindowTime = 4.5f, SwallowTime = 3.2f, MissTime = 3f;

        public DodoPhase Phase { get; private set; } = DodoPhase.Intro;
        /// <summary>Seconds in the current phase.</summary>
        public float T { get; private set; }
        /// <summary>Stones placed in the fire (0..7).</summary>
        public int Placed { get; private set; }
        /// <summary>Stones Dodo has swallowed.</summary>
        public int Hits { get; private set; }
        public int LivesLeft { get; private set; } = Lives;
        /// <summary>Times Dodo has roared without an answer this round (the second silence counts as a miss).</summary>
        public int Silences { get; private set; }

        float _clock;
        readonly float[] _placedAt = new float[Stones];

        /// <summary>Fired with each new phase.</summary>
        public event Action<DodoPhase> Changed;

        public bool Over => Phase == DodoPhase.Lost || Phase == DodoPhase.Won;

        /// <summary>How hot stone <paramref name="i"/> is (0 cold .. 1 red hot). Stones not yet placed are cold.</summary>
        public float Heat(int i) => i < Placed ? Math.Min(1f, Math.Max(0f, (_clock - _placedAt[i]) / HeatTime)) : 0f;

        /// <summary>Stones in the fire and ready to throw (placed, hot, not yet swallowed).</summary>
        public int Ready
        {
            get
            {
                var n = 0;
                for (var i = 0; i < Placed; i++) if (Heat(i) >= 1f) n++;
                return Math.Max(0, n - Hits);
            }
        }

        /// <summary>Seconds left to throw while Dodo's mouth is open (0 outside the window).</summary>
        public float WindowLeft => Phase == DodoPhase.Window ? Math.Max(0f, WindowTime - T) : 0f;

        void Go(DodoPhase p)
        {
            Phase = p; T = 0f;
            Changed?.Invoke(p);
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            _clock += dt; T += dt;
            switch (Phase)
            {
                case DodoPhase.Intro: if (T >= IntroTime) Go(DodoPhase.Heat); break;
                case DodoPhase.Heat: if (Placed == Stones && Heat(Stones - 1) >= 1f) Go(DodoPhase.Roar); break;
                case DodoPhase.Roar: if (T >= RoarTime) Go(DodoPhase.Answer); break;
                case DodoPhase.Answer:
                    if (T < AnswerTime) break;
                    Silences++;
                    if (Silences >= 2) Miss(); else Go(DodoPhase.Roar); // it roars once more, then loses patience
                    break;
                case DodoPhase.Approach: if (T >= ApproachTime) Go(DodoPhase.Window); break;
                case DodoPhase.Window: if (T >= WindowTime) Miss(); break;
                case DodoPhase.Swallow: if (T >= SwallowTime) Go(Hits >= Stones ? DodoPhase.Won : DodoPhase.Roar); break;
                case DodoPhase.Missed: if (T >= MissTime) Go(DodoPhase.Roar); break;
            }
        }

        /// <summary>A stone dropped into the fire. False once all seven are in, or outside the heating.</summary>
        public bool Place()
        {
            if (Phase != DodoPhase.Heat && Phase != DodoPhase.Intro || Placed >= Stones) return false;
            _placedAt[Placed++] = _clock;
            return true;
        }

        /// <summary>You answered "I am!". Only while Dodo waits for an answer (or still roaring its challenge).</summary>
        public bool Answer()
        {
            if (Phase != DodoPhase.Answer && Phase != DodoPhase.Roar) return false;
            Silences = 0;
            Go(DodoPhase.Approach);
            return true;
        }

        /// <summary>A hot stone went into Dodo's open mouth.</summary>
        public bool Hit()
        {
            if (Phase != DodoPhase.Window || Ready <= 0) return false;
            Hits++;
            Go(DodoPhase.Swallow);
            return true;
        }

        /// <summary>A thrown stone missed while the mouth was open (or time ran out). A life gone; the stone goes back to the fire.</summary>
        public bool Miss()
        {
            if (Phase != DodoPhase.Window && Phase != DodoPhase.Answer) return false;
            LivesLeft--;
            Silences = 0;
            Go(LivesLeft <= 0 ? DodoPhase.Lost : DodoPhase.Missed);
            return true;
        }
    }
}
