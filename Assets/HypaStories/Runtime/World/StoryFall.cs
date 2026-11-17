using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Ticket 3L: the fall through the portal. A tunnel of light on a sphere that follows your head (without turning), fading in as you
    /// step through the glass, rushing faster until you choose where to land, and gone when you arrive.
    /// </summary>
    public sealed class StoryFall : MonoBehaviour
    {
        const float Radius = 1.5f; // panels sit well inside this (a metre or less in front of you)

        Transform _follow;
        Material _mat;
        Renderer _r;
        float _amount, _amountTarget, _rush, _rushTarget, _phase;

        public bool Active => _amountTarget > 0.5f;
        public float Amount => _amount;

        public static StoryFall Create(Shader shader, Transform follow)
        {
            var go = new GameObject("Hypa Fall");
            var f = go.AddComponent<StoryFall>();
            f._follow = follow;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.AddComponent<MeshFilter>().sharedMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sphere);
            go.transform.localScale = Vector3.one * (Radius * 2f);
            f._r = go.AddComponent<MeshRenderer>();
            f._r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            f._r.receiveShadows = false;
            f._mat = new Material(shader) { name = "Fall" };
            f._r.sharedMaterial = f._mat;
            f._r.enabled = false;
            return f;
        }

        /// <summary>Fade the tunnel in all round you and start falling.</summary>
        public void Begin() { _amountTarget = 1f; _rushTarget = 0.6f; _r.enabled = true; }
        /// <summary>Faster: you chose, and you are dropping toward it.</summary>
        public void Rush() { _rushTarget = 1f; }
        public void End() { _amountTarget = 0f; _rushTarget = 0f; }
        /// <summary>Gone at once.</summary>
        public void Stop() { _amountTarget = 0f; _amount = 0f; _rush = 0f; _r.enabled = false; }

        void Update()
        {
            if (_follow != null) transform.SetPositionAndRotation(_follow.position, Quaternion.identity);
            var dt = Time.unscaledDeltaTime;
            _amount = Mathf.MoveTowards(_amount, _amountTarget, dt / (_amountTarget > _amount ? 0.7f : 0.4f));
            _rush = Mathf.MoveTowards(_rush, _rushTarget, dt * 1.2f);
            _phase += dt * (3.5f + 16f * _rush);
            _mat.SetFloat("_Amount", _amount);
            _mat.SetFloat("_Phase", _phase);
            _mat.SetFloat("_Rush", _rush);
            if (_amount <= 0.001f && _amountTarget <= 0.5f) _r.enabled = false;
        }

        void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}
