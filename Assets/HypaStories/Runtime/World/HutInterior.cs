using System.Collections.Generic;
using UnityEngine;
using MeshKit = HypaStories.TrailBuilder.MeshKit;

namespace HypaStories
{
    /// <summary>
    /// What makes the hut's room a village home, round its household things (<see cref="HutVisit"/>): the roof's rafters and
    /// the rings of lath under the thatch, a dark plinth and a painted band round the mud wall, a mud ledge at the back with
    /// calabashes and bowls on it, a three-stone hearth with embers and firelight, firewood against the wall, a woven basket,
    /// a rolled sleeping mat, a hoe on its peg and calabashes hanging from the roof. All made here like the rest of the camp
    /// (vertex colours on the pines' shader); only the hearth's glow and its one vertex light move.
    /// Built in the hut's own space: the centre at the origin, the door on +Z, angles round the wall measured from the door.
    /// </summary>
    public static class HutInterior
    {
        /// <summary>The hearth, in the hut's space (x, z): in front of the old woman, to her side.</summary>
        public static readonly Vector2 Hearth = new Vector2(0.95f, -0.9f);

        /// <summary>The parts that flicker: glow materials (with their base opacity), the fire's light and the cards that face you.</summary>
        public sealed class Fire
        {
            public readonly List<(Material m, float baseOpacity, float ph)> Glows = new List<(Material, float, float)>();
            public readonly List<Transform> Cards = new List<Transform>();
            public Light Light;
            public float LightBase;
        }

        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly Quaternion Id = Quaternion.identity;

        static Vector3 At(float deg, float r, float y)
        {
            var a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
        }

        static Vector3 X0Z(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        public static Fire Build(Transform parent, Material vc, StoryAssets a, float floorY)
        {
            var mk = new MeshKit();
            Roof(mk);
            Wall(mk, floorY);
            Ledge(mk, floorY);
            Hearth3(mk, floorY);
            Things(mk, floorY);
            var go = new GameObject("Hut Interior");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mk.Build("Hut Interior");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = vc;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return FireGlow(parent, a, floorY);
        }

        // ---------- the roof from inside: rafters to the apex, rings of lath, a ring beam on the wall ----------

        static void Roof(MeshKit mk)
        {
            const int rafters = 16;
            var wood = new Color(0.22f, 0.14f, 0.08f);
            var lath = new Color(0.36f, 0.26f, 0.14f);
            const float footR = HutTrailLayout.WallInner + 0.1f, footY = HutTrailLayout.WallHeight + 0.02f, headR = 0.08f;
            var headY = HutTrailLayout.RoofY(headR) - 0.06f;
            Vector3 Rafter(float deg, float t) => Vector3.Lerp(At(deg, footR, footY), At(deg, headR, headY), t);
            for (var i = 0; i < rafters; i++)
            {
                var deg = i * 360f / rafters + 11f;
                mk.Beam(Rafter(deg, 0f), Rafter(deg, 1f), 0.055f, Shade(wood, 0.9f + 0.2f * (i % 3) / 2f));
            }
            // rings of lath tied across the rafters, the thatch laid over them
            foreach (var t in new[] { 0.22f, 0.45f, 0.66f, 0.84f })
                for (var i = 0; i < rafters; i++)
                {
                    float d0 = i * 360f / rafters + 11f, d1 = (i + 1) * 360f / rafters + 11f;
                    mk.Beam(Rafter(d0, t) + Vector3.down * 0.03f, Rafter(d1, t) + Vector3.down * 0.03f, 0.03f, lath);
                }
            // the ring beam the rafters stand on, along the top of the wall
            const int seg = 28;
            for (var i = 0; i < seg; i++)
                mk.Beam(At(i * 360f / seg, footR - 0.05f, footY - 0.03f), At((i + 1) * 360f / seg, footR - 0.05f, footY - 0.03f), 0.08f, Shade(wood, 1.1f));
            // the knot at the top where the rafters meet
            mk.Ellipsoid(new Vector3(0f, headY, 0f), new Vector3(0.12f, 0.1f, 0.12f), Id, Shade(wood, 0.8f), 8, 5);
        }

        // ---------- the mud wall: a darker plinth, and a band of painted triangles at waist height ----------

        static void Wall(MeshKit mk, float floorY)
        {
            var r = HutTrailLayout.WallInner - 0.008f;
            var from = HutTrailLayout.DoorHalfAngle + 2f;
            var to = 360f - HutTrailLayout.DoorHalfAngle - 2f;
            const int seg = 72;
            var plinth = new Color(0.2f, 0.13f, 0.08f);
            var ochre = new Color(0.62f, 0.27f, 0.1f);
            var chalk = new Color(0.86f, 0.8f, 0.68f);
            // painted on both faces: which one faces the room depends on the winding, and these sit a few millimetres off the wall
            void Inside(float d0, float d1, float y0, float y1, Color lo, Color hi)
            {
                mk.Quad(At(d1, r, y0), At(d1, r, y1), At(d0, r, y1), At(d0, r, y0), lo, hi, hi, lo);
                mk.Quad(At(d0, r, y0), At(d0, r, y1), At(d1, r, y1), At(d1, r, y0), lo, hi, hi, lo);
            }
            void Tri2(Vector3 a, Vector3 b, Vector3 c, Color col) { mk.Tri(a, b, c, col, col, col); mk.Tri(c, b, a, col, col, col); }
            for (var i = 0; i < seg; i++)
            {
                float d0 = Mathf.Lerp(from, to, i / (float)seg), d1 = Mathf.Lerp(from, to, (i + 1) / (float)seg);
                Inside(d0, d1, floorY, 0.34f, Shade(plinth, 0.85f), plinth);                              // the plinth, darkened by feet and brooms
                Inside(d0, d1, 1.02f, 1.05f, ochre, ochre);                                               // the band's edges
                Inside(d0, d1, 1.27f, 1.3f, ochre, ochre);
                // a zigzag of chalk and ochre triangles between them
                var dm = (d0 + d1) * 0.5f;
                var lo = At(d0, r - 0.001f, 1.06f); var lo2 = At(d1, r - 0.001f, 1.06f); var hiM = At(dm, r - 0.001f, 1.26f);
                Tri2(lo2, hiM, lo, i % 2 == 0 ? chalk : ochre);
                var hi0 = At(d0, r - 0.001f, 1.26f); var hi1 = At(d1, r - 0.001f, 1.26f); var loM = At(dm, r - 0.001f, 1.06f);
                Tri2(hi0, loM, hi1, Shade(plinth, 1.4f));
            }
        }

        // ---------- a mud ledge along the back wall (a seat and a shelf), calabashes and bowls on it ----------

        static void Ledge(MeshKit mk, float floorY)
        {
            var mud = new Color(0.42f, 0.29f, 0.19f);
            float rOut = HutTrailLayout.WallInner - 0.01f, rIn = HutTrailLayout.WallInner - 0.36f;
            const float h = 0.36f, from = 152f, to = 208f;
            const int seg = 10;
            for (var i = 0; i < seg; i++)
            {
                float d0 = Mathf.Lerp(from, to, i / (float)seg), d1 = Mathf.Lerp(from, to, (i + 1) / (float)seg);
                mk.Quad(At(d0, rIn, h), At(d0, rOut, h), At(d1, rOut, h), At(d1, rIn, h), Shade(mud, 1.08f));      // top
                mk.Quad(At(d1, rIn, floorY), At(d1, rIn, h), At(d0, rIn, h), At(d0, rIn, floorY), Shade(mud, 0.8f), mud, mud, Shade(mud, 0.8f)); // front
            }
            foreach (var d in new[] { from, to })
                mk.Quad(At(d, rOut, floorY), At(d, rOut, h), At(d, rIn, h), At(d, rIn, floorY), Shade(mud, 0.75f));
            // on it: two calabashes, a stack of bowls, a small clay jar
            var gourd = new Color(0.78f, 0.6f, 0.3f);
            var rm = (rIn + rOut) * 0.5f;
            Calabash(mk, At(160f, rm, h), 0.13f, gourd);
            Calabash(mk, At(170f, rm, h), 0.1f, Shade(gourd, 0.85f));
            for (var k = 0; k < 3; k++)
                mk.Lathe(At(196f, rm, h + k * 0.035f), Id, new[] { (0.05f, 0.11f, 0.11f), (0.0f, 0.06f, 0.06f) }, Shade(new Color(0.5f, 0.3f, 0.18f), 1f - k * 0.08f), 12);
            mk.Lathe(At(186f, rm, h), Id, new[] { (0.2f, 0.04f, 0.04f), (0.17f, 0.05f, 0.05f), (0.1f, 0.09f, 0.09f), (0.02f, 0.07f, 0.07f), (0f, 0.05f, 0.05f) }, new Color(0.55f, 0.28f, 0.16f), 12);
        }

        /// <summary>A round gourd, a ridge where it was cut, a lid of the same gourd.</summary>
        static void Calabash(MeshKit mk, Vector3 at, float r, Color c)
        {
            mk.Ellipsoid(at + Vector3.up * r * 0.85f, new Vector3(r, r * 0.85f, r), Id, c, 12, 7);
            mk.Lathe(at + Vector3.up * r * 1.25f, Id, new[] { (0.035f, r * 0.62f, r * 0.62f), (0f, r * 0.74f, r * 0.74f) }, Shade(c, 0.7f), 12);
        }

        // ---------- the hearth: three stones, ash, embers, the ends of three logs, a pot on the stones ----------

        static void Hearth3(MeshKit mk, float floorY)
        {
            var c = X0Z(Hearth, floorY);
            mk.Ellipsoid(c + Vector3.up * 0.004f, new Vector3(0.3f, 0.008f, 0.3f), Id, new Color(0.3f, 0.29f, 0.27f), 14, 4);       // ash
            mk.Ellipsoid(c + Vector3.up * 0.006f, new Vector3(0.36f, 0.006f, 0.36f), Id, new Color(0.13f, 0.1f, 0.08f), 14, 4);     // the scorched ring
            var stone = new Color(0.33f, 0.31f, 0.28f);
            for (var i = 0; i < 3; i++)
            {
                var d = i * 120f + 30f;
                mk.Ellipsoid(c + At(d, 0.19f, 0.075f), new Vector3(0.1f, 0.08f, 0.09f), Quaternion.Euler(0f, d, 8f), Shade(stone, 0.9f + i * 0.08f), 9, 6);
                // a log between each pair of stones, burnt to coal at the middle
                var ld = d + 60f;
                mk.Limb(c + At(ld, 0.55f, 0.05f), c + At(ld, 0.08f, 0.04f), 0.045f, 0.04f, new Color(0.28f, 0.18f, 0.1f), 7);
                mk.Ellipsoid(c + At(ld, 0.1f, 0.04f), new Vector3(0.045f, 0.035f, 0.045f), Id, new Color(0.08f, 0.05f, 0.04f), 7, 4);
            }
            for (var i = 0; i < 9; i++) // embers
                mk.Ellipsoid(c + At(i * 41f, 0.04f + (i % 3) * 0.035f, 0.02f), new Vector3(0.025f, 0.015f, 0.025f), Id, new Color(0.75f, 0.2f + (i % 3) * 0.08f, 0.05f), 6, 3);
            // a blackened clay pot sitting on the stones
            mk.Lathe(c + Vector3.up * 0.12f, Id, new[]
            {
                (0.27f, 0.12f, 0.12f), (0.24f, 0.11f, 0.11f), (0.17f, 0.17f, 0.17f), (0.08f, 0.16f, 0.16f), (0.01f, 0.08f, 0.08f), (0f, 0.05f, 0.05f),
            }, new Color(0.13f, 0.1f, 0.08f), 14);
        }

        // ---------- things of the house round the wall ----------

        static void Things(MeshKit mk, float floorY)
        {
            var wall = HutTrailLayout.WallInner;
            // firewood stacked against the wall, near the hearth
            var wood = new Color(0.36f, 0.24f, 0.14f);
            var rnd = new System.Random(9);
            float R() => (float)rnd.NextDouble();
            for (var row = 0; row < 4; row++)
                for (var k = 0; k < 5 - row; k++)
                {
                    var d = -146f + (k - (4 - row) / 2f) * 3.2f;
                    var at = At(d, wall - 0.2f, floorY + 0.05f + row * 0.085f);
                    var along = (At(d + 90f, 1f, 0f)).normalized * (0.35f + R() * 0.1f);
                    mk.Limb(at - along, at + along, 0.042f, 0.038f, Shade(wood, 0.85f + R() * 0.3f), 7);
                }
            // a woven basket on the floor
            var straw = new Color(0.7f, 0.55f, 0.3f);
            var b = At(140f, wall - 0.35f, floorY);
            mk.Lathe(b, Id, new[] { (0.3f, 0.2f, 0.2f), (0.22f, 0.21f, 0.21f), (0.1f, 0.18f, 0.18f), (0.0f, 0.13f, 0.13f) }, straw, 14);
            foreach (var y in new[] { 0.08f, 0.16f, 0.24f })
                mk.Lathe(b + Vector3.up * y, Id, new[] { (0.02f, 0.205f + y * 0.05f, 0.205f + y * 0.05f), (0f, 0.205f + y * 0.05f, 0.205f + y * 0.05f) }, Shade(straw, 0.6f), 14);
            Calabash(mk, b + Vector3.up * 0.22f, 0.09f, new Color(0.8f, 0.62f, 0.32f)); // a gourd in it
            // a sleeping mat, rolled and stood by the wall near the door
            var mat = At(-40f, wall - 0.14f, floorY);
            mk.Cylinder(mat, 0.09f, 0.09f, 1.15f, new Color(0.72f, 0.6f, 0.34f), new Color(0.55f, 0.42f, 0.22f), 12);
            foreach (var y in new[] { 0.25f, 0.85f })
                mk.Cylinder(mat + Vector3.up * y, 0.095f, 0.095f, 0.04f, new Color(0.4f, 0.2f, 0.1f), new Color(0.4f, 0.2f, 0.1f), 12);
            // a hoe on a peg in the wall
            var peg = At(-72f, wall - 0.05f, 1.42f);
            var inward = -new Vector3(peg.x, 0f, peg.z).normalized;
            mk.Beam(peg, peg + inward * 0.1f, 0.03f, new Color(0.25f, 0.16f, 0.09f));
            var side = Vector3.Cross(Vector3.up, inward);
            var handleTop = peg + inward * 0.06f + Vector3.up * 0.02f;
            mk.Limb(handleTop, handleTop + Vector3.down * 0.62f + side * 0.08f, 0.02f, 0.018f, new Color(0.45f, 0.32f, 0.18f), 6);
            mk.Box(handleTop + Vector3.down * 0.66f + side * 0.09f + inward * 0.06f, new Vector3(0.14f, 0.04f, 0.17f),
                Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Euler(20f, 0f, 0f), new Color(0.22f, 0.21f, 0.2f));
            // calabashes hung by cords from the rings of lath
            foreach (var d in new[] { 83f, -84f })
            {
                var top = At(d, 1.72f, HutTrailLayout.RoofY(1.72f) - 0.12f);
                var bottom = At(d, 1.72f, 1.98f);
                mk.Beam(top, bottom + Vector3.up * 0.1f, 0.008f, new Color(0.3f, 0.22f, 0.12f));
                Calabash(mk, bottom - Vector3.up * 0.12f, 0.12f, new Color(0.74f, 0.56f, 0.28f));
            }
        }

        // ---------- the fire's glow: on the ash, two small flames that face you, and a light on the walls ----------

        static Fire FireGlow(Transform parent, StoryAssets a, float floorY)
        {
            var fire = new Fire();
            if (a == null || a.additive == null) return fire;
            var glowTex = StoryMeshes.Glow();
            var c = X0Z(Hearth, floorY);
            Material Glow(Color col, float o, float ph)
            {
                var m = new Material(a.additive) { mainTexture = glowTex, color = col, name = "Hearth Glow" };
                m.SetFloat(OpacityId, o);
                fire.Glows.Add((m, o, ph));
                return m;
            }
            Transform Card(string name, Vector3 pos, float size, Material m, bool flat)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                if (flat) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = StoryMeshes.Quad(size, size);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (!flat) fire.Cards.Add(go.transform);
                return go.transform;
            }
            Card("Hearth Ground Glow", c + Vector3.up * 0.03f, 1.6f, Glow(new Color(1f, 0.45f, 0.15f), 0.3f, 0.3f), true);
            Card("Hearth Embers", c + Vector3.up * 0.035f, 0.42f, Glow(new Color(1f, 0.35f, 0.08f), 0.9f, 1.7f), true);
            Card("Hearth Flame", c + Vector3.up * 0.13f, 0.32f, Glow(new Color(1f, 0.55f, 0.18f), 0.8f, 2.9f), false);
            Card("Hearth Flame Core", c + Vector3.up * 0.1f, 0.16f, Glow(new Color(1f, 0.85f, 0.5f), 0.9f, 4.1f), false);
            var light = new GameObject("Hearth Light").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = c + Vector3.up * 0.45f;
            light.type = LightType.Point; light.range = 3.4f; light.color = new Color(1f, 0.55f, 0.24f);
            light.intensity = fire.LightBase = 1.1f;
            light.shadows = LightShadows.None; light.renderMode = LightRenderMode.ForceVertex; // per vertex: the camp's fire stays the only per-pixel light
            fire.Light = light;
            return fire;
        }

        /// <summary>Per frame: the glows breathe, the flames face the eye, the light flickers.</summary>
        public static void Tick(Fire fire, Transform cam, float t)
        {
            if (fire == null) return;
            foreach (var (m, o, ph) in fire.Glows)
                m.SetFloat(OpacityId, o * (0.82f + 0.1f * Mathf.Sin(t * 8.1f + ph) + 0.08f * Mathf.Sin(t * 13.7f + ph * 2.1f)));
            foreach (var card in fire.Cards)
                if (cam != null) card.rotation = Quaternion.LookRotation(card.position - cam.position);
            if (fire.Light != null)
                fire.Light.intensity = fire.LightBase * (0.85f + 0.1f * Mathf.Sin(t * 7.3f) + 0.06f * Mathf.Sin(t * 17.9f));
        }
    }
}
