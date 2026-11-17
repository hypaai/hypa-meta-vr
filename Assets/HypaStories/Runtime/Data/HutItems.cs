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
        /// <summary>
        /// A household item: its story folder under Resources/Stories, the shape that stands for it (see <c>HutProps</c>), and
        /// whether it is heavy (both hands to lift it, see <see cref="StoryGrab"/>).
        /// </summary>
        public readonly struct Entry
        {
            public readonly string Id, Prop;
            public readonly bool Heavy;
            /// <summary>A place of its own (degrees round the wall from the door, metres from the centre, and the height it stands on), instead of the next floor place.</summary>
            public readonly float Angle, Radius, BaseY;
            public bool OwnPlace => !float.IsNaN(Angle);
            public Entry(string id, string prop, bool heavy = false, float angle = float.NaN, float radius = 0f, float baseY = 0f)
            { Id = id; Prop = prop; Heavy = heavy; Angle = angle; Radius = radius; BaseY = baseY; }
        }

        /// <summary>The items, in the order they take their places round the wall.</summary>
        public static readonly Entry[] Entries =
        {
            new Entry("hut/clay-pot", "clay-pot", heavy: true),
            new Entry("hut/mortar-pestle", "mortar-pestle", heavy: true),
            new Entry("hut/broom", "broom"),
            // on the mud ledge at the back, beside the old woman (HutInterior builds the ledge)
            new Entry("hut/oil-lamp", "oil-lamp", angle: 201f, radius: HutTrailLayout.WallInner - 0.18f, baseY: 0.36f),
        };

        static StoryInfo[] _all;
        /// <summary>Each item's content, in <see cref="Entries"/> order.</summary>
        public static StoryInfo[] All => _all ?? (_all = Entries.Select(e => StoryLibrary.Load(e.Id)).ToArray());

        // ---------- where things stand, in the hut's own space (x, z; the door on +Z, the centre at the origin) ----------

        /// <summary>How far from the centre the items stand (on the floor, by the wall), and where the old woman sits on her mat (at the back, facing the door).</summary>
        public const float ItemRadius = 1.95f, WomanRadius = 1.75f;
        /// <summary>The gap between the top of an item and the bottom of its card: the card sits right over the thing it is about.</summary>
        public const float CardGap = 0.07f;

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

        /// <summary>Where item <paramref name="index"/> stands (x, z) and the height of what it stands on (0: the floor). Pure.</summary>
        public static (Vector2 spot, float baseY) Place(int index)
        {
            var e = Entries[index];
            if (!e.OwnPlace) return (Spot(FloorIndex(index)), 0f);
            var a = e.Angle * Mathf.Deg2Rad;
            return (new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * e.Radius, e.BaseY);
        }

        /// <summary>Which floor place an item takes: items with a place of their own don't use one up.</summary>
        static int FloorIndex(int index)
        {
            var n = 0;
            for (var i = 0; i < index; i++) if (!Entries[i].OwnPlace) n++;
            return n;
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

    /// <summary>
    /// What the old woman says when you handle her things: pick one up (<see cref="Care"/>), try a heavy one with one hand
    /// (<see cref="BothHands"/>), or let one fall hard (<see cref="Careful"/>). One line per language code; a language without
    /// them uses English. The non-European lines are best checked by a native speaker.
    /// </summary>
    public static class HutLines
    {
        public enum Kind { Care, BothHands, Careful }

        static readonly System.Collections.Generic.Dictionary<string, string[]> Lines = new System.Collections.Generic.Dictionary<string, string[]>
        {
            ["en"] = new[] { "Gently, my child. Handle it with care.", "It is heavy. Use both hands.", "Careful! Put it down gently." },
            ["fr"] = new[] { "Doucement, mon enfant. Prends-en soin.", "C'est lourd. Prends-le à deux mains.", "Attention ! Pose-le doucement." },
            ["es"] = new[] { "Despacio, hijo mío. Trátalo con cuidado.", "Pesa mucho. Usa las dos manos.", "¡Cuidado! Déjalo con suavidad." },
            ["pcm"] = new[] { "Gently, my pikin. Handle am with care.", "E heavy. Use your two hand carry am.", "Careful! Put am down gently." },
            ["yo"] = new[] { "Pẹ̀lẹ́, ọmọ mi. Fi ìṣọ́ra mú un.", "Ó wúwo. Fi ọwọ́ méjèèjì gbé e.", "Ṣọ́ra! Rọra gbé e kalẹ̀." },
            ["ha"] = new[] { "A hankali, ɗana. Ka riƙe shi da kulawa.", "Yana da nauyi. Yi amfani da hannaye biyu.", "A kula! Ajiye shi a hankali." },
            ["ig"] = new[] { "Nwayọọ, nwa m. Jiri nlezianya jide ya.", "Ọ dị arọ. Jiri aka abụọ.", "Kpachara anya! Tọgbọ ya nwayọọ." },
            ["sw"] = new[] { "Polepole, mwanangu. Ishike kwa uangalifu.", "Ni nzito. Tumia mikono yote miwili.", "Angalia! Iweke chini polepole." },
        };

        /// <summary>The line in <paramref name="lang"/>, and the language it is actually in (English when there is none). Pure.</summary>
        public static (string text, string lang) For(Kind kind, string lang)
        {
            if (lang != null && Lines.TryGetValue(lang, out var l)) return (l[(int)kind], lang);
            return (Lines["en"][(int)kind], "en");
        }
    }
}
