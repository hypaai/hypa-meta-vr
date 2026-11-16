using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// How a story's game is laid out around you. The games are drawn for the forest: the fire at the camp's origin, you on the
    /// seat 3.1 m back on -Z, the storyteller about 2 m beyond the fire. In your room (3K) the fire is wherever the floor allowed,
    /// often nearer, and walls close it in, so the same layout is pulled in to fit: everything on your side of the fire scales with
    /// how far you really are from it, everything beyond it with the room the storyteller had. In the forest nothing changes.
    /// Pure, unit tested.
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

        /// <summary>You <paramref name="toFire"/> metres from the fire, and the storyteller <paramref name="gap"/> beyond it.</summary>
        public static GameFit InRoom(float toFire, float gap)
            => new GameFit(true, Mathf.Clamp(toFire / ForestSeat, 0.55f, 1.05f), Mathf.Clamp(gap / ForestGap, 0.5f, 1f), Mathf.Max(0.8f, gap));

        /// <summary>A forest layout point (camp ground, x and z) where it goes now; height is kept.</summary>
        public Vector3 At(Vector3 forest)
        {
            var k = forest.z < 0f ? Near : Far;
            return new Vector3(forest.x * k, forest.y, forest.z * k);
        }

        /// <summary>Dodo's size: 3.4 m in the forest; in a room, small enough to stand under a ceiling.</summary>
        public float DodoScale => Room ? 0.7f : 1f;

        public bool Same(GameFit o) => Room == o.Room && Mathf.Approximately(Near, o.Near) && Mathf.Approximately(Far, o.Far) && Mathf.Approximately(Gap, o.Gap);
    }
}
