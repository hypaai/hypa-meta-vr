using UnityEngine;

namespace HypaStories
{
    /// <summary>Seated-distance drum and feast geometry. Generated once; no per-frame mesh work.</summary>
    public static class DrumVisuals
    {
        public static Texture2D SurfaceTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true) {
                name = "Natural surface detail", wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear, anisoLevel = 2
            };
            var pixels = new Color[size * size];
            var random = new System.Random(817);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    // Tileable coarse mottling in R, fine pores in G, long wood grain in B.
                    var u = x / (float)size * Mathf.PI * 2f;
                    var v = y / (float)size * Mathf.PI * 2f;
                    var mottle = 0.5f + 0.18f * Mathf.Sin(u * 3f + Mathf.Sin(v * 2f)) + 0.12f * Mathf.Cos(v * 5f - Mathf.Cos(u * 2f));
                    var grain = 0.5f + 0.3f * Mathf.Sin(u * 19f + Mathf.Sin(v) * 0.8f + Mathf.Sin(u * 3f));
                    pixels[y * size + x] = new Color(mottle, (float)random.NextDouble(), grain);
                }
            texture.SetPixels(pixels); texture.Apply(true, true);
            return texture;
        }

        public static Mesh Skin(float radius, bool low)
        {
            var mk = new TrailBuilder.MeshKit();
            var hide = low ? new Color(0.78f, 0.65f, 0.46f) : new Color(0.87f, 0.77f, 0.59f);
            Cylinder(mk, Vector3.zero, radius, radius, 0.011f, hide * 0.7f, hide, 64);
            // A single flat membrane, with a soft off-centre wear patch rather than raised rings.
            const int rings = 12, segments = 64;
            var start = mk.V.Count;
            mk.Grid(rings, segments, (i, j) => {
                var a = j * Mathf.PI * 2f / segments;
                var r = radius * i / rings;
                return new Vector3(Mathf.Cos(a) * r, 0.0115f, Mathf.Sin(a) * r);
            }, hide);
            for (var i = start; i < mk.V.Count; i++)
            {
                var p = mk.V[i];
                var distance = new Vector2(p.x + radius * 0.08f, p.z - radius * 0.06f).magnitude / radius;
                var wear = Mathf.Lerp(0.77f, 1f, Mathf.SmoothStep(0f, 0.85f, distance));
                var c = hide * wear; c.a = 1f; mk.C[i] = c;
            }
            return mk.Build("Continuous natural hide");
        }

        public static Mesh Dish(int index)
        {
            var mk = new TrailBuilder.MeshKit();
            var random = new System.Random(521 + index * 31);
            var clay = new Color(0.48f, 0.25f, 0.135f);
            var cream = new Color(0.89f, 0.84f, 0.68f);
            var leaf = new Color(0.21f, 0.34f, 0.095f);
            // Woven serving mat: crossed strands, with a bound edge.
            var straw = new Color(0.56f, 0.43f, 0.24f);
            Cylinder(mk, Vector3.zero, 0.255f, 0.255f, 0.009f, straw * 0.8f, straw, 48);
            for (var k = -12; k <= 12; k++)
            {
                var x = k * 0.019f;
                var span = Mathf.Sqrt(0.25f * 0.25f - x * x);
                mk.Beam(new Vector3(x, 0.01f, -span), new Vector3(x, 0.01f, span), 0.003f, straw * 1.15f);
                mk.Beam(new Vector3(-span, 0.012f, x), new Vector3(span, 0.012f, x), 0.003f, straw * 0.9f);
            }
            switch (index)
            {
                case 0:
                    Bowl(mk, Vector3.zero, 0.215f, 0.043f, clay);
                    // A leaf lining and two whole grilled fish, with visible heads and forked tails.
                    mk.Ellipsoid(new Vector3(0f, 0.032f, 0f), new Vector3(0.18f, 0.004f, 0.16f), Quaternion.identity, leaf, 24, 6);
                    Fish(mk, new Vector3(0.02f, 0.066f, -0.065f), -8f);
                    Fish(mk, new Vector3(-0.015f, 0.068f, 0.065f), 174f);
                    for (var k = 0; k < 3; k++)
                    {
                        var c = new Vector3(0.09f - k * 0.045f, 0.047f, -0.15f);
                        mk.Ellipsoid(c, new Vector3(0.022f, 0.012f, 0.012f), Quaternion.Euler(0f, k * 24f, 0f), new Color(0.68f, 0.09f, 0.025f), 12, 6);
                        mk.Limb(c + Vector3.right * 0.018f, c + new Vector3(0.03f, 0.005f, 0.005f), 0.002f, 0.001f, leaf, 5);
                    }
                    break;
                case 1:
                    Bowl(mk, Vector3.zero, 0.19f, 0.085f, new Color(0.34f, 0.19f, 0.085f));
                    for (var k = 0; k < 3; k++)
                    {
                        var a = k * Mathf.PI * 2f / 3f;
                        var scale = 0.94f + (float)random.NextDouble() * 0.1f;
                        mk.Ellipsoid(new Vector3(Mathf.Cos(a) * 0.074f, 0.092f, Mathf.Sin(a) * 0.074f),
                            new Vector3(0.073f, 0.052f, 0.067f) * scale, Quaternion.Euler(0f, k * 37f, 0f), cream, 24, 12);
                    }
                    break;
                case 2:
                    Bowl(mk, Vector3.zero, 0.19f, 0.04f, clay);
                    for (var k = 0; k < 6; k++)
                    {
                        var a = k * 2.4f;
                        var c = new Vector3(Mathf.Sin(a) * 0.09f, 0.04f + k % 2 * 0.027f, Mathf.Cos(a) * 0.075f);
                        // Thick cut pieces with a narrow brown rind, rather than rectangular blocks.
                        Cylinder(mk, c, 0.046f, 0.043f, 0.035f, new Color(0.53f, 0.37f, 0.22f), cream, 12);
                        Cylinder(mk, c + Vector3.up * 0.0352f, 0.04f, 0.04f, 0.001f, cream, cream, 12);
                    }
                    mk.Ellipsoid(new Vector3(0.01f, 0.043f, 0.21f), new Vector3(0.17f, 0.034f, 0.033f), Quaternion.Euler(0f, -10f, 0f), new Color(0.33f, 0.22f, 0.12f), 24, 10);
                    break;
                case 3:
                case 4:
                    var palm = index == 4;
                    var radius = palm ? 0.205f : 0.18f;
                    var height = palm ? 0.173f : 0.15f;
                    Bowl(mk, Vector3.zero, radius, height, clay);
                    var soup = palm ? new Color(0.73f, 0.23f, 0.035f) : new Color(0.3f, 0.39f, 0.095f);
                    Cylinder(mk, Vector3.up * (height - 0.018f), radius * 0.87f, radius * 0.87f, 0.003f, soup, soup, 48);
                    for (var k = 0; k < 14; k++)
                    {
                        var a = k * 2.4f;
                        var r = radius * (0.2f + (float)random.NextDouble() * 0.53f);
                        var c = new Vector3(Mathf.Sin(a) * r, height - 0.013f, Mathf.Cos(a) * r);
                        if (!palm && k % 2 == 0)
                        {
                            Cylinder(mk, c, 0.014f, 0.014f, 0.005f, leaf, new Color(0.57f, 0.65f, 0.28f), 7);
                            for (var seed = 0; seed < 5; seed++)
                            {
                                var t = seed * Mathf.PI * 2f / 5f;
                                mk.Ellipsoid(c + new Vector3(Mathf.Cos(t) * 0.007f, 0.006f, Mathf.Sin(t) * 0.007f), Vector3.one * 0.0025f, Quaternion.identity, cream, 6, 4);
                            }
                        }
                        else mk.Ellipsoid(c, new Vector3(0.022f, 0.012f, 0.017f), Quaternion.Euler(0f, k * 33f, 0f), k % 3 == 0 ? new Color(0.28f, 0.13f, 0.055f) : leaf, 12, 6);
                    }
                    if (!palm)
                        for (var k = 0; k < 3; k++)
                        {
                            var c = new Vector3(-0.08f + k * 0.07f, 0.032f, 0.215f);
                            mk.Limb(c, c + new Vector3(0.05f, 0f, -0.025f), 0.017f, 0.002f, leaf, 7);
                        }
                    break;
                default:
                    var gourd = new Color(0.61f, 0.41f, 0.19f);
                    mk.Lathe(Vector3.zero, Quaternion.identity, new[] {
                        (0.35f, 0.034f, 0.034f), (0.34f, 0.04f, 0.04f), (0.30f, 0.036f, 0.036f),
                        (0.255f, 0.055f, 0.055f), (0.22f, 0.105f, 0.105f), (0.14f, 0.13f, 0.13f),
                        (0.06f, 0.10f, 0.10f), (0.025f, 0.045f, 0.045f), (0.025f, 0f, 0f)
                    }, gourd, 40);
                    Cylinder(mk, Vector3.up * 0.342f, 0.029f, 0.029f, 0.002f, cream, cream, 24);
                    Bowl(mk, new Vector3(0.17f, 0f, -0.11f), 0.068f, 0.068f, gourd * 0.8f);
                    Cylinder(mk, new Vector3(0.17f, 0.052f, -0.11f), 0.057f, 0.057f, 0.002f, cream, cream, 24);
                    break;
            }
            return mk.Build("Feast: " + index);
        }

        static void Cylinder(TrailBuilder.MeshKit mk, Vector3 bottom, float r0, float r1, float h, Color side, Color top, int segments)
        {
            // Local version: the shared scenery helper winds its cap inward.
            mk.Lathe(bottom, Quaternion.identity, new[] { (h, r1, r1), (0f, r0, r0) }, side, segments);
            var centre = bottom + Vector3.up * h;
            for (var j = 0; j < segments; j++)
            {
                var a = j * Mathf.PI * 2f / segments;
                var b = (j + 1) * Mathf.PI * 2f / segments;
                var p = centre + new Vector3(Mathf.Cos(a) * r1, 0f, Mathf.Sin(a) * r1);
                var q = centre + new Vector3(Mathf.Cos(b) * r1, 0f, Mathf.Sin(b) * r1);
                mk.Tri(centre, q, p, top, top, top);
            }
        }

        static void Bowl(TrailBuilder.MeshKit mk, Vector3 centre, float r, float h, Color clay)
        {
            // Profile runs from the interior floor, over the rolled lip, down the outer wall.
            mk.Lathe(centre, Quaternion.identity, new[] {
                (0.024f, 0f, 0f), (0.024f, r * 0.5f, r * 0.5f),
                (h * 0.7f, r * 0.85f, r * 0.85f), (h, r * 0.92f, r * 0.92f),
                (h + 0.005f, r * 0.97f, r * 0.97f), (h, r, r),
                (h * 0.65f, r * 0.94f, r * 0.94f), (0.016f, r * 0.62f, r * 0.62f),
                (0.014f, r * 0.5f, r * 0.5f), (0.014f, 0f, 0f)
            }, clay, 48);
        }

        static void Fish(TrailBuilder.MeshKit mk, Vector3 centre, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => centre + rot * new Vector3(x, y, z);
            var gold = new Color(0.49f, 0.29f, 0.105f);
            var charred = new Color(0.105f, 0.057f, 0.022f);
            // Tapered body: tail wrist, full belly, shoulders and a narrower mouth.
            mk.Lathe(centre, rot * Quaternion.FromToRotation(Vector3.up, Vector3.right), new[] {
                (0.14f, 0.009f, 0.013f), (0.12f, 0.021f, 0.026f), (0.075f, 0.029f, 0.037f),
                (0.02f, 0.032f, 0.045f), (-0.055f, 0.025f, 0.035f),
                (-0.105f, 0.012f, 0.016f), (-0.13f, 0.006f, 0.009f)
            }, gold, 32);
            mk.Quad2(P(-0.12f, 0f, 0f), P(-0.185f, -0.005f, -0.043f), P(-0.167f, -0.005f, 0f), P(-0.185f, -0.005f, 0.043f), gold * 0.7f);
            mk.Quad2(P(-0.06f, 0.01f, -0.025f), P(-0.04f, 0.006f, -0.064f), P(0.04f, 0.006f, -0.055f), P(0.065f, 0.01f, -0.025f), gold * 0.65f);
            mk.Quad2(P(0.04f, 0.01f, 0.025f), P(0.015f, 0.005f, 0.066f), P(0.065f, 0.005f, 0.05f), P(0.07f, 0.01f, 0.02f), gold * 0.75f);
            foreach (var side in new[] { -1f, 1f })
            {
                mk.Ellipsoid(P(0.109f, 0.018f, side * 0.019f), Vector3.one * 0.0065f, rot, new Color(0.61f, 0.48f, 0.27f), 12, 6);
                mk.Ellipsoid(P(0.109f, 0.022f, side * 0.021f), Vector3.one * 0.0038f, rot, charred, 10, 5);
            }
            for (var k = 0; k < 4; k++)
            {
                var x = -0.065f + k * 0.038f;
                mk.Limb(P(x - 0.009f, 0.022f, -0.022f), P(x, 0.033f, 0f), 0.0025f, 0.002f, charred, 5);
                mk.Limb(P(x, 0.033f, 0f), P(x + 0.009f, 0.022f, 0.022f), 0.002f, 0.0025f, charred, 5);
            }
            mk.Limb(P(0.077f, 0.022f, -0.023f), P(0.083f, 0.029f, 0.021f), 0.0015f, 0.0015f, charred, 5);
            mk.Limb(P(0.139f, 0.003f, -0.009f), P(0.139f, 0.003f, 0.009f), 0.0015f, 0.0015f, charred, 5);
        }
    }
}
