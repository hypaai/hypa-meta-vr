using System;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Ticket 3L: the doorway of light that opens in your room (on a real door, or a clear bit of wall) and shows the hut. It lives in
    /// real-room (tracking) space, never under the camp's world, because the camp moves and your room doesn't. One quad: the shader draws
    /// the glowing frame, the rippling glass and the pull as you come near. Pinching the glass steps through too (for anyone sitting).
    /// </summary>
    public sealed class StoryPortal : MonoBehaviour
    {
        const float FrameMargin = 0.14f;

        public PortalPlan Plan { get; private set; }
        /// <summary>0 shut .. 1 fully open (eases toward the target).</summary>
        public float Open { get; private set; }
        public bool Opening => _target > 0.5f;
        public bool Shut => Open <= 0.001f && _target <= 0.5f && _hintTarget <= 0.001f;
        /// <summary>Shut but live: a faint ember outline on the door, waiting to be pinched.</summary>
        public bool Armed => _hintTarget > 0.5f && _target <= 0.5f;

        Material _mat;
        Renderer _r;
        float _target, _pull, _pullSmooth, _hintTarget, _hint;
        PinchTarget _pinch;

        /// <summary>Builds a closed portal at <paramref name="plan"/>. <paramref name="stepThrough"/> runs when the glass is pinched.</summary>
        public static StoryPortal Create(Shader shader, PortalPlan plan, Action stepThrough)
        {
            var go = new GameObject("Hypa Portal");
            go.transform.SetPositionAndRotation(plan.Base, Quaternion.LookRotation(plan.Normal));
            var p = go.AddComponent<StoryPortal>();
            p.Plan = plan;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = StoryMeshes.Quad(plan.Width + 2f * FrameMargin, plan.Height + FrameMargin, true);
            p._r = go.AddComponent<MeshRenderer>();
            p._r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            p._r.receiveShadows = false;
            p._mat = new Material(shader) { name = "Portal" };
            p._mat.SetVector("_Size", new Vector4(plan.Width, plan.Height, 0f, 0f));
            p._mat.SetFloat("_Margin", FrameMargin);
            p._mat.SetFloat("_Open", 0f);
            p._r.sharedMaterial = p._mat;
            p._r.enabled = false;

            // the glass can be pinched: a thin box across the opening
            var glass = new GameObject("Portal Glass");
            glass.transform.SetParent(go.transform, false);
            glass.transform.localPosition = new Vector3(0f, plan.Height * 0.5f, 0f);
            glass.AddComponent<BoxCollider>().size = new Vector3(plan.Width, plan.Height, 0.12f);
            p._pinch = glass.AddComponent<PinchTarget>();
            p._pinch.OnPinch = k => { if (k == PinchKind.Short) stepThrough?.Invoke(); }; // armed or open: pinching the door is the way in
            glass.SetActive(false);
            return p;
        }

        /// <summary>Shut, with a faint ember outline: the door is live. Pinch it (or walk into it) to open.</summary>
        public void Arm() { _target = 0f; _hintTarget = 1f; _r.enabled = true; _pinch.gameObject.SetActive(true); }
        public void Show() { _target = 1f; _hintTarget = 0f; _r.enabled = true; _pinch.gameObject.SetActive(true); }
        public void Hide() { _target = 0f; _hintTarget = 0f; _pinch.gameObject.SetActive(false); }

        /// <summary>How near you are, 0..1 (see <see cref="PortalSpot.Pull"/>). The skin ripples harder as it grows.</summary>
        public void SetPull(float pull) => _pull = Mathf.Clamp01(pull);

        void Update()
        {
            Open = Mathf.MoveTowards(Open, _target, Time.unscaledDeltaTime / (_target > Open ? 0.9f : 0.5f));
            _pullSmooth = Mathf.MoveTowards(_pullSmooth, _pull, Time.unscaledDeltaTime * 3f);
            _hint = Mathf.MoveTowards(_hint, _hintTarget, Time.unscaledDeltaTime * 2f);
            _mat.SetFloat("_Open", Open);
            _mat.SetFloat("_Hint", _hint);
            _mat.SetFloat("_Pull", _pullSmooth);
            if (Shut && _hint <= 0.001f) _r.enabled = false;
        }

        void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}
