using System;

namespace HypaStories
{
    /// <summary>Where the hat game is (the playable scene after Eshu at the Crossroads).</summary>
    public enum HatPhase
    {
        /// <summary>The shrine settles; the hat floats over its post, turning slowly: red, white, red.</summary>
        Intro,
        /// <summary>A round: show each friend, in turn, the side he didn't see.</summary>
        Round,
        /// <summary>The quarrel boiled over: the friends fight in the dust, then the round starts again.</summary>
        Fight,
        /// <summary>Before the last round, Eshu's trick: the friends swap sides of the road.</summary>
        Swap,
        /// <summary>Both friends have seen both sides: set the calabash of palm wine on the post, under the hat.</summary>
        Cup,
        /// <summary>The calabash is on the post. The two friends come and drink from it.</summary>
        Drink,
        /// <summary>They are drinking; set a cowrie at the crossroads for Eshu.</summary>
        Offering,
        /// <summary>The cowrie is left. The town celebrates.</summary>
        Won,
    }

    /// <summary>What a moment of the hat facing a friend did.</summary>
    public enum ShowResult { Nothing, Seen, Wrong }

    /// <summary>
    /// The rules of the hat game, after the story. The left friend saw only red, the right one only white; the badge over each
    /// head shows the colour he still has to see. Each round one friend at a time listens (his badge glows; no timer). Turn the
    /// hat so the side matching his badge faces him and hold it for <see cref="HoldTime"/>: he sees it, then the other friend
    /// listens. Leave his own colour on him (the hat let go) and he shouts: the quarrel jumps; nothing counts against you while
    /// you're still turning it. A wobble out of line only drains the hold a little. The quarrel also grows slowly while you take
    /// your time; full, they fight and the round starts again (no lives). Three rounds; before the last, Eshu swaps their sides.
    /// Then the calabash goes on the post and the friends drink, then the cowrie. Pure, driven by <see cref="Tick"/> and <see cref="Face"/>, unit tested.
    /// </summary>
    public sealed class HatRules
    {
        public const int Rounds = 3, Friends = 2;
        public const float IntroTime = 6f, FightTime = 3.5f, SwapTime = 3f, StareTime = 1.6f, DrinkTime = 4.5f;
        /// <summary>How long a colour must face the listening friend to count.</summary>
        public const float HoldTime = 0.8f;
        /// <summary>The quarrel grows this much a second by itself, by round (full after ~80, 60, 45 s).</summary>
        public static readonly float[] Simmer = { 0.0125f, 0.0165f, 0.022f };
        /// <summary>The quarrel jump for his own colour.</summary>
        public const float WrongSide = 0.22f;

        public HatPhase Phase { get; private set; } = HatPhase.Intro;
        public float T { get; private set; }
        /// <summary>The round, 0..2.</summary>
        public int Round { get; private set; }
        /// <summary>How close the friends are to fighting (0..1).</summary>
        public float Quarrel { get; private set; }
        /// <summary>The friends' sides are swapped (Eshu's trick, last round): the red-shouting friend stands on the right.</summary>
        public bool Swapped { get; private set; }
        public int Fights { get; private set; }
        /// <summary>How far (0..1) the colour now facing the listener is to counting.</summary>
        public float Hold { get; private set; }
        /// <summary>The colour now facing the listener is his own (Hold counts toward a shout, not a look).</summary>
        public bool HoldingWrong { get; private set; }

        readonly bool[] _seen = new bool[Friends];
        bool _latched; // after a shout, the hat must leave him before it can count again

        public event Action<HatPhase> Changed;

        /// <summary>The colour friend <paramref name="i"/> (0 = the one who saw red, 1 = white) shouts: true for red.</summary>
        public static bool ShoutsRed(int i) => i == 0;

        /// <summary>Has friend <paramref name="i"/> seen the other side this round?</summary>
        public bool Seen(int i) => _seen[i];

        /// <summary>The friend listening now (-1 when nobody is): the first round starts with the left friend, then they alternate.</summary>
        public int Listener
        {
            get
            {
                if (Phase != HatPhase.Round) return -1;
                var first = Round % 2;
                if (!_seen[first]) return first;
                return _seen[1 - first] ? -1 : 1 - first;
            }
        }

        public bool Listening(int i) => Listener == i;

        public bool Over => Phase == HatPhase.Won;

        void Go(HatPhase p) { Phase = p; T = 0f; Hold = 0f; HoldingWrong = false; _latched = false; Changed?.Invoke(p); }

        void StartRound() { _seen[0] = _seen[1] = false; Go(HatPhase.Round); }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            T += dt;
            switch (Phase)
            {
                case HatPhase.Intro: if (T >= IntroTime) StartRound(); break;
                case HatPhase.Round:
                    Quarrel = Math.Min(1f, Quarrel + Simmer[Round] * dt);
                    if (Quarrel >= 1f) { Fights++; Go(HatPhase.Fight); }
                    break;
                case HatPhase.Fight:
                    if (T >= FightTime) { Quarrel = 0.3f; StartRound(); }
                    break;
                case HatPhase.Swap: if (T >= SwapTime) { Swapped = true; StartRound(); } break;
                case HatPhase.Drink: if (T >= DrinkTime) Go(HatPhase.Offering); break;
            }
        }

        /// <summary>
        /// Each frame: which colour squarely faces the listening friend (<c>true</c> red, <c>false</c> white, <c>null</c> neither,
        /// side-on), and whether you're still turning the hat. The colour he hasn't seen, held for <see cref="HoldTime"/>, is
        /// Seen (even while you hold the hat). His own colour, left on him once you let go, is Wrong. Out of line, the hold
        /// drains rather than resets, so a shaky hand doesn't lose it.
        /// </summary>
        public ShowResult Face(bool? redFacing, float dt, bool turning = false)
        {
            var i = Listener;
            if (i < 0) { Hold = 0f; HoldingWrong = false; _latched = false; return ShowResult.Nothing; }
            if (redFacing == null) { Hold = Math.Max(0f, Hold - dt * 1.5f / HoldTime); _latched = false; return ShowResult.Nothing; }
            var other = redFacing.Value != ShoutsRed(i);
            if (other == HoldingWrong) { Hold = 0f; _latched = false; } // the colour changed: start over
            HoldingWrong = !other;
            if (_latched) return ShowResult.Nothing;
            if (!other && turning) { Hold = Math.Max(0f, Hold - dt / HoldTime); return ShowResult.Nothing; } // passing by: no harm
            Hold = Math.Min(1f, Hold + dt / HoldTime);
            if (Hold < 1f) return ShowResult.Nothing;
            Hold = 0f;
            if (!other)
            {
                _latched = true;
                Quarrel = Math.Min(1f, Quarrel + WrongSide);
                if (Quarrel >= 1f) { Fights++; Go(HatPhase.Fight); }
                return ShowResult.Wrong;
            }
            _seen[i] = true;
            HoldingWrong = false;
            if (_seen[0] && _seen[1])
            {
                Quarrel = Math.Max(0f, Quarrel - 0.3f);
                if (Round + 1 < Rounds) { Round++; if (Round == Rounds - 1) Go(HatPhase.Swap); else StartRound(); }
                else Go(HatPhase.Cup);
            }
            return ShowResult.Seen;
        }

        /// <summary>The calabash is set on the post, under the hat. The friends come and drink.</summary>
        public bool CupLanded()
        {
            if (Phase != HatPhase.Cup) return false;
            Go(HatPhase.Drink);
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
