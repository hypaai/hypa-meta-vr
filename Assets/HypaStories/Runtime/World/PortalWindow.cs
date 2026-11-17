using UnityEngine;
using UnityEngine.XR;

namespace HypaStories
{
    /// <summary>
    /// Ticket 3L: the live view through the doorway. A hidden camera draws the destination once per eye, from where your eye would be if you
    /// stood on the far side of the doorway (the real doorway's frame linked to the destination's doorway, see <see cref="PortalDestinations.Link"/>),
    /// with everything on your side of the doorway cut off (an oblique near plane). The two pictures go on the glass, so what you see through the
    /// door is what you step into, with the parallax of a real window. Without XR (the editor with no headset) it draws one view for both eyes.
    /// </summary>
    public sealed class PortalWindow : System.IDisposable
    {
        /// <summary>The picture is this fraction of the headset's eye size (the doorway is a small part of the view).</summary>
        public const float Scale = 0.5f;
        static bool _rendering;
        /// <summary>True while the hidden camera is drawing (so the doorway being drawn into it does nothing).</summary>
        public static bool Rendering => _rendering;

        Camera _pc;
        readonly RenderTexture[] _rt = new RenderTexture[2];
        int _w, _h;

        public Camera Camera => _pc;

        /// <summary>
        /// Draws the view for the camera <paramref name="main"/> is drawing now. <paramref name="link"/> takes a point on your side of the
        /// doorway to the destination. Returns false (nothing drawn) when you are on the far side of the doorway or no camera could be made.
        /// </summary>
        public bool Render(Camera main, Matrix4x4 link, PortalPlan plan, Material glass, Vector2? fog)
        {
            if (_rendering || main == null) return false;
            EnsureCamera(main);
            var stereo = main.stereoEnabled && XRSettings.eyeTextureWidth > 0;
            EnsureTextures(stereo ? XRSettings.eyeTextureWidth : main.pixelWidth, stereo ? XRSettings.eyeTextureHeight : main.pixelHeight);

            var keep = link.MultiplyVector(-plan.Normal).normalized;   // into the destination
            var door = link.MultiplyPoint(plan.Base);
            var inv = link.inverse;
            var oldStart = RenderSettings.fogStartDistance; var oldEnd = RenderSettings.fogEndDistance;
            var drew = false;
            _rendering = true;
            try
            {
                for (var eye = 0; eye < (stereo ? 2 : 1); eye++)
                {
                    var view = stereo ? main.GetStereoViewMatrix((Camera.StereoscopicEye)eye) : main.worldToCameraMatrix;
                    var proj = stereo ? main.GetStereoProjectionMatrix((Camera.StereoscopicEye)eye) : main.projectionMatrix;
                    var vview = view * inv;
                    var n = vview.MultiplyVector(keep);
                    var p = vview.MultiplyPoint(door);
                    var d = -Vector3.Dot(n, p);
                    if (d >= -0.02f) continue; // the eye is already past the doorway: nothing to cut off against
                    // the eye in the destination: for lights, billboards and the like, which read the camera's own position
                    var c2w = vview.inverse * Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
                    _pc.transform.SetPositionAndRotation(c2w.GetColumn(3), Quaternion.LookRotation(c2w.GetColumn(2), c2w.GetColumn(1)));
                    _pc.worldToCameraMatrix = vview;
                    _pc.projectionMatrix = proj;
                    _pc.cullingMatrix = proj * vview;
                    _pc.projectionMatrix = _pc.CalculateObliqueMatrix(new Vector4(n.x, n.y, n.z, d));
                    _pc.targetTexture = _rt[eye];
                    if (fog.HasValue) { RenderSettings.fogStartDistance = fog.Value.x; RenderSettings.fogEndDistance = fog.Value.y; }
                    _pc.Render();
                    drew = true;
                }
            }
            finally
            {
                RenderSettings.fogStartDistance = oldStart; RenderSettings.fogEndDistance = oldEnd;
                _rendering = false;
                _pc.targetTexture = null;
            }
            if (!drew) return false;
            glass.SetTexture("_WindowL", _rt[0]);
            glass.SetTexture("_WindowR", stereo && _rt[1] != null ? _rt[1] : _rt[0]);
            return true;
        }

        void EnsureCamera(Camera main)
        {
            if (_pc != null) return;
            var go = new GameObject("Hypa Portal Camera") { hideFlags = HideFlags.HideAndDontSave };
            _pc = go.AddComponent<Camera>();
            _pc.enabled = false;
            _pc.stereoTargetEye = StereoTargetEyeMask.None;
            _pc.clearFlags = CameraClearFlags.SolidColor;
            _pc.backgroundColor = CampWorld.FogColor;
            _pc.cullingMask = 1 | (1 << CampWorld.DestLayer);   // the camp and its scenery, never the panels
            _pc.nearClipPlane = main.nearClipPlane;
            _pc.farClipPlane = Mathf.Min(main.farClipPlane, 120f);
            _pc.allowHDR = false;
            _pc.allowMSAA = false;
            _pc.useOcclusionCulling = false;
            _pc.depth = -100f;
        }

        void EnsureTextures(int w, int h)
        {
            w = Mathf.Max(64, Mathf.RoundToInt(w * Scale));
            h = Mathf.Max(64, Mathf.RoundToInt(h * Scale));
            if (_rt[0] != null && _w == w && _h == h) return;
            Release();
            _w = w; _h = h;
            for (var i = 0; i < 2; i++)
            {
                _rt[i] = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = "Portal view " + i, antiAliasing = 1, useMipMap = false };
                _rt[i].Create();
            }
        }

        void Release()
        {
            for (var i = 0; i < 2; i++) if (_rt[i] != null) { _rt[i].Release(); Object.Destroy(_rt[i]); _rt[i] = null; }
        }

        public void Dispose()
        {
            Release();
            if (_pc != null) Object.Destroy(_pc.gameObject);
        }
    }
}
