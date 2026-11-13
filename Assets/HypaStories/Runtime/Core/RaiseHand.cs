using System;

namespace HypaStories
{
    /// <summary>
    /// "Raise a hand to ask" (ticket 3B). <see cref="Probe"/> reports whether a hand is up right now; HypaStories.Quest sets it on
    /// device (tracked hand or controller above the head). In the editor, H stands in for it.
    /// </summary>
    public static class RaiseHand
    {
        public static Func<bool> Probe;
    }

    /// <summary>Fires once when a hand has been up for <see cref="HoldSeconds"/>; the hand must come down before it can fire again. Pure.</summary>
    public sealed class RaiseHandGesture
    {
        public float HoldSeconds = 0.35f;
        float _up;
        bool _fired;

        public bool Update(bool raised, float dt)
        {
            if (!raised) { _up = 0f; _fired = false; return false; }
            _up += dt;
            if (_fired || _up < HoldSeconds) return false;
            _fired = true;
            return true;
        }
    }
}
