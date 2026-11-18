using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Where the trail to the village runs (ticket 3M, reached on foot like the hut), in the camp's ground plane (x, z; the
    /// fire at the origin, the seats on -Z). It leaves the clearing on the right as you face the fire (+X, where the Village
    /// button sits), past the VILLAGE signpost, crosses a stream on a plank bridge, and ends at the village gate. Past the
    /// gate: an open plaza, ringed by round huts. A woven fence crosses it: the crowd works and dances on the far side, and you
    /// stay on the near side, with the one local by the fence. Pure, unit tested.
    /// </summary>
    public static class VillageLayout
    {
        /// <summary>The trail's bends, from the clearing (by the signpost) to just before the gate.</summary>
        public static readonly Vector2[] Path =
        {
            new Vector2(4.3f, -2.5f), new Vector2(11.4f, -4.9f), new Vector2(19.2f, -6.6f),
            new Vector2(27.2f, -3.6f), new Vector2(33.2f, 1.4f),
        };

        /// <summary>The gate: two carved posts and a beam, where the trail meets the plaza.</summary>
        public static readonly Vector2 Gate = new Vector2(35.7f, 3.9f);

        /// <summary>The plaza's middle.</summary>
        public static readonly Vector2 Plaza = new Vector2(40.6f, 8.6f);

        /// <summary>Where the signpost stands: at the mouth of the trail, readable from the seat.</summary>
        public static readonly Vector2 Sign = new Vector2(3.75f, -1.45f);

        public const float PathHalf = 0.8f, WalkHalf = 1.25f;
        /// <summary>How far from the plaza's middle you can walk (your side of the fence), and how far the huts stand.</summary>
        public const float PlazaWalk = 7.4f, HutRing = 12f, HutRadius = 2.1f;
        /// <summary>The fence crosses the plaza this far past its middle (along <see cref="Into"/>).</summary>
        public const float FenceAt = 1.1f;

        const float Eps = 1e-3f;

        /// <summary>The way into the village (unit): from the gate toward the plaza's middle and the crowd beyond.</summary>
        public static Vector2 Into => (Plaza - Gate).normalized;

        /// <summary>To the right of <see cref="Into"/> (as you walk in).</summary>
        public static Vector2 Right => new Vector2(Into.y, -Into.x);

        /// <summary>A spot in the plaza, <paramref name="into"/> metres past its middle (toward the crowd) and <paramref name="right"/> to the right.</summary>
        public static Vector2 At(float into, float right) => Plaza + Into * into + Right * right;

        /// <summary>Where you arrive by the button, and where the village's buttons show: just outside the gate, facing in.</summary>
        public static Vector2 FrontOfGate => Gate - Into * 1.6f;

        /// <summary>The one local: on your side of the fence, a little to the left.</summary>
        public static Vector2 Local => At(FenceAt - 0.75f, -2.4f);

        /// <summary>Where "Meet the locals" puts you: in front of the local, facing them.</summary>
        public static Vector2 MeetSpot => Local - Into * 1.5f + Right * 0.5f;

        /// <summary>The crowd's yam pile, the village fire, the drummers: beyond the fence.</summary>
        public static Vector2 YamPile => At(4.2f, -3.2f);
        public static Vector2 Fire => At(6.2f, 2.4f);

        /// <summary>The huts round the plaza: angles (degrees from <see cref="Into"/>, clockwise seen from above) at <see cref="HutRing"/>.</summary>
        public static readonly float[] HutAngles = { -150f, -112f, -72f, -30f, 18f, 62f, 104f, 146f };

        public static Vector2 HutAt(int i)
        {
            var a = HutAngles[i] * Mathf.Deg2Rad;
            return Plaza + (Into * Mathf.Cos(a) + Right * Mathf.Sin(a)) * HutRing;
        }

        /// <summary>Distance from <paramref name="p"/> to the trail's centre line (the bends, then on to the gate).</summary>
        public static float DistanceToPath(Vector2 p)
        {
            var d = float.MaxValue;
            for (var i = 0; i + 1 < Path.Length; i++) d = Mathf.Min(d, HutTrailLayout.DistanceToSegment(p, Path[i], Path[i + 1]));
            return Mathf.Min(d, HutTrailLayout.DistanceToSegment(p, Path[Path.Length - 1], Gate));
        }

        public static bool NearPath(float x, float z, float within) => DistanceToPath(new Vector2(x, z)) < within;

        /// <summary>Within the village (its huts included) plus <paramref name="margin"/>: no forest pines here.</summary>
        public static bool NearVillage(float x, float z, float margin) => Vector2.Distance(new Vector2(x, z), Plaza) < HutRing + HutRadius + margin;

        /// <summary>In the plaza, on your side of the fence.</summary>
        public static bool InPlaza(Vector2 p) => Vector2.Distance(p, Plaza) <= PlazaWalk + Eps && Vector2.Dot(p - Plaza, Into) <= FenceAt + Eps;

        /// <summary>Near the gate (either side): the village's buttons show.</summary>
        public static bool AtGate(Vector2 p) => Vector2.Distance(p, FrontOfGate) < 2.2f;

        /// <summary>Close enough to greet the elder, in the same ground coordinates as the gate and walking limits.</summary>
        public static bool AtLocal(Vector2 p) => Vector2.Distance(p, Local) < 2.2f;

        /// <summary>Far enough from the elder that you have left him. The gate is this far, and the way back is farther.</summary>
        public static bool LeftElder(Vector2 p) => Vector2.Distance(p, Local) >= Vector2.Distance(Gate, Local) - 0.05f;

        /// <summary>Can you stand at <paramref name="p"/>? The village trail and the plaza (your side of the fence). The clearing is <see cref="Grounds"/>'.</summary>
        public static bool Allowed(Vector2 p)
        {
            if (InPlaza(p)) return true;
            if (HutTrailLayout.DistanceToSegment(p, Path[Path.Length - 1], Gate) <= WalkHalf + Eps) return true;
            for (var i = 0; i + 1 < Path.Length; i++)
                if (HutTrailLayout.DistanceToSegment(p, Path[i], Path[i + 1]) <= WalkHalf + Eps) return true;
            return false;
        }

        /// <summary>The nearest spot on the village trail or in the plaza to <paramref name="p"/>.</summary>
        public static Vector2 Nearest(Vector2 p)
        {
            if (Allowed(p)) return p;
            var best = NearestInPlaza(p);
            void Try(Vector2 q) { if ((q - p).sqrMagnitude < (best - p).sqrMagnitude) best = q; }
            for (var i = 0; i + 1 < Path.Length; i++) Try(OnStrip(p, Path[i], Path[i + 1]));
            Try(OnStrip(p, Path[Path.Length - 1], Gate));
            return best;
        }

        static Vector2 OnStrip(Vector2 p, Vector2 a, Vector2 b)
        {
            var c = HutTrailLayout.ClosestOnSegment(p, a, b);
            var d = p - c;
            return d.magnitude <= WalkHalf ? p : c + d.normalized * WalkHalf;
        }

        /// <summary>The nearest point of the plaza's walkable part: a disc cut by the fence line.</summary>
        static Vector2 NearestInPlaza(Vector2 p)
        {
            var rel = p - Plaza;
            // onto the disc, then back behind the fence if that's past it
            var q = rel.magnitude > PlazaWalk ? rel.normalized * PlazaWalk : rel;
            if (Vector2.Dot(q, Into) <= FenceAt) return Plaza + q;
            // on the fence line, clamped to its ends on the disc
            var half = Mathf.Sqrt(PlazaWalk * PlazaWalk - FenceAt * FenceAt);
            var across = Mathf.Clamp(Vector2.Dot(rel, Right), -half, half);
            return Plaza + Into * FenceAt + Right * across;
        }

        /// <summary>A point <paramref name="u"/> (0..1) of the way along the trail, from the clearing to the gate, and the way it runs there.</summary>
        public static (Vector2 point, Vector2 dir) Along(float u)
        {
            var pts = new Vector2[Path.Length + 1];
            Path.CopyTo(pts, 0);
            pts[Path.Length] = Gate;
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
            return (Gate, Into);
        }

        /// <summary>Where along the trail (0..1) the bats circle and the bushbaby's trees stand.</summary>
        public const float BatsAt = 0.48f, BushbabyAt = 0.6f;

        /// <summary>The bushbaby's two trees stand by this spot, left of the trail: no forest pines on top of them.</summary>
        public static Vector2 BushbabyTrees
        {
            get { var (p, dir) = Along(BushbabyAt); return p + new Vector2(-dir.y, dir.x) * 3.4f + dir * 1.6f; }
        }

        /// <summary>Where the stream crosses the trail (a plank bridge), and the way the water runs.</summary>
        public static (Vector2 point, Vector2 flow) Stream
        {
            get
            {
                var (p, dir) = Along(0.36f);
                return (p, new Vector2(-dir.y, dir.x));
            }
        }
    }

    /// <summary>
    /// Everywhere you can walk in the camp's world: the clearing round the fire, the trail to the hut and its inside, the
    /// trail to the village and its plaza (your side of the fence). Pure, unit tested.
    /// </summary>
    public static class Grounds
    {
        public static bool Allowed(Vector2 p, float clearing) => HutTrailLayout.Allowed(p, clearing) || VillageLayout.Allowed(p);

        /// <summary>The nearest place you can stand to <paramref name="p"/> (itself when allowed).</summary>
        public static Vector2 Clamp(Vector2 p, float clearing)
        {
            if (Allowed(p, clearing)) return p;
            var a = HutTrailLayout.Clamp(p, clearing);
            var b = VillageLayout.Nearest(p);
            return (a - p).sqrMagnitude <= (b - p).sqrMagnitude ? a : b;
        }

        /// <summary>Keep forest (pines, ferns, rocks) off both trails, the hut and the village.</summary>
        public static bool KeepClear(float x, float z, float pathMargin, float placeMargin) =>
            HutTrailLayout.NearPath(x, z, pathMargin) || HutTrailLayout.NearHut(x, z, placeMargin)
            || VillageLayout.NearPath(x, z, pathMargin) || VillageLayout.NearVillage(x, z, placeMargin);
    }
}
