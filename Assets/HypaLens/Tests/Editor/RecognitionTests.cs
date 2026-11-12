using NUnit.Framework;
using UnityEngine;

namespace HypaLens.Tests
{
    public class RecognitionTests
    {
        // ---------- Camera intrinsics: must match MRUK PassthroughCameraAccess.ViewportPointToRay ----------

        /// <summary>Independent copy of MRUK v209's ViewportPointToLocalRay math (bottom-up viewport).</summary>
        static Vector3 MrukDir(Vector2 f, Vector2 pp, Vector2Int sensor, Vector2Int cur, Vector2 vp)
        {
            var scale = (Vector2)cur / (Vector2)sensor;
            scale /= Mathf.Max(scale.x, scale.y);
            var crop = new Rect(sensor.x * (1f - scale.x) * 0.5f, sensor.y * (1f - scale.y) * 0.5f, sensor.x * scale.x, sensor.y * scale.y);
            var ppUp = new Vector2(pp.x, sensor.y - pp.y);
            return new Vector3((crop.x + crop.width * vp.x - ppUp.x) / f.x, (crop.y + crop.height * vp.y - ppUp.y) / f.y, 1f).normalized;
        }

        [TestCase(1280, 1280, 1280, 960)]   // square sensor, 4:3 image (crop top/bottom)
        [TestCase(1280, 960, 1280, 960)]    // same aspect
        [TestCase(1280, 960, 640, 480)]     // downscaled
        [TestCase(1280, 960, 960, 960)]     // square image (crop left/right)
        public void Intrinsics_FromSensor_MatchesMruk(int sw, int sh, int iw, int ih)
        {
            var f = new Vector2(870f, 868f); var pp = new Vector2(sw * 0.52f, sh * 0.47f);
            var sensor = new Vector2Int(sw, sh); var image = new Vector2Int(iw, ih);
            var k = CameraIntrinsics.FromSensor(f, pp, sensor, image);
            foreach (var vp in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.1f, 0.9f), new Vector2(0.95f, 0.05f), new Vector2(0.3f, 0.6f) })
            {
                var pixelTopDown = new Vector2(vp.x * iw, (1f - vp.y) * ih);
                var ours = FrameProjector.PixelToWorldRay(k, new Pose(Vector3.zero, Quaternion.identity), pixelTopDown).direction;
                Assert.That(Vector3.Angle(ours, MrukDir(f, pp, sensor, image, vp)), Is.LessThan(0.01f), $"viewport {vp}");
            }
        }

        // ---------- ML Kit bridge (C# side; the Java side runs only on the headset) ----------

        [Test] public void MlKit_ParsesLinesAndWords_SkipsBlanks()
        {
            const string json = "{\"lines\":[{\"text\":\"Fresh fish\",\"x\":10,\"y\":20,\"w\":200,\"h\":40,\"words\":[" +
                "{\"text\":\"Fresh\",\"x\":10,\"y\":20,\"w\":90,\"h\":40},{\"text\":\"fish\",\"x\":120,\"y\":20,\"w\":90,\"h\":40}]}," +
                "{\"text\":\" \",\"x\":0,\"y\":0,\"w\":1,\"h\":1,\"words\":[]}]}";
            var r = HypaLens.Quest.MlKitRecognizer.Parse(json, 1280, 960);
            Assert.AreEqual(1, r.Lines.Count);
            Assert.AreEqual(2, r.Lines[0].Words.Count);
            Assert.AreEqual(120f, r.Lines[0].Words[1].Box.X);
            Assert.AreSame(r.Lines[0], r.Lines[0].Words[1].Line);
            Assert.IsTrue(HypaLens.Quest.MlKitRecognizer.Parse("{\"lines\":[]}", 1, 1).IsEmpty);
            Assert.IsTrue(HypaLens.Quest.MlKitRecognizer.Parse(null, 1, 1).IsEmpty);
        }

        [Test] public void MlKit_PixelsFlippedToTopRowFirst()
        {
            // 1x2 image in Unity order: [0] = bottom (red), [1] = top (blue). Android wants the top row first.
            var b = HypaLens.Quest.MlKitRecognizer.ToTopDownRgba(new[] { new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255) }, 1, 2);
            Assert.AreEqual(8, b.Length);
            Assert.AreEqual(0, b[0]); Assert.AreEqual(unchecked((sbyte)255), b[2]);   // first pixel = blue (top)
            Assert.AreEqual(unchecked((sbyte)255), b[4]); Assert.AreEqual(0, b[6]);   // second = red (bottom)
        }
    }
}
