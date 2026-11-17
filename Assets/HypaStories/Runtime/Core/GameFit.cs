using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// How a story's game is laid out around you. The games are drawn for the forest: the fire at the camp's origin, you on the
    /// seat 3.1 m back on -Z, the storyteller about 2 m beyond the fire. In your room (3K) you sit at the edge of the stones, so
    /// the same layout is pulled into that small ring: your side stays between you and the stones, and the far side stays within
    /// the storyteller's gap. In the forest nothing changes. Pure, unit tested.
    /// </summary>
    public readonly struct GameFit
    {
        /// <summary>The forest's story seat: you, this far back from the fire. And the storyteller's distance beyond it.</summary>
        public const float ForestSeat = 3.1f, ForestGap = 2.05f;

        /// <summary>In your room (false: the forest, as built).</summary>
        public readonly bool Room;
        /// <summary>Scale for your side of the fire, and for the far side.</summary>
        public readonly float Near, Far;
        /// <summary>Fire to the storyteller's spot, metres: the open floor the room has beyond the fire.</summary>
        public readonly float Gap;

        GameFit(bool room, float near, float far, float gap) { Room = room; Near = near; Far = far; Gap = gap; }

        public static GameFit Forest => new GameFit(false, 1f, 1f, ForestGap);

        /// <summary>In your room the whole campfire (stones and flames) is this size, so it fits a living room.</summary>
        public const float FireScale = 0.7f;
        /// <summary>The room fire's stone ring, outer edge (1 m in the forest, scaled). Your side of a game stays outside it.</summary>
        public const float StoneEdge = 1.0f * FireScale;

        /// <summary>You <paramref name="toFire"/> metres from the fire, and the storyteller <paramref name="gap"/> beyond it.</summary>
        public static GameFit InRoom(float toFire, float gap)
        {
            var sit = Mathf.Clamp(toFire, StoneEdge + 0.05f, 2.2f);
            return new GameFit(true, sit / ForestSeat, Mathf.Clamp(gap / ForestGap, 0.45f, 1f), Mathf.Max(StoneEdge, gap));
        }

        /// <summary>A forest layout point (camp ground, x and z) where it goes now; height is kept.</summary>
        public Vector3 At(Vector3 forest)
        {
            if (!Room) return forest;
            var xk = Mathf.Clamp(Mathf.Max(Near, Far), 0.4f, 1f);
            if (forest.z >= 0f) return new Vector3(forest.x * Far, forest.y, forest.z * Far);
            // your side: the forest span from the fire out to your old seat squeezes into the stone edge, just in front of you
            var sit = Near * ForestSeat;
            var t = Mathf.Clamp01(-forest.z / ForestSeat);
            var z = Mathf.Lerp(-StoneEdge, -(sit - 0.08f), t);
            return new Vector3(forest.x * xk, forest.y, z);
        }

        /// <summary>Dodo's size: 3.4 m in the forest; in a room, small enough to stand under a ceiling.</summary>
        public float DodoScale => Room ? 0.7f : 1f;

        public bool Same(GameFit o) => Room == o.Room && Mathf.Approximately(Near, o.Near) && Mathf.Approximately(Far, o.Far) && Mathf.Approximately(Gap, o.Gap);
    }
}
