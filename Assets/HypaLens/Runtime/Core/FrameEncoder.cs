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
    }
}
