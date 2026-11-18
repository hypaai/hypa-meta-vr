using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// What the trails share (the hut's, 3G, and the village's): meshes made from boxes and quads with vertex colours (the
    /// pines' Lambert shader, with fog), glow cards that face you, flickering lanterns, a dirt trail ribbon, a wooden
    /// signpost with an arrow, a round mud hut with a thatched roof, spatial voices and footsteps. Lanterns light by glow
    /// cards and per-vertex lights, so the Quest budget holds.
    /// </summary>
    public abstract class TrailBuilder : MonoBehaviour
    {
        protected StoryAssets _a;
        protected Texture2D _glow;
        protected Material _vc;           // vertex-coloured, lit, fogged (the pine shader, no sway)
        protected System.Random _r;
        protected float R() => (float)_r.NextDouble();
        protected float _t;

        protected readonly List<(Material m, float baseOpacity, float ph)> _flicker = new List<(Material, float, float)>();
        protected readonly List<(Light l, float baseIntensity, float ph)> _lamps = new List<(Light, float, float)>();
        protected readonly List<Transform> _billboards = new List<Transform>();

        // footsteps
        AudioSource _feet;
        AudioClip[] _steps;
        Vector2 _lastStep;
        bool _stepInit;
        int _stepN;

        protected static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        /// <summary>The viewer's spot on the camp's ground (x, z), set each frame.</summary>
        public Vector2 Viewer { get; private set; }

        /// <summary>While the portal in your room shows this place: measure from the far side of the doorway (camp ground x, z) instead of your head (3L).</summary>
        public Vector2? PreviewFrom;

        protected void Init(StoryAssets a, Texture2D glow, int seed, string name)
        {
            _a = a; _glow = glow;
            _r = new System.Random(seed);
            _vc = new Material(a.pine) { name = name + " (vertex colour)" };
            _vc.SetFloat("_Sway", 0f);
        }

        /// <summary>Where you are on the camp's ground, and whether footsteps sound there (the trail, the hut, the plaza).</summary>
        protected abstract bool OnGround(Vector2 viewer);

        /// <summary>Per frame: the viewer, lantern flicker, cards facing you, footsteps. Returns the camera (null if none).</summary>
        protected Transform Tick()
        {
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return null;
            _t += Time.deltaTime;
            var local = transform.InverseTransformPoint(cam.position);
            Viewer = PreviewFrom ?? new Vector2(local.x, local.z);
            foreach (var (m, o, ph) in _flicker)
                m.SetFloat(OpacityId, o * (0.86f + 0.08f * Mathf.Sin(_t * 8.3f + ph) + 0.06f * Mathf.Sin(_t * 13.1f + ph * 2.3f)));
            foreach (var (l, k, ph) in _lamps)
                l.intensity = k * (0.88f + 0.08f * Mathf.Sin(_t * 7.7f + ph) + 0.05f * Mathf.Sin(_t * 15.3f + ph));
            foreach (var b in _billboards)
                if (b.gameObject.activeInHierarchy) b.rotation = Quaternion.LookRotation(b.position - cam.position);
            Footsteps();
            return cam;
        }

        // ---------- mesh building ----------

        public sealed class MeshKit
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();
            /// <summary>Texture coordinates: (0, 0), the pattern atlas' plain white corner, unless a shape says otherwise.</summary>
            public readonly List<Vector2> U = new List<Vector2>();

            /// <summary>Where untextured shapes sample the pattern atlas: its plain white tile.</summary>
            public static readonly Vector2 Plain = new Vector2(0.02f, 0.02f);

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
            {
                var i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                U.Add(Plain); U.Add(Plain); U.Add(Plain); U.Add(Plain);
                C.Add(ca); C.Add(cb); C.Add(cc); C.Add(cd);
                T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col) => Quad(a, b, c, d, col, col, col, col);

            /// <summary>A quad mapped across one print tile, drawn from both sides (a hanging cloth).</summary>
            public void Cloth(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect tile, Color col)
            {
                void Face(Vector3 p, Vector3 q, Vector3 r, Vector3 s, Color tint)
                {
                    var i = V.Count;
                    V.Add(p); V.Add(q); V.Add(r); V.Add(s);
                    C.Add(tint); C.Add(tint); C.Add(tint); C.Add(tint);
                    U.Add(new Vector2(tile.xMin, tile.yMax));
                    U.Add(new Vector2(tile.xMax, tile.yMax));
                    U.Add(new Vector2(tile.xMax, tile.yMin));
                    U.Add(new Vector2(tile.xMin, tile.yMin));
                    T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
                }
                Face(a, b, c, d, col);
                var back = col * 0.75f; back.a = 1f;
                Face(d, c, b, a, back);
            }

            /// <summary>A quad seen from both sides (cloth, leaves, wings).</summary>
            public void Quad2(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
            {
                Quad(a, b, c, d, col);
                var back = col * 0.75f; back.a = 1f;
                Quad(d, c, b, a, back);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
            {
                var i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); C.Add(ca); C.Add(cb); C.Add(cc);
                U.Add(Plain); U.Add(Plain); U.Add(Plain);
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

            /// <summary>A box from <paramref name="from"/> to <paramref name="to"/> (a pole, a log, a stick), <paramref name="w"/> thick.</summary>
            public void Beam(Vector3 from, Vector3 to, float w, Color col) =>
                Box((from + to) * 0.5f, new Vector3(w, w, (to - from).magnitude), Quaternion.LookRotation((to - from).normalized, Mathf.Abs((to - from).normalized.y) > 0.95f ? Vector3.forward : Vector3.up), col);

            /// <summary>A short upright cylinder (a drum, a pot, a stool), <paramref name="seg"/> sides.</summary>
            public void Cylinder(Vector3 bottom, float r0, float r1, float h, Color side, Color top, int seg = 10)
            {
                for (var i = 0; i < seg; i++)
                {
                    float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                    Vector3 P(float a, float r, float y) => bottom + new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
                    var s = side * (0.8f + 0.2f * Mathf.Cos(a0)); s.a = 1f;
                    Quad(P(a0, r0, 0f), P(a0, r1, h), P(a1, r1, h), P(a1, r0, 0f), s);
                    Tri(bottom + Vector3.up * h, P(a1, r1, h), P(a0, r1, h), top, top, top);
                }
            }

            // ---------- smooth shapes (shared vertices, so they shade round): people, animals ----------

            /// <summary>
            /// A smooth grid of rows x cols vertices round a shape; <paramref name="at"/> gives each one's position and its
            /// place (0..1, 0..1) on the texture tile <paramref name="tile"/> (null: plain). Rows run top to bottom, columns
            /// round the way that keeps the outside facing out.
            /// </summary>
            public void Grid(int rows, int cols, System.Func<int, int, Vector3> at, Color col, Rect? tile = null)
            {
                var start = V.Count;
                for (var i = 0; i <= rows; i++)
                    for (var j = 0; j <= cols; j++)
                    {
                        V.Add(at(i, j)); C.Add(col);
                        U.Add(tile is Rect r ? new Vector2(r.x + r.width * j / cols, r.y + r.height * (1f - i / (float)rows)) : Plain);
                    }
                for (var i = 0; i < rows; i++)
                    for (var j = 0; j < cols; j++)
                    {
                        int a = start + i * (cols + 1) + j, b = a + 1, c = a + cols + 1, d = c + 1;
                        T.Add(a); T.Add(b); T.Add(c);
                        T.Add(b); T.Add(d); T.Add(c);
                    }
            }

            /// <summary>
            /// An ellipsoid (a head, a shoulder, a hand) with radii <paramref name="r"/>, turned by <paramref name="rot"/>.
            /// <paramref name="capDegrees"/> below 180 keeps only the top (a cap of hair, a head wrap).
            /// </summary>
            public void Ellipsoid(Vector3 c, Vector3 r, Quaternion rot, Color col, int seg = 10, int rings = 7, Rect? tile = null, float capDegrees = 180f)
            {
                var cap = capDegrees * Mathf.Deg2Rad;
                Grid(rings, seg, (i, j) =>
                {
                    float th = cap * i / rings, ph = Mathf.PI * 2f * j / seg;
                    return c + rot * new Vector3(Mathf.Sin(th) * Mathf.Cos(ph) * r.x, Mathf.Cos(th) * r.y, Mathf.Sin(th) * Mathf.Sin(ph) * r.z);
                }, col, tile);
            }

            /// <summary>
            /// A smooth body of revolution about local up: <paramref name="profile"/> gives (height, half-width, half-depth) from
            /// top to bottom (a torso, a wrapper, a robe), at <paramref name="origin"/>, turned by <paramref name="rot"/>.
            /// </summary>
            public void Lathe(Vector3 origin, Quaternion rot, (float y, float rx, float rz)[] profile, Color col, int seg = 12, Rect? tile = null)
            {
                Grid(profile.Length - 1, seg, (i, j) =>
                {
                    var (y, rx, rz) = profile[i];
                    var ph = Mathf.PI * 2f * j / seg;
                    return origin + rot * new Vector3(Mathf.Cos(ph) * rx, y, Mathf.Sin(ph) * rz);
                }, col, tile);
            }

            /// <summary>A tapered round limb from <paramref name="a"/> (radius <paramref name="ra"/>) to <paramref name="b"/> (radius <paramref name="rb"/>), round at both ends.</summary>
            public void Limb(Vector3 a, Vector3 b, float ra, float rb, Color col, int seg = 8, Rect? tile = null, bool roundEnds = true)
            {
                var d = b - a;
                var rot = Quaternion.FromToRotation(Vector3.up, d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.up);
                var len = d.magnitude;
                Lathe(a, rot, new[] { (len, rb, rb), (len * 0.5f, (ra + rb) * 0.5f, (ra + rb) * 0.5f), (0f, ra, ra) }, col, seg, tile);
                if (!roundEnds) return;
                Ellipsoid(a, Vector3.one * ra, rot, col, seg, 4, tile);
                Ellipsoid(b, Vector3.one * rb, rot, col, seg, 4, tile);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(V); m.SetColors(C); m.SetTriangles(T, 0);
                if (U.Count == V.Count) m.SetUVs(0, U);
                m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }

        protected Transform Node(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            return go.transform;
        }

        protected Transform MeshNode(string name, Mesh mesh, Material mat, Transform parent = null)
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

        protected Material GlowMat(Color c, float opacity)
        {
            var m = new Material(_a.additive) { mainTexture = _glow, color = c };
            m.SetFloat(OpacityId, opacity);
            return m;
        }

        /// <summary>A glow card that always faces you (lantern flames, eyes).</summary>
        protected Transform GlowCard(string name, Vector3 pos, float size, Material m, Transform parent = null)
        {
            var t = MeshNode(name, StoryMeshes.Quad(size, size), m, parent);
            t.localPosition = pos;
            _billboards.Add(t);
            return t;
        }

        /// <summary>A soft pool of light on the ground.</summary>
        protected void GroundPool(Vector2 at, float w, float h, Color c, float opacity, float ph, Transform parent = null)
        {
            var m = GlowMat(c, opacity);
            var t = MeshNode("Light Pool", StoryMeshes.Quad(w, h), m, parent);
            t.position = transform.TransformPoint(new Vector3(at.x, 0.025f, at.y));
            t.rotation = transform.rotation * Quaternion.Euler(90f, 0f, 0f);
            _flicker.Add((m, opacity, ph));
        }

        protected static Vector3 X0Z(Vector2 p, float y = 0f) => new Vector3(p.x, y, p.y);

        protected Color Jitter(Color c, float amount)
        {
            var k = 1f + (R() - 0.5f) * 2f * amount;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        protected static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            var t2 = t * t; var t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        protected AudioSource Voice(string name, Transform at, float maxDistance)
        {
            var s = Node("Audio " + name, at).gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 1f; s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1.5f; s.maxDistance = maxDistance;
            return s;
        }

        /// <summary>A looping 3D sound bed (a stream, drums, a crowd) at <paramref name="at"/> on the camp's ground.</summary>
        protected AudioSource Bed(string name, AudioClip clip, Vector3 at, float volume, float minDistance, float maxDistance)
        {
            var s = Node("Audio " + name).gameObject.AddComponent<AudioSource>();
            s.transform.localPosition = at;
            s.clip = clip; s.loop = true; s.volume = volume; s.playOnAwake = false;
            s.spatialBlend = 1f; s.dopplerLevel = 0f; s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = minDistance; s.maxDistance = maxDistance;
            s.time = R() * clip.length * 0.9f;
            s.Play();
            return s;
        }

        // ---------- the trail ----------

        /// <summary>A dirt ribbon through <paramref name="pts"/>, smoothed through the bends, with ragged edges and grit.</summary>
        protected void Ribbon(string name, IList<Vector2> pts, float pathHalf, Color dirt, Color edge)
        {
            var line = new List<Vector2>();
            for (var i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
                var steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 0.45f));
                for (var s = 0; s < steps; s++) line.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
            }
            line.Add(pts[pts.Count - 1]);

            var mk = new MeshKit();
            Vector3 prevL = default, prevC = default, prevR = default;
            Color prevLc = default, prevCc = default, prevRc = default;
            for (var i = 0; i < line.Count; i++)
            {
                var a = line[Mathf.Max(0, i - 1)]; var b = line[Mathf.Min(line.Count - 1, i + 1)];
                var dir = (b - a).normalized;
                var side = new Vector2(-dir.y, dir.x);
                // narrower at the clearing end, so it grows out of the camp; ragged edges
                var u = i / (float)(line.Count - 1);
                var half = pathHalf * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(u * 6f));
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
            MeshNode(name, mk.Build(name), _vc);
        }

        // ---------- the ground along a trail: grass, fallen leaves, low mist ----------

        /// <summary>
        /// Grass tufts and fallen leaves along the trail's edges (and a few leaves on it), all in one mesh, so it costs one draw.
        /// <paramref name="skip"/> keeps them off places (a hut's floor, the stream).
        /// </summary>
        protected void Undergrowth(string name, System.Func<float, (Vector2 point, Vector2 dir)> along, float pathHalf, System.Func<Vector2, bool> skip, int tufts = 150, int leaves = 520)
        {
            var mk = new MeshKit();
            Color[] greens = { new Color(0.16f, 0.24f, 0.1f), new Color(0.2f, 0.27f, 0.11f), new Color(0.24f, 0.25f, 0.12f), new Color(0.28f, 0.26f, 0.14f) };
            Color[] browns = { new Color(0.36f, 0.2f, 0.09f), new Color(0.45f, 0.3f, 0.12f), new Color(0.3f, 0.17f, 0.08f), new Color(0.5f, 0.36f, 0.16f), new Color(0.25f, 0.2f, 0.12f) };
            Vector2 Spot(float minOff, float maxOff, out Vector2 dir)
            {
                var (p, d) = along(0.03f + R() * 0.94f);
                dir = d;
                var side = new Vector2(-d.y, d.x) * (R() < 0.5f ? 1f : -1f);
                return p + side * Mathf.Lerp(minOff, maxOff, R() * R()) + d * (R() - 0.5f) * 0.8f;
            }
            for (var i = 0; i < tufts; i++)
            {
                var at = Spot(pathHalf * 0.85f, pathHalf + 2.2f, out _);
                if (skip != null && skip(at)) continue;
                var blades = 5 + (int)(R() * 5f);
                var h0 = 0.18f + R() * 0.22f;
                for (var b = 0; b < blades; b++)
                {
                    var a = R() * Mathf.PI * 2f;
                    var lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.05f + R() * 0.1f);
                    var foot = X0Z(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R() * 0.07f, 0f);
                    var across = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.012f;
                    var h = h0 * (0.6f + R() * 0.5f);
                    var g = greens[(int)(R() * greens.Length) % greens.Length];
                    var tip = g * 1.35f; tip.a = 1f;
                    var root = g * 0.55f; root.a = 1f;
                    mk.Tri(foot - across, foot + Vector3.up * h + lean, foot + across, root, tip, root);
                    mk.Tri(foot + across, foot + Vector3.up * h + lean, foot - across, root, tip, root); // both sides
                }
            }
            for (var i = 0; i < leaves; i++)
            {
                var onPath = i % 6 == 0;
                var at = onPath ? Spot(0f, pathHalf * 0.9f, out _) : Spot(pathHalf * 0.7f, pathHalf + 2.6f, out _);
                if (skip != null && skip(at)) continue;
                var c = browns[(int)(R() * browns.Length) % browns.Length];
                var rot = Quaternion.Euler((R() - 0.5f) * 30f, R() * 360f, (R() - 0.5f) * 30f);
                var l = 0.05f + R() * 0.05f; var w = l * (0.35f + R() * 0.25f);
                var o = X0Z(at, 0.02f + R() * 0.01f);
                // a leaf: two triangles
                Vector3 tipF = o + rot * new Vector3(0f, 0f, l), tipB = o + rot * new Vector3(0f, 0f, -l);
                mk.Tri(tipB, o + rot * new Vector3(-w, 0f, 0f), tipF, c, c * 0.9f, c);
                mk.Tri(tipB, tipF, o + rot * new Vector3(w, 0f, 0f), c * 0.95f, c, c * 0.85f);
                if (i % 23 == 0) mk.Beam(o, o + rot * new Vector3(0f, 0.01f, 0.25f + R() * 0.2f), 0.012f, new Color(0.22f, 0.15f, 0.09f)); // a twig
            }
            MeshNode(name, mk.Build(name), _vc);
        }

        /// <summary>Soft low mist lying between the trees beside the trail, a few banks of it, never on the trail itself.</summary>
        protected void TrailMist(System.Func<float, (Vector2 point, Vector2 dir)> along, int banks, float pathHalf)
        {
            for (var i = 0; i < banks; i++)
            {
                var u = (i + 0.5f) / banks;
                var (p, d) = along(u);
                var side = new Vector2(-d.y, d.x) * (i % 2 == 0 ? 1f : -1f);
                var at = p + side * (pathHalf + 2.6f + R() * 1.6f);
                var m = new Material(_a.mist) { color = new Color(0.36f, 0.5f, 0.56f) };
                m.SetFloat("_Alpha", 0.13f); m.SetFloat("_Seed", 3f + i * 1.7f);
                var t = MeshNode("Trail Mist", StoryMeshes.Quad(7f + R() * 3f, 1.3f), m);
                t.localPosition = X0Z(at, 0.5f);
                t.localRotation = Quaternion.LookRotation(X0Z(side)); // faces the trail, lying along it
            }
        }

        // ---------- the signpost ----------

        /// <summary>
        /// A wooden post at <paramref name="at"/> with an arrow board pointing along <paramref name="along"/>, painted
        /// <paramref name="text"/> on the side you see from the seat, and a small lantern hanging from it.
        /// </summary>
        protected Transform Signpost(string text, Vector2 at, Vector2 along, float boardLength, float fontSize, float letterSpacing = 18f)
        {
            var mk = new MeshKit();
            var wood = new Color(0.36f, 0.24f, 0.14f);
            var plank = new Color(0.45f, 0.31f, 0.18f);
            mk.Box(new Vector3(0f, 0.8f, 0f), new Vector3(0.1f, 1.6f, 0.1f), Quaternion.identity, Jitter(wood, 0.05f));    // the post
            mk.Box(new Vector3(0f, 1.62f, 0f), new Vector3(0.13f, 0.04f, 0.13f), Quaternion.identity, wood * 0.8f);       // its cap
            // the arrow board: a plank with a pointed end (pointing along local -X), nailed to the post
            float len = boardLength; const float hh = 0.13f, tip = 0.16f, th = 0.035f, y = 1.3f;
            Vector3 P(float x, float yy, float z) => new Vector3(x, y + yy, z);
            float x0 = 0.18f, x1 = x0 - len, xt = x1 - tip;
            var front = Jitter(plank, 0.04f); var back = plank * 0.8f; back.a = 1f; var rim = plank * 0.7f; rim.a = 1f;
            float zf = -0.07f - th, zb = -0.07f;
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
            sign.localPosition = X0Z(at);
            // local -X is the arrow's tip: point it down the trail
            sign.localRotation = Quaternion.FromToRotation(Vector3.left, X0Z(along).normalized);

            // painted letters, a little pale against the wood so they read from the seat at night
            // (adding TextMeshPro swaps the Transform for a RectTransform: use the component from here on, not the old Transform)
            var tmp = Node("Sign Text", sign).gameObject.AddComponent<TextMeshPro>();
            tmp.rectTransform.localPosition = new Vector3((x0 + xt) * 0.5f - 0.02f, y, zf - 0.006f);
            tmp.rectTransform.localRotation = Quaternion.identity;
            if (_a.uiFontBold != null) tmp.font = _a.uiFontBold;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.characterSpacing = letterSpacing;
            tmp.color = new Color(0.93f, 0.84f, 0.66f, 0.95f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(len, 0.2f);
            tmp.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // if the painted side faces the woods, turn the letters to the side that looks back at the seat
            var toSeat = new Vector3(-at.x, 0f, CampWorld.SplashSeat.z - at.y);
            if (Vector3.Dot(sign.TransformDirection(Vector3.back), toSeat) < 0f)
            {
                tmp.rectTransform.localPosition = new Vector3(tmp.rectTransform.localPosition.x, tmp.rectTransform.localPosition.y, 0.07f + th + 0.006f);
                tmp.rectTransform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            // a small lantern hanging from the post, so you notice the sign from the fire
            var lanternAt = sign.TransformPoint(new Vector3(0.11f, 1.48f, 0.02f));
            Lantern(transform.InverseTransformPoint(lanternAt), 0.75f, 1.1f);
            return sign;
        }

        // ---------- lanterns ----------

        /// <summary>A lantern: a dark frame, a flickering flame card and halo, and (optionally) a soft per-vertex light.</summary>
        protected Light Lantern(Vector3 at, float scale, float ph, float lightRange = 0f, Transform parent = null)
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
            MeshNode("Lantern", mk.Build("Lantern"), _vc, parent);
            var flame = GlowMat(new Color(1f, 0.72f, 0.32f), 0.95f);
            GlowCard("Lantern Flame", at, s * 1.1f, flame, parent);
            _flicker.Add((flame, 0.95f, ph));
            var halo = GlowMat(new Color(1f, 0.58f, 0.22f), 0.32f);
            GlowCard("Lantern Halo", at, s * 7f, halo, parent);
            _flicker.Add((halo, 0.32f, ph + 0.7f));
            return lightRange > 0f ? VertexLight(at, lightRange, 1.2f, new Color(1f, 0.66f, 0.36f), ph, parent) : null;
        }

        /// <summary>A cheap point light: lights vertices only, never adds a pass.</summary>
        protected Light VertexLight(Vector3 at, float range, float intensity, Color c, float ph, Transform parent = null)
        {
            var l = Node("Lantern Light", parent).gameObject.AddComponent<Light>();
            l.transform.localPosition = at;
            l.type = LightType.Point; l.range = range; l.color = c; l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForceVertex;
            _lamps.Add((l, intensity, ph));
            return l;
        }

        /// <summary>Lanterns on posts at <paramref name="at"/> (0..1 along the trail), hanging over it, each with a pool of light.</summary>
        protected void LanternPosts(float[] at, System.Func<float, (Vector2 point, Vector2 dir)> along, float pathHalf)
        {
            for (var i = 0; i < at.Length; i++)
            {
                var (p, dir) = along(at[i]);
                var side = new Vector2(-dir.y, dir.x) * (i % 2 == 0 ? 1f : -1f);
                var post = p + side * (pathHalf + 0.35f);
                var mk = new MeshKit();
                var wood = new Color(0.3f, 0.2f, 0.12f);
                mk.Box(X0Z(post, 0.6f), new Vector3(0.07f, 1.2f, 0.07f), Quaternion.Euler(0f, R() * 90f, (R() - 0.5f) * 6f), Jitter(wood, 0.1f));
                var arm = post - side * 0.18f;
                mk.Box(X0Z(arm, 1.18f), new Vector3(0.36f, 0.04f, 0.04f), Quaternion.LookRotation(X0Z(side)) * Quaternion.Euler(0f, 90f, 0f), wood);
                MeshNode("Lantern Post", mk.Build("Lantern Post"), _vc);
                var hang = post - side * 0.32f;
                Lantern(X0Z(hang, 1.02f), 1f, i * 1.9f);
                GroundPool(hang, 2.6f, 2.6f, new Color(1f, 0.55f, 0.2f), 0.13f, i * 1.9f);
            }
        }

        // ---------- a round mud hut ----------

        public struct HutShape
        {
            public float RIn, ROut, WallH, DoorH, DoorHalfAngle, Eave, EaveY, ApexY;
            public Color Mud, MudDark, Thatch;
            public bool Floor;
        }

        /// <summary>A round mud hut under <paramref name="hut"/>, its door on local +Z: walls, a pole door frame, a step, a thatched cone roof.</summary>
        protected void RoundHut(Transform hut, HutShape s, int seg = 44)
        {
            float rIn = s.RIn, rOut = s.ROut, wallH = s.WallH, doorH = s.DoorH;
            var gap = s.DoorHalfAngle * Mathf.Deg2Rad;
            var wall = new MeshKit();
            Vector3 At(float ang, float r, float y) => new Vector3(Mathf.Sin(ang) * r, y, Mathf.Cos(ang) * r);
            for (var i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                var mid = Mathf.DeltaAngle(0f, (a0 + a1) * 0.5f * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                var inDoor = Mathf.Abs(mid) < gap;
                var y0 = inDoor ? doorH : 0f;
                Color lo = Jitter(s.MudDark, 0.04f), hi = Jitter(s.Mud, 0.05f), hi2 = Jitter(s.Mud, 0.05f);
                wall.Quad(At(a0, rOut, y0), At(a0, rOut, wallH), At(a1, rOut, wallH), At(a1, rOut, y0), inDoor ? hi : lo, hi, hi2, inDoor ? hi2 : lo); // outside
                wall.Quad(At(a1, rIn, y0), At(a1, rIn, wallH), At(a0, rIn, wallH), At(a0, rIn, y0), lo * 0.9f, hi * 0.85f, hi2 * 0.85f, lo * 0.9f);       // inside
                wall.Quad(At(a0, rIn, wallH), At(a1, rIn, wallH), At(a1, rOut, wallH), At(a0, rOut, wallH), hi * 0.8f);                                    // top
                if (inDoor) wall.Quad(At(a0, rOut, y0), At(a1, rOut, y0), At(a1, rIn, y0), At(a0, rIn, y0), s.MudDark);                                     // lintel's underside
            }
            foreach (var k in new[] { -1f, 1f })
            {
                var a = k * gap;
                wall.Quad(At(a, rOut, 0f), At(a, rIn, 0f), At(a, rIn, doorH), At(a, rOut, doorH), s.MudDark);
                if (k < 0f) wall.Quad(At(a, rOut, doorH), At(a, rIn, doorH), At(a, rIn, 0f), At(a, rOut, 0f), s.MudDark);
            }
            var pole = new Color(0.28f, 0.18f, 0.1f);
            foreach (var k in new[] { -1f, 1f })
                wall.Box(At(k * gap * 0.97f, rOut + 0.05f, doorH * 0.5f), new Vector3(0.11f, doorH + 0.05f, 0.11f), Quaternion.Euler(0f, k * 8f, 0f), Jitter(pole, 0.08f));
            wall.Box(At(0f, rOut + 0.06f, doorH + 0.04f), new Vector3(Mathf.Sin(gap) * rOut * 2f + 0.35f, 0.12f, 0.13f), Quaternion.identity, Jitter(pole, 0.08f));
            wall.Box(At(0f, rOut + 0.35f, 0.03f), new Vector3(1.1f, 0.06f, 0.5f), Quaternion.Euler(0f, 4f, 0f), new Color(0.32f, 0.31f, 0.3f));
            if (s.Floor)
            {
                const int fseg = 36;
                var floor = new Color(0.27f, 0.2f, 0.14f);
                for (var i = 0; i < fseg; i++)
                {
                    float a0 = i / (float)fseg * Mathf.PI * 2f, a1 = (i + 1) / (float)fseg * Mathf.PI * 2f;
                    wall.Tri(new Vector3(0f, 0.015f, 0f), At(a0, rIn + 0.12f, 0.015f), At(a1, rIn + 0.12f, 0.015f), floor, Jitter(floor * 0.85f, 0.1f), Jitter(floor * 0.85f, 0.1f));
                }
            }
            else
            {
                // no way in: a dark doorway
                var dark = new Color(0.04f, 0.03f, 0.025f);
                wall.Quad(At(-gap, rIn, 0.01f), At(-gap, rIn, doorH), At(gap, rIn, doorH), At(gap, rIn, 0.01f), dark);
            }
            MeshNode("Hut Walls", wall.Build("Hut Walls"), _vc, hut);

            var roof = new MeshKit();
            float eave = s.Eave, eaveY = s.EaveY, apexY = s.ApexY;
            var droops = new float[seg + 1];
            for (var i = 0; i < seg; i++) droops[i] = (R() - 0.5f) * 0.1f;
            droops[seg] = droops[0]; // neighbouring straw meets: no cracks between segments
            for (var i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                var droop0 = droops[i]; var droop1 = droops[i + 1];
                Color c0 = Jitter(s.Thatch, 0.16f), c1 = Jitter(s.Thatch, 0.16f), top = Jitter(s.Thatch * 0.75f, 0.08f);
                for (var b = 0; b < 3; b++)
                {
                    float u0 = b / 3f, u1 = (b + 1) / 3f;
                    Vector3 A(float ang, float u, float droop) => new Vector3(Mathf.Sin(ang) * eave * (1f - u), Mathf.Lerp(eaveY + droop, apexY, u), Mathf.Cos(ang) * eave * (1f - u));
                    Color k(Color c, float u) { var x = Color.Lerp(c, top, u); x.a = 1f; return x; }
                    var d0 = b == 0 ? droop0 : 0f; var d1 = b == 0 ? droop1 : 0f;
                    roof.Quad(A(a0, u0, d0), A(a0, u1, 0f), A(a1, u1, 0f), A(a1, u0, d1), k(c0, u0), k(c0, u1), k(c1, u1), k(c1, u0));
                    roof.Quad(A(a1, u0, d1), A(a1, u1, 0f), A(a0, u1, 0f), A(a0, u0, d0), k(c1 * 0.55f, u0), k(c1 * 0.55f, u1), k(c0 * 0.55f, u1), k(c0 * 0.55f, u0));
                }
                var fa = (a0 + a1) * 0.5f;
                var fl = 0.12f + R() * 0.12f;
                roof.Quad(new Vector3(Mathf.Sin(a0) * eave, eaveY + droop0, Mathf.Cos(a0) * eave), new Vector3(Mathf.Sin(a1) * eave, eaveY + droop1, Mathf.Cos(a1) * eave),
                    new Vector3(Mathf.Sin(fa) * eave * 1.01f, eaveY - fl, Mathf.Cos(fa) * eave * 1.01f), new Vector3(Mathf.Sin(fa) * eave * 1.01f, eaveY - fl, Mathf.Cos(fa) * eave * 1.01f),
                    c0 * 0.85f, c1 * 0.85f, c0 * 0.7f, c0 * 0.7f);
            }
            roof.Box(new Vector3(0f, apexY + 0.08f, 0f), new Vector3(0.28f, 0.22f, 0.28f), Quaternion.Euler(0f, 30f, 0f), s.Thatch * 0.7f);
            MeshNode("Hut Roof", roof.Build("Hut Roof"), _vc, hut);
        }

        // ---------- footsteps ----------

        protected void BuildFootsteps()
        {
            _feet = Node("Audio Footsteps").gameObject.AddComponent<AudioSource>();
            _feet.playOnAwake = false; _feet.spatialBlend = 0f;
            _steps = new AudioClip[4];
            for (var i = 0; i < _steps.Length; i++) _steps[i] = ProceduralClips.Clip("step", ProceduralClips.StepSamples(60 + i));
        }

        void Footsteps()
        {
            // a step for every ~0.7 m walked where this trail says there's ground underfoot
            if (_feet == null) return;
            if (!_stepInit) { _lastStep = Viewer; _stepInit = true; return; }
            var moved = Vector2.Distance(Viewer, _lastStep);
            if (moved > 3f) { _lastStep = Viewer; return; } // a jump (recentre, teleport), not a walk
            if (moved < 0.7f) return;
            _lastStep = Viewer;
            if (!OnGround(Viewer)) return;
            _feet.pitch = 0.9f + R() * 0.2f;
            _feet.PlayOneShot(_steps[_stepN++ % _steps.Length], 0.32f);
        }
    }
}
