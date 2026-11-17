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
        public bool Shut => Open <= 0.001f && _target <= 0.5f;

        Material _mat, _holeMat;
        Renderer _r;
        float _target, _pull, _pullSmooth;
        PortalWindow _window;

        /// <summary>The link from your side of this doorway to the destination (see <see cref="PortalDestinations.Link"/>), or null for none (the painted glimpse shows).</summary>
        public System.Func<Matrix4x4?> Link;
        /// <summary>True while the glass shows the live view.</summary>
        public bool Live { get; private set; }
        /// <summary>The fog the destination is drawn with (the forest has fog; your room does not).</summary>
        public System.Func<bool> Fog;
        PinchTarget _pinch;

        /// <summary>Builds a closed portal at <paramref name="plan"/>. <paramref name="stepThrough"/> runs when the glass is pinched.</summary>
        public static StoryPortal Create(Shader shader, Shader holeShader, PortalPlan plan, Action stepThrough)
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
            p._mat.SetColor("_Glow", new Color(0.2f, 0.13f, 0.08f, 1f));
            p._mat.SetFloat("_Open", 0f);
            p._r.sharedMaterial = p._mat;
            if (holeShader != null)
            {
                p._holeMat = new Material(holeShader) { name = "Portal hole" };
                p._holeMat.SetVector("_Size", new Vector4(plan.Width, plan.Height, 0f, 0f));
                p._holeMat.SetFloat("_Margin", FrameMargin);
                p._holeMat.SetFloat("_Open", 1f);
            }
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

        /// <summary>True in a place: the opening is a hole onto your real room (passthrough), and only the frame is drawn.</summary>
        public bool Hole { get; private set; }

        /// <summary>The opening shows your real room (you are in a place) or the place (you are in your room).</summary>
        public void SetHole(bool on)
        {
            Hole = on && _holeMat != null;
            _mat.SetFloat("_Hole", Hole ? 1f : 0f);
            _r.sharedMaterials = Hole ? new[] { _mat, _holeMat } : new[] { _mat };
            if (Hole) { Open = 1f; _target = 1f; _mat.SetFloat("_Open", 1f); _r.enabled = true; }
        }

        public void Show() { _target = 1f; _r.enabled = true; _pinch.gameObject.SetActive(true); }
        public void Hide() { _target = 0f; _pinch.gameObject.SetActive(false); }

        /// <summary>How near you are, 0..1 (see <see cref="PortalSpot.Pull"/>). The skin ripples harder as it grows.</summary>
        public void SetPull(float pull) => _pull = Mathf.Clamp01(pull);

        void Update()
        {
            Open = Mathf.MoveTowards(Open, _target, Time.unscaledDeltaTime / (_target > Open ? 0.9f : 0.5f));
            _pullSmooth = Mathf.MoveTowards(_pullSmooth, _pull, Time.unscaledDeltaTime * 3f);
            _mat.SetFloat("_Open", Open);
            _mat.SetFloat("_Pull", _pullSmooth);
            if (Shut) _r.enabled = false;
        }

        /// <summary>Called for each camera that is about to draw the doorway: the main one draws the live view of the destination for both eyes first.</summary>
        void OnWillRenderObject()
        {
            var cam = Camera.current;
            if (cam == null || Link == null || PortalWindow.Rendering) return;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.VR) return;
            var link = Link();
            var ok = false;
            if (link.HasValue)
            {
                if (_window == null) _window = new PortalWindow();
                ok = _window.Render(cam, link.Value, Plan, _mat, Fog != null && Fog());
            }
            Live = ok;
            _mat.SetFloat("_UseWindow", ok ? 1f : 0f);
        }

        void OnDestroy()
        {
            _window?.Dispose();
            if (_mat != null) Destroy(_mat);
            if (_holeMat != null) Destroy(_holeMat);
        }
    }
}
