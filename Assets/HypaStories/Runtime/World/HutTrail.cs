using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The walk from the campfire to the hut (ticket 3G), built at runtime inside the camp (see <see cref="HutTrailLayout"/>):
    ///  - a wooden signpost by the fallen log, "HUT" with its arrow pointing down the trail;
    ///  - a dirt trail through the pines, lit by a few lanterns on posts, with fireflies drifting along it;
    ///  - a round mud hut with a thatched roof, a glowing lantern by its door, smoke from the roof and an empty room inside;
    ///  - an owl on a branch that watches you pass and hoots, and a wolf pacing beside the middle of the trail: you see it
    ///    and hear it as you come around the bend; it stops and growls, then runs off into the trees and howls;
    ///  - footsteps while you walk the trail.
    /// Every mesh is made here (vertex colours on the pines' Lambert shader, with fog), every sound is synthesised
    /// (<see cref="ProceduralClips"/>). Lanterns light by glow cards and per-vertex lights, so the Quest budget holds:
    /// the fire stays the only per-pixel light.
    /// </summary>
    public sealed class HutTrail : MonoBehaviour
    {
        StoryAssets _a;
        Texture2D _glow;
        Material _vc;           // vertex-coloured, lit, fogged (the pine shader, no sway)
        System.Random _r;
        float R() => (float)_r.NextDouble();

        readonly List<(Material m, float baseOpacity, float ph)> _flicker = new List<(Material, float, float)>();
        readonly List<(Light l, float baseIntensity, float ph)> _lamps = new List<(Light, float, float)>();
        readonly List<Transform> _billboards = new List<Transform>();
        Material _smoke;
        float _t;

        // owl
        Transform _owlHead;
        Material _owlEyes;
        AudioSource _owlVoice;
        AudioClip _hoot;
        float _nextHoot, _blinkAt;

        // wolf
        enum WolfState { Pacing, Watching, Running, Gone }
        WolfState _wolfState;
        Transform _wolf, _wolfHead;
        readonly Transform[] _legs = new Transform[4];
        Material _wolfEyes;
        AudioSource _wolfVoice;
        AudioClip _growl, _howl, _snap, _rustle;
        float _wolfT;
        Vector3 _wolfHome, _runDir;
        Vector2 _paceA, _paceB;
        bool _paceToB, _heard;

        // footsteps
        AudioSource _feet;
        AudioClip[] _steps;
        Vector2 _lastStep;
        bool _stepInit;
        int _stepN;

        static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        /// <summary>The viewer's spot on the camp's ground (x, z), set each frame.</summary>
        public Vector2 Viewer { get; private set; }

        public void Build(StoryAssets a, Texture2D glow)
        {
            _a = a; _glow = glow;
            _r = new System.Random(3);
            _vc = new Material(a.pine) { name = "Trail (vertex colour)" };
            _vc.SetFloat("_Sway", 0f);

            BuildPath();
            BuildSign();
            BuildLanterns();
            BuildHut();
            BuildFireflies();
            BuildOwl();
            BuildWolf();
            BuildFootsteps();
        }

        // ---------- mesh building ----------

        sealed class MeshKit
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
            {
                var i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                C.Add(ca); C.Add(cb); C.Add(cc); C.Add(cd);
                T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col) => Quad(a, b, c, d, col, col, col, col);

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
            {
                var i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); C.Add(ca); C.Add(cb); C.Add(cc);
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
            }

            /// <summary>An axis-aligned box (centre, size), rotated by <paramref name="rot"/> about its centre.</summary>
            public void Box(Vector3 centre, Vector3 size, Quaternion rot, Color col, float shade = 0.82f)
            {
                var h = size * 0.5f;
                Vector3 P(float x, float y, float z) => centre + rot * new Vector3(x * h.x, y * h.y, z * h.z);
                var side = col * shade; side.a = 1f;
                var bottom = col * 0.6f; bottom.a = 1f;
                Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), col);         // top
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), bottom);  // bottom
                Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), side);    // -z
                Quad(P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), side);        // +z
                Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), col * 0.9f); // -x
                Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), col * 0.9f);  // +x
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(V); m.SetColors(C); m.SetTriangles(T, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }

        Transform Node(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            return go.transform;
        }

        Transform MeshNode(string name, Mesh mesh, Material mat, Transform parent = null)
        {
            var t = Node(name, parent);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return t;
        }

        Material GlowMat(Color c, float opacity)
        {
            var m = new Material(_a.additive) { mainTexture = _glow, color = c };
            m.SetFloat(OpacityId, opacity);
            return m;
        }

        /// <summary>A glow card that always faces you (lantern flames, eyes).</summary>
        Transform GlowCard(string name, Vector3 pos, float size, Material m, Transform parent = null)
        {
            var t = MeshNode(name, StoryMeshes.Quad(size, size), m, parent);
            t.localPosition = pos;
            _billboards.Add(t);
            return t;
        }

        /// <summary>A soft warm pool of light on the ground.</summary>
        void GroundPool(Vector2 at, float w, float h, Color c, float opacity, float ph)
        {
            var m = GlowMat(c, opacity);
            var t = MeshNode("Light Pool", StoryMeshes.Quad(w, h), m);
            t.localPosition = new Vector3(at.x, 0.025f, at.y);
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _flicker.Add((m, opacity, ph));
        }

        static Vector3 X0Z(Vector2 p, float y = 0f) => new Vector3(p.x, y, p.y);

        Color Jitter(Color c, float amount)
        {
            var k = 1f + (R() - 0.5f) * 2f * amount;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        // ---------- the trail ----------

        void BuildPath()
        {
            // a ribbon down the trail's centre line, smoothed through the bends, with ragged edges and grit
            var pts = new List<Vector2>(HutTrailLayout.Path) { HutTrailLayout.Door };
            var line = new List<Vector2>();
            for (var i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
                var steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 0.45f));
                for (var s = 0; s < steps; s++) line.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
            }
            line.Add(pts[pts.Count - 1]);

            var mk = new MeshKit();
            var dirt = new Color(0.17f, 0.135f, 0.1f);
            var edge = new Color(0.11f, 0.095f, 0.075f);
            Vector3 prevL = default, prevC = default, prevR = default;
            Color prevLc = default, prevCc = default, prevRc = default;
            for (var i = 0; i < line.Count; i++)
            {
                var a = line[Mathf.Max(0, i - 1)]; var b = line[Mathf.Min(line.Count - 1, i + 1)];
                var dir = (b - a).normalized;
                var side = new Vector2(-dir.y, dir.x);
                // narrower at the clearing end, so it grows out of the camp; ragged edges
                var u = i / (float)(line.Count - 1);
                var half = HutTrailLayout.PathHalf * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(u * 6f));
                var hl = half * (0.82f + R() * 0.3f); var hr = half * (0.82f + R() * 0.3f);
                var y = 0.012f;
                var L = X0Z(line[i] + side * hl, y); var C = X0Z(line[i] + side * (R() - 0.5f) * 0.15f, y + 0.004f); var Rr = X0Z(line[i] - side * hr, y);
                Color Lc = Jitter(edge, 0.15f), Cc = Jitter(dirt, 0.18f), Rc = Jitter(edge, 0.15f);
                if (i > 0)
                {
                    mk.Quad(prevL, L, C, prevC, prevLc, Lc, Cc, prevCc);
                    mk.Quad(prevC, C, Rr, prevR, prevCc, Cc, Rc, prevRc);
                }
                prevL = L; prevC = C; prevR = Rr; prevLc = Lc; prevCc = Cc; prevRc = Rc;

                // grit and pebbles along the edges, a few in the middle
                if (R() < 0.55f)
                {
                    var off = (R() < 0.7f ? (R() < 0.5f ? 1f : -1f) * (half * (0.75f + R() * 0.45f)) : (R() - 0.5f) * half);
                    var p = line[i] + side * off + dir * (R() - 0.5f) * 0.4f;
                    var s = 0.025f + R() * 0.05f;
                    mk.Box(X0Z(p, s * 0.2f), new Vector3(s, s * 0.45f, s * (0.7f + R() * 0.6f)), Quaternion.Euler(R() * 20f, R() * 360f, R() * 20f),
                        Jitter(new Color(0.22f, 0.22f, 0.21f), 0.25f));
                }
            }
            MeshNode("Trail", mk.Build("Trail"), _vc);
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            var t2 = t * t; var t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ---------- the signpost ----------

        void BuildSign()
        {
            var mk = new MeshKit();
            var wood = new Color(0.36f, 0.24f, 0.14f);
            var plank = new Color(0.45f, 0.31f, 0.18f);
            mk.Box(new Vector3(0f, 0.8f, 0f), new Vector3(0.1f, 1.6f, 0.1f), Quaternion.identity, Jitter(wood, 0.05f));    // the post
            mk.Box(new Vector3(0f, 1.62f, 0f), new Vector3(0.13f, 0.04f, 0.13f), Quaternion.identity, wood * 0.8f);       // its cap
            // the arrow board: a plank with a pointed end (pointing along local -X), nailed to the post
            const float len = 0.78f, hh = 0.13f, tip = 0.16f, th = 0.035f, y = 1.3f;
            Vector3 P(float x, float yy, float z) => new Vector3(x, y + yy, z);
            float x0 = 0.18f, x1 = x0 - len, xt = x1 - tip;
            var front = Jitter(plank, 0.04f); var back = plank * 0.8f; back.a = 1f; var rim = plank * 0.7f; rim.a = 1f;
            float zf = -0.07f - th, zb = -0.07f;
            // front and back faces (rectangle + tip), rims
            mk.Quad(P(x0, -hh, zf), P(x0, hh, zf), P(x1, hh, zf), P(x1, -hh, zf), front);
            mk.Tri(P(x1, -hh, zf), P(x1, hh, zf), P(xt, 0f, zf), front, front, front);
            mk.Quad(P(x1, -hh, zb), P(x1, hh, zb), P(x0, hh, zb), P(x0, -hh, zb), back);
            mk.Tri(P(xt, 0f, zb), P(x1, hh, zb), P(x1, -hh, zb), back, back, back);
            mk.Quad(P(x0, hh, zf), P(x0, hh, zb), P(x1, hh, zb), P(x1, hh, zf), rim);
            mk.Quad(P(x1, -hh, zf), P(x1, -hh, zb), P(x0, -hh, zb), P(x0, -hh, zf), rim);
            mk.Quad(P(x0, -hh, zb), P(x0, hh, zb), P(x0, hh, zf), P(x0, -hh, zf), rim);
            mk.Quad(P(x1, hh, zf), P(x1, hh, zb), P(xt, 0f, zb), P(xt, 0f, zf), rim);
            mk.Quad(P(xt, 0f, zf), P(xt, 0f, zb), P(x1, -hh, zb), P(x1, -hh, zf), rim);
            // two nail heads
            mk.Box(new Vector3(0f, y + 0.06f, zf - 0.004f), new Vector3(0.018f, 0.018f, 0.01f), Quaternion.identity, new Color(0.15f, 0.15f, 0.16f));
            mk.Box(new Vector3(0f, y - 0.06f, zf - 0.004f), new Vector3(0.018f, 0.018f, 0.01f), Quaternion.identity, new Color(0.15f, 0.15f, 0.16f));

            var sign = MeshNode("Signpost", mk.Build("Signpost"), _vc);
            var along = (HutTrailLayout.Path[1] - HutTrailLayout.Path[0]).normalized;
            sign.localPosition = X0Z(HutTrailLayout.Sign);
            // local -X is the arrow's tip: point it down the trail
            sign.localRotation = Quaternion.FromToRotation(Vector3.left, X0Z(along).normalized);

            // painted letters, a little pale against the wood so they read from the seat at night
            // (adding TextMeshPro swaps the Transform for a RectTransform: use the component from here on, not the old Transform)
            var tmp = Node("Sign Text", sign).gameObject.AddComponent<TextMeshPro>();
            tmp.rectTransform.localPosition = new Vector3((x0 + xt) * 0.5f - 0.02f, y, zf - 0.006f);
            tmp.rectTransform.localRotation = Quaternion.identity;
            if (_a.uiFontBold != null) tmp.font = _a.uiFontBold;
            tmp.text = "HUT";
            tmp.fontSize = 1.5f;
            tmp.characterSpacing = 18f;
            tmp.color = new Color(0.93f, 0.84f, 0.66f, 0.95f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(0.6f, 0.2f);
            tmp.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // if the painted side faces the woods, turn the letters to the side that looks back at the seat
            var toSeat = new Vector3(-HutTrailLayout.Sign.x, 0f, CampWorld.SplashSeat.z - HutTrailLayout.Sign.y);
            if (Vector3.Dot(sign.TransformDirection(Vector3.back), toSeat) < 0f)
            {
                tmp.rectTransform.localPosition = new Vector3(tmp.rectTransform.localPosition.x, tmp.rectTransform.localPosition.y, 0.07f + th + 0.006f);
                tmp.rectTransform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            // a small lantern hanging from the post, so you notice the sign from the fire
            var lanternAt = sign.TransformPoint(new Vector3(0.11f, 1.48f, 0.02f));
            var lp = transform.InverseTransformPoint(lanternAt);
            Lantern(lp, 0.75f, 1.1f);
        }

        // ---------- lanterns along the trail ----------

        /// <summary>A lantern: a dark frame, a flickering flame card and halo, and (optionally) a soft per-vertex light.</summary>
        void Lantern(Vector3 at, float scale, float ph, float lightRange = 0f)
        {
            var mk = new MeshKit();
            var frame = new Color(0.12f, 0.1f, 0.08f);
            var s = 0.13f * scale;
            mk.Box(at + new Vector3(0f, s * 0.55f, 0f), new Vector3(s * 1.1f, s * 0.12f, s * 1.1f), Quaternion.identity, frame);
            mk.Box(at - new Vector3(0f, s * 0.55f, 0f), new Vector3(s * 1.1f, s * 0.12f, s * 1.1f), Quaternion.identity, frame);
            for (var k = 0; k < 4; k++)
            {
                var o = Quaternion.Euler(0f, k * 90f + 45f, 0f) * new Vector3(s * 0.5f, 0f, 0f);
                mk.Box(at + o, new Vector3(s * 0.08f, s, s * 0.08f), Quaternion.identity, frame);
            }
            MeshNode("Lantern", mk.Build("Lantern"), _vc);
            var flame = GlowMat(new Color(1f, 0.72f, 0.32f), 0.95f);
            GlowCard("Lantern Flame", at, s * 1.1f, flame);
            _flicker.Add((flame, 0.95f, ph));
            var halo = GlowMat(new Color(1f, 0.58f, 0.22f), 0.32f);
            GlowCard("Lantern Halo", at, s * 7f, halo);
            _flicker.Add((halo, 0.32f, ph + 0.7f));
            if (lightRange > 0f)
            {
                var l = Node("Lantern Light").gameObject.AddComponent<Light>();
                l.transform.localPosition = at;
                l.type = LightType.Point; l.range = lightRange; l.color = new Color(1f, 0.66f, 0.36f); l.intensity = 1.2f;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForceVertex; // cheap: lights the hut's vertices, never adds a pass
                _lamps.Add((l, 1.2f, ph));
            }
        }

        void BuildLanterns()
        {
            float[] at = { 0.12f, 0.26f, 0.40f, 0.52f, 0.64f, 0.76f, 0.88f };
            for (var i = 0; i < at.Length; i++)
            {
                var (p, dir) = HutTrailLayout.Along(at[i]);
                var side = new Vector2(-dir.y, dir.x) * (i % 2 == 0 ? 1f : -1f);
                var post = p + side * (HutTrailLayout.PathHalf + 0.35f);
                var mk = new MeshKit();
                var wood = new Color(0.3f, 0.2f, 0.12f);
                mk.Box(X0Z(post, 0.6f), new Vector3(0.07f, 1.2f, 0.07f), Quaternion.Euler(0f, R() * 90f, (R() - 0.5f) * 6f), Jitter(wood, 0.1f));
                // an arm out over the trail
                var arm = post - side * 0.18f;
                mk.Box(X0Z(arm, 1.18f), new Vector3(0.36f, 0.04f, 0.04f), Quaternion.LookRotation(X0Z(side)) * Quaternion.Euler(0f, 90f, 0f), wood);
                MeshNode("Lantern Post", mk.Build("Lantern Post"), _vc);
                var hang = post - side * 0.32f;
                Lantern(X0Z(hang, 1.02f), 1f, i * 1.9f);
                GroundPool(hang, 2.6f, 2.6f, new Color(1f, 0.55f, 0.2f), 0.13f, i * 1.9f);
            }
        }

        // ---------- the hut ----------

        void BuildHut()
        {
            var hut = Node("Hut");
            hut.localPosition = X0Z(HutTrailLayout.Hut);
            // built with its door on local +Z, then turned to face back down the trail
            hut.localRotation = Quaternion.LookRotation(X0Z(HutTrailLayout.DoorDir));

            const int seg = 44;
            float rIn = HutTrailLayout.WallInner, rOut = HutTrailLayout.HutRadius, wallH = HutTrailLayout.WallHeight, doorH = HutTrailLayout.DoorHeight;
            var gap = HutTrailLayout.DoorHalfAngle * Mathf.Deg2Rad;
            var mud = new Color(0.47f, 0.33f, 0.22f);
            var mudDark = new Color(0.3f, 0.21f, 0.14f);
            var wall = new MeshKit();
            Vector3 At(float ang, float r, float y) => new Vector3(Mathf.Sin(ang) * r, y, Mathf.Cos(ang) * r);
            for (var i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                // the door is centred on angle 0 (+Z): split the segments that cross it
                var mid = Mathf.DeltaAngle(0f, (a0 + a1) * 0.5f * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                var inDoor = Mathf.Abs(mid) < gap;
                var y0 = inDoor ? doorH : 0f;
                Color lo = Jitter(mudDark, 0.04f), hi = Jitter(mud, 0.05f), hi2 = Jitter(mud, 0.05f);
                wall.Quad(At(a0, rOut, y0), At(a0, rOut, wallH), At(a1, rOut, wallH), At(a1, rOut, y0), inDoor ? hi : lo, hi, hi2, inDoor ? hi2 : lo); // outside
                wall.Quad(At(a1, rIn, y0), At(a1, rIn, wallH), At(a0, rIn, wallH), At(a0, rIn, y0), lo * 0.9f, hi * 0.85f, hi2 * 0.85f, lo * 0.9f);       // inside
                wall.Quad(At(a0, rIn, wallH), At(a1, rIn, wallH), At(a1, rOut, wallH), At(a0, rOut, wallH), hi * 0.8f);                                    // top
                if (inDoor) wall.Quad(At(a0, rOut, y0), At(a1, rOut, y0), At(a1, rIn, y0), At(a0, rIn, y0), mudDark);                                       // lintel's underside
            }
            // the door's jambs
            foreach (var s in new[] { -1f, 1f })
            {
                var a = s * gap;
                wall.Quad(At(a, rOut, 0f), At(a, rIn, 0f), At(a, rIn, doorH), At(a, rOut, doorH), mudDark);
                if (s < 0f) wall.Quad(At(a, rOut, doorH), At(a, rIn, doorH), At(a, rIn, 0f), At(a, rOut, 0f), mudDark);
            }
            // a door frame of rough poles and a lintel log
            var pole = new Color(0.28f, 0.18f, 0.1f);
            foreach (var s in new[] { -1f, 1f })
                wall.Box(At(s * gap * 0.97f, rOut + 0.05f, doorH * 0.5f), new Vector3(0.11f, doorH + 0.05f, 0.11f), Quaternion.Euler(0f, s * 8f, 0f), Jitter(pole, 0.08f));
            wall.Box(At(0f, rOut + 0.06f, doorH + 0.04f), new Vector3(Mathf.Sin(gap) * rOut * 2f + 0.35f, 0.12f, 0.13f), Quaternion.identity, Jitter(pole, 0.08f));
            // a low step of flat stones at the door
            wall.Box(At(0f, rOut + 0.35f, 0.03f), new Vector3(1.1f, 0.06f, 0.5f), Quaternion.Euler(0f, 4f, 0f), new Color(0.32f, 0.31f, 0.3f));
            // the floor inside: beaten earth with a woven mat
            const int fseg = 36;
            var floor = new Color(0.27f, 0.2f, 0.14f);
            for (var i = 0; i < fseg; i++)
            {
                float a0 = i / (float)fseg * Mathf.PI * 2f, a1 = (i + 1) / (float)fseg * Mathf.PI * 2f;
                wall.Tri(new Vector3(0f, 0.015f, 0f), At(a0, rIn + 0.12f, 0.015f), At(a1, rIn + 0.12f, 0.015f), floor, Jitter(floor * 0.85f, 0.1f), Jitter(floor * 0.85f, 0.1f));
            }
            MeshNode("Hut Walls", wall.Build("Hut Walls"), _vc, hut);

            // the roof: a thatched cone with an overhang, seen from outside and from within
            var roof = new MeshKit();
            const float eave = 3.2f, eaveY = 1.95f, apexY = 4.5f;
            var thatch = new Color(0.74f, 0.55f, 0.29f);
            var droops = new float[seg + 1];
            for (var i = 0; i < seg; i++) droops[i] = (R() - 0.5f) * 0.1f;
            droops[seg] = droops[0]; // neighbouring straw meets: no cracks between segments
            for (var i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                var droop0 = droops[i]; var droop1 = droops[i + 1];
                Color c0 = Jitter(thatch, 0.16f), c1 = Jitter(thatch, 0.16f), top = Jitter(thatch * 0.75f, 0.08f);
                // three bands, so the straw catches light unevenly
                for (var b = 0; b < 3; b++)
                {
                    float u0 = b / 3f, u1 = (b + 1) / 3f;
                    Vector3 A(float ang, float u, float droop) => new Vector3(Mathf.Sin(ang) * eave * (1f - u), Mathf.Lerp(eaveY + droop, apexY, u), Mathf.Cos(ang) * eave * (1f - u));
                    Color k(Color c, float u) { var x = Color.Lerp(c, top, u); x.a = 1f; return x; }
                    var d0 = b == 0 ? droop0 : 0f; var d1 = b == 0 ? droop1 : 0f;
                    roof.Quad(A(a0, u0, d0), A(a0, u1, 0f), A(a1, u1, 0f), A(a1, u0, d1), k(c0, u0), k(c0, u1), k(c1, u1), k(c1, u0));
                    roof.Quad(A(a1, u0, d1), A(a1, u1, 0f), A(a0, u1, 0f), A(a0, u0, d0), k(c1 * 0.55f, u0), k(c1 * 0.55f, u1), k(c0 * 0.55f, u1), k(c0 * 0.55f, u0));
                }
                // a ragged fringe hanging from the eave
                var fa = (a0 + a1) * 0.5f;
                var fl = 0.12f + R() * 0.12f;
                roof.Quad(new Vector3(Mathf.Sin(a0) * eave, eaveY + droop0, Mathf.Cos(a0) * eave), new Vector3(Mathf.Sin(a1) * eave, eaveY + droop1, Mathf.Cos(a1) * eave),
                    new Vector3(Mathf.Sin(fa) * eave * 1.01f, eaveY - fl, Mathf.Cos(fa) * eave * 1.01f), new Vector3(Mathf.Sin(fa) * eave * 1.01f, eaveY - fl, Mathf.Cos(fa) * eave * 1.01f),
                    c0 * 0.85f, c1 * 0.85f, c0 * 0.7f, c0 * 0.7f);
            }
            // a knot of straw at the top
            roof.Box(new Vector3(0f, apexY + 0.08f, 0f), new Vector3(0.28f, 0.22f, 0.28f), Quaternion.Euler(0f, 30f, 0f), thatch * 0.7f);
            MeshNode("Hut Roof", roof.Build("Hut Roof"), _vc, hut);

            // the lantern by the door, warm light on the walls (per vertex) and a glow on the ground
            var doorLantern = hut.TransformPoint(At(gap + 0.16f, rOut + 0.22f, 1.75f));
            Lantern(transform.InverseTransformPoint(doorLantern), 1.1f, 0.4f, 6.5f);
            GroundPool(HutTrailLayout.Door + HutTrailLayout.DoorDir * 0.8f, 3.6f, 3.6f, new Color(1f, 0.56f, 0.2f), 0.18f, 0.4f);
            // a dim glow inside, so the empty room isn't pitch black
            var inside = Node("Hut Inside Light", hut).gameObject.AddComponent<Light>();
            inside.transform.localPosition = new Vector3(0f, 1.6f, -0.3f);
            inside.type = LightType.Point; inside.range = 3.6f; inside.color = new Color(1f, 0.62f, 0.32f); inside.intensity = 0.7f;
            inside.shadows = LightShadows.None; inside.renderMode = LightRenderMode.ForceVertex;
            _lamps.Add((inside, 0.7f, 2.2f));
            GroundPool(HutTrailLayout.Hut, 3.4f, 3.4f, new Color(1f, 0.5f, 0.2f), 0.08f, 2.2f);

            // smoke curling up from the roof's peak
            _smoke = new Material(_a.particles) { name = "Hut Smoke", color = new Color(0.55f, 0.58f, 0.6f) };
            _smoke.SetFloat("_Size", 0.5f); _smoke.SetFloat("_Alpha", 0.16f); _smoke.SetFloat("_Speed", 0.22f);
            _smoke.SetFloat("_Blink", 0f); _smoke.SetFloat("_Ember", 1f); _smoke.SetFloat("_Scale", 1.6f);
            _smoke.SetVector("_EmberBase", new Vector4(HutTrailLayout.Hut.x, apexY + 0.1f, HutTrailLayout.Hut.y, 0f));
            MeshNode("Hut Smoke", StoryMeshes.Billboards(22, 23, r => Vector3.zero), _smoke);
        }

        // ---------- fireflies along the trail ----------

        void BuildFireflies()
        {
            var m = new Material(_a.particles) { name = "Trail Fireflies", color = new Color(0.78f, 1f, 0.48f) };
            m.SetFloat("_Size", 0.06f); m.SetFloat("_Alpha", 1f); m.SetFloat("_Speed", 0.8f);
            m.SetFloat("_Blink", 1f); m.SetFloat("_Ember", 0f);
            var r = new System.Random(77);
            var mesh = StoryMeshes.Billboards(34, 78, rr =>
            {
                var (p, dir) = HutTrailLayout.Along(0.1f + (float)r.NextDouble() * 0.85f);
                var side = new Vector2(-dir.y, dir.x) * (((float)r.NextDouble() - 0.5f) * 5f);
                return X0Z(p + side, 0.3f + (float)r.NextDouble() * 1.8f);
            }, 3f);
            MeshNode("Trail Fireflies", mesh, m);
        }

        // ---------- the owl ----------

        void BuildOwl()
        {
            // a dead branch out of a pine beside the trail, the owl at its end
            var (p, dir) = HutTrailLayout.Along(0.3f);
            var side = new Vector2(dir.y, -dir.x); // the right of the trail as you walk
            var trunk = p + side * 3.4f;
            var perch = p + side * 2.1f;
            var mk = new MeshKit();
            var bark = new Color(0.11f, 0.085f, 0.065f);
            mk.Box(X0Z(trunk, 2.6f), new Vector3(0.28f, 5.2f, 0.28f), Quaternion.Euler(0f, 20f, 0f), bark);
            var mid = (trunk + perch) * 0.5f;
            mk.Box(X0Z(mid, 3.15f), new Vector3(0.09f, 0.09f, Vector2.Distance(trunk, perch) + 0.3f), Quaternion.LookRotation(X0Z(perch - trunk)) * Quaternion.Euler(-8f, 0f, 0f), bark * 1.2f);
            MeshNode("Owl Branch", mk.Build("Owl Branch"), _vc);

            var owl = Node("Owl");
            owl.localPosition = X0Z(perch, 3.24f);
            var plumage = new Color(0.33f, 0.26f, 0.2f);
            var body = new MeshKit();
            body.Box(new Vector3(0f, 0.14f, 0f), new Vector3(0.2f, 0.28f, 0.17f), Quaternion.identity, plumage);
            body.Box(new Vector3(0f, 0.1f, 0.075f), new Vector3(0.14f, 0.18f, 0.04f), Quaternion.identity, new Color(0.5f, 0.42f, 0.32f)); // pale breast
            body.Box(new Vector3(-0.05f, 0f, 0.02f), new Vector3(0.04f, 0.03f, 0.06f), Quaternion.identity, new Color(0.25f, 0.22f, 0.12f)); // talons
            body.Box(new Vector3(0.05f, 0f, 0.02f), new Vector3(0.04f, 0.03f, 0.06f), Quaternion.identity, new Color(0.25f, 0.22f, 0.12f));
            MeshNode("Owl Body", body.Build("Owl Body"), _vc, owl);
            _owlHead = Node("Owl Head", owl);
            _owlHead.localPosition = new Vector3(0f, 0.34f, 0f);
            var head = new MeshKit();
            head.Box(Vector3.zero, new Vector3(0.19f, 0.15f, 0.16f), Quaternion.identity, plumage * 1.05f);
            head.Box(new Vector3(-0.06f, 0.09f, 0f), new Vector3(0.04f, 0.06f, 0.04f), Quaternion.Euler(0f, 0f, 15f), plumage * 0.9f);  // ear tufts
            head.Box(new Vector3(0.06f, 0.09f, 0f), new Vector3(0.04f, 0.06f, 0.04f), Quaternion.Euler(0f, 0f, -15f), plumage * 0.9f);
            head.Box(new Vector3(0f, 0f, 0.075f), new Vector3(0.16f, 0.11f, 0.02f), Quaternion.identity, new Color(0.55f, 0.47f, 0.37f)); // face disc
            head.Box(new Vector3(0f, -0.02f, 0.09f), new Vector3(0.02f, 0.03f, 0.02f), Quaternion.identity, new Color(0.2f, 0.18f, 0.12f)); // beak
            MeshNode("Owl Head", head.Build("Owl Head"), _vc, _owlHead);
            _owlEyes = GlowMat(new Color(1f, 0.82f, 0.32f), 0.9f);
            GlowCard("Owl Eye", new Vector3(-0.038f, 0.012f, 0.088f), 0.05f, _owlEyes, _owlHead);
            GlowCard("Owl Eye", new Vector3(0.038f, 0.012f, 0.088f), 0.05f, _owlEyes, _owlHead);
            owl.localRotation = Quaternion.LookRotation(X0Z(p - perch));

            _hoot = ProceduralClips.Clip("hoot", ProceduralClips.HootSamples(41));
            _owlVoice = Voice("Owl", _owlHead, 22f);
            _nextHoot = 3f;
            _blinkAt = 2f;
        }

        // ---------- the wolf ----------

        void BuildWolf()
        {
            _wolf = Node("Wolf");
            _wolfHome = X0Z(HutTrailLayout.WolfAt);
            _wolf.localPosition = _wolfHome;
            var fur = new Color(0.24f, 0.23f, 0.22f);
            var furLight = new Color(0.4f, 0.38f, 0.34f);
            var mk = new MeshKit();
            // body along local +Z (its head end), shoulder about 0.75 m
            mk.Box(new Vector3(0f, 0.68f, 0f), new Vector3(0.34f, 0.36f, 0.95f), Quaternion.identity, fur);                     // trunk
            mk.Box(new Vector3(0f, 0.72f, 0.36f), new Vector3(0.4f, 0.44f, 0.36f), Quaternion.Euler(-6f, 0f, 0f), fur * 1.05f);  // chest and ruff
            mk.Box(new Vector3(0f, 0.6f, 0.38f), new Vector3(0.24f, 0.2f, 0.26f), Quaternion.identity, furLight);               // pale throat
            mk.Box(new Vector3(0f, 0.7f, -0.42f), new Vector3(0.3f, 0.3f, 0.24f), Quaternion.identity, fur * 0.95f);            // haunch
            mk.Box(new Vector3(0f, 0.62f, -0.72f), new Vector3(0.1f, 0.1f, 0.48f), Quaternion.Euler(28f, 0f, 0f), fur * 0.9f); // tail, low
            MeshNode("Wolf Body", mk.Build("Wolf Body"), _vc, _wolf);

            _wolfHead = Node("Wolf Head", _wolf);
            _wolfHead.localPosition = new Vector3(0f, 0.86f, 0.58f);
            var hk = new MeshKit();
            hk.Box(Vector3.zero, new Vector3(0.22f, 0.2f, 0.22f), Quaternion.identity, fur);
            hk.Box(new Vector3(0f, -0.04f, 0.17f), new Vector3(0.11f, 0.1f, 0.2f), Quaternion.Euler(8f, 0f, 0f), furLight * 0.9f); // muzzle
            hk.Box(new Vector3(0f, -0.01f, 0.27f), new Vector3(0.05f, 0.04f, 0.03f), Quaternion.identity, new Color(0.05f, 0.05f, 0.05f)); // nose
            hk.Box(new Vector3(-0.07f, 0.14f, -0.02f), new Vector3(0.06f, 0.11f, 0.04f), Quaternion.Euler(0f, 0f, 12f), fur * 0.9f);    // ears
            hk.Box(new Vector3(0.07f, 0.14f, -0.02f), new Vector3(0.06f, 0.11f, 0.04f), Quaternion.Euler(0f, 0f, -12f), fur * 0.9f);
            MeshNode("Wolf Head", hk.Build("Wolf Head"), _vc, _wolfHead);
            _wolfEyes = GlowMat(new Color(1f, 0.78f, 0.28f), 0.95f);
            GlowCard("Wolf Eye", new Vector3(-0.055f, 0.035f, 0.115f), 0.09f, _wolfEyes, _wolfHead);
            GlowCard("Wolf Eye", new Vector3(0.055f, 0.035f, 0.115f), 0.09f, _wolfEyes, _wolfHead);

            // four legs, each pivoting at the top
            Vector3[] hips = { new Vector3(-0.11f, 0.56f, 0.32f), new Vector3(0.11f, 0.56f, 0.32f), new Vector3(-0.11f, 0.56f, -0.4f), new Vector3(0.11f, 0.56f, -0.4f) };
            for (var i = 0; i < 4; i++)
            {
                _legs[i] = Node("Wolf Leg", _wolf);
                _legs[i].localPosition = hips[i];
                var lk = new MeshKit();
                lk.Box(new Vector3(0f, -0.27f, 0f), new Vector3(0.08f, 0.56f, 0.09f), Quaternion.identity, fur * 0.9f);
                lk.Box(new Vector3(0f, -0.54f, 0.03f), new Vector3(0.09f, 0.04f, 0.12f), Quaternion.identity, fur * 0.7f); // paw
                MeshNode("Wolf Leg", lk.Build("Wolf Leg"), _vc, _legs[i]);
            }
            _growl = ProceduralClips.Clip("growl", ProceduralClips.GrowlSamples(51));
            _howl = ProceduralClips.Clip("howl", ProceduralClips.HowlSamples(52));
            _snap = ProceduralClips.Clip("snap", ProceduralClips.SnapSamples(53));
            _rustle = ProceduralClips.Clip("rustle", ProceduralClips.RustleSamples(54));
            _wolfVoice = Voice("Wolf", _wolf, 30f);
            _paceA = HutTrailLayout.WolfAt;
            var (ahead, dir) = HutTrailLayout.Along(0.58f);
            var side = new Vector2(dir.y, -dir.x);
            if (Vector2.Dot(side, _paceA - ahead) < 0f) side = -side;
            _paceB = ahead + side * 1.7f;
            _wolfHome = X0Z(_paceA);
            _wolf.localPosition = _wolfHome;
            _wolf.localRotation = Quaternion.LookRotation(X0Z(_paceB - _paceA).normalized);
            _wolfState = WolfState.Pacing;
        }

        AudioSource Voice(string name, Transform at, float maxDistance)
        {
            var s = Node("Audio " + name, at).gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 1f; s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1.5f; s.maxDistance = maxDistance;
            return s;
        }

        void BuildFootsteps()
        {
            _feet = Node("Audio Footsteps").gameObject.AddComponent<AudioSource>();
            _feet.playOnAwake = false; _feet.spatialBlend = 0f;
            _steps = new AudioClip[4];
            for (var i = 0; i < _steps.Length; i++) _steps[i] = ProceduralClips.Clip("step", ProceduralClips.StepSamples(60 + i));
        }

        // ---------- every frame ----------

        void Update()
        {
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return;
            var dt = Time.deltaTime;
            _t += dt;
            var local = transform.InverseTransformPoint(cam.position);
            Viewer = new Vector2(local.x, local.z);

            // lantern flicker
            foreach (var (m, o, ph) in _flicker)
                m.SetFloat(OpacityId, o * (0.86f + 0.08f * Mathf.Sin(_t * 8.3f + ph) + 0.06f * Mathf.Sin(_t * 13.1f + ph * 2.3f)));
            foreach (var (l, k, ph) in _lamps)
                l.intensity = k * (0.88f + 0.08f * Mathf.Sin(_t * 7.7f + ph) + 0.05f * Mathf.Sin(_t * 15.3f + ph));
            foreach (var b in _billboards)
                if (b.gameObject.activeInHierarchy) b.rotation = Quaternion.LookRotation(b.position - cam.position);

            Owl(cam);
            Wolf(cam, dt);
            Footsteps();
        }

        void Owl(Transform cam)
        {
            if (_owlHead == null) return;
            // turn the head to follow you (owls turn a long way), blink now and then, hoot when you're near
            var to = cam.position - _owlHead.position;
            var want = Quaternion.LookRotation(new Vector3(to.x, to.y * 0.5f, to.z));
            _owlHead.rotation = Quaternion.Slerp(_owlHead.rotation, want, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            var blink = _t > _blinkAt && _t < _blinkAt + 0.14f;
            if (_t > _blinkAt + 0.14f) _blinkAt = _t + 2.5f + R() * 4f;
            _owlEyes.SetFloat(OpacityId, blink ? 0f : 0.9f);
            if (_t > _nextHoot)
            {
                _nextHoot = _t + 9f + R() * 9f;
                if (to.magnitude < 16f) { _owlVoice.pitch = 0.95f + R() * 0.1f; _owlVoice.PlayOneShot(_hoot, 0.5f); }
            }
        }

        void Wolf(Transform cam, float dt)
        {
            if (_wolf == null) return;
            var here = new Vector2(_wolf.localPosition.x, _wolf.localPosition.z);
            var dist = Vector2.Distance(Viewer, here);
            switch (_wolfState)
            {
                case WolfState.Pacing:
                {
                    var goal = _paceToB ? _paceB : _paceA;
                    var to = goal - here;
                    if (to.magnitude < 0.25f) _paceToB = !_paceToB;
                    else
                    {
                        var step = to.normalized * (0.7f * dt);
                        if (step.magnitude > to.magnitude) step = to;
                        here += step;
                        _wolf.localPosition = X0Z(here);
                        _wolf.localRotation = Quaternion.Slerp(_wolf.localRotation, Quaternion.LookRotation(X0Z(to).normalized), 1f - Mathf.Exp(-4f * dt));
                    }
                    for (var i = 0; i < 4; i++)
                        _legs[i].localRotation = Quaternion.Euler((i < 2 ? 1f : -1f) * Mathf.Sin(_t * 3.2f + (i % 2) * Mathf.PI) * 22f, 0f, 0f);
                    _wolfEyes.SetFloat(OpacityId, 0.95f);
                    if (!_heard && dist < 18f && HutTrailLayout.DistanceToPath(Viewer) < 4f)
                    {
                        _heard = true;
                        _wolfVoice.PlayOneShot(_growl, 0.55f);
                    }
                    if (dist < 9f && HutTrailLayout.DistanceToPath(Viewer) < 3.2f)
                    {
                        _wolfState = WolfState.Watching; _wolfT = 0f;
                        _wolfVoice.PlayOneShot(_snap, 0.85f);
                        _wolfVoice.PlayOneShot(_growl, 0.9f);
                    }
                    break;
                }
                case WolfState.Watching:
                {
                    _wolfT += dt;
                    var to = cam.position - _wolf.position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f)
                        _wolf.rotation = Quaternion.Slerp(_wolf.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-4f * dt));
                    _wolfHead.localRotation = Quaternion.Euler(-6f + Mathf.Sin(_wolfT * 1.7f) * 4f, 0f, Mathf.Sin(_wolfT * 0.9f) * 7f);
                    _wolfEyes.SetFloat(OpacityId, 1f);
                    for (var i = 0; i < 4; i++) _legs[i].localRotation = Quaternion.identity;
                    if (_wolfT > 2.6f || dist < 3.4f)
                    {
                        _wolfState = WolfState.Running; _wolfT = 0f;
                        var away = here - Viewer;
                        if (away.sqrMagnitude < 0.04f) away = _paceB - _paceA;
                        var outward = _paceA - HutTrailLayout.Along(0.48f).point;
                        _runDir = X0Z((away.normalized + outward.normalized * 1.4f).normalized);
                        _wolfVoice.PlayOneShot(_rustle, 0.9f);
                    }
                    break;
                }
                case WolfState.Running:
                {
                    _wolfT += dt;
                    var speed = Mathf.Lerp(1.6f, 5.5f, Mathf.Clamp01(_wolfT / 0.8f));
                    _wolf.localRotation = Quaternion.Slerp(_wolf.localRotation, Quaternion.LookRotation(_runDir), 1f - Mathf.Exp(-8f * dt));
                    _wolf.localPosition += _runDir * speed * dt;
                    for (var i = 0; i < 4; i++)
                        _legs[i].localRotation = Quaternion.Euler((i < 2 ? 1f : -1f) * Mathf.Sin(_wolfT * 14f + (i % 2) * Mathf.PI) * 36f, 0f, 0f);
                    _wolfEyes.SetFloat(OpacityId, Mathf.Clamp01(1.2f - _wolfT / 1.4f));
                    if (_wolfT > 3.2f)
                    {
                        _wolf.gameObject.SetActive(false);
                        _wolfState = WolfState.Gone; _wolfT = 0f;
                    }
                    break;
                }
                case WolfState.Gone:
                    _wolfT += dt;
                    if (_wolfT > 1.6f && _wolfT - dt <= 1.6f && _howl != null)
                    {
                        var s = Voice("Distant Howl", transform, 80f);
                        s.transform.position = _wolf.position + _runDir * 18f + Vector3.up * 1.5f;
                        s.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 2800f;
                        s.clip = _howl; s.volume = 0.7f; s.Play();
                        Destroy(s.gameObject, _howl.length + 0.2f);
                    }
                    break;
            }
        }

        void Footsteps()
        {
            // a step for every ~0.7 m walked on the trail (beyond the clearing's seats) or inside the hut
            if (!_stepInit) { _lastStep = Viewer; _stepInit = true; return; }
            var moved = Vector2.Distance(Viewer, _lastStep);
            if (moved > 3f) { _lastStep = Viewer; return; } // a jump (recentre, teleport), not a walk
            if (moved < 0.7f) return;
            _lastStep = Viewer;
            var onTrail = (Viewer.magnitude > 5f && HutTrailLayout.DistanceToPath(Viewer) < HutTrailLayout.WalkHalf + 0.3f) || HutTrailLayout.InsideHut(Viewer);
            if (!onTrail) return;
            _feet.pitch = 0.9f + R() * 0.2f;
            _feet.PlayOneShot(_steps[_stepN++ % _steps.Length], 0.32f);
        }
    }
}
