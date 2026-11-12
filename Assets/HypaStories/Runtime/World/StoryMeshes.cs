using System;
using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>Procedural meshes and textures for the camp (the prototype's textures.js plus the meshes its shaders animate).</summary>
    public static class StoryMeshes
    {
        /// <summary>A quad in the XY plane, front facing -Z (toward a viewer looking +Z). anchorBottom puts y=0 at the bottom edge.</summary>
        public static Mesh Quad(float w, float h, bool anchorBottom = false)
        {
            var y0 = anchorBottom ? 0f : -h / 2f;
            var m = new Mesh { name = "Hypa Quad" };
            m.vertices = new[] { new Vector3(-w / 2, y0, 0), new Vector3(w / 2, y0, 0), new Vector3(-w / 2, y0 + h, 0), new Vector3(w / 2, y0 + h, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Camera-facing particle quads for the HypaStories/Particles shader: four vertices per particle at its home position,
        /// uv0 = corner, uv1 = random seed. The shader moves them, so the bounds are padded.
        /// </summary>
        public static Mesh Billboards(int count, int seed, Func<System.Random, Vector3> home, float pad = 6f)
        {
            var r = new System.Random(seed);
            var v = new Vector3[count * 4]; var uv = new Vector2[count * 4]; var s = new List<Vector4>(count * 4); var t = new int[count * 6];
            for (var i = 0; i < count; i++)
            {
                var p = home(r);
                var sd = new Vector4((float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble());
                for (var k = 0; k < 4; k++) { v[i * 4 + k] = p; uv[i * 4 + k] = new Vector2(k & 1, k >> 1); s.Add(sd); }
                t[i * 6] = i * 4; t[i * 6 + 1] = i * 4 + 2; t[i * 6 + 2] = i * 4 + 1;
                t[i * 6 + 3] = i * 4 + 2; t[i * 6 + 4] = i * 4 + 3; t[i * 6 + 5] = i * 4 + 1;
            }
            var m = new Mesh { name = "Hypa Particles", vertices = v, uv = uv };
            m.SetUVs(1, s);
            m.triangles = t;
            m.RecalculateBounds();
            var b = m.bounds; b.Expand(pad * 2f); m.bounds = b;
            return m;
        }

        /// <summary>Rain drops for HypaStories/Rain: a w x h x w box of streaks around the origin (the shader wraps them vertically).</summary>
        public static Mesh Rain(int count, float w, float h, int seed)
        {
            var r = new System.Random(seed);
            var v = new Vector3[count * 4]; var uv = new Vector2[count * 4]; var s = new Vector2[count * 4]; var t = new int[count * 6];
            for (var i = 0; i < count; i++)
            {
                var p = new Vector3(((float)r.NextDouble() - 0.5f) * w, (float)r.NextDouble() * h, ((float)r.NextDouble() - 0.5f) * w);
                var sd = (float)r.NextDouble();
                for (var k = 0; k < 4; k++) { v[i * 4 + k] = p; uv[i * 4 + k] = new Vector2(k & 1, k >> 1); s[i * 4 + k] = new Vector2(sd, 0); }
                t[i * 6] = i * 4; t[i * 6 + 1] = i * 4 + 2; t[i * 6 + 2] = i * 4 + 1;
                t[i * 6 + 3] = i * 4 + 2; t[i * 6 + 4] = i * 4 + 3; t[i * 6 + 5] = i * 4 + 1;
            }
            var m = new Mesh { name = "Hypa Rain", vertices = v, uv = uv, uv2 = s, triangles = t };
            m.bounds = new Bounds(new Vector3(0, h / 2, 0), new Vector3(w + 2, h + 2, w + 2));
            return m;
        }

        /// <summary>A flat ring (annulus) in the XY plane; dashes &gt; 0 cuts it into that many dashes.</summary>
        public static Mesh Ring(float inner, float outer, int segments = 96, int dashes = 0)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (var i = 0; i < segments; i++)
            {
                if (dashes > 0 && (i * dashes / segments) % 2 == 1) continue;
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var b = v.Count;
                v.Add(new Vector3(Mathf.Cos(a0) * inner, Mathf.Sin(a0) * inner)); v.Add(new Vector3(Mathf.Cos(a0) * outer, Mathf.Sin(a0) * outer));
                v.Add(new Vector3(Mathf.Cos(a1) * inner, Mathf.Sin(a1) * inner)); v.Add(new Vector3(Mathf.Cos(a1) * outer, Mathf.Sin(a1) * outer));
                uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f, 0.5f));
                t.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 });
            }
            var m = new Mesh { name = "Hypa Ring" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// A low-poly pine: a tapered trunk and stacked, slightly jagged cone tiers, vertex coloured for HypaStories/Pine. Stands in
        /// for the prototype's EZ-Tree pines (same density and silhouette, a fraction of the triangles for Quest). Height 1 m; scale it.
        /// </summary>
        public static Mesh Pine(int seed)
        {
            var r = new System.Random(seed);
            float R() => (float)r.NextDouble();
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            var bark = new Color(0.17f, 0.11f, 0.07f);

            void Cone(float y0, float y1, float r0, float r1, int sides, Color col0, Color col1, float jag)
            {
                var b = v.Count;
                var rot = R() * Mathf.PI;
                for (var i = 0; i <= sides; i++)
                {
                    var a = rot + i * Mathf.PI * 2f / sides;
                    var k = i % sides;
                    var j = 1f + (k % 2 == 0 ? jag : -jag);
                    v.Add(new Vector3(Mathf.Cos(a) * r0 * j, y0 - (k % 2) * jag * 0.4f, Mathf.Sin(a) * r0 * j)); c.Add(col0);
                    v.Add(new Vector3(Mathf.Cos(a) * r1, y1, Mathf.Sin(a) * r1)); c.Add(col1);
                }
                for (var i = 0; i < sides; i++)
                {
                    var q = b + i * 2;
                    t.AddRange(new[] { q, q + 1, q + 2, q + 2, q + 1, q + 3 });
                }
                if (r0 > 0.05f) // underside, so tiers don't look hollow from below
                {
                    var centre = v.Count; v.Add(new Vector3(0, y0 + 0.02f, 0)); c.Add(col0 * 0.5f);
                    for (var i = 0; i < sides; i++) t.AddRange(new[] { centre, b + (i + 1) * 2, b + i * 2 });
                }
            }

            Cone(0f, 0.75f, 0.035f, 0.012f, 6, bark, bark, 0f);
            const int tiers = 7;
            for (var i = 0; i < tiers; i++)
            {
                var u = i / (float)(tiers - 1);
                var y0 = 0.16f + u * 0.62f;
                var half = (0.24f - u * 0.2f) * (0.85f + R() * 0.3f);
                var shade = 0.75f + R() * 0.35f;
                var low = new Color(0.035f, 0.075f, 0.07f) * shade;
                var high = new Color(0.07f, 0.13f, 0.11f) * shade;
                Cone(y0, y0 + 0.2f + (1f - u) * 0.06f, half, 0.01f, 9, low, high, 0.08f);
            }
            var m = new Mesh { name = "Hypa Pine" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>White radial glow with alpha (the prototype's glowTexture).</summary>
        public static Texture2D Glow(int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Hypa Glow", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    var a = d < 0.25f ? Mathf.Lerp(1f, 0.45f, d / 0.25f) : Mathf.Lerp(0.45f, 0f, Mathf.Clamp01((d - 0.25f) / 0.75f));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply(true, true);
            return tex;
        }

        /// <summary>A pine silhouette (white + alpha) for the far treeline cards: nine jagged tiers, like the prototype's pineTexture.</summary>
        public static Texture2D PineSilhouette(int seed, int w = 128, int h = 256)
        {
            var r = new System.Random(seed);
            float R() => (float)r.NextDouble();
            var px = new Color32[w * h];
            float cx = w / 2f;
            var trunk = new List<Vector2> { new Vector2(cx - 2, h * 0.7f), new Vector2(cx + 2, h * 0.7f), new Vector2(cx + 2, h), new Vector2(cx - 2, h) };
            Fill(px, w, h, trunk);
            const int tiers = 9;
            for (var i = 0; i < tiers; i++)
            {
                var u = i / (float)(tiers - 1);
                var y = h * 0.03f + u * h * 0.72f;
                var half = (8f + u * 46f) * (0.85f + R() * 0.3f);
                var hh = h * 0.17f;
                var right = new List<Vector2>();
                for (var s = 1; s <= 4; s++)
                {
                    var k = s / 4f;
                    right.Add(new Vector2(cx + half * k + (R() - 0.5f) * 4f, y + hh * k));
                    if (s < 4) right.Add(new Vector2(cx + half * k - half * 0.2f, y + hh * k + hh * 0.06f));
                }
                var poly = new List<Vector2> { new Vector2(cx, y) };
                poly.AddRange(right);
                for (var k = right.Count - 1; k >= 0; k--) poly.Add(new Vector2(2 * cx - right[k].x, right[k].y));
                Fill(px, w, h, poly);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Hypa Pine Card", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply(true, true);
            return tex;
        }

        /// <summary>Even-odd scanline fill. Polygon in canvas coordinates (y down); the texture is y up.</summary>
        static void Fill(Color32[] px, int w, int h, List<Vector2> poly)
        {
            var xs = new List<float>();
            for (var row = 0; row < h; row++)
            {
                var y = row + 0.5f;
                xs.Clear();
                for (var i = 0; i < poly.Count; i++)
                {
                    var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                    if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y)) xs.Add(a.x + (y - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (var k = 0; k + 1 < xs.Count; k += 2)
                    for (var x = Mathf.Max(0, Mathf.CeilToInt(xs[k] - 0.5f)); x < Mathf.Min(w, Mathf.CeilToInt(xs[k + 1] - 0.5f)); x++)
                        px[(h - 1 - row) * w + x] = new Color32(255, 255, 255, 255);
            }
        }
    }
}
