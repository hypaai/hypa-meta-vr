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

    /// <summary>A fake menu laid out like a real photo: word and line boxes in frame pixels.</summary>
    public sealed class SimRecognizer : ITextRecognizer
    {
        static readonly string[] Menu = { "Welcome to the market", "Fresh fish and rice", "Please pay at the counter", "Open every day" };

        public async Task<OcrResult> RecognizeAsync(CapturedFrame frame, CancellationToken ct)
        {
            await Task.Delay(120, ct);
            var r = new OcrResult { FrameWidth = Math.Max(frame.Width, 1), FrameHeight = frame.Height };
            if (frame.Width == 0) return r;

            const float wordH = 60f, charW = 30f, gap = 24f, top = 260f, lineGap = 110f;
            for (var li = 0; li < Menu.Length; li++)
            {
                var words = Menu[li].Split(' ');
                var total = -gap;
                foreach (var w in words) total += w.Length * charW + gap;
                var x = (frame.Width - total) * 0.5f;
                var y = top + li * lineGap;
                var line = new OcrLine { Text = Menu[li], Box = new FrameRect(x, y, total, wordH) };
                foreach (var w in words)
                {
                    var ww = w.Length * charW;
                    line.Words.Add(new OcrWord { Text = w, Box = new FrameRect(x, y, ww, wordH), Line = line });
                    x += ww + gap;
                }
                r.Lines.Add(line);
            }
            return r;
        }
    }

    public sealed class SimHypaApi : IHypaLensApi
    {
        public bool Offline;        // O in the sim
        public int LatencyMs = 150;

        static readonly HypaLanguage[] Langs =
        {
            L("English", "en"), L("Yoruba", "yo"), L("Hausa", "ha"), L("Igbo", "ig"), L("French", "fr"), L("Spanish", "es"),
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
        public void SetWallFromPose(Pose p, float distance)
        {
            var fwd = p.rotation * Vector3.forward;
            _normal = -fwd;
            _point = p.position + fwd * distance;
        }
        public bool TryPlace(Ray ray, out Vector3 position, out Vector3 normal)
        {
            if (new Plane(_normal, _point).Raycast(ray, out var d)) { position = ray.GetPoint(d); normal = _normal; return true; }
            position = default; normal = default; return false;
        }
    }
}
