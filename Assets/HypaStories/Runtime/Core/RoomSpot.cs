using System;
using UnityEngine;

namespace HypaStories
{
    /// <summary>Where the fire goes in the real room: its spot on the floor (x, z), the way it faces away from you, and the gap to the storyteller.</summary>
    public readonly struct RoomSpotPlan
    {
        /// <summary>The fire, on the floor (world x, z).</summary>
        public readonly Vector2 Fire;
        /// <summary>Unit direction from you through the fire, on to the storyteller (x, z).</summary>
        public readonly Vector2 Away;
        /// <summary>Metres between the fire and the storyteller, behind it.</summary>
        public readonly float Gap;
        /// <summary>Found in the scanned room (false: simply in front of you, without a scan or when nothing was open).</summary>
        public readonly bool Scanned;

        public RoomSpotPlan(Vector2 fire, Vector2 away, float gap, bool scanned) { Fire = fire; Away = away; Gap = gap; Scanned = scanned; }
        public Vector2 Teller => Fire + Away * Gap;
    }

    /// <summary>
    /// Ticket 3K: finds an open spot in the room for the fire and the storyteller (pure maths, no Meta types, so it is tested in
    /// EditMode). The scan is reduced to one question: <c>clearance(p)</c>, how far the floor point <c>p</c> (x, z) is from the
    /// nearest wall or furniture, with zero or less meaning outside the room or inside something.
    /// You, the fire and the storyteller stand in a line, the way the camp always was: you, then the fire, then the storyteller
    /// across it, facing you. Small rooms squeeze the gap between the fire and the storyteller, down to <see cref="MinGap"/>.
    /// </summary>
    public static class RoomSpot
    {
        /// <summary>Fire to storyteller in the forest is about 2.1 m; in a room the gap shrinks to fit, but no closer than this.</summary>
        public const float WoodsGap = 2.1f, MinGap = 1.1f;
        /// <summary>Open floor needed round the fire (its stone ring is 1 m across) and the storyteller's log, metres.</summary>
        public const float FireClear = 0.65f, TellerClear = 0.5f;
        /// <summary>With no scan the fire is put this far in front (2 m, so the player panel floating about 0.9 m ahead stays clear of it) of you, and the storyteller this far behind it.</summary>
        public const float InFrontDistance = 2f, InFrontGap = 1.6f;

        static readonly float[] Distances = { 1.6f, 2f, 2.4f, 2.8f, 3.2f };
        // straight ahead first, then to either side, then behind (a corner room may have nothing but the way you are not facing)
        static readonly float[] Angles = { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };

        /// <summary>
        /// The best open spot in the room for the fire and the storyteller, or null when there is nowhere at all. First within a few
        /// metres of <paramref name="user"/> (x, z) with comfortable room round both; failing that with less room; failing that anywhere
        /// on the floor (<paramref name="floor"/>, the room's outline) so the fire is never put on a bed or a sofa. Prefers a spot
        /// ahead of you, a wide gap, and plenty of room round both.
        /// </summary>
        public static RoomSpotPlan? Find(Vector2 user, Vector2 forward, Func<Vector2, float> clearance, Rect? floor = null)
        {
            if (clearance == null) return null;
            forward = forward.sqrMagnitude < 1e-6f ? Vector2.up : forward.normalized;
            return Search(user, forward, clearance, null, FireClear, TellerClear)
                   ?? Search(user, forward, clearance, null, 0.45f, 0.3f)
                   ?? (floor.HasValue ? Search(user, forward, clearance, floor.Value, 0.4f, 0.25f) : null);
        }

        static RoomSpotPlan? Search(Vector2 user, Vector2 forward, Func<Vector2, float> clearance, Rect? floor, float fireClear, float tellerClear)
        {
            RoomSpotPlan? best = null;
            var bestScore = float.NegativeInfinity;
            void Try(Vector2 fire, float angle)
            {
                var toFire = fire - user;
                if (toFire.magnitude < 1.2f) return;
                var dir = toFire.normalized;
                var dist = toFire.magnitude;
                var fireRoom = clearance(fire);
                if (fireRoom < fireClear) return;
                if (!PathOpen(user, fire, clearance)) return;
                // the widest gap that still leaves the storyteller room
                var gap = 0f; var tellerRoom = 0f;
                for (var g = WoodsGap; g >= MinGap - 1e-3f; g -= 0.1f)
                {
                    var room = clearance(fire + dir * g);
                    if (room < tellerClear) continue;
                    gap = g; tellerRoom = room;
                    break;
                }
                if (gap <= 0f) return;
                var score = gap + Mathf.Min(fireRoom, 1.5f) * 0.8f + Mathf.Min(tellerRoom, 1.5f) * 0.5f
                            - Mathf.Abs(angle) / 90f * 0.9f - Mathf.Abs(dist - 2.2f) * 0.5f;
                if (score > bestScore) { bestScore = score; best = new RoomSpotPlan(fire, dir, gap, true); }
            }

            if (floor == null)
            {
                foreach (var angle in Angles)
                {
                    var dir = Rotate(forward, angle);
                    foreach (var dist in Distances) Try(user + dir * dist, angle);
                }
            }
            else
            {
                // the whole floor, on a grid (a bed or a sofa in front of you, or you in a corner, left nothing nearby)
                var r = floor.Value;
                for (var x = r.xMin + 0.3f; x <= r.xMax - 0.3f; x += 0.4f)
                    for (var y = r.yMin + 0.3f; y <= r.yMax - 0.3f; y += 0.4f)
                    {
                        var p = new Vector2(x, y);
                        Try(p, Vector2.Angle(forward, p - user));
                    }
            }
            return best;
        }

        /// <summary>No scan (or nothing open): the fire straight ahead on the floor, the storyteller across it.</summary>
        public static RoomSpotPlan InFront(Vector2 user, Vector2 forward, float gap = InFrontGap)
        {
            forward = forward.sqrMagnitude < 1e-6f ? Vector2.up : forward.normalized;
            return new RoomSpotPlan(user + forward * InFrontDistance, forward, Mathf.Max(MinGap, gap), false);
        }

        // the way from you to the fire shouldn't run through a sofa or table
        static bool PathOpen(Vector2 from, Vector2 to, Func<Vector2, float> clearance)
        {
            var steps = Mathf.CeilToInt((to - from).magnitude / 0.4f);
            for (var i = 1; i < steps; i++)
                if (clearance(Vector2.Lerp(from, to, i / (float)steps)) <= 0.05f) return false;
            return true;
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(r); var s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
