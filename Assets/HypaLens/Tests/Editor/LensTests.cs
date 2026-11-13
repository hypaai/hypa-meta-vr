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
        // The helpers below block on tasks. Under Unity's main-thread SynchronizationContext the awaits inside the
        // controller would try to resume on this (blocked) thread and deadlock, so run each test without it.
        SynchronizationContext _unityContext;
        [SetUp] public void DropUnityContext() { _unityContext = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(null); }
        [TearDown] public void RestoreUnityContext() => SynchronizationContext.SetSynchronizationContext(_unityContext);

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

        // ---------- Word labels (readable pills) ----------

        [Test] public void WordLabel_PrintsTheWord_AndStaysPinchable()
        {
            var label = LensApp.NewWordLabel(new OcrWord { Text = "Welcome" }, new Vector2(0.05f, 0.035f), Vector3.zero, Quaternion.identity, null);
            try
            {
                var tm = label.GetComponentInChildren<TextMesh>();
                Assert.IsNotNull(tm, "label must print its word, not show an empty pill");
                Assert.AreEqual("Welcome", tm.text);
                Assert.IsNotNull(label.Target, "label must keep its pinch target");
                Assert.IsNotNull(label.Target.GetComponent<Collider>(), "pill needs a collider for the hand ray");
            }
            finally { UnityEngine.Object.DestroyImmediate(label.gameObject); }
        }

        [Test] public void FitPillToWord_GrowsForLongWords_KeepsShortOnes()
        {
            var grown = LensApp.FitPillToWord("Welcome", new Vector2(0.03f, 0.035f), 0.0175f);
            Assert.Greater(grown.x, 0.03f, "long word must widen the pill so the text fits");
            var kept = LensApp.FitPillToWord("a", new Vector2(0.05f, 0.035f), 0.0175f);
            Assert.AreEqual(0.05f, kept.x, "short word keeps the OCR-sized pill");
        }

        [Test] public void DefaultTarget_IsAnnang_AndSimListsIt()
        {
            var c = new LensController(new SimFrameSource(), new SimRecognizer(), new SimHypaApi { LatencyMs = 0 });
            Assert.AreEqual("Annang", c.TargetLanguage, "demo default must be Annang");
            var langs = Run(new SimHypaApi { LatencyMs = 0 }.ListLanguagesAsync(CancellationToken.None));
            var found = false;
            foreach (var l in langs) if (l.Name == "Annang") found = true;
            Assert.IsTrue(found, "Annang must appear in the language picker");
        }

        [Test] public void MenuTargets_StayAboveSimulatorMinimums()
        {
            Assert.GreaterOrEqual(LensTouch.MenuButtonH, 0.05f, "menu rows must stay tall enough for simulator hands");
            Assert.GreaterOrEqual(LensTouch.LauncherRadius, 0.04f, "Hypa button must stay a big round target");
            Assert.GreaterOrEqual(LensTouch.PickerRowH, 0.05f, "language rows must stay tall enough for simulator hands");
            // Six items in two columns: the whole menu is within one small hand movement of the launcher.
            var rows = (6 + LensTouch.MenuColumns - 1) / LensTouch.MenuColumns;
            var height = rows * LensTouch.MenuButtonH + (rows - 1) * LensTouch.MenuGap;
            Assert.LessOrEqual(height, 0.22f, "menu must stay compact (was a 0.4 m tall column)");
        }

        [Test] public void LanguageLabel_DropsTheBracketedVariant_BeforeTrimming()
        {
            Assert.AreEqual("To: Yoruba", LensApp.LanguageLabel("To: ", "Yoruba", 0.02f, 0.2f));
            Assert.AreEqual("To: Arabic (Libyan)", LensApp.LanguageLabel("To: ", "Arabic (Libyan)", 0.02f, 0.5f), "kept when it fits");
            Assert.AreEqual("To: Arabic", LensApp.LanguageLabel("To: ", "Arabic (Libyan)", 0.02f, 0.15f), "variant dropped when it does not");
            Assert.AreEqual("From: Auto", LensApp.LanguageLabel("From: ", "Auto", 0.02f, 0.05f), "nothing to drop: left for the ellipsis");
        }

        // ---------- Live mode: dwell trigger, region crop, label reuse ----------

        [Test] public void Dwell_FiresAfterASteadySecond_ThenAtMostOncePerSecond()
        {
            var d = new DwellTrigger { DwellSeconds = 1f, MinInterval = 1f, RadiusPx = 120f };
            var p = new Vector2(600, 400);
            Assert.IsFalse(d.Update(p, false, 0));
            Assert.IsFalse(d.Update(p + new Vector2(50, 0), false, 0.5), "small wobble keeps the dwell");
            Assert.IsFalse(d.Update(p, false, 0.9), "not yet");
            Assert.IsTrue(d.Update(p, false, 1.0), "a second on the same spot");
            Assert.IsFalse(d.Update(p, false, 1.5), "throttled");
            Assert.IsTrue(d.Update(p, false, 2.0), "still looking: one more run a second later");
            Assert.IsFalse(d.Update(p, true, 3.0), "never while a run is in progress");
            Assert.IsTrue(d.Update(p, false, 3.1), "and as soon as it is free");
        }

        [Test] public void Dwell_MovingOrLosingTheAim_StartsOver()
        {
            var d = new DwellTrigger { DwellSeconds = 1f, MinInterval = 1f, RadiusPx = 120f };
            d.Update(new Vector2(100, 100), false, 0);
            d.Update(new Vector2(700, 100), false, 0.8); // moved to new text
            Assert.IsFalse(d.Update(new Vector2(700, 100), false, 1.2), "the second has to pass on the new spot");
            Assert.IsTrue(d.Update(new Vector2(700, 100), false, 1.8));
            d.Update(null, false, 2.5); // hand lost
            Assert.IsFalse(d.Update(new Vector2(700, 100), false, 3.0), "back again: starts over");
            Assert.IsTrue(d.Update(new Vector2(700, 100), false, 4.0));
        }

        static CapturedFrame Frame4x3()
        {
            // pixel value encodes its image position: r = x, g = y (top-down). Stored bottom row first, as Unity does.
            var px = new Color32[12];
            for (var yTop = 0; yTop < 3; yTop++)
                for (var x = 0; x < 4; x++)
                    px[(2 - yTop) * 4 + x] = new Color32((byte)x, (byte)yTop, 0, 255);
            return new CapturedFrame { Width = 4, Height = 3, Pixels = px, Intrinsics = new CameraIntrinsics(4, 4, 2, 1.5f) };
        }

        [Test] public void Crop_TakesTheRightPixels_InUnityRowOrder_AndRemembersWhereItCameFrom()
        {
            var c = FrameCrop.Crop(Frame4x3(), new FrameRect(1, 1, 2, 2));
            Assert.AreEqual((2, 2, 1, 1, 4, 3), (c.Width, c.Height, c.CropX, c.CropY, c.FullWidth, c.FullHeight));
            Assert.IsTrue(c.IsCrop);
            // bottom row of the crop = image row 2; top row = image row 1
            Assert.AreEqual(new Color32(1, 2, 0, 255), c.Pixels[0]); Assert.AreEqual(new Color32(2, 2, 0, 255), c.Pixels[1]);
            Assert.AreEqual(new Color32(1, 1, 0, 255), c.Pixels[2]); Assert.AreEqual(new Color32(2, 1, 0, 255), c.Pixels[3]);
            Assert.IsNull(FrameCrop.Crop(Frame4x3(), new FrameRect(4, 0, 2, 2)), "a region outside the frame is nothing");
            var sim = FrameCrop.Crop(new CapturedFrame { Width = 1280, Height = 960, Pixels = new Color32[0] }, new FrameRect(100, 100, 200, 200));
            Assert.AreEqual(0, sim.Pixels.Length, "a pixel-less simulator frame still crops its geometry");
        }

        [Test] public void Crop_RegionAroundTheAim_StaysInsideTheFrame_AndBoxesComeBackInFullFramePixels()
        {
            var r = FrameCrop.RegionAround(new Vector2(0, 0), 1280, 960, 0.4f);
            Assert.AreEqual((0f, 0f, 512f, 384f), (r.X, r.Y, r.Width, r.Height), "clamped to the top-left corner");
            r = FrameCrop.RegionAround(new Vector2(640, 480), 1280, 960, 0.4f);
            Assert.AreEqual((384f, 288f), (r.X, r.Y), "centred");
            var ocr = new OcrResult();
            var line = new OcrLine { Text = "a b", Box = new FrameRect(10, 20, 100, 30) };
            line.Words.Add(new OcrWord { Text = "a", Box = new FrameRect(10, 20, 40, 30), Line = line });
            ocr.Lines.Add(line);
            FrameCrop.OffsetBack(ocr, new CapturedFrame { CropX = 300, CropY = 200, FullWidth = 1280, FullHeight = 960 });
            Assert.AreEqual((310f, 220f), (line.Words[0].Box.X, line.Words[0].Box.Y));
            Assert.AreEqual((310f, 220f), (line.Box.X, line.Box.Y));
            Assert.AreEqual(1280, ocr.FrameWidth);
        }

        [Test] public void Projector_WorldToPixel_IsTheInverseOfPixelToWorldRay()
        {
            var k = new CameraIntrinsics(900, 900, 640, 480);
            var pose = new Pose(new Vector3(0.3f, 1.6f, 0), Quaternion.Euler(10, 40, 0));
            var ray = FrameProjector.PixelToWorldRay(k, pose, new Vector2(200, 700));
            Assert.IsTrue(FrameProjector.WorldToPixel(k, pose, ray.GetPoint(1.7f), out var px));
            Assert.That(Vector2.Distance(px, new Vector2(200, 700)), Is.LessThan(0.01f));
            Assert.IsFalse(FrameProjector.WorldToPixel(k, pose, pose.position - pose.rotation * Vector3.forward, out _), "behind the camera");
        }

        [Test] public void LiveLabels_ReuseTheSameWordInTheSamePlace_NewOtherwise()
        {
            var s = new LiveLabelSet<string> { MatchDistance = 0.06f };
            s.BeginRun(); s.Add("L1", "Fish", new Vector3(1, 1, 2), 0);
            s.BeginRun();
            Assert.AreEqual("L1", s.Match("fish", new Vector3(1.03f, 1, 2), 1), "same word within 6 cm: reuse (case-insensitive)");
            Assert.IsNull(s.Match("Fish", new Vector3(1.5f, 1, 2), 1), "same word elsewhere: a new label");
            Assert.IsNull(s.Match("Rice", new Vector3(1, 1, 2), 1), "different word: a new label");
            Assert.IsNull(s.Match("fish", new Vector3(1.03f, 1, 2), 1), "a label is matched once per run");
        }

        [Test] public void LiveLabels_DroppedAfterThreeLooksWithoutTheWord_OrTwoSecondsOutOfView()
        {
            var s = new LiveLabelSet<string> { MissLimit = 3, KeepOutOfViewSeconds = 2f };
            s.Add("A", "Open", Vector3.zero, 0); s.Add("B", "Day", new Vector3(5, 0, 0), 0);
            Func<Vector3, bool> coversA = p => p.x < 1f;
            for (var i = 0; i < 2; i++) { s.BeginRun(); Assert.IsEmpty(s.EndRun(coversA), "two misses are forgiven"); }
            s.BeginRun(); Assert.AreEqual("A", s.Match("Open", Vector3.zero, 1), "found again: misses reset");
            Assert.IsEmpty(s.EndRun(coversA));
            for (var i = 0; i < 2; i++) { s.BeginRun(); s.EndRun(coversA); }
            s.BeginRun();
            Assert.AreEqual(new[] { "A" }, s.EndRun(coversA), "third miss in a row: dropped; B was never looked at");
            Assert.IsEmpty(s.Tick(p => true, 1.5), "in view: kept");
            Assert.IsEmpty(s.Tick(p => false, 3.0), "out of view for 1.5 s: kept");
            Assert.AreEqual(new[] { "B" }, s.Tick(p => false, 3.6), "out of view for over 2 s: dropped");
            Assert.AreEqual(0, s.Count);
        }

        [Test] public void SimRecognizer_CroppedFrame_SeesOnlyWordsInsideTheCrop_InCropPixels()
        {
            var full = Run(new SimRecognizer().RecognizeAsync(new CapturedFrame { Width = 1280, Height = 960, Pixels = new Color32[0] }, CancellationToken.None));
            var market = full.Lines[0].Words[3]; // "Welcome to the market"
            Assert.AreEqual("market", market.Text);
            var crop = FrameCrop.Crop(new CapturedFrame { Width = 1280, Height = 960, Pixels = new Color32[0] },
                new FrameRect(market.Box.X - 10, market.Box.Y - 10, market.Box.Width + 20, market.Box.Height + 20));
            var part = Run(new SimRecognizer().RecognizeAsync(crop, CancellationToken.None));
            Assert.AreEqual(1, part.Lines.Count);
            Assert.AreEqual(1, part.Lines[0].Words.Count, "only the word inside the crop");
            Assert.AreEqual("market", part.Lines[0].Words[0].Text);
            Assert.AreEqual(10f, part.Lines[0].Words[0].Box.X, 0.01f, "in the crop's own pixels");
        }

        [Test] public void SimRecognizer_AnchoredToTheWall_TextStaysPut_WhenTheHeadTurns()
        {
            var wall = new SimPlanePlacer();
            var anchor = new Pose(new Vector3(0, 1.6f, 0), Quaternion.identity);
            wall.SetWallFromPose(anchor, 1.0f);
            var rec = new SimRecognizer { Wall = wall };
            var k = new CameraIntrinsics(900, 900, 640, 480);
            CapturedFrame From(Pose p) => new CapturedFrame { Width = 1280, Height = 960, Pixels = new Color32[0], CameraPose = p, Intrinsics = k };

            var same = Run(rec.RecognizeAsync(From(anchor), CancellationToken.None));
            var plain = Run(new SimRecognizer().RecognizeAsync(From(anchor), CancellationToken.None));
            Assert.AreEqual(plain.Lines[1].Words[1].Box.X, same.Lines[1].Words[1].Box.X, 0.5f, "from the anchor pose the layout is the original one");

            var nudged = Run(rec.RecognizeAsync(From(new Pose(anchor.position, Quaternion.Euler(0, 5f, 0))), CancellationToken.None));
            Assert.Less(nudged.Lines[1].Words[1].Box.X, same.Lines[1].Words[1].Box.X - 50f, "turn right: the text slides left in the frame");

            var away = Run(rec.RecognizeAsync(From(new Pose(anchor.position, Quaternion.Euler(0, 70f, 0))), CancellationToken.None));
            Assert.IsTrue(away.IsEmpty, "look well away: the sign has left the frame");
        }

        [Test] public void LiveLabels_HardCap_DropsTheStalest()
        {
            var s = new LiveLabelSet<string> { MaxLabels = 3 };
            Assert.IsNull(s.Add("A", "a", Vector3.zero, 0));
            Assert.IsNull(s.Add("B", "b", Vector3.one, 5));
            Assert.IsNull(s.Add("C", "c", Vector3.up, 3));
            Assert.AreEqual("A", s.Add("D", "d", Vector3.right, 6), "the fourth pushes out the one seen longest ago");
            Assert.AreEqual(3, s.Count);
        }

        [Test] public void BaseLanguage_DropsTheVariant_OnlyWhenTheBaseExists()
        {
            var langs = new[] { new HypaLanguage { Name = "Arabic", Code = "ar" }, new HypaLanguage { Name = "Arabic (Libyan)", Code = "ar-LY" }, new HypaLanguage { Name = "Pidgin (Nigerian)", Code = "pcm" } };
            Assert.AreEqual("Arabic", LensApp.BaseLanguage("Arabic (Libyan)", langs));
            Assert.IsNull(LensApp.BaseLanguage("Pidgin (Nigerian)", langs), "no plain Pidgin in the list");
            Assert.IsNull(LensApp.BaseLanguage("Yoruba", langs));
            Assert.IsNull(LensApp.BaseLanguage(null, langs));
        }

        [Test] public void Capture_WithRegion_ReturnsBoxesInFullFramePixels()
        {
            var c = Make();
            var all = Capture(c);
            var fish = all.Lines[1].Words[1]; // "Fresh fish and rice"
            var region = new FrameRect(fish.Box.X - 5, fish.Box.Y - 5, fish.Box.Width + 10, fish.Box.Height + 10);
            var ocr = Run(c.CaptureAsync(CancellationToken.None, region));
            Assert.AreEqual(1, ocr.Lines.Count);
            Assert.AreEqual("fish", ocr.Lines[0].Words[0].Text);
            Assert.AreEqual(fish.Box.X, ocr.Lines[0].Words[0].Box.X, 0.01f, "same place as in the full frame");
            Assert.AreEqual(1280, c.LastFrameMeta.Width, "the frame meta is the full frame");
            Assert.AreEqual(LensErrorKind.NoTextFound, KindOf(() => c.CaptureAsync(CancellationToken.None, new FrameRect(0, 0, 50, 50))), "a patch with no text");
        }

        // ---------- HUD placement: stays put, re-centres only after a big turn, never while in use ----------

        static Pose Settle(LazyFollow f, Vector3 head, Quaternion rot, Pose p, bool hold = false)
        {
            for (var i = 0; i < 120; i++) p = f.Update(head, rot, p, hold, 1f / 60f);
            return p;
        }

        [Test] public void LazyFollow_FirstPlacement_InFrontAndBelowEyes_IgnoringPitch()
        {
            var f = new LazyFollow { Distance = 0.6f, Height = -0.17f };
            var lookingDown = Quaternion.Euler(40f, 0f, 0f);
            var p = f.Update(new Vector3(0, 1.5f, 0), lookingDown, default, false, 0.016f);
            Assert.That(Vector3.Distance(p.position, new Vector3(0, 1.33f, 0.6f)), Is.LessThan(0.001f), "0.6 m ahead, 0.17 m down, pitch ignored");
            Assert.Greater(new LazyFollow().Distance, 0.75f, "the HUD must sit beyond the hands or a hand ray runs level with it");
            Assert.That(Vector3.Angle(p.rotation * Vector3.forward, p.position - new Vector3(0, 1.5f, 0)), Is.LessThan(0.5f), "faces the eyes");
        }

        [Test] public void LazyFollow_SmallTurn_OrAnyTurnWhileInUse_DoesNotMoveIt()
        {
            var f = new LazyFollow();
            var head = new Vector3(0, 1.5f, 0);
            var p = f.Update(head, Quaternion.identity, default, false, 0.016f);
            var after = Settle(f, head, Quaternion.Euler(0, 20f, 0), p);
            Assert.AreEqual(p.position, after.position, "20 degrees is inside the dead zone");
            after = Settle(f, head, Quaternion.Euler(0, 90f, 0), p, hold: true);
            Assert.AreEqual(p.position, after.position, "never moves while the menu or hand is on it");
        }

        [Test] public void LazyFollow_BigTurn_GlidesBackInFront()
        {
            var f = new LazyFollow();
            var head = new Vector3(0, 1.5f, 0);
            var p = f.Update(head, Quaternion.identity, default, false, 0.016f);
            var one = f.Update(head, Quaternion.Euler(0, 60f, 0), p, false, 1f / 60f);
            Assert.AreNotEqual(p.position, one.position, "starts moving");
            Assert.That(Vector3.Distance(one.position, p.position), Is.LessThan(0.1f), "but glides rather than jumps");
            var end = Settle(f, head, Quaternion.Euler(0, 60f, 0), one);
            var expected = head + Quaternion.Euler(0, 60f, 0) * Vector3.forward * f.Distance + Vector3.up * f.Height;
            Assert.That(Vector3.Distance(end.position, expected), Is.LessThan(0.005f));
            Assert.IsFalse(f.IsGliding);
        }

        [Test] public void LazyFollow_TurnsInPlaceToSquareWithTheView_WithoutMoving()
        {
            var f = new LazyFollow();
            var head = new Vector3(0, 1.5f, 0);
            var p = f.Update(head, Quaternion.identity, default, false, 0.016f);
            var turned = Settle(f, head, Quaternion.Euler(0, 20f, 0), p); // inside the dead zone: stays put
            Assert.AreEqual(p.position, turned.position);
            var yaw = turned.rotation.eulerAngles.y;
            Assert.AreEqual(20f, yaw, 0.5f, "but squares up with where the head is looking");
            var pitch = turned.rotation.eulerAngles.x;
            Assert.That(pitch, Is.LessThan(40f).And.GreaterThan(0.5f), "still tilted up to meet the eyes");
        }

        [Test] public void MenuLayout_FiveItems_LastOneSpansTheRow_SixFillTheGrid()
        {
            const float w = 0.2f, h = 0.05f, gap = 0.01f, pad = 0.012f;
            var alone = MenuLayout.Cell(4, 5, 2, w, h, gap, pad);
            Assert.AreEqual(0f, alone.x, 1e-5f, "fifth of five is centred");
            Assert.AreEqual(2 * w + gap, alone.width, 1e-5f, "and spans both columns");
            var left = MenuLayout.Cell(2, 5, 2, w, h, gap, pad);
            var right = MenuLayout.Cell(3, 5, 2, w, h, gap, pad);
            Assert.AreEqual(w, left.width, 1e-5f);
            Assert.AreEqual(left.y, right.y, 1e-5f, "same row");
            Assert.Greater(right.x, left.x);
            Assert.Greater(left.y, alone.y, "the lone item sits on the bottom row");
            var sixth = MenuLayout.Cell(5, 6, 2, w, h, gap, pad);
            Assert.AreEqual(w, sixth.width, 1e-5f, "six items: a plain 2x3 grid");
            var size = MenuLayout.PanelSize(5, 2, w, h, gap, pad);
            Assert.AreEqual(2 * w + gap + 2 * pad, size.x, 1e-5f);
            Assert.AreEqual(3 * h + 2 * gap + 2 * pad, size.y, 1e-5f);
        }

        [Test] public void LazyFollow_Reset_SnapsToTheNewHeadAtOnce()
        {
            var f = new LazyFollow();
            var p = f.Update(new Vector3(0, 1.13f, 0), Quaternion.identity, default, false, 0.016f); // before the XR session: wrong height
            f.Reset();
            var snapped = f.Update(new Vector3(0, 1.70f, 0), Quaternion.identity, p, false, 0.016f);
            Assert.That(Vector3.Distance(snapped.position, new Vector3(0, 1.70f + f.Height, f.Distance)), Is.LessThan(0.001f), "no glide: straight to eye height");
        }

        [Test] public void LazyFollow_WalkingAway_BringsItAlong()
        {
            var f = new LazyFollow();
            var p = f.Update(new Vector3(0, 1.5f, 0), Quaternion.identity, default, false, 0.016f);
            var end = Settle(f, new Vector3(0, 1.5f, 0.5f), Quaternion.identity, p);
            Assert.That(Vector3.Distance(end.position, new Vector3(0, 1.5f + f.Height, 0.5f + f.Distance)), Is.LessThan(0.005f));
        }

        // ---------- Launcher menu: opens on hover, closes itself after the pointer leaves ----------

        [Test] public void MenuLinger_ClosesOnlyAfterThePointerHasLeftForAWhile()
        {
            var l = new MenuLinger { CloseAfterSeconds = 1.5f };
            Assert.IsFalse(l.Update(true, true, false, 0), "pointer inside: stay open");
            Assert.IsFalse(l.Update(true, false, false, 1), "just left: not yet");
            Assert.IsFalse(l.Update(true, false, false, 2.4), "still inside the grace period");
            Assert.IsTrue(l.Update(true, false, false, 2.5), "grace over: close");
            Assert.IsFalse(l.Update(false, false, false, 3), "closed menus do nothing");
        }

        [Test] public void MenuLinger_ForgivesAWander_AndNeverClosesMidPinch()
        {
            var l = new MenuLinger { CloseAfterSeconds = 1.5f };
            l.Update(true, false, false, 0);
            Assert.IsFalse(l.Update(true, true, false, 1), "came back: timer resets");
            Assert.IsFalse(l.Update(true, false, false, 2));
            Assert.IsFalse(l.Update(true, false, false, 3.4), "timer restarted when the pointer left again");
            Assert.IsFalse(l.Update(true, false, true, 3.6), "pinching (e.g. a word) keeps it open");
            Assert.IsFalse(l.Update(true, false, false, 4), "released: timer starts fresh");
            Assert.IsTrue(l.Update(true, false, false, 5.5));
        }

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
            HypaSpeech replayed = null;
            Run(c.ResolveAsync(sel, CancellationToken.None, null, a => replayed = a));
            Assert.AreEqual(1, api.Translates);
            Assert.AreEqual(1, api.PairChecks);
            Assert.IsNotNull(replayed, "a cached result still plays its audio");
        }

        [Test] public void Resolve_PlaysTargetAudio_BeforeMeaningArrives()
        {
            var gate = new TaskCompletionSource<string>();
            var c = Make(new FakeApi { LookUpGate = gate });
            HypaSpeech played = null;
            var task = c.ResolveAsync(LensController.SelectWord(Capture(c).Lines[0].Words[0]), CancellationToken.None, null, a => played = a);
            Assert.IsNotNull(played, "target audio should play without waiting for the dictionary");
            Assert.IsFalse(task.IsCompleted);
            gate.SetResult("meaning");
            Assert.AreEqual("meaning", Run(task).Meaning);
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
            public TaskCompletionSource<string> LookUpGate;
            public Task<string> LookUpAsync(string term, string lang, CancellationToken ct)
                => FailExtras ? throw new HypaException(HypaErrorKind.Server, "x") : LookUpGate?.Task ?? Task.FromResult("meaning");
            public Task<HypaSpeech> SpeakAsync(string text, string lang, CancellationToken ct)
                => FailExtras ? throw new HypaException(HypaErrorKind.Server, "x") : Task.FromResult(new HypaSpeech { AudioUrl = "https://x" });
        }
    }
}
