using System.Collections.Generic;
using UnityEngine;
using MeshKit = HypaStories.TrailBuilder.MeshKit;

namespace HypaStories
{
    /// <summary>
    /// What makes the hut's room a village home, round its household things (<see cref="HutVisit"/>): the roof's rafters and
    /// the rings of lath under the thatch, a dark plinth and a painted band round the mud wall, a mud ledge at the back with
    /// calabashes and bowls on it (and the oil lamp, one of the household items), folded wrappers stacked on a board and cloth bundles tied in their knots,
    /// grain sacks, a carved stool, a woven basket, a rolled sleeping mat, a hoe on its peg and calabashes hanging from the
    /// roof. Nobody cooks in here (that is done outside). All made here like the rest of the camp (vertex colours on the pines'
    /// shader). The lamp's flame (<see cref="Flame"/>) goes with the lamp when it is carried.
    /// Built in the hut's own space: the centre at the origin, the door on +Z, angles round the wall measured from the door.
    /// </summary>
    public static class HutInterior
    {
        /// <summary>The parts that flicker: glow materials (with their base opacity), the fire's light and the cards that face you.</summary>
        /// <summary>How far the mud ledge comes out from the wall; the height and top of the low table a thing stands on.</summary>
        public const float LedgeDepth = 0.36f, TableHeight = 0.55f;
        public static readonly Vector2 TableSize = new Vector2(0.56f, 0.44f);

        public sealed class Fire
        {
            public readonly List<(Material m, float baseOpacity, float ph)> Glows = new List<(Material, float, float)>();
            public readonly List<Transform> Cards = new List<Transform>();
            public Light Light;
            public float LightBase;
            public bool Lit = true;
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
            Goods(mk, floorY);
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
            return null;
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
            float rOut = HutTrailLayout.WallInner - 0.01f, rIn = HutTrailLayout.WallInner - LedgeDepth;
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
            Calabash(mk, At(158f, rm, h), 0.13f, gourd);
            Calabash(mk, At(167f, rm, h), 0.1f, Shade(gourd, 0.85f));
            for (var k = 0; k < 3; k++)
                mk.Lathe(At(187f, rm, h + k * 0.035f), Id, new[] { (0.05f, 0.11f, 0.11f), (0.0f, 0.06f, 0.06f) }, Shade(new Color(0.5f, 0.3f, 0.18f), 1f - k * 0.08f), 12);
            mk.Lathe(At(176f, rm, h), Id, new[] { (0.2f, 0.04f, 0.04f), (0.17f, 0.05f, 0.05f), (0.1f, 0.09f, 0.09f), (0.02f, 0.07f, 0.07f), (0f, 0.05f, 0.05f) }, new Color(0.55f, 0.28f, 0.16f), 12);
        }

        /// <summary>A round gourd, a ridge where it was cut, a lid of the same gourd.</summary>
        static void Calabash(MeshKit mk, Vector3 at, float r, Color c)
        {
            mk.Ellipsoid(at + Vector3.up * r * 0.85f, new Vector3(r, r * 0.85f, r), Id, c, 12, 7);
            mk.Lathe(at + Vector3.up * r * 1.25f, Id, new[] { (0.035f, r * 0.62f, r * 0.62f), (0f, r * 0.74f, r * 0.74f) }, Shade(c, 0.7f), 12);
        }

        // ---------- the household goods: cloth, grain, a stool ----------

        static void Goods(MeshKit mk, float floorY)
        {
            var wall = HutTrailLayout.WallInner;
            // folded wrappers stacked on a low board, and two bundles of cloth tied up in a wrapper, by the wall
            var cloths = new[]
            {
                new Color(0.16f, 0.22f, 0.5f), new Color(0.72f, 0.18f, 0.12f), new Color(0.9f, 0.68f, 0.18f),
                new Color(0.18f, 0.45f, 0.26f), new Color(0.88f, 0.84f, 0.74f), new Color(0.5f, 0.16f, 0.38f), new Color(0.85f, 0.4f, 0.12f),
            };
            var stackAt = At(-146f, wall - 0.3f, floorY);
            var face = Quaternion.LookRotation(-new Vector3(stackAt.x, 0f, stackAt.z).normalized);
            mk.Box(stackAt + Vector3.up * 0.04f, new Vector3(0.66f, 0.08f, 0.46f), face, new Color(0.3f, 0.2f, 0.12f));
            var y = 0.08f;
            for (var i = 0; i < cloths.Length; i++)
            {
                var h = 0.055f + (i % 3) * 0.012f;
                var turn = face * Quaternion.Euler(0f, (i % 2 == 0 ? 1f : -1f) * (2f + i * 0.7f), 0f);
                var c = cloths[i];
                mk.Box(stackAt + Vector3.up * (y + h / 2f) + face * new Vector3(((i * 37) % 5 - 2) * 0.008f, 0f, 0f), new Vector3(0.56f, h, 0.38f), turn, c, 0.7f);
                // the fold's edge, a little paler: reads as cloth, not a box
                mk.Box(stackAt + Vector3.up * (y + h / 2f) + turn * new Vector3(0f, 0f, 0.19f), new Vector3(0.54f, h * 0.7f, 0.012f), turn, Shade(c, 1.25f), 0.9f);
                y += h;
            }
            foreach (var (deg, r, sz, col) in new[] { (-130f, wall - 0.32f, 0.26f, cloths[1]), (-160f, wall - 0.36f, 0.22f, cloths[0]) })
            {
                var at = At(deg, r, floorY);
                mk.Ellipsoid(at + Vector3.up * sz * 0.75f, new Vector3(sz * 1.15f, sz * 0.75f, sz), Quaternion.Euler(0f, deg, 0f), col, 12, 7);
                // the corners knotted on top, and a band of another cloth showing through
                mk.Ellipsoid(at + Vector3.up * sz * 1.5f, new Vector3(sz * 0.32f, sz * 0.22f, sz * 0.3f), Id, Shade(col, 0.85f), 8, 5);
                mk.Ellipsoid(at + Vector3.up * sz * 1.62f + Vector3.right * sz * 0.18f, new Vector3(sz * 0.12f, sz * 0.18f, sz * 0.1f), Quaternion.Euler(0f, 0f, -35f), Shade(col, 0.85f), 6, 4);
                mk.Lathe(at + Vector3.up * sz * 0.72f, Quaternion.Euler(0f, deg, 0f), new[] { (0.035f, sz * 1.17f, sz * 1.02f), (0f, sz * 1.17f, sz * 1.02f) }, cloths[2], 14);
            }
            // grain sacks, tied at the neck; one open, the grain showing
            var burlap = new Color(0.6f, 0.5f, 0.34f);
            foreach (var (deg, open) in new[] { (136f, false), (146f, true), (128f, false) })
            {
                var at = At(deg, wall - (open ? 0.42f : 0.26f), floorY);
                var sackH = open ? 0.42f : 0.58f;
                mk.Lathe(at, Quaternion.Euler(0f, deg * 3f, 0f), new[]
                {
                    (sackH, open ? 0.2f : 0.05f, open ? 0.19f : 0.05f), (sackH * 0.86f, open ? 0.22f : 0.1f, open ? 0.21f : 0.1f), (sackH * 0.6f, 0.23f, 0.21f),
                    (sackH * 0.25f, 0.24f, 0.22f), (0.02f, 0.2f, 0.19f), (0f, 0.16f, 0.15f),
                }, Shade(burlap, 0.9f + (deg % 3) * 0.05f), 14);
                if (open) mk.Ellipsoid(at + Vector3.up * sackH * 0.98f, new Vector3(0.19f, 0.05f, 0.18f), Id, new Color(0.86f, 0.74f, 0.42f), 12, 5); // grain
                else
                {
                    mk.Lathe(at + Vector3.up * sackH * 0.86f, Id, new[] { (0.03f, 0.1f, 0.1f), (0f, 0.1f, 0.1f) }, new Color(0.3f, 0.2f, 0.12f), 10); // the cord
                    mk.Ellipsoid(at + Vector3.up * (sackH + 0.04f), new Vector3(0.07f, 0.06f, 0.07f), Id, burlap, 8, 5);                             // the tuft
                }
            }
            // a low carved table for every household thing that stands on one (the lamp), out in the room where it is seen
            for (var i = 0; i < HutItems.Entries.Length; i++)
            {
                var (spot, baseY) = HutItems.Place(i);
                if (baseY <= 0f || spot.magnitude > HutTrailLayout.WallInner - LedgeDepth - 0.1f) continue;
                Table(mk, new Vector3(spot.x, floorY, spot.y), Quaternion.LookRotation(new Vector3(-spot.x, 0f, -spot.y).normalized), baseY);
            }
            // a carved stool on her other side, a small bowl on it
            var stool = At(-150f, 1.3f, floorY);
            var dark = new Color(0.27f, 0.17f, 0.09f);
            mk.Lathe(stool, Id, new[] { (0.3f, 0.17f, 0.17f), (0.27f, 0.17f, 0.17f), (0.22f, 0.07f, 0.07f), (0.1f, 0.06f, 0.06f), (0.04f, 0.13f, 0.13f), (0f, 0.14f, 0.14f) }, dark, 14);
            mk.Lathe(stool + Vector3.up * 0.3f, Id, new[] { (0.05f, 0.09f, 0.09f), (0f, 0.05f, 0.05f) }, new Color(0.7f, 0.52f, 0.28f), 12);
        }

        /// <summary>A low table of dark wood: a thick top with a lip, four turned legs with a stretcher between them.</summary>
        static void Table(MeshKit mk, Vector3 at, Quaternion facing, float height)
        {
            var wood = new Color(0.3f, 0.19f, 0.1f);
            float w = TableSize.x, d = TableSize.y;
            mk.Box(at + Vector3.up * (height - 0.025f), new Vector3(w, 0.05f, d), facing, Shade(wood, 1.15f));
            mk.Box(at + Vector3.up * (height - 0.065f), new Vector3(w - 0.04f, 0.03f, d - 0.04f), facing, Shade(wood, 0.8f)); // the apron
            foreach (var kx in new[] { -1f, 1f })
                foreach (var kz in new[] { -1f, 1f })
                {
                    var foot = at + facing * new Vector3(kx * (w / 2f - 0.05f), 0f, kz * (d / 2f - 0.05f));
                    mk.Limb(foot + Vector3.up * 0.01f, foot + Vector3.up * (height - 0.06f), 0.026f, 0.03f, wood, 8);
                }
            foreach (var kz in new[] { -1f, 1f })
                mk.Beam(at + facing * new Vector3(-(w / 2f - 0.05f), 0.14f, kz * (d / 2f - 0.05f)), at + facing * new Vector3(w / 2f - 0.05f, 0.14f, kz * (d / 2f - 0.05f)), 0.025f, Shade(wood, 0.9f));
        }

        // ---------- things of the house round the wall ----------

        static void Things(MeshKit mk, float floorY)
        {
            var wall = HutTrailLayout.WallInner;
            // a woven basket on the floor
            var straw = new Color(0.7f, 0.55f, 0.3f);
            var b = At(-124f, wall - 0.35f, floorY);
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

        // ---------- a flame (the oil lamp's): a small flame and its halo that face you, and a warm light on the walls ----------

        /// <summary>
        /// A flame under <paramref name="parent"/> at <paramref name="local"/> (the lamp's wick), so it goes wherever the lamp is
        /// carried. <paramref name="unscale"/> undoes the parent's scale, so the flame keeps its size. Its light is per vertex: the
        /// camp's fire stays the only per-pixel light.
        /// </summary>
        public static Fire Flame(Transform parent, StoryAssets a, Vector3 local, float unscale)
        {
            var fire = new Fire();
            if (a == null || a.additive == null) return fire;
            var glowTex = StoryMeshes.Glow();
            Material Glow(Color col, float o, float ph)
            {
                var m = new Material(a.additive) { mainTexture = glowTex, color = col, name = "Lamp Glow" };
                m.SetFloat(OpacityId, o);
                fire.Glows.Add((m, o, ph));
                return m;
            }
            void Card(string name, float up, float size, Material m)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = local + Vector3.up * up * unscale;
                go.transform.localScale = Vector3.one * unscale;
                go.AddComponent<MeshFilter>().sharedMesh = StoryMeshes.Quad(size, size);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                fire.Cards.Add(go.transform);
            }
            Card("Lamp Halo", 0.02f, 0.45f, Glow(new Color(1f, 0.55f, 0.2f), 0.35f, 0.7f));
            Card("Lamp Flame", 0.018f, 0.06f, Glow(new Color(1f, 0.8f, 0.45f), 1f, 2.3f));
            var light = new GameObject("Lamp Light").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = local + Vector3.up * 0.12f * unscale;
            light.type = LightType.Point; light.range = 3.2f; light.color = new Color(1f, 0.6f, 0.3f);
            light.intensity = fire.LightBase = 0.9f;
            light.shadows = LightShadows.None; light.renderMode = LightRenderMode.ForceVertex;
            fire.Light = light;
            return fire;
        }

        /// <summary>Puts a flame out (the lamp tipped over, the oil drowned it) or lights it again.</summary>
        public static void SetLit(Fire fire, bool lit)
        {
            if (fire == null) return;
            fire.Lit = lit;
            foreach (var c in fire.Cards) c.gameObject.SetActive(lit);
            if (fire.Light != null) fire.Light.enabled = lit;
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
