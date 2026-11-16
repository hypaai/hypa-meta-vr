using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The nighttime campfire clearing (port of the prototype's environment.js), at real-world scale with the fire at the origin and
    /// the viewer's seats on the -Z side. Layers: sky dome, far treeline cards, procedural pines all around (in VR you can look
    /// behind you), Poly Haven props from glTF, forest floor, firelight and moonlight pools, the fire, mist, fireflies / motes /
    /// embers, rain, and the storyteller across the fire. Lighting is an ambient fill, one moon light, one flickering fire light and
    /// a lightning light; no shadows or post-processing (Quest).
    /// State the flow animates: <see cref="Boost"/> (fire swell), <see cref="Dim"/> (world dims for the story list),
    /// <see cref="Gather"/> (fireflies draw in while you ask).
    /// </summary>
    public sealed class CampWorld : MonoBehaviour
    {
        /// <summary>Where the viewer stands for the mode select, and sits for the stories (world-local, facing +Z).</summary>
        public static readonly Vector3 SplashSeat = new Vector3(0f, 0f, -6.2f), StorySeat = new Vector3(0f, 0f, -3.1f);
        public static readonly Color FogColor = new Color(0.051f, 0.133f, 0.188f);

        public float Boost = 1f, Dim, Gather;
        /// <summary>0..1: the portal ring bursts outward and fades (entering the stories).</summary>
        public float RingBurst;
        public Storyteller Teller { get; private set; }
        public StoryFire Fire { get; private set; }
        public Transform PortalRing { get; private set; }
        /// <summary>The trail to the hut (3G): signpost, trail, hut, owl and wolf.</summary>
        public HutTrail Trail { get; private set; }
        /// <summary>The trail to the village and the village itself (3M).</summary>
        public VillageTrail VillageTrail { get; private set; }
        public Village Village { get; private set; }

        StoryAssets _a;
        Material _sky, _pool, _fireflies, _motes, _embers, _rain, _ringMat, _ringDashMat;
        Transform _rainT, _ringDash;
        Light _moon, _flash;
        readonly List<(Transform t, float ph, float amp)> _sway = new List<(Transform, float, float)>();
        StorySample? _story;
        float _t, _ringLit;
        System.Random _r;

        static readonly int GatherId = Shader.PropertyToID("_Gather"), BoostId = Shader.PropertyToID("_Boost"),
            FlashId = Shader.PropertyToID("_Flash"), OpacityId = Shader.PropertyToID("_Opacity"), IntensityId = Shader.PropertyToID("_Intensity"),
            WindId = Shader.PropertyToID("_StoryWind");

        float R() => (float)_r.NextDouble();

        public void Build(StoryAssets a)
        {
            _a = a;
            _r = new System.Random(42);
            var glow = StoryMeshes.Glow();

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 8f;
            RenderSettings.fogEndDistance = 36f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.skybox = null;

            // sky dome
            _sky = new Material(a.sky) { name = "Sky" };
            var sky = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sky.name = "Sky";
            Destroy(sky.GetComponent<Collider>());
            sky.transform.SetParent(transform, false);
            sky.transform.localScale = Vector3.one * 180f;
            Plain(sky.GetComponent<Renderer>(), _sky);

            // forest floor
            var ground = Child("Ground", StoryMeshes.Quad(140f, 140f), a.ground != null ? a.ground : Tinted(new Color(0.12f, 0.1f, 0.08f)));
            ground.localPosition = new Vector3(0, 0, 10f);
            ground.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // light pools on the ground: warm under the fire, cool where the moon falls
            _pool = Glow(glow, new Color(1f, 0.48f, 0.13f), 0.09f);
            var pool = Child("Fire Pool", StoryMeshes.Quad(7f, 5f), _pool);
            pool.localPosition = new Vector3(0, 0.02f, -0.3f);
            pool.localScale = Vector3.one * 1.3f;
            pool.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var moonPool = Child("Moon Pool", StoryMeshes.Quad(10f, 6f), Glow(glow, new Color(0.18f, 0.42f, 0.48f), 0.3f));
            moonPool.localPosition = new Vector3(-3.5f, 0.015f, 3f);
            moonPool.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // lights: ambient fill, moon key, lightning, and the fire (inside StoryFire)
            _moon = NewLight("Moon", LightType.Directional, new Color(0.62f, 0.79f, 0.88f), 0.3f);
            _moon.transform.localRotation = Quaternion.LookRotation(-new Vector3(0.35f, 0.8f, 0.5f));
            _flash = NewLight("Lightning", LightType.Directional, new Color(0.71f, 0.78f, 1f), 0f);
            _flash.transform.localRotation = Quaternion.LookRotation(-new Vector3(-0.3f, 1f, -0.5f));

            Fire = new StoryFire(transform, new Vector3(0f, 0.02f, 0f), a, glow);
            BuildTrees();
            BuildProps();
            BuildTrail(glow);
            BuildEyes(glow);
            BuildMist();
            BuildParticles();
            BuildRain();
            BuildPortalRing(glow);

            var teller = new GameObject("Storyteller");
            teller.transform.SetParent(transform, false);
            teller.transform.localPosition = new Vector3(0.55f, 0f, 2.05f);
            teller.transform.localRotation = Quaternion.Euler(0f, 188f, 0f); // faces the viewer's seat, across the fire
            Teller = teller.AddComponent<Storyteller>();
            Teller.Fire = Fire.Root;
            Teller.Build(a.storyteller, a.lit);

            // don't draw what the fog hides: the far ends of the trails, the hut, the pines round them (the village has its own)
            var cull = gameObject.AddComponent<DistanceCull>();
            cull.Collect(transform, t => Village != null && t == Village.transform || t == Fire.Root || t == PortalRing || t == Teller.transform,
                14f, n => n == "Wolf" || n == "Owl" || n == "Bat" || n == "Bushbaby" || n == "Frog", 16f);
        }

        // ---------- builders ----------

        Transform Child(string name, Mesh mesh, Material mat, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            Plain(go.AddComponent<MeshRenderer>(), mat);
            return go.transform;
        }

        static void Plain(Renderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        Material Tinted(Color c) => new Material(_a.lit) { color = c };

        Material Glow(Texture2D tex, Color c, float opacity)
        {
            var m = new Material(_a.additive) { mainTexture = tex, color = c };
            m.SetFloat(OpacityId, opacity);
            return m;
        }

        Light NewLight(string name, LightType type, Color c, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = type; l.color = c; l.intensity = intensity; l.shadows = LightShadows.None;
            return l;
        }

        void BuildTrees()
        {
            // far treeline: cheap silhouette cards in the fog, all the way round
            var cards = new Material[4];
            for (var i = 0; i < cards.Length; i++)
            {
                cards[i] = new Material(_a.cutout) { mainTexture = StoryMeshes.PineSilhouette(11 * (i + 1)), color = new Color(0.02f, 0.045f, 0.06f) };
                cards[i].SetFloat("_Cutoff", 0.5f);
            }
            var cardMesh = StoryMeshes.Quad(1f, 1f, true);
            for (var i = 0; i < 44; i++)
            {
                var ang = i / 44f * Mathf.PI * 2f + R() * 0.12f;
                var d = 24f + R() * 10f;
                var h = 13f + R() * 6f;
                var x = Mathf.Sin(ang) * d; var z = Mathf.Cos(ang) * d;
                if (VillageLayout.NearVillage(x, z, 6f) || VillageLayout.NearPath(x, z, 5f)) d += 34f; // behind the village, its backdrop
                else if (HutTrailLayout.NearPath(x, z, 5f) || HutTrailLayout.NearHut(x, z, 4f)) d += 16f; // the long trail passes through the old ring
                var t = Child("Far Pine", cardMesh, cards[i % cards.Length]);
                t.localPosition = new Vector3(Mathf.Sin(ang) * d, -0.1f, Mathf.Cos(ang) * d);
                t.localRotation = Quaternion.LookRotation(t.localPosition.normalized);
                t.localScale = new Vector3(h * 0.5f, h, 1f);
            }

            // pines: a ring around the clearing, denser behind the storyteller, plus a few close beside the seats
            var pineMat = new Material(_a.pine) { name = "Pine" };
            var meshes = new[] { StoryMeshes.Pine(44166), StoryMeshes.Pine(13977), StoryMeshes.Pine(31337), StoryMeshes.Pine(7771) };
            void Pine(float x, float z, float h)
            {
                var t = Child("Pine", meshes[_r.Next(meshes.Length)], pineMat);
                t.localPosition = new Vector3(x, -0.05f, z);
                t.localRotation = Quaternion.Euler(0f, R() * 360f, 0f);
                t.localScale = new Vector3(h * (0.9f + R() * 0.25f), h, h * (0.9f + R() * 0.25f));
            }
            for (var placed = 0; placed < 46;)
            {
                var ang = R() * Mathf.PI * 2f;
                var d = 7f + R() * 13f;
                float x = Mathf.Sin(ang) * d, z = Mathf.Cos(ang) * d;
                if (z > 2f && Mathf.Abs(x) < 2.2f) continue; // keep a gap behind the storyteller so the fire has a backdrop
                if (Grounds.KeepClear(x, z, 3.15f, 3.2f)) continue; // the trails stay open; pine boughs stay off the hut and the village
                var fromSeat = new Vector2(x, z - SplashSeat.z).magnitude;
                if (fromSeat < 4.5f || (x > 2f && fromSeat < 6f)) continue; // no pine on top of the seat; the village trail's mouth shows from it
                Pine(x, z, 7f + R() * 5f);
                placed++;
            }
            float[,] near = { { -4.75f, -1.5f, 10f }, { 4.25f, -1f, 9f }, { -7.5f, 1.5f, 12f }, { 7f, 2f, 11f }, { -3.6f, -4.6f, 7.5f }, { 3.9f, -4.9f, 7f } };
            for (var i = 0; i < near.GetLength(0); i++)
                if (!Grounds.KeepClear(near[i, 0], near[i, 1], 3.15f, 3.2f)) Pine(near[i, 0], near[i, 1], near[i, 2]);

            // pines along the trail, past the clearing, and round the hut (a gap left in front of its door)
            for (var placed = 0; placed < 52;)
            {
                var (p, dir) = HutTrailLayout.Along(0.1f + R() * 0.84f);
                var at = p + new Vector2(-dir.y, dir.x) * ((R() < 0.5f ? 1f : -1f) * (4.4f + R() * 7f));
                if (at.magnitude < 14f) continue;
                if (Grounds.KeepClear(at.x, at.y, 3.15f, 3.2f)) continue;
                if (Vector2.Distance(at, HutTrailLayout.FrontOfDoor) < 4f) continue;
                Pine(at.x, at.y, 8f + R() * 6f);
                placed++;
            }
            // two pines frame the mouth of the trail, so the gap between the trees reads from the seat
            var mouth = HutTrailLayout.Along(0.08f);
            var gap = new Vector2(-mouth.dir.y, mouth.dir.x) * 3.7f;
            Pine(mouth.point.x + gap.x, mouth.point.y + gap.y, 11f);
            Pine(mouth.point.x - gap.x, mouth.point.y - gap.y, 9.5f);

            // the same along the village trail, and a ring of forest round the village beyond its huts
            for (var placed = 0; placed < 46;)
            {
                var (p, dir) = VillageLayout.Along(0.1f + R() * 0.84f);
                var at = p + new Vector2(-dir.y, dir.x) * ((R() < 0.5f ? 1f : -1f) * (4.4f + R() * 7f));
                if (at.magnitude < 14f || Grounds.KeepClear(at.x, at.y, 3.15f, 3.2f)) continue;
                var (sp, _) = VillageLayout.Stream;
                if (Vector2.Distance(at, sp) < 2.5f || Vector2.Distance(at, VillageLayout.BushbabyTrees) < 5.5f) continue;
                Pine(at.x, at.y, 8f + R() * 6f);
                placed++;
            }
            for (var placed = 0; placed < 40;)
            {
                var ang = R() * Mathf.PI * 2f;
                var at = VillageLayout.Plaza + new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * (VillageLayout.HutRing + VillageLayout.HutRadius + 2.5f + R() * 8f);
                if (VillageLayout.NearPath(at.x, at.y, 3.6f) || HutTrailLayout.NearPath(at.x, at.y, 3.15f)) continue;
                Pine(at.x, at.y, 9f + R() * 7f);
                placed++;
            }
            // (the near side of the mouth stays open: a pine there would stand beside the seat)
            var vmouth = VillageLayout.Along(0.09f);
            var vgap = new Vector2(-vmouth.dir.y, vmouth.dir.x) * 3.7f;
            Pine(vmouth.point.x + vgap.x, vmouth.point.y + vgap.y, 10.5f);
            var vmouth2 = VillageLayout.Along(0.2f);
            var vgap2 = new Vector2(-vmouth2.dir.y, vmouth2.dir.x) * 3.9f;
            Pine(vmouth2.point.x - vgap2.x, vmouth2.point.y - vgap2.y, 9f);
        }

        GameObject Spawn(GameObject prefab, float size, Vector3 pos, float rotY, bool byWidth, float sway = 0f)
        {
            if (prefab == null) return null;
            var holder = new GameObject(prefab.name);
            holder.transform.SetParent(transform, false);
            var inner = Instantiate(prefab, holder.transform);
            inner.transform.localPosition = Vector3.zero; inner.transform.localRotation = Quaternion.identity; inner.transform.localScale = Vector3.one;
            var rs = inner.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return holder;
            var b = rs[0].bounds;
            foreach (var r in rs) { b.Encapsulate(r.bounds); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            var sz = b.size;
            var s = size / Mathf.Max(1e-4f, byWidth ? Mathf.Max(sz.x, sz.z) : sz.y);
            var centre = holder.transform.InverseTransformPoint(b.center);
            var min = holder.transform.InverseTransformPoint(b.min);
            inner.transform.localScale = Vector3.one * s;
            inner.transform.localPosition = new Vector3(-centre.x * s, -min.y * s, -centre.z * s);
            holder.transform.localPosition = pos;
            holder.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            if (sway > 0f) _sway.Add((holder.transform, R() * 6.28f, sway));
            return holder;
        }

        // keeps props out of the seats, the storyteller's log and the fire
        static bool Clear(float x, float z)
        {
            float dx = x, dz = z - StorySeat.z;
            var ang = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            if (ang > -8f && ang < 52f && new Vector2(dx, dz).magnitude < 4.5f) return false; // the cards and the player panel
            return new Vector2(dx, dz).magnitude > 1.1f && new Vector2(x - 0.55f, z - 2.05f).magnitude > 1.1f
                   && new Vector2(x, z).magnitude > 1.5f && !(Mathf.Abs(x) < 0.8f && z < StorySeat.z && z > SplashSeat.z - 0.8f)
                   && !HutTrailLayout.NearPath(x, z, 1.2f) && Vector2.Distance(new Vector2(x, z), HutTrailLayout.Sign) > 0.9f
                   && !VillageLayout.NearPath(x, z, 1.2f) && Vector2.Distance(new Vector2(x, z), VillageLayout.Sign) > 0.9f;
        }

        void BuildProps()
        {
            if (_a.campFire != null) Spawn(_a.campFire, 2.0f, Vector3.zero, 0f, true); // the stone ring round the bonfire
            else FallbackFirePit();
            Spawn(_a.deadTrunk, 2.5f, new Vector3(HutTrailLayout.Log.x, 0f, HutTrailLayout.Log.y), 20f, true);
            Spawn(_a.stump, 0.75f, new Vector3(-2.1f, 0f, 1.7f), 57f, false); // clear of the cards and the player panel (right of the seat)

            for (var i = 0; i < 9;)
            {
                var ang = R() * Mathf.PI * 2f; var d = 2.6f + R() * 3f;
                float x = Mathf.Cos(ang) * d, z = Mathf.Sin(ang) * d * 0.8f + 0.6f;
                if (!Clear(x, z)) continue;
                var size = 0.5f + R() * 1.0f;
                if (_a.rocks.Length > 0) Spawn(_a.rocks[i % _a.rocks.Length], size, new Vector3(x, -0.05f, z), R() * 360f, true);
                else FallbackRock(new Vector3(x, 0f, z), size);
                i++;
            }
            for (var i = 0; i < 10;)
            {
                var ang = R() * Mathf.PI * 2f; var d = 2.2f + R() * 4f;
                float x = Mathf.Cos(ang) * d, z = Mathf.Sin(ang) * d * 0.7f + 0.4f;
                if (!Clear(x, z)) continue;
                Spawn(_a.fern, 0.7f + R() * 0.7f, new Vector3(x, -0.05f, z), R() * 360f, true, 0.01f);
                i++;
            }
            for (var i = 0; i < 12;)
            {
                var ang = R() * Mathf.PI * 2f; var d = 1.6f + R() * 5f;
                float x = Mathf.Cos(ang) * d, z = Mathf.Sin(ang) * d * 0.7f;
                if (!Clear(x, z)) continue;
                Spawn(_a.grass, 0.6f + R() * 0.45f, new Vector3(x, -0.05f, z), R() * 360f, true, 0.015f);
                i++;
            }
        }

        /// <summary>The trail to the hut, and ferns and rocks along its edges.</summary>
        // ---------- eyes in the dark (a story's "dark" cue) ----------

        readonly List<(Transform pair, Material m, float ph)> _eyes = new List<(Transform, Material, float)>();
        Transform _eyesRoot;

        /// <summary>
        /// Pairs of eyes among the trees round the clearing, unseen until a story's night gets deep: then they open one by one,
        /// blink, watch you, and close again when the dark lifts.
        /// </summary>
        void BuildEyes(Texture2D glow)
        {
            _eyesRoot = new GameObject("Eyes in the Dark").transform;
            _eyesRoot.SetParent(transform, false);
            var r = new System.Random(19);
            float Rr() => (float)r.NextDouble();
            for (var i = 0; i < 7; i++)
            {
                // round the far side and the flanks, never behind the seats, never on a trail
                Vector2 at;
                var tries = 0;
                do
                {
                    var a = (-110f + Rr() * 220f) * Mathf.Deg2Rad;
                    at = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (8f + Rr() * 4.5f);
                } while (Grounds.KeepClear(at.x, at.y, 2f, 2f) && ++tries < 20);
                var pair = new GameObject("Eyes").transform;
                pair.SetParent(_eyesRoot, false);
                pair.localPosition = new Vector3(at.x, 0.45f + Rr() * 1.3f, at.y);
                var m = new Material(_a.additive) { name = "Eyes", mainTexture = glow, color = i % 3 == 0 ? new Color(1f, 0.45f, 0.15f) : new Color(1f, 0.85f, 0.35f) };
                m.SetFloat(OpacityId, 0f);
                var gap = 0.1f + Rr() * 0.06f;
                foreach (var k in new[] { -1f, 1f })
                {
                    var eye = Child("Eye", StoryMeshes.Quad(0.2f, 0.2f), m, pair); // a glowing point with a soft halo, readable from the seat
                    eye.localPosition = new Vector3(k * gap, 0f, 0f);
                }
                _eyes.Add((pair, m, Rr() * 10f));
            }
            _eyesRoot.gameObject.SetActive(false);
        }

        void Eyes(float dark, float t, Transform cam)
        {
            var open = Mathf.Clamp01((dark - 0.38f) / 0.3f);
            _eyesRoot.gameObject.SetActive(open > 0.01f);
            if (open <= 0.01f) return;
            for (var i = 0; i < _eyes.Count; i++)
            {
                var (pair, m, ph) = _eyes[i];
                var mine = Mathf.Clamp01(open * _eyes.Count - i * 0.8f);                 // they open one by one
                var blink = Mathf.Repeat(t * 0.31f + ph, 1f) < 0.035f ? 0f : 1f;
                var drift = 0.75f + 0.25f * Mathf.Sin(t * 0.7f + ph);                       // now and then they half-close, as if looking away
                m.SetFloat(OpacityId, mine * blink * drift);
                if (cam != null)
                {
                    var to = cam.position - pair.position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f) pair.rotation = Quaternion.LookRotation(-to);
                }
            }
        }

        void BuildTrail(Texture2D glow)
        {
            var go = new GameObject("Hut Trail");
            go.transform.SetParent(transform, false);
            Trail = go.AddComponent<HutTrail>();
            Trail.Build(_a, glow);
            var vt = new GameObject("Village Trail");
            vt.transform.SetParent(transform, false);
            VillageTrail = vt.AddComponent<VillageTrail>();
            VillageTrail.Build(_a, glow);
            var vg = new GameObject("Village");
            vg.transform.SetParent(transform, false);
            Village = vg.AddComponent<Village>();
            Village.Build(_a, glow);
            for (var i = 0; i < 48; i++)
            {
                var (p, dir) = HutTrailLayout.Along(0.04f + i / 48f * 0.9f + R() * 0.02f);
                var side = new Vector2(-dir.y, dir.x) * ((i % 2 == 0 ? 1f : -1f) * (HutTrailLayout.PathHalf + 0.45f + R() * 1.1f));
                var at = p + side;
                if (HutTrailLayout.NearHut(at.x, at.y, 0.4f) || Vector2.Distance(at, HutTrailLayout.Sign) < 0.8f) continue;
                if (i % 3 == 2 && _a.rocks.Length > 0) Spawn(_a.rocks[i % _a.rocks.Length], 0.35f + R() * 0.5f, new Vector3(at.x, -0.05f, at.y), R() * 360f, true);
                else Spawn(_a.fern, 0.6f + R() * 0.7f, new Vector3(at.x, -0.05f, at.y), R() * 360f, true, 0.01f);
            }
            var (stream, _) = VillageLayout.Stream;
            for (var i = 0; i < 44; i++)
            {
                var (p, dir) = VillageLayout.Along(0.04f + i / 44f * 0.9f + R() * 0.02f);
                var side = new Vector2(-dir.y, dir.x) * ((i % 2 == 0 ? 1f : -1f) * (VillageLayout.PathHalf + 0.45f + R() * 1.1f));
                var at = p + side;
                if (Vector2.Distance(at, stream) < 2.6f || Vector2.Distance(at, VillageLayout.Sign) < 0.8f || VillageLayout.NearVillage(at.x, at.y, -8f)) continue;
                if (i % 3 == 2 && _a.rocks.Length > 0) Spawn(_a.rocks[i % _a.rocks.Length], 0.35f + R() * 0.5f, new Vector3(at.x, -0.05f, at.y), R() * 360f, true);
                else Spawn(_a.fern, 0.6f + R() * 0.7f, new Vector3(at.x, -0.05f, at.y), R() * 360f, true, 0.01f);
            }
        }

        void FallbackFirePit()
        {
            var stone = Tinted(new Color(0.22f, 0.22f, 0.24f));
            for (var i = 0; i < 10; i++)
            {
                var ang = i / 10f * Mathf.PI * 2f;
                FallbackRock(new Vector3(Mathf.Cos(ang) * 0.6f, 0f, Mathf.Sin(ang) * 0.6f), 0.22f, stone);
            }
            var wood = Tinted(new Color(0.16f, 0.1f, 0.06f));
            for (var i = 0; i < 4; i++)
            {
                var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(log.GetComponent<Collider>());
                log.transform.SetParent(transform, false);
                log.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                log.transform.localRotation = Quaternion.Euler(70f, i * 45f, 0f);
                log.transform.localScale = new Vector3(0.09f, 0.35f, 0.09f);
                Plain(log.GetComponent<Renderer>(), wood);
            }
        }

        void FallbackRock(Vector3 pos, float size, Material m = null)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(rock.GetComponent<Collider>());
            rock.transform.SetParent(transform, false);
            rock.transform.localPosition = pos;
            rock.transform.localScale = new Vector3(size, size * 0.6f, size * 0.85f);
            rock.transform.localRotation = Quaternion.Euler(0f, R() * 360f, 0f);
            Plain(rock.GetComponent<Renderer>(), m ?? Tinted(new Color(0.2f, 0.21f, 0.2f)));
        }

        void BuildMist()
        {
            // two bands (the prototype had three; mist is overdraw-heavy on Quest)
            var bands = new[] { (z: 9f, y: 1.2f, w: 34f, h: 3f, a: 0.16f, seed: 1f), (z: 4.5f, y: 0.7f, w: 22f, h: 2.2f, a: 0.09f, seed: 5f) };
            foreach (var b in bands)
            {
                var m = new Material(_a.mist) { color = new Color(0.36f, 0.56f, 0.63f) };
                m.SetFloat("_Alpha", b.a); m.SetFloat("_Seed", b.seed);
                var t = Child("Mist", StoryMeshes.Quad(b.w, b.h), m);
                t.localPosition = new Vector3(0f, b.y + b.h * 0.25f, b.z);
            }
        }

        Material Particles(string name, Mesh mesh, Color c, float size, float alpha, float speed, bool blink, bool ember)
        {
            var m = new Material(_a.particles) { name = name, color = c };
            m.SetFloat("_Size", size); m.SetFloat("_Alpha", alpha); m.SetFloat("_Speed", speed);
            m.SetFloat("_Blink", blink ? 1f : 0f); m.SetFloat("_Ember", ember ? 1f : 0f);
            Child(name, mesh, m);
            return m;
        }

        void BuildParticles()
        {
            // fireflies and motes halved for Quest (70 → 40, 90 → 45); more embers for the bonfire
            _fireflies = Particles("Fireflies", StoryMeshes.Billboards(40, 5, r => new Vector3(((float)r.NextDouble() - 0.5f) * 12f, 0.3f + (float)r.NextDouble() * 2f, -3f + (float)r.NextDouble() * 9f)),
                new Color(0.78f, 1f, 0.48f), 0.07f, 1f, 1f, true, false);
            _motes = Particles("Motes", StoryMeshes.Billboards(45, 9, r => new Vector3(((float)r.NextDouble() - 0.5f) * 13f, (float)r.NextDouble() * 4f, -4f + (float)r.NextDouble() * 9f)),
                new Color(0.81f, 0.91f, 0.94f), 0.03f, 0.35f, 0.4f, false, false);
            _embers = Particles("Embers", StoryMeshes.Billboards(70, 11, r => Vector3.zero), new Color(1f, 0.6f, 0.23f), 0.05f, 1f, 1f, false, true);
            _embers.SetFloat("_Scale", 0.7f);
            _embers.SetVector("_EmberBase", new Vector4(0f, 0.3f, 0f, 0f));
            _fireflies.SetVector("_Fire", new Vector4(0f, 0.9f, 0f, 0f));
        }

        void BuildRain()
        {
            _rain = new Material(_a.rain) { name = "Rain" };
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = StoryMeshes.Rain(1800, 16f, 8f, 9);
            Plain(go.AddComponent<MeshRenderer>(), _rain);
            _rainT = go.transform;
            go.SetActive(false);
        }

        void BuildPortalRing(Texture2D glow)
        {
            // the splash "portal": a breathing ring round the fire with a slowly turning dashed ring inside (the prototype's .ring)
            PortalRing = new GameObject("Portal Ring").transform;
            PortalRing.SetParent(transform, false);
            PortalRing.localPosition = new Vector3(0f, 0.95f, 0f);
            _ringMat = Glow(Texture2D.whiteTexture, new Color(1f, 0.7f, 0.36f), 0.22f);
            _ringDashMat = Glow(Texture2D.whiteTexture, new Color(1f, 0.7f, 0.36f), 0.22f);
            Child("Ring", StoryMeshes.Ring(1.4f, 1.415f), _ringMat, PortalRing);
            _ringDash = Child("Dashes", StoryMeshes.Ring(1.3f, 1.312f, 160, 36), _ringDashMat, PortalRing);
        }

        /// <summary>0 = idle (dim, breathing), 1 = lit (hovered).</summary>
        public void SetPortalLit(float lit) => _ringLit = lit;

        /// <summary>The weather and lightning of the story being told (null = none).</summary>
        public void SetStory(StorySample? s) => _story = s;

        float _burst, _gutter;
        static readonly int DarkId = Shader.PropertyToID("_Dark"), DawnId = Shader.PropertyToID("_Dawn"), TintId = Shader.PropertyToID("_Tint");
        static readonly Color DawnFog = new Color(0.36f, 0.3f, 0.33f);

        /// <summary>
        /// A story sound's look: a hot stone, a throw or sparks burst embers out of the fire and make it flare; a roar, a stomp
        /// or a door slam makes the flames gutter for a moment. Others have no look of their own.
        /// </summary>
        public void Effect(string name)
        {
            switch (name)
            {
                case "sizzle": case "sparks": case "throw": _burst = 1f; break;
                case "roar": case "stomp": case "door": case "fall": _gutter = 1f; break;
            }
        }

        public bool RainVisible => _rainT != null && _rainT.gameObject.activeSelf;

        void Update()
        {
            var dt = Time.deltaTime;
            _t += dt;
            var t = _t;
            var cam = Camera.main != null ? Camera.main.transform : null;

            var flick = 0.88f + 0.08f * Mathf.Sin(t * 9.1f) + 0.05f * Mathf.Sin(t * 17.3f + 1.7f) + 0.04f * Mathf.Sin(t * 3.1f);
            // the story's fire: its strength cue, a flare for sparks, a gutter for a roar or a slammed door
            _burst = Mathf.Max(0f, _burst - dt * 1.4f);
            _gutter = Mathf.Max(0f, _gutter - dt * 1.8f);
            var storyFire = (_story?.Fire ?? 1f) * (1f + _burst * 0.6f) * (1f - _gutter * 0.35f * (0.6f + 0.4f * Mathf.Sin(t * 31f)));
            var k = Boost * flick * (1f - Dim * 0.35f) * storyFire;
            Fire.Update(cam, t, k / (1f - Dim * 0.35f)); // the flame itself isn't dimmed
            Fire.Light.intensity = StoryFire.LightIntensity * k;
            _pool.SetFloat(OpacityId, 0.09f * k);

            // story weather: rain, wind in the trees, lightning. The fire stays steady.
            float rain = _story?.Rain ?? 0f, wind = _story?.Wind ?? 0f, flash = _story?.Flash ?? 0f;
            _rainT.gameObject.SetActive(rain > 0.01f);
            if (rain > 0.01f && cam != null)
            {
                _rainT.position = cam.position + Vector3.down * 3f;
                _rain.SetFloat(IntensityId, rain);
            }
            Shader.SetGlobalFloat(WindId, wind);
            foreach (var (tr, ph, amp) in _sway)
                tr.localRotation = Quaternion.Euler(0f, tr.localEulerAngles.y, Mathf.Sin(t * (0.45f + wind * 0.9f) + ph) * amp * (1f + wind * 4f) * Mathf.Rad2Deg);
            // deeper night, daybreak, police lights (red and blue, two quick pulses each)
            float dark = _story?.Dark ?? 0f, dawn = _story?.Dawn ?? 0f, police = _story?.Police ?? 0f;
            var phase = Mathf.Repeat(t * 1.25f, 1f);
            var redSide = phase < 0.5f;
            var p2 = Mathf.Repeat(phase * 2f, 1f);
            var pulse = police * (p2 < 0.18f || (p2 > 0.28f && p2 < 0.46f) ? 1f : 0.3f);
            var policeCol = (redSide ? new Color(1f, 0.08f, 0.06f) : new Color(0.1f, 0.25f, 1f)) * pulse;
            _sky.SetFloat(FlashId, flash);
            _sky.SetFloat(DarkId, dark);
            _sky.SetFloat(DawnId, dawn);
            // the Dodo game's blood-red night: red sky low down, red haze, the firelight turned to embers
            var danger = _story?.Danger ?? 0f;
            var dangerCol = new Color(0.55f, 0.04f, 0.02f) * danger;
            _sky.SetColor(TintId, policeCol * 0.3f + dangerCol * 0.5f);
            _flash.color = flash > 0.02f ? new Color(0.71f, 0.78f, 1f) : (redSide ? new Color(1f, 0.15f, 0.12f) : new Color(0.2f, 0.35f, 1f));
            _flash.intensity = flash * 1.6f + pulse * 1.3f;
            _moon.intensity = 0.3f * (1f - dark * 0.7f) + dawn * 0.55f;
            _moon.color = Color.Lerp(new Color(0.62f, 0.79f, 0.88f), new Color(1f, 0.78f, 0.6f), dawn);
            RenderSettings.fogColor = Color.Lerp(Color.Lerp(FogColor, DawnFog, dawn), new Color(0.16f, 0.02f, 0.02f), danger * 0.85f);
            var fill = (1f - Dim * 0.3f) * (1f + flash * 1.2f) * (1f - dark * 0.55f) * (1f + dawn * 1.4f);
            var warm = Color.Lerp(Color.white, new Color(1.25f, 0.95f, 0.8f), dawn);
            RenderSettings.ambientSkyColor = new Color(0.29f, 0.48f, 0.57f) * 0.42f * fill * warm + policeCol * 0.22f;
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.24f, 0.27f) * 0.35f * fill * warm + policeCol * 0.2f + dangerCol * 0.35f;
            if (danger > 0.01f) RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, new Color(0.2f, 0.03f, 0.03f), danger * 0.8f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.055f, 0.035f) * 0.5f * fill;

            _embers.SetFloat(BoostId, Boost * Mathf.Max(0.5f, storyFire) + _burst * 2.5f);

            // a god's light: the fire's glow swings slowly from deep red to pale white and back
            var spirit = _story?.Spirit ?? 0f;
            if (spirit > 0.01f)
            {
                var swing = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 4.5f);
                var spiritCol = Color.Lerp(new Color(1f, 0.1f, 0.07f), new Color(0.95f, 0.95f, 1f), swing);
                Fire.Light.color = Color.Lerp(Fire.Light.color, spiritCol, spirit * 0.85f);
                RenderSettings.ambientEquatorColor += spiritCol * spirit * 0.08f;
            }
            if (danger > 0.01f) Fire.Light.color = Color.Lerp(Fire.Light.color, new Color(1f, 0.25f, 0.08f), danger * 0.6f);
            Eyes(dark, t, cam);
            _fireflies.SetFloat(GatherId, Gather);

            // portal ring: breathes while idle, brightens on hover, faces the viewer
            if (PortalRing.gameObject.activeSelf)
            {
                var breathe = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 5f);
                var o = Mathf.Lerp(0.16f + 0.1f * breathe, 0.85f, _ringLit) * (1f - RingBurst);
                PortalRing.localScale = Vector3.one * (1f + 1.4f * RingBurst);
                _ringMat.SetFloat(OpacityId, o); _ringDashMat.SetFloat(OpacityId, o);
                _ringDash.localRotation = Quaternion.Euler(0f, 0f, -t * 4f);
                if (cam != null)
                {
                    var d = PortalRing.position - cam.position; d.y = 0f;
                    if (d.sqrMagnitude > 1e-4f) PortalRing.rotation = Quaternion.LookRotation(d);
                }
            }
        }
    }
}
