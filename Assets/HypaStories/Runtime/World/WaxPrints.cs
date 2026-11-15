using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Bold wax-print cloth for the villagers' wrappers, tops, head wraps and the masquerade: one small texture made in code,
    /// a 4 x 3 atlas of tiles. Tile 0 is plain white (skin, eyes, anything untextured samples it). Tiles 1-9 are prints, each
    /// repeating seamlessly across its tile: rings, diamonds, chevrons, leaves, dotted checks, waves, woven bands,
    /// indigo resist dots (adire) and kente blocks.
    /// Used through the pines' shader (its optional pattern), so the cloth costs no extra material or draw.
    /// </summary>
    public static class WaxPrints
    {
        public const int Count = 9;
        const int TileSize = 128, Cols = 4, Rows = 3;

        static Texture2D _atlas;

        /// <summary>The atlas (built on first use).</summary>
        public static Texture2D Atlas => _atlas != null ? _atlas : (_atlas = Build());

        /// <summary>
        /// The texture rect of print <paramref name="k"/> (1..<see cref="Count"/>; anything else wraps into that range), inset
        /// a little so filtering never reaches a neighbour. Pure, unit tested.
        /// </summary>
        public static Rect Tile(int k)
        {
            k = 1 + (((k - 1) % Count) + Count) % Count;
            const float inset = 0.012f;
            float w = 1f / Cols, h = 1f / Rows;
            return new Rect((k % Cols) * w + inset, (k / Cols) * h + inset * 2f, w - inset * 2f, h - inset * 4f);
        }

        static readonly Color[][] Palettes =
        {
            null,
            new[] { new Color(0.12f, 0.17f, 0.45f), new Color(0.95f, 0.55f, 0.12f), new Color(0.95f, 0.9f, 0.78f) },  // rings: indigo, orange, cream
            new[] { new Color(0.75f, 0.14f, 0.1f), new Color(0.96f, 0.78f, 0.18f), new Color(0.08f, 0.06f, 0.05f) },  // diamonds: red, gold, black
            new[] { new Color(0.1f, 0.42f, 0.24f), new Color(0.97f, 0.82f, 0.2f), new Color(0.08f, 0.07f, 0.05f) },   // chevrons: green, yellow, black
            new[] { new Color(0.08f, 0.45f, 0.48f), new Color(0.95f, 0.5f, 0.15f), new Color(0.35f, 0.08f, 0.1f) },   // leaves: teal, orange, plum
            new[] { new Color(0.42f, 0.12f, 0.4f), new Color(0.95f, 0.9f, 0.8f), new Color(0.96f, 0.7f, 0.15f) },     // dotted checks: purple, white, gold
            new[] { new Color(0.1f, 0.3f, 0.62f), new Color(0.92f, 0.92f, 0.85f), new Color(0.9f, 0.62f, 0.15f) },    // waves: blue, white, gold
            new[] { new Color(0.85f, 0.62f, 0.12f), new Color(0.12f, 0.4f, 0.2f), new Color(0.7f, 0.12f, 0.1f) },     // woven bands: gold, green, red
            new[] { new Color(0.07f, 0.1f, 0.32f), new Color(0.93f, 0.9f, 0.8f), new Color(0.45f, 0.62f, 0.82f) },    // adire: indigo, cream, pale blue
            new[] { new Color(0.93f, 0.72f, 0.1f), new Color(0.1f, 0.38f, 0.16f), new Color(0.72f, 0.1f, 0.08f) },    // kente blocks: gold, green, red
        };

        static Texture2D Build()
        {
            var tex = new Texture2D(TileSize * Cols, TileSize * Rows, TextureFormat.RGB24, true) { name = "Wax Prints", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            var px = new Color32[tex.width * tex.height];
            for (var k = 0; k < Cols * Rows; k++)
            {
                int ox = (k % Cols) * TileSize, oy = (k / Cols) * TileSize;
                for (var y = 0; y < TileSize; y++)
                    for (var x = 0; x < TileSize; x++)
                        px[(oy + y) * tex.width + ox + x] = k == 0 || k > Count ? (Color32)Color.white : (Color32)Print(k, (x + 0.5f) / TileSize, (y + 0.5f) / TileSize);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static float Smooth(float edge, float v) => Mathf.Clamp01((v - edge) * 40f + 0.5f); // a crisp edge with a little softness

        /// <summary>One print at (u, v) within its tile, 0..1, repeating seamlessly (whole repeats across the tile).</summary>
        static Color Print(int k, float u, float v)
        {
            var p = Palettes[k];
            const float n = 4f; // repeats across the tile
            float x = u * n, y = v * n;
            float fx = x - Mathf.Floor(x), fy = y - Mathf.Floor(y);
            switch (k)
            {
                case 1: // concentric rings on a ground, one ring set per cell
                {
                    var d = Vector2.Distance(new Vector2(fx, fy), new Vector2(0.5f, 0.5f));
                    var ring = Mathf.Sin(d * 38f);
                    var inDisc = Smooth(d, 0.44f);
                    return d < 0.07f ? p[2] : Color.Lerp(p[0], ring > 0.1f ? p[1] : p[2], inDisc * (ring > -0.3f ? 1f : 0f));
                }
                case 2: // a lattice of diamonds with a black dot in each
                {
                    var dd = Mathf.Abs(fx - 0.5f) + Mathf.Abs(fy - 0.5f);
                    if (Vector2.Distance(new Vector2(fx, fy), new Vector2(0.5f, 0.5f)) < 0.09f) return p[2];
                    if (dd < 0.36f) return dd > 0.27f ? p[2] : p[1];
                    return p[0];
                }
                case 3: // chevron stripes
                {
                    var c = Mathf.Repeat(y * 1.5f + Mathf.Abs(fx - 0.5f) * 1.5f, 1f);
                    return c < 0.42f ? p[0] : c < 0.62f ? p[1] : c < 0.72f ? p[2] : p[0] * 0.85f;
                }
                case 4: // leaves, turned one way then the other, with a dark vein
                {
                    var flip = (Mathf.FloorToInt(x) + Mathf.FloorToInt(y)) % 2 == 0 ? 1f : -1f;
                    var q = new Vector2(fx - 0.5f, fy - 0.5f);
                    var a = 0.7f * flip;
                    var r = new Vector2(q.x * Mathf.Cos(a) - q.y * Mathf.Sin(a), q.x * Mathf.Sin(a) + q.y * Mathf.Cos(a));
                    var leaf = (r.x * r.x) / 0.16f + (r.y * r.y) / 0.03f;
                    if (leaf < 1f) return Mathf.Abs(r.y) < 0.018f ? p[2] : p[1];
                    return p[0];
                }
                case 5: // checks, a dot on every other square
                {
                    var chk = (Mathf.FloorToInt(x * 2f) + Mathf.FloorToInt(y * 2f)) % 2 == 0;
                    var gx = x * 2f - Mathf.Floor(x * 2f); var gy = y * 2f - Mathf.Floor(y * 2f);
                    var dot = Vector2.Distance(new Vector2(gx, gy), new Vector2(0.5f, 0.5f)) < 0.22f;
                    return chk ? (dot ? p[2] : p[0]) : p[1];
                }
                case 6: // waves
                {
                    var w = Mathf.Repeat(y * 2f + Mathf.Sin(u * Mathf.PI * 2f * n) * 0.18f, 1f);
                    return w < 0.45f ? p[0] : w < 0.6f ? p[1] : w < 0.7f ? p[2] : w < 0.85f ? p[1] : p[0];
                }
                case 8: // indigo resist: cream dots and a pale ring on a deep blue ground
                {
                    var cell = 0.5f;
                    var cx = fx / cell - Mathf.Floor(fx / cell);
                    var cy = fy / cell - Mathf.Floor(fy / cell);
                    var dot = Vector2.Distance(new Vector2(cx, cy), new Vector2(0.5f, 0.5f));
                    if (dot < 0.12f) return p[1];
                    if (dot > 0.28f && dot < 0.36f) return p[2];
                    return p[0];
                }
                case 9: // kente: wide blocks of gold, green and red with a dark seam
                {
                    var row = Mathf.FloorToInt(y * 2f);
                    var col = Mathf.FloorToInt(x * 2f);
                    var by = y * 2f - row;
                    var bx = x * 2f - col;
                    if (by < 0.08f || bx < 0.06f) { var seam = p[0] * 0.2f; seam.a = 1f; return seam; }
                    var which = (row + col * 2) % 3;
                    return p[which];
                }
                default: // woven bands, like strip-woven cloth
                {
                    var band = Mathf.FloorToInt(y * 2f) % 3;
                    var by = y * 2f - Mathf.Floor(y * 2f);
                    var block = (Mathf.FloorToInt(x * 2f) + Mathf.FloorToInt(y * 2f)) % 2 == 0;
                    var thin = Mathf.Repeat(x * 16f, 1f) < 0.5f;
                    if (by < 0.12f || by > 0.88f) return p[2];
                    if (block) return thin ? p[(band + 1) % 3] : p[band];
                    return p[band] * (by > 0.45f && by < 0.55f ? 0.6f : 1f);
                }
            }
        }
    }
}
