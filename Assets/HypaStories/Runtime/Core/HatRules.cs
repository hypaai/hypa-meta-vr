using System;

namespace HypaStories
{
    /// <summary>Where the hat game is (the playable scene after Eshu at the Crossroads).</summary>
    public enum HatPhase
    {
        /// <summary>The shrine settles; the hat floats over its post, turning slowly: red, white, red.</summary>
        Intro,
        /// <summary>A round: show each friend the side he didn't see.</summary>
        Round,
        /// <summary>The quarrel boiled over: the friends fight in the dust, then the round starts again.</summary>
        Fight,
        /// <summary>Before the last round, Eshu's trick: the friends swap sides of the road.</summary>
        Swap,
        /// <summary>Both friends have seen both sides: flick the calabash between them.</summary>
        Cup,
        /// <summary>They drink and laugh; set a cowrie at the crossroads for Eshu.</summary>
        Offering,
        /// <summary>The cowrie is left. The town celebrates.</summary>
        Won,
    }

    /// <summary>What happened when you turned the hat toward a friend.</summary>
    public enum ShowResult { Ignored, Seen, Wrong, NotLooking }

    /// <summary>
    /// The rules of the hat game, after the story: three rounds; in each, the two friends pace and shout their own colour (the
    /// left friend saw red, the right one white). Now and then one stops to catch his breath: a short window. Turn the hat so
    /// its other side faces him then, and he sees it. Showing him his own colour, or turning it to him while he's still shouting,
    /// feeds the quarrel; it also grows by itself while you wait. When it boils over they fight and the round starts again.
    /// The windows get shorter each round, and before the last one Eshu swaps the friends' sides. Then the calabash, then the
    /// cowrie. No lives: you can always finish. Pure, driven by <see cref="Tick"/>, unit tested.
    /// </summary>
    public sealed class HatRules
    {
        public const int Rounds = 3, Friends = 2;
        public const float IntroTime = 5f, FightTime = 3.5f, SwapTime = 3f, StareTime = 1.6f;
        /// <summary>How long a friend holds still for you, by round.</summary>
        public static readonly float[] Window = { 3.6f, 2.9f, 2.3f };
        /// <summary>Seconds of shouting between pauses, by round.</summary>
        public static readonly float[] Shouting = { 3.2f, 2.7f, 2.2f };
        /// <summary>The quarrel grows this much a second by itself, and by these for a mistake.</summary>
        public const float Simmer = 0.028f, WrongSide = 0.26f, Shouted = 0.12f;

        public HatPhase Phase { get; private set; } = HatPhase.Intro;
        public float T { get; private set; }
        /// <summary>The round, 0..2.</summary>
        public int Round { get; private set; }
        /// <summary>How close the friends are to fighting (0..1).</summary>
        public float Quarrel { get; private set; }
        /// <summary>The friends' sides are swapped (Eshu's trick, last round): the red-shouting friend stands on the right.</summary>
        public bool Swapped { get; private set; }
        public int Fights { get; private set; }

        readonly bool[] _seen = new bool[Friends];
        readonly float[] _pauseAt = new float[Friends];
        float _clock;

        public event Action<HatPhase> Changed;

        /// <summary>The colour friend <paramref name="i"/> (0 = the one who saw red, 1 = white) shouts: true for red.</summary>
        public static bool ShoutsRed(int i) => i == 0;

        /// <summary>Has friend <paramref name="i"/> seen the other side this round?</summary>
        public bool Seen(int i) => _seen[i];

        /// <summary>Is friend <paramref name="i"/> holding still (catching his breath) now: the window to show him?</summary>
        public bool Pausing(int i) => Phase == HatPhase.Round && !_seen[i] && _clock >= _pauseAt[i] && _clock < _pauseAt[i] + Window[Round];

        /// <summary>Seconds left in friend <paramref name="i"/>'s pause (0 when he isn't pausing).</summary>
        public float PauseLeft(int i) => Pausing(i) ? _pauseAt[i] + Window[Round] - _clock : 0f;

        public bool Over => Phase == HatPhase.Won;

        void Go(HatPhase p) { Phase = p; T = 0f; Changed?.Invoke(p); }

        void StartRound()
        {
            _seen[0] = _seen[1] = false;
            // they take turns to pause, the first a little after the round begins
            _pauseAt[0] = _clock + 1.6f;
            _pauseAt[1] = _clock + 1.6f + Shouting[Round] * 0.5f + Window[Round] * 0.6f;
            Go(HatPhase.Round);
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            _clock += dt; T += dt;
            switch (Phase)
            {
                case HatPhase.Intro: if (T >= IntroTime) StartRound(); break;
                case HatPhase.Round:
                    Quarrel = Math.Min(1f, Quarrel + Simmer * dt);
                    for (var i = 0; i < Friends; i++)
                        if (!_seen[i] && _clock >= _pauseAt[i] + Window[Round])
                            _pauseAt[i] = _clock + Shouting[Round] + (i == 0 ? 0.3f : 0f); // missed: he shouts on, and pauses again later
                    if (Quarrel >= 1f) { Fights++; Go(HatPhase.Fight); }
                    break;
                case HatPhase.Fight:
                    if (T >= FightTime) { Quarrel = 0.3f; StartRound(); }
                    break;
                case HatPhase.Swap: if (T >= SwapTime) { Swapped = true; StartRound(); } break;
            }
        }

        /// <summary>
        /// You turned the hat so <paramref name="redFacing"/> (true: red) faces friend <paramref name="i"/>. Seen: he holds still and
        /// it's the other side. Wrong: his own colour. NotLooking: he's still shouting.
        /// </summary>
        public ShowResult Show(int i, bool redFacing)
        {
            if (Phase != HatPhase.Round || _seen[i]) return ShowResult.Ignored;
            var other = redFacing != ShoutsRed(i);
            if (!Pausing(i))
            {
                if (!other) return ShowResult.Ignored; // turning his own colour past him while he shouts: no harm
                Quarrel = Math.Min(1f, Quarrel + Shouted);
                return ShowResult.NotLooking;
            }
            if (!other) { Quarrel = Math.Min(1f, Quarrel + WrongSide); return ShowResult.Wrong; }
            _seen[i] = true;
            if (_seen[0] && _seen[1])
            {
                Quarrel = Math.Max(0f, Quarrel - 0.35f);
                if (Round + 1 < Rounds) { Round++; if (Round == Rounds - 1) Go(HatPhase.Swap); else StartRound(); }
                else Go(HatPhase.Cup);
            }
            return ShowResult.Seen;
        }

        /// <summary>The calabash landed between them.</summary>
        public bool CupLanded()
        {
            if (Phase != HatPhase.Cup) return false;
            Go(HatPhase.Offering);
            return true;
        }

        /// <summary>The cowrie is set down at the crossroads.</summary>
        public bool OfferingLeft()
        {
            if (Phase != HatPhase.Offering) return false;
            Go(HatPhase.Won);
            return true;
        }
    }
}
