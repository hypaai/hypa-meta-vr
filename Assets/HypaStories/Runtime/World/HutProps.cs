using System.Collections.Generic;
using UnityEngine;
using MeshKit = HypaStories.TrailBuilder.MeshKit;

namespace HypaStories
{
    /// <summary>
    /// The shapes of the hut's household things, made here like everything else in the camp (vertex colours on the pines'
    /// shader): a clay pot, a mortar with its pestle, a palm-rib broom. Each is built standing on the origin (y up), with
    /// boxes for its colliders. A new shape = a case in <see cref="Build"/>; a name it doesn't know gets a plain crate.
    /// </summary>
    public static class HutProps
    {
        /// <summary>Adds the shape to <paramref name="mk"/> and its collider boxes to <paramref name="boxes"/>. False = the stand-in crate.</summary>
        public static bool Build(string prop, MeshKit mk, List<Bounds> boxes)
        {
            switch (prop)
            {
                case "clay-pot": ClayPot(mk, boxes); return true;
                case "mortar-pestle": MortarAndPestle(mk, boxes); return true;
                case "broom": Broom(mk, boxes); return true;
                case "oil-lamp": OilLamp(mk, boxes); return true;
                default: Crate(mk, boxes); return false;
            }
        }

        /// <summary>
        /// How a thing rests at its place, in the place's own space: the origin on the floor, +Z into the room, the wall
        /// <paramref name="toWall"/> metres behind (-Z). The pot and the mortar stand on the floor; the broom stands on its tied
        /// end and leans on the wall.
        /// </summary>
        public static Pose Rest(string prop, float toWall)
        {
            var size = Size(prop);
            // a wide thing stands a little out from the wall, so its belly doesn't sink into it (the meshes are at most 15 cm in radius)
            if (prop != "broom") return new Pose(new Vector3(0f, 0f, Mathf.Max(0f, 0.15f * size + 0.05f - toWall)), Quaternion.identity);
            const float lean = 14f;
            var half = 0.3f * size;
            var up = Quaternion.Euler(-lean, 0f, 0f) * Vector3.up;           // along the broom, its top toward the wall
            var foot = new Vector3(0f, 0.02f * size, -(toWall - 0.26f * size)); // where the tied end stands
            return new Pose(foot + up * half, Quaternion.Euler(-90f - lean, 0f, 0f)); // built lying along z: stood up, then leaned
        }

        /// <summary>
        /// How much bigger than its mesh a thing is shown (the meshes are built small): a big water pot of about 62 cm, a mortar
        /// of 64 cm with its pestle up to your shoulder, a broom of 85 cm. The number to change if a thing looks too small.
        /// </summary>
        public static float Size(string prop) => prop == "clay-pot" ? 2.4f : prop == "mortar-pestle" ? 2f : prop == "broom" ? 1.4f : prop == "oil-lamp" ? 1.6f : 1.7f;

        /// <summary>Where a thing's flame burns, in its own (mesh) space: the oil lamp's wick. Null for things without one.</summary>
        public static Vector3? Flame(string prop) => prop == "oil-lamp" ? new Vector3(0f, 0.062f, 0.105f) : (Vector3?)null;

        /// <summary>
        /// A picture of a thing for its card, drawn here from its own mesh (so a new shape has its picture at once, with no art
        /// to make): seen from the front and a little above, standing as <paramref name="pose"/> has it, lit from the upper left,
        /// its foot near the bottom edge. Transparent around the thing. A small software rasteriser: a few hundred triangles, once.
        /// </summary>
        public static Texture2D Picture(Mesh mesh, Quaternion pose, int size = 512)
        {
            var verts = mesh.vertices; var cols = mesh.colors; var norms = mesh.normals; var tris = mesh.triangles;
            var toCam = Quaternion.Inverse(Quaternion.Euler(16f, 180f + 28f, 0f)) * pose; // the eye in front (+Z, the room side), up and to one side
            var p = new Vector3[verts.Length];
            var n = new Vector3[verts.Length];
            var min = new Vector2(float.MaxValue, float.MaxValue); var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < verts.Length; i++)
            {
                p[i] = toCam * verts[i];
                n[i] = i < norms.Length ? toCam * norms[i] : Vector3.back;
                min = Vector2.Min(min, p[i]); max = Vector2.Max(max, p[i]);
            }
            const float margin = 0.06f;
            var span = Mathf.Max(max.x - min.x, max.y - min.y, 1e-4f);
            var scale = size * (1f - 2f * margin) / span;
            var ox = size * 0.5f - (min.x + max.x) * 0.5f * scale;   // centred across
            var oy = size * margin - min.y * scale;                  // standing on the bottom margin
            for (var i = 0; i < p.Length; i++) p[i] = new Vector3(p[i].x * scale + ox, p[i].y * scale + oy, p[i].z);

            var px = new Color32[size * size];
            var depth = new float[size * size];
            for (var i = 0; i < depth.Length; i++) depth[i] = float.MaxValue;
            var light = new Vector3(-0.45f, 0.6f, -0.66f).normalized;
            for (var t = 0; t + 2 < tris.Length; t += 3)
            {
                int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
                Vector3 a = p[ia], b = p[ib], c = p[ic];
                var area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area) < 1e-6f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                Color ca = ia < cols.Length ? cols[ia] : Color.white, cb = ib < cols.Length ? cols[ib] : Color.white, cc = ic < cols.Length ? cols[ic] : Color.white;
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        float fx = x + 0.5f, fy = y + 0.5f;
                        var wa = ((b.x - fx) * (c.y - fy) - (b.y - fy) * (c.x - fx)) / area;
                        var wb = ((c.x - fx) * (a.y - fy) - (c.y - fy) * (a.x - fx)) / area;
                        var wc = 1f - wa - wb;
                        if (wa < 0f || wb < 0f || wc < 0f) continue;
                        var z = a.z * wa + b.z * wb + c.z * wc;
                        var at = y * size + x;
                        if (z >= depth[at]) continue;
                        depth[at] = z;
                        var nn = (n[ia] * wa + n[ib] * wb + n[ic] * wc).normalized;
                        if (nn.z > 0f) nn = -nn; // both sides are seen: light the one that faces the eye
                        // lit warmly and well: the card is small and the hut is dim, and wood and clay are dark colours
                        var lit = 0.75f + 1.25f * Mathf.Max(0f, Vector3.Dot(nn, light)) + 0.35f * Mathf.Pow(1f - Mathf.Abs(nn.z), 2f);
                        var col = (ca * wa + cb * wb + cc * wc) * lit;
                        col = new Color(Mathf.Sqrt(col.r) * 1.05f, Mathf.Sqrt(col.g), Mathf.Sqrt(col.b) * 0.95f); // lifted, as the eye sees firelit things
                        px[at] = new Color32((byte)Mathf.Clamp(col.r * 255f, 0f, 255f), (byte)Mathf.Clamp(col.g * 255f, 0f, 255f), (byte)Mathf.Clamp(col.b * 255f, 0f, 255f), 255);
                    }
            }
            // give the empty pixels beside the thing its colour (still transparent), so its edge doesn't blend toward black
            for (var pass = 0; pass < 2; pass++)
            {
                var src = (Color32[])px.Clone();
                for (var y = 1; y < size - 1; y++)
                    for (var x = 1; x < size - 1; x++)
                    {
                        var at = y * size + x;
                        if (src[at].a != 0 || (src[at].r | src[at].g | src[at].b) != 0) continue;
                        foreach (var o in new[] { at - 1, at + 1, at - size, at + size })
                            if (src[o].a != 0 || (src[o].r | src[o].g | src[o].b) != 0) { px[at] = new Color32(src[o].r, src[o].g, src[o].b, 0); break; }
                    }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = mesh.name + " picture", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static readonly Quaternion Id = Quaternion.identity;

        /// <summary>A darker or lighter shade of a colour, still opaque.</summary>
        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        /// <summary>A round-bellied water pot of fired clay, 26 cm tall, with a darker band round the shoulder.</summary>
        static void ClayPot(MeshKit mk, List<Bounds> boxes)
        {
            var clay = new Color(0.62f, 0.33f, 0.2f);
            mk.Lathe(Vector3.zero, Id, new[]
            {
                (0.26f, 0.088f, 0.088f), (0.245f, 0.074f, 0.074f), (0.215f, 0.08f, 0.08f), (0.18f, 0.125f, 0.125f),
                (0.12f, 0.15f, 0.15f), (0.06f, 0.125f, 0.125f), (0.012f, 0.07f, 0.07f), (0f, 0.055f, 0.055f),
            }, clay, 16);
            mk.Lathe(Vector3.zero, Id, new[] { (0.2f, 0.108f, 0.108f), (0.185f, 0.124f, 0.124f), (0.17f, 0.136f, 0.136f) }, Shade(clay, 0.72f), 16); // the band
            mk.Ellipsoid(new Vector3(0f, 0.25f, 0f), new Vector3(0.074f, 0.006f, 0.074f), Id, new Color(0.07f, 0.05f, 0.04f), 12, 4);             // the dark inside
            mk.Ellipsoid(new Vector3(0f, 0.004f, 0f), new Vector3(0.055f, 0.004f, 0.055f), Id, Shade(clay, 0.6f), 12, 4);                       // its foot, seen when you turn it over
            boxes.Add(new Bounds(new Vector3(0f, 0.13f, 0f), new Vector3(0.24f, 0.26f, 0.24f)));
        }

        /// <summary>A waisted hardwood mortar, 32 cm tall, the pestle standing in it.</summary>
        static void MortarAndPestle(MeshKit mk, List<Bounds> boxes)
        {
            var wood = new Color(0.36f, 0.23f, 0.13f);
            mk.Lathe(Vector3.zero, Id, new[]
            {
                (0.32f, 0.125f, 0.125f), (0.3f, 0.135f, 0.135f), (0.22f, 0.115f, 0.115f), (0.13f, 0.075f, 0.075f),
                (0.05f, 0.11f, 0.11f), (0.012f, 0.125f, 0.125f), (0f, 0.12f, 0.12f),
            }, wood, 14);
            mk.Ellipsoid(new Vector3(0f, 0.305f, 0f), new Vector3(0.112f, 0.012f, 0.112f), Id, Shade(wood, 0.45f), 12, 4); // the hollow
            mk.Ellipsoid(new Vector3(0f, 0.004f, 0f), new Vector3(0.12f, 0.004f, 0.12f), Id, Shade(wood, 0.7f), 12, 4);   // its foot
            var pale = new Color(0.55f, 0.4f, 0.24f);
            var foot = new Vector3(0.02f, 0.25f, 0f);
            var top = new Vector3(0.1f, 0.74f, 0.03f);
            mk.Limb(foot, top, 0.036f, 0.03f, pale, 8);                                      // the pestle, leaning on the rim
            mk.Ellipsoid(foot, new Vector3(0.045f, 0.05f, 0.045f), Id, Shade(pale, 0.9f), 8, 5);   // its heavy end
            boxes.Add(new Bounds(new Vector3(0f, 0.16f, 0f), new Vector3(0.25f, 0.32f, 0.25f)));
            boxes.Add(new Bounds(new Vector3(0.06f, 0.53f, 0.015f), new Vector3(0.11f, 0.42f, 0.08f)));
        }

        /// <summary>A short broom of palm-leaf ribs, tied at one end, lying along z (60 cm).</summary>
        static void Broom(MeshKit mk, List<Bounds> boxes)
        {
            var rib = new Color(0.66f, 0.56f, 0.32f);
            var r = new System.Random(5);
            float Rnd() => (float)r.NextDouble();
            for (var i = 0; i < 22; i++)
            {
                var a = Rnd() * Mathf.PI * 2f;
                var spread = Mathf.Sqrt(Rnd());
                var from = new Vector3(Mathf.Cos(a) * 0.012f * spread, 0.03f + Mathf.Sin(a) * 0.012f * spread, -0.3f);
                var to = new Vector3(Mathf.Cos(a) * 0.075f * spread, 0.03f + Mathf.Abs(Mathf.Sin(a)) * 0.03f * spread, 0.26f + Rnd() * 0.05f);
                var c = Shade(rib, 0.82f + Rnd() * 0.3f);
                mk.Beam(from, to, 0.006f, c);
            }
            // the cord that ties it, twice round the handle end
            var cord = new Color(0.3f, 0.2f, 0.12f);
            mk.Limb(new Vector3(0f, 0.03f, -0.27f), new Vector3(0f, 0.03f, -0.22f), 0.022f, 0.024f, cord, 8);
            mk.Limb(new Vector3(0f, 0.03f, -0.14f), new Vector3(0f, 0.03f, -0.11f), 0.028f, 0.03f, cord, 8);
            boxes.Add(new Bounds(new Vector3(0f, 0.032f, 0f), new Vector3(0.15f, 0.064f, 0.62f)));
        }

        /// <summary>A small clay oil lamp: a shallow round bowl with a pinched spout at the front (+Z) and a cotton wick in it, 13 cm across.</summary>
        static void OilLamp(MeshKit mk, List<Bounds> boxes)
        {
            var clay = new Color(0.58f, 0.3f, 0.17f);
            mk.Lathe(Vector3.zero, Id, new[]
            {
                (0.05f, 0.062f, 0.062f), (0.042f, 0.068f, 0.068f), (0.022f, 0.06f, 0.06f), (0.006f, 0.04f, 0.04f), (0f, 0.032f, 0.032f),
            }, clay, 14);
            mk.Ellipsoid(new Vector3(0f, 0.044f, 0f), new Vector3(0.056f, 0.006f, 0.056f), Id, new Color(0.3f, 0.22f, 0.08f), 12, 3);              // the oil
            mk.Limb(new Vector3(0f, 0.04f, 0.04f), new Vector3(0f, 0.05f, 0.1f), 0.022f, 0.012f, clay, 8);                                         // the spout
            mk.Limb(new Vector3(0f, 0.045f, 0.05f), new Vector3(0f, 0.058f, 0.104f), 0.005f, 0.004f, new Color(0.85f, 0.8f, 0.68f), 5);            // the wick
            mk.Ellipsoid(new Vector3(0f, 0.058f, 0.104f), new Vector3(0.005f, 0.006f, 0.005f), Id, new Color(0.12f, 0.08f, 0.06f), 5, 3);           // its burnt tip
            mk.Limb(new Vector3(0f, 0.04f, -0.055f), new Vector3(0f, 0.052f, -0.085f), 0.012f, 0.01f, clay, 6);                                    // a little handle
            boxes.Add(new Bounds(new Vector3(0f, 0.025f, 0.015f), new Vector3(0.13f, 0.05f, 0.18f)));
        }

        /// <summary>The stand-in for a shape that isn't made yet: a small wooden crate.</summary>
        static void Crate(MeshKit mk, List<Bounds> boxes)
        {
            mk.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.2f, 0.2f, 0.2f), Id, new Color(0.45f, 0.32f, 0.2f));
            boxes.Add(new Bounds(new Vector3(0f, 0.1f, 0f), new Vector3(0.2f, 0.2f, 0.2f)));
        }
    }
}
