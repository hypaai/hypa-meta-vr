using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;

namespace HypaLens.Sim
{
    // Lets the whole Lens flow run in the Unity Editor or the Meta XR Simulator without a headset.
    // Canned data only; no Hypa logic. The real API can still be used from the editor (LensApp > Use Simulated Api off).

    public sealed class SimFrameSource : IFrameSource
    {
        public bool ReturnBlankFrame;   // N in the sim: test "no text found"
        public Task<CapturedFrame> CaptureAsync(CancellationToken ct)
        {
            var cam = Camera.main;
            var pose = cam != null ? new Pose(cam.transform.position, cam.transform.rotation) : Pose.identity;
            return Task.FromResult(new CapturedFrame
            {
                Width = ReturnBlankFrame ? 0 : 1280, Height = 960,
                Pixels = new Color32[0],
                CameraPose = pose,
                Intrinsics = new CameraIntrinsics(900f, 900f, 640f, 480f),
            });
        }
    }

    /// <summary>
    /// A fake menu laid out like a real photo: word and line boxes in frame pixels. With a <see cref="Wall"/> the menu
    /// is printed on that wall once (where the first capture looked) and each frame sees it from its own pose, as a
    /// real sign would: look away and it leaves the frame, look back and it is where it was, so live mode's label
    /// reuse behaves as on a headset. Without a wall the menu is centred in every frame. A cropped frame (live mode)
    /// sees only the words inside the crop, in the crop's own pixels, as ML Kit would.
    /// </summary>
    public sealed class SimRecognizer : ITextRecognizer
    {
        static readonly string[] Menu = { "Welcome to the market", "Fresh fish and rice", "Please pay at the counter", "Open every day" };

        /// <summary>The simulator's wall; the menu is anchored to it once it has been placed.</summary>
        public SimPlanePlacer Wall;

        public async Task<OcrResult> RecognizeAsync(CapturedFrame frame, CancellationToken ct)
        {
            await Task.Delay(120, ct);
            var r = new OcrResult { FrameWidth = Math.Max(frame.Width, 1), FrameHeight = frame.Height };
            var fullW = frame.FullWidth > 0 ? frame.FullWidth : frame.Width;
            var fullH = frame.FullHeight > 0 ? frame.FullHeight : frame.Height;
            if (fullW == 0) return r;
            var crop = frame.IsCrop ? new FrameRect(frame.CropX, frame.CropY, frame.Width, frame.Height) : new FrameRect(0, 0, fullW, fullH);
            var anchored = Wall != null && Wall.HasWall;

            const float wordH = 60f, charW = 30f, gap = 24f, top = 260f, lineGap = 110f;
            for (var li = 0; li < Menu.Length; li++)
            {
                var words = Menu[li].Split(' ');
                var total = -gap;
                foreach (var w in words) total += w.Length * charW + gap;
                var x = (fullW - total) * 0.5f;
                var y = top + li * lineGap;
                var line = new OcrLine { Text = Menu[li] };
                float lx0 = float.MaxValue, ly0 = float.MaxValue, lx1 = float.MinValue, ly1 = float.MinValue;
                foreach (var w in words)
                {
                    var ww = w.Length * charW;
                    var box = new FrameRect(x, y, ww, wordH);
                    x += ww + gap;
                    if (anchored && !SeenFromHere(box, frame, out box)) continue;
                    if (!FrameCrop.Contains(crop, box.Center)) continue;
                    line.Words.Add(new OcrWord { Text = w, Box = new FrameRect(box.X - crop.X, box.Y - crop.Y, box.Width, box.Height), Line = line });
                    lx0 = Math.Min(lx0, box.X); ly0 = Math.Min(ly0, box.Y); lx1 = Math.Max(lx1, box.X + box.Width); ly1 = Math.Max(ly1, box.Y + box.Height);
                }
                if (line.Words.Count == 0) continue;
                line.Box = new FrameRect(lx0 - crop.X, ly0 - crop.Y, lx1 - lx0, ly1 - ly0);
                r.Lines.Add(line);
            }
            return r;
        }

        /// <summary>Where a word printed on the wall (laid out from the wall's anchor pose) lands in this frame.</summary>
        bool SeenFromHere(FrameRect printed, CapturedFrame frame, out FrameRect box)
        {
            box = default;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var corner in new[] { new Vector2(printed.X, printed.Y), new Vector2(printed.X + printed.Width, printed.Y),
                                           new Vector2(printed.X, printed.Y + printed.Height), new Vector2(printed.X + printed.Width, printed.Y + printed.Height) })
            {
                var ray = FrameProjector.PixelToWorldRay(frame.Intrinsics, Wall.AnchorPose, corner);
                if (!Wall.TryPlace(ray, out var world, out _)) return false;
                if (!FrameProjector.WorldToPixel(frame.Intrinsics, frame.CameraPose, world, out var px)) return false;
                x0 = Math.Min(x0, px.x); y0 = Math.Min(y0, px.y); x1 = Math.Max(x1, px.x); y1 = Math.Max(y1, px.y);
            }
            box = new FrameRect(x0, y0, x1 - x0, y1 - y0);
            return true;
        }
    }

    public sealed class SimHypaApi : IHypaLensApi
    {
        public bool Offline;        // O in the sim
        public int LatencyMs = 150;

        static readonly HypaLanguage[] Langs =
        {
            L("Annang", "ann"), L("English", "en"), L("Yoruba", "yo"), L("Hausa", "ha"), L("Igbo", "ig"), L("French", "fr"), L("Spanish", "es"),
        };
        static HypaLanguage L(string n, string c) => new HypaLanguage { Name = n, Code = c, Voices = new[] { "female" } };

        static readonly Dictionary<string, string> Yoruba = new Dictionary<string, string>
        {
            { "fresh", "tuntun" }, { "fish", "eja" }, { "rice", "iresi" }, { "market", "ọjà" }, { "open", "ṣí" }, { "pay", "sanwó" },
        };

        async Task Gate(CancellationToken ct)
        {
            await Task.Delay(LatencyMs, ct);
            if (Offline) throw new HypaException(HypaErrorKind.Offline, "sim offline");
        }

        public async Task<IReadOnlyList<HypaLanguage>> ListLanguagesAsync(CancellationToken ct) { await Gate(ct); return Langs; }

        public async Task<bool> CheckPairAsync(string s, string t, CancellationToken ct)
        {
            await Gate(ct);
            return !(s == "Hausa" && t == "Igbo"); // one unsupported pair to exercise the error card
        }

        public async Task<HypaTranslation> TranslateAsync(string text, string s, string t, CancellationToken ct)
        {
            await Gate(ct);
            var outText = t == "Yoruba" && Yoruba.TryGetValue(text.ToLowerInvariant(), out var v) ? v : $"[{t}] {text}";
            return new HypaTranslation { Text = outText, DetectedLanguage = s ?? "English" };
        }

        public async Task<string> LookUpAsync(string term, string lang, CancellationToken ct)
        {
            await Gate(ct);
            return term.Length < 3 ? null : $"(sim) \"{term}\": a common {lang} word used in everyday speech.";
        }

        public async Task<HypaSpeech> SpeakAsync(string text, string lang, CancellationToken ct)
        {
            await Gate(ct);
            return new HypaSpeech { AudioBytes = SimTone.MakeWav(220f + (text.Length % 8) * 40f, 0.25f), Mime = "audio/wav" };
        }

        public async Task<HypaImageTranslation> TranslateImageAsync(byte[] jpeg, string s, string t, CancellationToken ct)
        {
            await Gate(ct);
            const string text = "Welcome to the market. Fresh fish and rice. Please pay at the counter. Open every day.";
            return new HypaImageTranslation { Text = text, Translation = $"[{t}] {text}", Language = s ?? "English" };
        }
    }

    static class SimTone
    {
        public static byte[] MakeWav(float hz, float seconds)
        {
            const int rate = 16000;
            var n = (int)(rate * seconds);
            var data = new byte[44 + n * 2];
            void Tag(int o, string s) { for (var k = 0; k < s.Length; k++) data[o + k] = (byte)s[k]; }
            void I32(int o, int v) => BitConverter.GetBytes(v).CopyTo(data, o);
            void I16(int o, short v) => BitConverter.GetBytes(v).CopyTo(data, o);
            Tag(0, "RIFF"); I32(4, 36 + n * 2); Tag(8, "WAVE"); Tag(12, "fmt "); I32(16, 16); I16(20, 1); I16(22, 1);
            I32(24, rate); I32(28, rate * 2); I16(32, 2); I16(34, 16); Tag(36, "data"); I32(40, n * 2);
            for (var i = 0; i < n; i++)
            {
                var env = Math.Min(1f, Math.Min(i, n - i) / (rate * 0.02f));
                I16(44 + i * 2, (short)(Math.Sin(2 * Math.PI * hz * i / rate) * 8000f * env));
            }
            return data;
        }
    }

    /// <summary>Pins labels to an imaginary wall a fixed distance in front of the capture pose.</summary>
    public sealed class SimPlanePlacer : ISurfacePlacer
    {
        Vector3 _point, _normal = Vector3.back;
        public bool HasWall { get; private set; }
        /// <summary>The pose the wall was placed from (the fake menu is printed as seen from here).</summary>
        public Pose AnchorPose { get; private set; }
        public void SetWallFromPose(Pose p, float distance)
        {
            var fwd = p.rotation * Vector3.forward;
            _normal = -fwd;
            _point = p.position + fwd * distance;
            AnchorPose = p;
            HasWall = true;
        }
        public bool TryPlace(Ray ray, out Vector3 position, out Vector3 normal)
        {
            if (new Plane(_normal, _point).Raycast(ray, out var d)) { position = ray.GetPoint(d); normal = _normal; return true; }
            position = default; normal = default; return false;
        }
    }
}
