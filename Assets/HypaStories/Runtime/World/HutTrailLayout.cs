using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Where the trail to the hut runs (ticket 3G), in the camp's ground plane (x, z; the fire at the origin, the seats on -Z).
    /// The trail leaves the clearing on the left as you face the fire (-X, where the Hut button sits), past the fallen log and
    /// the signpost, bends through the pines and ends at the hut's door. Also where you may walk: the clearing, the trail, the
    /// doorway and the inside of the hut, never through the hut's walls. Pure, unit tested.
    /// </summary>
    public static class HutTrailLayout
    {
        /// <summary>The trail's bends, from the clearing (by the log and the signpost) to just before the hut.</summary>
        public static readonly Vector2[] Path =
        {
            new Vector2(-4.15f, -1.85f), new Vector2(-11.2f, -4.4f), new Vector2(-19f, -6.8f),
            new Vector2(-27f, -3.2f), new Vector2(-33.5f, 2.2f),
        };

        /// <summary>The hut's centre; its door faces back down the trail.</summary>
        public static readonly Vector2 Hut = new Vector2(-39.5f, 6.8f);

        /// <summary>The fallen log the signpost stands beside (left of the fire, as you face it from the seat).</summary>
        public static readonly Vector2 Log = new Vector2(-2.3f, -0.6f);

        public const float HutRadius = 2.6f;    // outside of the wall
        public const float WallInner = 2.32f;   // inside of the wall
        public const float DoorHalfAngle = 13f; // degrees either side of the door's middle (about 1.2 m wide)
        public const float DoorHeight = 1.95f, WallHeight = 2.2f;
        /// <summary>The thatched cone: its eave (radius and height) and its apex. The rafters inside follow it.</summary>
        public const float RoofEave = 3.2f, RoofEaveY = 1.95f, RoofApexY = 4.5f;

        /// <summary>The underside of the thatch at <paramref name="r"/> metres from the hut's centre.</summary>
        public static float RoofY(float r) => Mathf.Lerp(RoofApexY, RoofEaveY, Mathf.Clamp01(r / RoofEave));
        /// <summary>Half the width of the trail you see, and of the strip you can walk on (a little wider, so the edge is soft).</summary>
        public const float PathHalf = 0.8f, WalkHalf = 1.25f;
        /// <summary>Where the signpost stands: by the log, at the mouth of the trail, readable from the seat.</summary>
        public static readonly Vector2 Sign = new Vector2(-3.55f, -1.4f);

        // InsideMargin: how close to the wall your head may go inside the hut. Small, so you can lean in over the things that stand
        // by the wall: with a wider margin, leaning in pushed the whole room away (the item you looked at seemed to slide off).
        const float FireKeepOut = 1.3f, DoorHalf = 0.45f, InsideMargin = 0.06f, Eps = 1e-3f;

        /// <summary>The way out of the door (unit, pointing back down the trail).</summary>
        public static Vector2 DoorDir => (Path[Path.Length - 1] - Hut).normalized;

        /// <summary>The middle of the doorway, on the outside of the wall.</summary>
        public static Vector2 Door => Hut + DoorDir * HutRadius;

        /// <summary>Where the walkable trail stops, a step outside the door (its round end never reaches the wall).</summary>
        public static Vector2 TrailEnd => Hut + DoorDir * (HutRadius + WalkHalf + 0.05f);

        /// <summary>Where you stand to see the hut's buttons: just outside the door.</summary>
        public static Vector2 FrontOfDoor => Hut + DoorDir * (HutRadius + 0.9f);

        /// <summary>Distance from <paramref name="p"/> to the trail's centre line (the bends, then on to the door).</summary>
        public static float DistanceToPath(Vector2 p)
        {
            var d = float.MaxValue;
            for (var i = 0; i + 1 < Path.Length; i++) d = Mathf.Min(d, DistanceToSegment(p, Path[i], Path[i + 1]));
            return Mathf.Min(d, DistanceToSegment(p, Path[Path.Length - 1], Door));
        }

        public static bool NearPath(float x, float z, float within) => DistanceToPath(new Vector2(x, z)) < within;

        public static bool NearHut(float x, float z, float margin) => Vector2.Distance(new Vector2(x, z), Hut) < HutRadius + margin;

        /// <summary>Inside the hut (clear of the wall).</summary>
        public static bool InsideHut(Vector2 p) => Vector2.Distance(p, Hut) <= WallInner - InsideMargin;

        /// <summary>Near the door, outside: the hut's buttons show.</summary>
        public static bool AtDoor(Vector2 p) => Vector2.Distance(p, FrontOfDoor) < 1.9f && Vector2.Distance(p, Hut) > HutRadius;

        /// <summary>Can you stand at <paramref name="p"/>? The clearing (within <paramref name="clearing"/> of the fire, not in it), the trail, the doorway, the hut.</summary>
        public static bool Allowed(Vector2 p, float clearing)
        {
            var r = p.magnitude;
            if (r >= FireKeepOut - Eps && r <= clearing + Eps) return true;
            for (var i = 0; i < Strips.Length; i++)
                if (DistanceToSegment(p, Strips[i].a, Strips[i].b) <= Strips[i].half + Eps) return true;
            return Vector2.Distance(p, Hut) <= WallInner - InsideMargin + Eps;
        }

        /// <summary>The nearest place you can stand to <paramref name="p"/> (itself when allowed). Pure, unit tested.</summary>
        public static Vector2 Clamp(Vector2 p, float clearing)
        {
            if (Allowed(p, clearing)) return p;
            var best = p.magnitude < FireKeepOut ? Away(Vector2.zero, p, FireKeepOut) : Toward(Vector2.zero, p, clearing);
            void Try(Vector2 q) { if ((q - p).sqrMagnitude < (best - p).sqrMagnitude) best = q; }
            foreach (var s in Strips)
            {
                var c = ClosestOnSegment(p, s.a, s.b);
                Try(Toward(c, p, s.half));
            }
            Try(Toward(Hut, p, WallInner - InsideMargin));
            return best;
        }

        /// <summary>The walkable strips: the trail's legs, its last leg to a step outside the door, and the narrow doorway in.</summary>
        static (Vector2 a, Vector2 b, float half)[] _strips;
        static (Vector2 a, Vector2 b, float half)[] Strips
        {
            get
            {
                if (_strips != null) return _strips;
                var list = new System.Collections.Generic.List<(Vector2, Vector2, float)>();
                for (var i = 0; i + 1 < Path.Length; i++) list.Add((Path[i], Path[i + 1], WalkHalf));
                list.Add((Path[Path.Length - 1], TrailEnd, WalkHalf));
                list.Add((TrailEnd, Hut, DoorHalf));
                return _strips = list.ToArray();
            }
        }

        static Vector2 Toward(Vector2 centre, Vector2 p, float maxDist)
        {
            var d = p - centre;
            var m = d.magnitude;
            return m <= maxDist || m < 1e-6f ? p : centre + d / m * maxDist;
        }

        static Vector2 Away(Vector2 centre, Vector2 p, float minDist)
        {
            var d = p - centre;
            var m = d.magnitude;
            return m >= minDist ? p : centre + (m < 1e-6f ? Vector2.down : d / m) * minDist;
        }

        public static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return a + ab * t;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) => Vector2.Distance(p, ClosestOnSegment(p, a, b));

        /// <summary>A point <paramref name="u"/> (0..1) of the way along the trail, from the clearing to the door, and the way it runs there.</summary>
        public static (Vector2 point, Vector2 dir) Along(float u)
        {
            var pts = new Vector2[Path.Length + 1];
            Path.CopyTo(pts, 0);
            pts[Path.Length] = Door;
            var total = 0f;
            for (var i = 0; i + 1 < pts.Length; i++) total += Vector2.Distance(pts[i], pts[i + 1]);
            var want = Mathf.Clamp01(u) * total;
            for (var i = 0; i + 1 < pts.Length; i++)
            {
                var len = Vector2.Distance(pts[i], pts[i + 1]);
                if (want <= len || i + 2 == pts.Length)
                    return (Vector2.Lerp(pts[i], pts[i + 1], len < 1e-6f ? 0f : Mathf.Clamp01(want / len)), (pts[i + 1] - pts[i]).normalized);
                want -= len;
            }
            return (pts[pts.Length - 1], DoorDir * -1f);
        }

        /// <summary>Where the wolf waits, a few steps off the trail so it shows between the trees.</summary>
        public static Vector2 WolfAt
        {
            get
            {
                var (p, dir) = Along(0.62f);
                return p + new Vector2(dir.y, -dir.x) * 1.7f;
            }
        }
    }
}
