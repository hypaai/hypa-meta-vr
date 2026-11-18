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
            // on a low table at the old woman's right hand, in plain sight from the door (HutInterior builds the table)
            new Entry("hut/oil-lamp", "oil-lamp", angle: 145f, radius: 1.45f, baseY: HutInterior.TableHeight),
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
    /// What the old woman says when you handle her things: pick one up (<see cref="Kind.Care"/>, and a line of her own for each
    /// thing), try a heavy one with one hand (<see cref="Kind.BothHands"/>), let one fall hard or tip the lamp
    /// (<see cref="Kind.Careful"/>), sweep with her broom (<see cref="Kind.Sweep"/>), and sweep on (<see cref="Kind.Enough"/>).
    /// Several ways to say each, never the same twice running. Per language code; a language without a line uses its English.
    /// The non-European lines are best checked by a native speaker.
    /// </summary>
    public static class HutLines
    {
        public enum Kind { Care, BothHands, Careful, Sweep, Enough }

        static string Key(Kind k) => k == Kind.Care ? "care" : k == Kind.BothHands ? "both" : k == Kind.Careful ? "careful" : k == Kind.Sweep ? "sweep" : "enough";

        // language → key → the ways to say it. "care.{prop}" is said instead of "care" for that thing (mixed with the general ones).
        static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string[]>> Lines =
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string[]>>
        {
            ["en"] = new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["care"] = new[] { "Gently, my child. Handle it with care.", "Slowly, slowly. It is older than you are.", "Hold it well. We do not have another." },
                ["care.clay-pot"] = new[] { "Ah, my water pot. Hold it gently, it was my mother's.", "Mind the rim, my child. Clay chips so easily." },
                ["care.mortar-pestle"] = new[] { "Mind your toes! That mortar is heavier than it looks.", "Carry it close to your body, like the women do." },
                ["care.broom"] = new[] { "Yes, take the broom. Show me how you sweep.", "Hold it low, by the tied end." },
                ["care.oil-lamp"] = new[] { "Carry the lamp upright, or the oil will spill.", "Slowly with the lamp. Keep the flame alive." },
                ["both"] = new[] { "It is heavy. Use both hands.", "Ah-ah, one hand? Use both, my child.", "Not like that. Two hands, and lift with your knees." },
                ["careful"] = new[] { "Careful! Put it down gently.", "Eh! Gently, gently!", "Mind it, my child, it can break." },
                ["sweep"] = new[] { "Yes, like that! Sweep from the back toward the door.", "Good. Short strokes, my child, like this." },
                ["enough"] = new[] { "That is enough, my child. The floor is clean now.", "Enough, enough! You will sweep away the floor itself." },
            },
            ["fr"] = new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["care"] = new[] { "Doucement, mon enfant. Prends-en soin.", "Doucement, doucement. Il est plus vieux que toi.", "Tiens-le bien. Nous n'en avons pas d'autre." },
                ["care.clay-pot"] = new[] { "Ah, mon pot à eau. Tiens-le doucement, il était à ma mère.", "Attention au bord, mon enfant. L'argile s'ébrèche si facilement." },
                ["care.mortar-pestle"] = new[] { "Attention à tes pieds ! Ce mortier est plus lourd qu'il n'en a l'air.", "Porte-le contre toi, comme le font les femmes." },
                ["care.broom"] = new[] { "Oui, prends le balai. Montre-moi comment tu balaies.", "Tiens-le bas, par le bout attaché." },
                ["care.oil-lamp"] = new[] { "Porte la lampe bien droite, sinon l'huile va se renverser.", "Doucement avec la lampe. Garde la flamme en vie." },
                ["both"] = new[] { "C'est lourd. Prends-le à deux mains.", "Une seule main ? Les deux, mon enfant.", "Pas comme ça. Deux mains, et soulève avec les genoux." },
                ["careful"] = new[] { "Attention ! Pose-le doucement.", "Eh ! Doucement, doucement !", "Fais attention, mon enfant, ça peut se casser." },
                ["sweep"] = new[] { "Oui, comme ça ! Balaie du fond vers la porte.", "Bien. De petits coups, mon enfant, comme ça." },
                ["enough"] = new[] { "Ça suffit, mon enfant. Le sol est propre maintenant.", "Assez, assez ! Tu vas balayer le sol lui-même." },
            },
            ["es"] = new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["care"] = new[] { "Despacio, hijo mío. Trátalo con cuidado.", "Despacio, despacio. Es más viejo que tú.", "Sostenlo bien. No tenemos otro." },
                ["care.clay-pot"] = new[] { "Ah, mi olla del agua. Sostenla con cuidado, era de mi madre.", "Cuidado con el borde, hijo mío. El barro se desportilla fácilmente." },
                ["care.mortar-pestle"] = new[] { "¡Cuidado con los pies! Ese mortero pesa más de lo que parece.", "Llévalo pegado al cuerpo, como hacen las mujeres." },
                ["care.broom"] = new[] { "Sí, toma la escoba. Enséñame cómo barres.", "Sostenla abajo, por el extremo atado." },
                ["care.oil-lamp"] = new[] { "Lleva la lámpara derecha, o se derramará el aceite.", "Despacio con la lámpara. Mantén viva la llama." },
                ["both"] = new[] { "Pesa mucho. Usa las dos manos.", "¿Una sola mano? Las dos, hijo mío.", "Así no. Dos manos, y levanta con las rodillas." },
                ["careful"] = new[] { "¡Cuidado! Déjalo con suavidad.", "¡Eh! ¡Despacio, despacio!", "Cuidado, hijo mío, se puede romper." },
                ["sweep"] = new[] { "¡Sí, así! Barre desde el fondo hacia la puerta.", "Bien. Golpes cortos, hijo mío, así." },
                ["enough"] = new[] { "Ya basta, hijo mío. El suelo ya está limpio.", "¡Basta, basta! Vas a barrer el suelo mismo." },
            },
            ["pcm"] = new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["care"] = new[] { "Gently, my pikin. Handle am with care.", "Small small. E old pass you.", "Hold am well. We no get another one." },
                ["care.clay-pot"] = new[] { "Ah, my water pot. Hold am gently, na my mama own." },
                ["care.mortar-pestle"] = new[] { "Mind your leg! That mortar heavy pass how e be." },
                ["care.broom"] = new[] { "Yes, carry the broom. Show me how you dey sweep." },
                ["care.oil-lamp"] = new[] { "Carry the lamp straight, make the oil no pour." },
                ["both"] = new[] { "E heavy. Use your two hand carry am.", "Ah-ah, one hand? Use two, my pikin." },
                ["careful"] = new[] { "Careful! Put am down gently.", "Eh! Gently, gently!" },
                ["sweep"] = new[] { "Na so! Sweep am from back go door." },
                ["enough"] = new[] { "E don do, my pikin. The floor don clean now." },
            },
            ["yo"] = new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["care"] = new[] { "Pẹ̀lẹ́, ọmọ mi. Fi ìṣọ́ra mú un.", "Díẹ̀díẹ̀. Ó ti dàgbà jù ọ́ lọ." },
                ["both"] = new[] { "Ó wúwo. Fi ọwọ́ méjèèjì gbé e." },
                ["careful"] = new[] { "Ṣọ́ra! Rọra gbé e kalẹ̀.", "Ẹ̀! Pẹ̀lẹ́, pẹ̀lẹ́!" },
                ["sweep"] = new[] { "Bẹ́ẹ̀ ni! Gbá a láti ẹ̀yìn lọ sí ẹnu ọ̀nà." },
                ["enough"] = new[] { "Ó tó gẹ́ẹ́, ọmọ mi. Ilẹ̀ ti mọ́ báyìí." },
            },
            ["ha"] = new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["care"] = new[] { "A hankali, ɗana. Ka riƙe shi da kulawa." },
                ["both"] = new[] { "Yana da nauyi. Yi amfani da hannaye biyu." },
                ["careful"] = new[] { "A kula! Ajiye shi a hankali." },
                ["sweep"] = new[] { "Haka ne! Ka share daga baya zuwa ƙofa." },
                ["enough"] = new[] { "Ya isa haka, ɗana. Ƙasa ta yi tsabta yanzu." },
            },
            ["ig"] = new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["care"] = new[] { "Nwayọọ, nwa m. Jiri nlezianya jide ya." },
                ["both"] = new[] { "Ọ dị arọ. Jiri aka abụọ." },
                ["careful"] = new[] { "Kpachara anya! Tọgbọ ya nwayọọ." },
                ["sweep"] = new[] { "Ọ dị mma! Zachaa site n'azụ ruo n'ọnụ ụzọ." },
                ["enough"] = new[] { "O zuola, nwa m. Ala adịla ọcha ugbu a." },
            },
            ["sw"] = new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["care"] = new[] { "Polepole, mwanangu. Ishike kwa uangalifu." },
                ["both"] = new[] { "Ni nzito. Tumia mikono yote miwili." },
                ["careful"] = new[] { "Angalia! Iweke chini polepole." },
                ["sweep"] = new[] { "Ndiyo hivyo! Fagia kutoka nyuma hadi mlangoni." },
                ["enough"] = new[] { "Inatosha, mwanangu. Sakafu ni safi sasa." },
            },
        };

        /// <summary>
        /// The ways to say <paramref name="kind"/> (about <paramref name="prop"/>, for Care) in <paramref name="lang"/>, and the language
        /// they are in: the language's own when it has the line, else English. Pure.
        /// </summary>
        public static (string[] lines, string lang) Options(Kind kind, string lang, string prop = null)
        {
            System.Collections.Generic.List<string> Get(string code)
            {
                if (code == null || !Lines.TryGetValue(code, out var d)) return null;
                var l = new System.Collections.Generic.List<string>();
                if (kind == Kind.Care && prop != null && d.TryGetValue("care." + prop, out var own)) l.AddRange(own);
                if (d.TryGetValue(Key(kind), out var gen)) l.AddRange(gen);
                return l.Count > 0 ? l : null;
            }
            var mine = Get(lang);
            return mine != null ? (mine.ToArray(), lang) : (Get("en").ToArray(), "en");
        }

        /// <summary>One of the ways to say it, never <paramref name="last"/> again when there is another. Pure given <paramref name="pick"/> (0 ≤ pick &lt; 1).</summary>
        public static (string text, string lang) For(Kind kind, string lang, string prop = null, string last = null, float pick = 0f)
        {
            var (lines, code) = Options(kind, lang, prop);
            var choices = System.Array.FindAll(lines, l => l != last);
            if (choices.Length == 0) choices = lines;
            return (choices[Mathf.Clamp((int)(pick * choices.Length), 0, choices.Length - 1)], code);
        }
    }
}
