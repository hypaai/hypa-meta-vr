using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HypaLens.Quest
{
    /// <summary>
    /// On-headset text recognition with Google ML Kit (bundled Latin model, no Google Play services needed), through
    /// the Java bridge in Assets/HypaLens/Plugins/Android/HypaMlKit.androidlib. Returns every line and word with its
    /// box in frame pixels. Android player only; <see cref="TryCreate"/> returns null elsewhere (editor, simulator),
    /// where LensApp shows a clear message instead.
    ///
    /// Threading: each call runs on its own thread that attaches to the JVM and detaches when done (a thread-pool
    /// thread left attached would abort the app when it exits).
    /// </summary>
    public sealed class MlKitRecognizer : ITextRecognizer
    {
        const string JavaClass = "ai.hypa.lens.MlKitText";

        MlKitRecognizer() { }

        /// <summary>Main thread. Null when not running on an Android device or the bridge/ML Kit is missing.</summary>
        public static MlKitRecognizer TryCreate()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var c = new AndroidJavaClass(JavaClass))
                    if (c.CallStatic<bool>("isAvailable")) return new MlKitRecognizer();
                Debug.LogWarning("Hypa Lens: ML Kit text recognition isn't bundled in this build.");
            }
            catch (Exception e) { Debug.LogWarning("Hypa Lens: ML Kit bridge not found: " + e.Message); }
#endif
            return null;
        }

        public Task<OcrResult> RecognizeAsync(CapturedFrame frame, CancellationToken ct)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var tcs = new TaskCompletionSource<OcrResult>();
            var w = frame.Width; var h = frame.Height; var px = frame.Pixels;
            var t = new Thread(() =>
            {
                AndroidJNI.AttachCurrentThread();
                try
                {
                    ct.ThrowIfCancellationRequested();
                    string json;
                    using (var c = new AndroidJavaClass(JavaClass))
                        json = c.CallStatic<string>("recognize", ToTopDownRgba(px, w, h), w, h);
                    tcs.TrySetResult(Parse(json, w, h));
                }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); }
                catch (Exception e) { tcs.TrySetException(new LensException(LensErrorKind.Recognition, "ML Kit: " + e.Message, e)); }
                finally { AndroidJNI.DetachCurrentThread(); }
            }) { IsBackground = true, Name = "Hypa ML Kit" };
            t.Start();
            return tcs.Task;
#else
            throw new LensException(LensErrorKind.Recognition, "ML Kit runs only on the headset.");
#endif
        }

        /// <summary>Unity-order Color32 (bottom row first) to top-down RGBA bytes, the layout Android's ARGB_8888 Bitmap expects.</summary>
        public static sbyte[] ToTopDownRgba(Color32[] px, int w, int h)
        {
            var b = new sbyte[w * h * 4];
            var i = 0;
            for (var y = h - 1; y >= 0; y--)
            {
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    var c = px[row + x];
                    b[i++] = unchecked((sbyte)c.r); b[i++] = unchecked((sbyte)c.g); b[i++] = unchecked((sbyte)c.b); b[i++] = unchecked((sbyte)c.a);
                }
            }
            return b;
        }

        // ---- JSON from MlKitText.recognize: {"lines":[{"text","x","y","w","h","words":[{"text","x","y","w","h"}]}]} ----
#pragma warning disable 0649 // assigned by JsonUtility
        [Serializable] class JBox { public string text; public int x, y, w, h; }
        [Serializable] class JLine { public string text; public int x, y, w, h; public JBox[] words; }
        [Serializable] class JResult { public JLine[] lines; }
#pragma warning restore 0649

        public static OcrResult Parse(string json, int frameWidth, int frameHeight)
        {
            var r = new OcrResult { FrameWidth = frameWidth, FrameHeight = frameHeight };
            var parsed = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<JResult>(json);
            if (parsed?.lines == null) return r;
            foreach (var jl in parsed.lines)
            {
                if (jl == null || string.IsNullOrWhiteSpace(jl.text)) continue;
                var line = new OcrLine { Text = jl.text, Box = new FrameRect(jl.x, jl.y, jl.w, jl.h) };
                if (jl.words != null)
                    foreach (var jw in jl.words)
                        if (jw != null && !string.IsNullOrWhiteSpace(jw.text))
                            line.Words.Add(new OcrWord { Text = jw.text, Box = new FrameRect(jw.x, jw.y, jw.w, jw.h), Line = line });
                if (line.Words.Count > 0) r.Lines.Add(line);
            }
            return r;
        }
    }
}
