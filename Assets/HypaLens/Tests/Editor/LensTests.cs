using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using HypaLens.Sim;
using NUnit.Framework;
using UnityEngine;

namespace HypaLens.Tests
{
    public class LensTests
    {
        static LensController Make(IHypaLensApi api = null, Func<bool> online = null, bool blank = false)
            => new LensController(new SimFrameSource { ReturnBlankFrame = blank }, new SimRecognizer(), api ?? new SimHypaApi { LatencyMs = 0 }, online)
               { SourceLanguage = "English", TargetLanguage = "Yoruba" };

        static T Run<T>(Task<T> t) { Assert.IsTrue(t.Wait(5000), "timed out"); return t.Result; }

        static LensErrorKind KindOf(Func<Task> f)
        {
            var ex = Assert.Throws<AggregateException>(() => f().Wait(5000));
            return ((LensException)ex.InnerException).Kind;
        }

        OcrResult Capture(LensController c) => Run(c.CaptureAsync(CancellationToken.None));

        // ---------- Projection (labels pinned with the capture pose) ----------

        [Test] public void Projector_CentrePixel_IsStraightAhead()
        {
            var ray = FrameProjector.PixelToWorldRay(new CameraIntrinsics(900, 900, 640, 480), new Pose(Vector3.zero, Quaternion.identity), new Vector2(640, 480));
            Assert.That(Vector3.Angle(ray.direction, Vector3.forward), Is.LessThan(0.01f));
        }

        [Test] public void Projector_ImageUpIsWorldUp_RightIsRight()
        {
            var k = new CameraIntrinsics(900, 900, 640, 480);
            var pose = new Pose(Vector3.zero, Quaternion.identity);
            Assert.Greater(FrameProjector.PixelToWorldRay(k, pose, new Vector2(640, 100)).direction.y, 0f);
            Assert.Greater(FrameProjector.PixelToWorldRay(k, pose, new Vector2(1000, 480)).direction.x, 0f);
        }

        [Test] public void Projector_UsesCapturePose()
        {
            var pose = new Pose(new Vector3(1, 2, 3), Quaternion.Euler(0, 90, 0));
            var ray = FrameProjector.PixelToWorldRay(new CameraIntrinsics(900, 900, 640, 480), pose, new Vector2(640, 480));
            Assert.AreEqual(new Vector3(1, 2, 3), ray.origin);
            Assert.That(Vector3.Angle(ray.direction, Vector3.right), Is.LessThan(0.01f));
        }

        // ---------- Capture ----------

        [Test] public void Capture_WordsHaveLines_AndPixelsAreDropped()
        {
            var c = Make();
            var ocr = Capture(c);
            Assert.IsFalse(ocr.IsEmpty);
            Assert.AreSame(ocr.Lines[0], ocr.Lines[0].Words[0].Line);
            Assert.IsNull(c.LastFrameMeta.Pixels, "frame pixels must not outlive recognition");
        }

        [Test] public void Capture_NoText_IsNoTextFound()
            => Assert.AreEqual(LensErrorKind.NoTextFound, KindOf(() => Make(blank: true).CaptureAsync(CancellationToken.None)));

        // ---------- Resolve ----------

        [Test] public void Resolve_Word_TranslationMeaningAudio()
        {
            var c = Make();
            var fish = Capture(c).Lines[1].Words[1];
            LensResult partial = null;
            var r = Run(c.ResolveAsync(LensController.SelectWord(fish), CancellationToken.None, p => partial = p));
            Assert.AreEqual("eja", r.Translation);
            Assert.IsNotNull(r.Meaning);
            Assert.IsNotNull(r.SourceAudio);
            Assert.IsNotNull(r.TargetAudio);
            Assert.IsNotNull(partial, "card should update as soon as the translation arrives");
            Assert.AreEqual("eja", partial.Translation);
        }

        [Test] public void Resolve_Sentence_UsesLine_NoDictionary()
        {
            var c = Make();
            var r = Run(c.ResolveAsync(LensController.SelectSentence(Capture(c).Lines[1].Words[0]), CancellationToken.None));
            Assert.AreEqual("Fresh fish and rice", r.Selection.Text);
            Assert.IsNull(r.Meaning);
            Assert.IsNotEmpty(r.MeaningNote);
        }

        [Test] public void Resolve_AutoDetect_UsesDetectedLanguage()
        {
            var c = Make(); c.SourceLanguage = null;
            var r = Run(c.ResolveAsync(LensController.SelectWord(Capture(c).Lines[1].Words[1]), CancellationToken.None));
            Assert.AreEqual("English", r.SourceLanguage);
            Assert.IsNotNull(r.Meaning);
        }

        [Test] public void Resolve_AutoDetectReturnsNull_StillShowsTranslation_AndAsksForSource()
        {
            var api = new FakeApi { Detected = null };
            var c = Make(api); c.SourceLanguage = null;
            var r = Run(c.ResolveAsync(LensController.SelectWord(Capture(c).Lines[0].Words[0]), CancellationToken.None));
            Assert.AreEqual("T", r.Translation);
            Assert.IsNull(r.Meaning);
            StringAssert.Contains("From", r.MeaningNote);
        }

        [Test] public void Resolve_UnsupportedPair()
        {
            var c = Make(); c.SourceLanguage = "Hausa"; c.TargetLanguage = "Igbo";
            var w = Capture(c).Lines[0].Words[0];
            Assert.AreEqual(LensErrorKind.UnsupportedPair, KindOf(() => c.ResolveAsync(LensController.SelectWord(w), CancellationToken.None)));
        }

        [Test] public void Resolve_Offline_FromReachability_AndFromNetworkError()
        {
            var w = Capture(Make()).Lines[0].Words[0];
            Assert.AreEqual(LensErrorKind.Offline, KindOf(() => Make(online: () => false).ResolveAsync(LensController.SelectWord(w), CancellationToken.None)));
            Assert.AreEqual(LensErrorKind.Offline, KindOf(() => Make(new SimHypaApi { LatencyMs = 0, Offline = true }).ResolveAsync(LensController.SelectWord(w), CancellationToken.None)));
        }

        [Test] public void Resolve_DictionaryAndAudioFailures_NeverBlankTheCard()
        {
            var c = Make(new FakeApi { FailExtras = true });
            var r = Run(c.ResolveAsync(LensController.SelectWord(Capture(c).Lines[0].Words[0]), CancellationToken.None));
            Assert.AreEqual("T", r.Translation);
            Assert.IsNotEmpty(r.MeaningNote);
            Assert.IsNotEmpty(r.AudioNote);
        }

        [Test] public void Resolve_HypaError_KeepsSpecificMessage()
        {
            var c = Make(new FakeApi { TranslateError = new HypaException(HypaErrorKind.MissingKey, "no key") });
            var w = Capture(c).Lines[0].Words[0];
            var ex = Assert.Throws<AggregateException>(() => c.ResolveAsync(LensController.SelectWord(w), CancellationToken.None).Wait(5000));
            var le = (LensException)ex.InnerException;
            Assert.AreEqual(LensErrorKind.Api, le.Kind);
            StringAssert.Contains("hypa.local.json", le.UserMessage());
        }

        [Test] public void Resolve_CachesResults_AndPairChecks()
        {
            var api = new FakeApi();
            var c = Make(api);
            var sel = LensController.SelectWord(Capture(c).Lines[0].Words[0]);
            Run(c.ResolveAsync(sel, CancellationToken.None));
            Run(c.ResolveAsync(sel, CancellationToken.None));
            Assert.AreEqual(1, api.Translates);
            Assert.AreEqual(1, api.PairChecks);
        }

        [Test] public void EveryErrorKind_HasAMessage()
        {
            foreach (LensErrorKind k in Enum.GetValues(typeof(LensErrorKind)))
                Assert.IsNotEmpty(new LensException(k).UserMessage("English", "Yoruba"), k.ToString());
        }

        // ---------- Saved list + practice ----------

        [Test] public void Saved_SurvivesRestart_AndDedupes()
        {
            var path = Path.Combine(Path.GetTempPath(), "hypalens_test_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var r = new LensResult { Selection = new Selection { Text = "fish" }, Translation = "eja", SourceLanguage = "English", TargetLanguage = "Yoruba" };
                var a = new SavedListStore(path);
                Assert.IsTrue(a.Add(r));
                Assert.IsFalse(a.Add(r));
                var b = new SavedListStore(path); // simulated restart
                Assert.AreEqual(1, b.Items.Count);
                Assert.AreEqual("eja", b.Items[0].Translation);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test] public void Practice_FlipWrapAndLanguage()
        {
            var d = new PracticeDeck(new[]
            {
                new SavedItem { Source = "a", Translation = "A", SourceLanguage = "English", TargetLanguage = "Yoruba" },
                new SavedItem { Source = "b", Translation = "B" },
            });
            Assert.AreEqual("a", d.Face); Assert.AreEqual("English", d.FaceLanguage);
            d.Flip(); Assert.AreEqual("A", d.Face); Assert.AreEqual("Yoruba", d.FaceLanguage);
            d.Next(); Assert.AreEqual("b", d.Face);
            d.Next(); Assert.AreEqual("a", d.Face);
            d.Prev(); Assert.AreEqual("b", d.Face);
            Assert.IsTrue(new PracticeDeck(new SavedItem[0]).IsEmpty);
        }

        sealed class FakeApi : IHypaLensApi
        {
            public string Detected = "English";
            public bool FailExtras;
            public Exception TranslateError;
            public int Translates, PairChecks;

            public Task<IReadOnlyList<HypaLanguage>> ListLanguagesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<HypaLanguage>>(new HypaLanguage[0]);
            public Task<bool> CheckPairAsync(string s, string t, CancellationToken ct) { PairChecks++; return Task.FromResult(true); }
            public Task<HypaTranslation> TranslateAsync(string text, string s, string t, CancellationToken ct)
            {
                Translates++;
                if (TranslateError != null) throw TranslateError;
                return Task.FromResult(new HypaTranslation { Text = "T", DetectedLanguage = Detected });
            }
            public Task<string> LookUpAsync(string term, string lang, CancellationToken ct)
                => FailExtras ? throw new HypaException(HypaErrorKind.Server, "x") : Task.FromResult("meaning");
            public Task<HypaSpeech> SpeakAsync(string text, string lang, CancellationToken ct)
                => FailExtras ? throw new HypaException(HypaErrorKind.Server, "x") : Task.FromResult(new HypaSpeech { AudioUrl = "https://x" });
        }
    }
}
