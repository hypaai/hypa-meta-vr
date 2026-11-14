using UnityEngine;

namespace HypaLens
{
    /// <summary>Turns a captured frame into JPEG bytes for the Caption feature. Main thread (uses a temporary texture).</summary>
    public static class FrameEncoder
    {
        /// <summary>Null when the frame has no pixels (the simulator). Quality 80 keeps a 1280x960 frame around 150 KB.</summary>
        public static byte[] ToJpeg(CapturedFrame f, int quality = 80)
        {
            if (f == null || f.Width <= 0 || f.Height <= 0 || f.Pixels == null || f.Pixels.Length < f.Width * f.Height) return null;
            var tex = new Texture2D(f.Width, f.Height, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(f.Pixels);
                tex.Apply(false);
                return ImageConversion.EncodeToJPG(tex, quality);
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(tex); else Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Editor stand-in for the passthrough camera: renders the main camera (with <paramref name="hide"/>, the HUD,
        /// switched off so the app's own UI isn't read) into a 1280x960 image and encodes it as JPEG.
        /// </summary>
        public static byte[] ViewSnapshotJpeg(params Transform[] hide)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            // Only the room goes in the picture: every piece of UI (instructions, buttons, the pointer) is hidden for it.
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (var t in hide ?? new Transform[0])
                if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden.Add(t.gameObject); }
            var rt = RenderTexture.GetTemporary(1280, 960, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
            var tex = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
                tex.Apply(false);
                return ImageConversion.EncodeToJPG(tex, 80);
            }
            finally
            {
                cam.targetTexture = prevTarget; RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(tex);
                foreach (var go in hidden) if (go != null) go.SetActive(true);
            }
        }
    }
}
