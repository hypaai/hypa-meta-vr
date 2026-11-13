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
                Pine(x, z, 7f + R() * 5f);
                placed++;
            }
            float[,] near = { { -4.75f, -1.5f, 10f }, { 4.25f, -1f, 9f }, { -7.5f, 1.5f, 12f }, { 7f, 2f, 11f }, { -3.6f, -4.6f, 7.5f }, { 3.9f, -4.9f, 7f } };
            for (var i = 0; i < near.GetLength(0); i++) Pine(near[i, 0], near[i, 1], near[i, 2]);
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
                   && new Vector2(x, z).magnitude > 1.5f && !(Mathf.Abs(x) < 0.8f && z < StorySeat.z && z > SplashSeat.z - 0.8f);
        }

        void BuildProps()
        {
            if (_a.campFire != null) Spawn(_a.campFire, 2.0f, Vector3.zero, 0f, true); // the stone ring round the bonfire
            else FallbackFirePit();
            Spawn(_a.deadTrunk, 2.5f, new Vector3(-2.3f, 0f, -0.6f), 20f, true);
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

        public bool RainVisible => _rainT != null && _rainT.gameObject.activeSelf;

        void Update()
        {
            var dt = Time.deltaTime;
            _t += dt;
            var t = _t;
            var cam = Camera.main != null ? Camera.main.transform : null;

            var flick = 0.88f + 0.08f * Mathf.Sin(t * 9.1f) + 0.05f * Mathf.Sin(t * 17.3f + 1.7f) + 0.04f * Mathf.Sin(t * 3.1f);
            var k = Boost * flick * (1f - Dim * 0.35f);
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
            _sky.SetFloat(FlashId, flash);
            _flash.intensity = flash * 1.6f;
            var fill = (1f - Dim * 0.3f) * (1f + flash * 1.2f);
            RenderSettings.ambientSkyColor = new Color(0.29f, 0.48f, 0.57f) * 0.42f * fill;
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.24f, 0.27f) * 0.35f * fill;
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.055f, 0.035f) * 0.5f * fill;

            _embers.SetFloat(BoostId, Boost);
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
