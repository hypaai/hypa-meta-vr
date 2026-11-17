using System.Linq;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The household things in the hut, each with a story card over it. An item's content is a story like any other:
    /// Resources/Stories/hut/{id}/story.json (card text and the old woman's lines) and one {language code}.json per
    /// translation, loaded by <see cref="StoryLibrary.Load"/>. They are not in the campfire's row (<see cref="StoryLibrary.All"/>).
    ///
    /// Adding an item = a folder with its story.json, and one line in <see cref="Entries"/> naming the shape to show
    /// (<c>HutProps</c>; an unknown shape shows a plain crate). Its place in the hut comes from its position in the list.
    /// </summary>
    public static class HutItems
    {
        /// <summary>A household item: its story folder under Resources/Stories, and the shape that stands for it (see <c>HutProps</c>).</summary>
        public readonly struct Entry
        {
            public readonly string Id, Prop;
            public Entry(string id, string prop) { Id = id; Prop = prop; }
        }

        /// <summary>The items, in the order they take their places round the wall.</summary>
        public static readonly Entry[] Entries =
        {
            new Entry("hut/clay-pot", "clay-pot"),
            new Entry("hut/mortar-pestle", "mortar-pestle"),
            new Entry("hut/broom", "broom"),
        };

        static StoryInfo[] _all;
        /// <summary>Each item's content, in <see cref="Entries"/> order.</summary>
        public static StoryInfo[] All => _all ?? (_all = Entries.Select(e => StoryLibrary.Load(e.Id)).ToArray());

        // ---------- where things stand, in the hut's own space (x, z; the door on +Z, the centre at the origin) ----------

        /// <summary>How far from the centre the items stand (on the floor, by the wall), and where the old woman sits on her mat (at the back, facing the door).</summary>
        public const float ItemRadius = 1.95f, WomanRadius = 1.75f;
        /// <summary>The gap between the top of an item and the bottom of its card: the card sits right over the thing it is about.</summary>
        public const float CardGap = 0.04f;

        /// <summary>
        /// Angles round the wall from the door (degrees, clockwise from above): the two sides, then just inside the door (where a
        /// broom is kept in a house: the third item), then the other side of the door, then beside the old woman.
        /// </summary>
        static readonly float[] Angles = { 108f, -108f, 58f, -58f, 144f, -144f };

        /// <summary>How many items have a place.</summary>
        public static int Places => Angles.Length;

        /// <summary>The angle of item <paramref name="index"/>'s place. Past the last place they go round again (they would overlap: add an angle).</summary>
        public static float AngleOf(int index) => Angles[((index % Angles.Length) + Angles.Length) % Angles.Length];

        /// <summary>Where item <paramref name="index"/> starts (and where its card stays). Pure, unit tested.</summary>
        public static Vector2 Spot(int index)
        {
            var a = AngleOf(index) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * ItemRadius;
        }

        /// <summary>Where the old woman sits: at the back of the room, opposite the door.</summary>
        public static Vector2 Woman => new Vector2(0f, -WomanRadius);

        /// <summary>
        /// Has an item left the room (carried or thrown out, or dropped through the floor)? <paramref name="local"/> is its place in
        /// the hut's space. Such an item goes back to where it started. Pure, unit tested.
        /// </summary>
        public static bool Lost(Vector3 local)
            => local.y < -0.4f || new Vector2(local.x, local.z).magnitude > HutTrailLayout.WallInner + 0.1f;
    }
}
