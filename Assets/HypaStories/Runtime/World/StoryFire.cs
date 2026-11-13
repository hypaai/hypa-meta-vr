using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// A bonfire: a teepee of bark logs over a bed of glowing coals, with flames that read as a volume (port of the prototype's
    /// fire.js): six flame cards crossed at 30° steps (not billboards, so it has depth from any angle and in stereo), a slow swirl,
    /// a soft halo, and a point light that jitters and shifts colour. No shadows: on Quest the fire light stays real-time without them.
    /// </summary>
    public sealed class StoryFire
    {
        const int Count = 6;
        const float Scale = 0.6f; // prototype units → metres: flames up through the log teepee
        public const float LightIntensity = 3.2f;

        readonly Transform[] _cards = new Transform[Count];
        readonly Renderer[] _renderers = new Renderer[Count];
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        readonly Transform _halo;
        readonly Material _haloMat;
        readonly Light _light;
        readonly Vector3 _lightBase;
        static readonly Color Hot = new Color(1f, 0.75f, 0.44f), Warm = new Color(1f, 0.48f, 0.17f);
        static readonly int IntensityId = Shader.PropertyToID("_Intensity"), SeedId = Shader.PropertyToID("_Seed"), OpacityId = Shader.PropertyToID("_Opacity");

        public Transform Root { get; }
        public Light Light => _light;

        public StoryFire(Transform parent, Vector3 position, StoryAssets a, Texture2D glow)
        {
            Root = new GameObject("Fire").transform;
            Root.SetParent(parent, false);
            Root.localPosition = position;
            BuildLogs(a);
            var coals = new GameObject("Coals");
            coals.transform.SetParent(Root, false);
            coals.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            coals.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            coals.AddComponent<MeshFilter>().sharedMesh = StoryMeshes.Quad(1.3f, 1.3f);
            var coalMat = new Material(a.additive) { name = "Coals", mainTexture = glow, color = new Color(1f, 0.32f, 0.06f) };
            coalMat.SetFloat("_Opacity", 0.9f);
            coals.AddComponent<MeshRenderer>().sharedMaterial = coalMat;
            var flame = new Material(a.flame) { name = "Flame" };
            for (var i = 0; i < Count; i++)
            {
                float w = (1.9f + (i % 3) * 0.2f) * Scale, h = (3.3f + ((i * 7) % 5) * 0.25f) * Scale;
                var go = new GameObject("Flame " + i);
                go.transform.SetParent(Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = StoryMeshes.Quad(w, h, true);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = flame;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var ang = i / (float)Count * Mathf.PI;
                go.transform.localPosition = new Vector3(Mathf.Sin(ang) * 0.03f, 0f, Mathf.Cos(ang) * 0.03f);
                _cards[i] = go.transform; _renderers[i] = r;
            }

            var halo = new GameObject("Halo");
            halo.transform.SetParent(Root, false);
            halo.transform.localPosition = new Vector3(0, 0.85f, 0);
            halo.AddComponent<MeshFilter>().sharedMesh = StoryMeshes.Quad(3.6f, 3.6f);
            _haloMat = new Material(a.additive) { name = "Fire Halo", mainTexture = glow, color = new Color(1f, 0.48f, 0.16f) };
            halo.AddComponent<MeshRenderer>().sharedMaterial = _haloMat;
            _halo = halo.transform;

            var lightGo = new GameObject("Fire Light");
            lightGo.transform.SetParent(Root, false);
            lightGo.transform.localPosition = _lightBase = new Vector3(0f, 0.75f, 0.1f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = Warm;
            _light.range = 12f;
            _light.intensity = LightIntensity;
            _light.shadows = LightShadows.None;
            _light.renderMode = LightRenderMode.ForcePixel;
        }

        /// <summary>Six logs leaning into an open teepee (the flames show between them), plus three lying across the coals.</summary>
        void BuildLogs(StoryAssets a)
        {
            var mat = a.log != null ? a.log : new Material(a.lit) { color = new Color(0.2f, 0.13f, 0.08f) };
            var r = new System.Random(7);
            float R() => (float)r.NextDouble();
            void Log(Vector3 from, Vector3 to, float radius)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                g.name = "Log";
                Object.Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(Root, false);
                g.transform.localPosition = (from + to) / 2f;
                g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from) * Quaternion.Euler(0f, R() * 360f, 0f);
                g.transform.localScale = new Vector3(radius * 2f, (to - from).magnitude / 2f, radius * 2f);
                var rend = g.GetComponent<Renderer>();
                rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            for (var i = 0; i < 6; i++)
            {
                var ang = i / 6f * Mathf.PI * 2f + R() * 0.3f;
                var foot = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (0.5f + R() * 0.08f);
                var top = new Vector3(Mathf.Cos(ang + 2.6f), 0f, Mathf.Sin(ang + 2.6f)) * 0.07f + Vector3.up * (0.72f + R() * 0.12f);
                Log(foot, top, 0.032f + R() * 0.012f);
            }
            for (var i = 0; i < 3; i++)
            {
                var ang = i / 3f * Mathf.PI + R() * 0.4f;
                var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.45f;
                Log(-d + Vector3.up * 0.05f, d + Vector3.up * 0.07f, 0.055f);
            }
        }

        /// <param name="k">Fire intensity: 1 normal, above 1 swells (hover, transitions).</param>
        public void Update(Transform viewer, float t, float k)
        {
            for (var i = 0; i < Count; i++)
            {
                _cards[i].localRotation = Quaternion.Euler(0f, (i / (float)Count * Mathf.PI + t * 0.12f) * Mathf.Rad2Deg, 0f);
                _renderers[i].GetPropertyBlock(_mpb);
                _mpb.SetFloat(IntensityId, k * 0.85f);
                _mpb.SetFloat(SeedId, i * 2.7f);
                _renderers[i].SetPropertyBlock(_mpb);
            }
            if (viewer != null) _halo.rotation = Quaternion.LookRotation(_halo.position - viewer.position);
            _haloMat.SetFloat(OpacityId, 0.1f * Mathf.Min(k, 1.2f));

            var f = 0.5f + 0.5f * Mathf.Sin(t * 11.3f + Mathf.Sin(t * 4.1f) * 2f);
            _light.transform.localPosition = _lightBase + new Vector3(Mathf.Sin(t * 7.7f) * 0.05f, Mathf.Sin(t * 5.3f) * 0.04f, Mathf.Cos(t * 6.1f) * 0.05f);
            _light.color = Color.Lerp(Warm, Hot, f * 0.45f);
        }
    }
}
