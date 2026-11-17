using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    /// <summary>The drum game after The King's Magic Drum: hear the king's beat, play it back on the two drums, a dish for each, dawn after the last.</summary>
    public class DrumRulesTests
    {
        const float Dt = 0.01f;

        static void Run(DrumRules r, float seconds) { for (var t = 0f; t < seconds; t += Dt) r.Tick(Dt); }

        /// <summary>Waits through the call (and anything before it) until it's your turn.</summary>
        static void ToAnswer(DrumRules r)
        {
            for (var k = 0; k < 3000 && r.Phase != DrumPhase.Answer; k++) r.Tick(Dt);
            Assert.AreEqual(DrumPhase.Answer, r.Phase);
        }

        /// <summary>Plays the current beat back with its gaps stretched by <paramref name="stretch"/>; returns the last stroke's result.</summary>
        static StrokeResult PlayBack(DrumRules r, float stretch = 1f)
        {
            var beat = r.Current;
            var result = StrokeResult.Ignored;
            for (var i = 0; i < beat.Length; i++)
            {
                if (i > 0) Run(r, (beat[i].T - beat[i - 1].T) * stretch);
                result = r.Stroke(beat[i].Tone);
            }
            return result;
        }

        [Test] public void EachBeatPlayedBack_ServesADish_ThenDawn()
        {
            var r = new DrumRules();
            Assert.AreEqual(DrumPhase.Intro, r.Phase);
            Assert.AreEqual(StrokeResult.Ignored, r.Stroke(Tone.Low), "nothing counts before your turn");
            for (var dish = 0; dish < DrumRules.Patterns.Length; dish++)
            {
                ToAnswer(r);
                Assert.AreEqual(StrokeResult.Done, PlayBack(r), $"beat {dish}");
                Assert.AreEqual(DrumPhase.Feast, r.Phase);
                Assert.AreEqual(dish + 1, r.Served);
            }
            Run(r, DrumRules.FeastTime + 0.1f);
            Assert.IsTrue(r.Over);
            Assert.AreEqual(0, r.Misses);
            Assert.AreEqual(DrumRules.Patterns.Length, DrumRules.Dishes.Length, "one dish for each beat");
        }

        [Test] public void EveryBeat_IsABar_OnBothDrums_AndTheCallIsWholeBars()
        {
            foreach (var p in DrumRules.Patterns)
            {
                Assert.AreEqual(DrumRules.StepsPerBar, p.Length, p);
                Assert.AreEqual('L', p[0], $"{p}: starts on the low drum, on the bar line");
            }
            var r = new DrumRules();
            Assert.AreEqual(0f, r.CallLength % DrumRules.Bar, 1e-4f, "the bell runs straight on into your turn");
            Assert.AreEqual(DrumRules.Bar, r.CallAt(0), 1e-4f, "a bar of the bell first");
            var both = false;
            foreach (var p in DrumRules.Patterns) if (p.Contains("L") && p.Contains("H")) both = true;
            Assert.IsTrue(both);
        }

        [Test] public void YourTurn_StartsOnTheBarLine()
        {
            var r = new DrumRules();
            ToAnswer(r);
            Assert.Less(r.T, Dt + 1e-4f, "the frame's overshoot is kept, so the bell's grid doesn't slip");
            Assert.IsTrue(r.Keeping);
        }

        [Test] public void Strokes_DuringTheCall_DoNotCount()
        {
            var r = new DrumRules();
            Run(r, DrumRules.IntroTime + 0.1f);
            Assert.AreEqual(DrumPhase.Call, r.Phase);
            Assert.AreEqual(StrokeResult.Ignored, r.Stroke(Tone.Low));
            Assert.AreEqual(0, r.Next);
        }

        [Test] public void OnlyTheGapsCount_ASlowerOrFasterBeatStillServes()
        {
            var r = new DrumRules();
            ToAnswer(r);
            Assert.AreEqual(StrokeResult.Done, PlayBack(r, 1.25f), "a little slow");
            ToAnswer(r);
            Assert.AreEqual(StrokeResult.Done, PlayBack(r, 0.75f), "a little quick");
        }

        [Test] public void TheWrongDrum_IsAMiss_AndTheDrumsCallAgain()
        {
            var r = new DrumRules();
            ToAnswer(r);
            Assert.AreEqual(Tone.Low, r.Current[0].Tone);
            Assert.AreEqual(StrokeResult.Wrong, r.Stroke(Tone.High));
            Assert.AreEqual(DrumPhase.Miss, r.Phase);
            Assert.AreEqual(MissReason.WrongTone, r.Missed);
            Assert.AreEqual(1, r.Misses);
            Run(r, DrumRules.MissTime + 0.1f);
            Assert.AreEqual(DrumPhase.Call, r.Phase, "the same beat again, no lives lost");
            Assert.AreEqual(0, r.Served);
        }

        [Test] public void TooQuick_AndTooSlow_AreMisses()
        {
            var r = new DrumRules();
            ToAnswer(r);
            r.Stroke(Tone.Low);
            Run(r, 0.15f); // the next stroke is due a beat later
            Assert.AreEqual(StrokeResult.Wrong, r.Stroke(Tone.Low));
            Assert.AreEqual(MissReason.Early, r.Missed);

            ToAnswer(r);
            r.Stroke(Tone.Low);
            Run(r, 1f); // past the slack: the beat is lost before the stroke comes
            Assert.AreEqual(DrumPhase.Miss, r.Phase);
            Assert.AreEqual(MissReason.Late, r.Missed);
            Assert.AreEqual(StrokeResult.Ignored, r.Stroke(Tone.Low), "too late to count");
            Assert.AreEqual(2, r.Misses);
        }

        [Test] public void TwoHandsLandingTogether_AreOneStroke()
        {
            var r = new DrumRules();
            ToAnswer(r);
            Assert.AreEqual(StrokeResult.Good, r.Stroke(Tone.Low));
            Run(r, 0.05f);
            Assert.AreEqual(StrokeResult.Ignored, r.Stroke(Tone.Low), "a flam");
            Assert.AreEqual(1, r.Next);
        }

        [Test] public void NotStarting_JustPlaysTheCallAgain()
        {
            var r = new DrumRules();
            ToAnswer(r);
            Run(r, DrumRules.AnswerWait + 0.1f);
            Assert.AreEqual(DrumPhase.Call, r.Phase);
            Assert.AreEqual(0, r.Misses);
        }

        [Test] public void Beats_ParseWithRests()
        {
            var b = DrumRules.Parse("LL.H");
            Assert.AreEqual(3, b.Length);
            Assert.AreEqual(3 * DrumRules.Step, b[2].T, 1e-5f);
            Assert.AreEqual(3, b[2].Slot);
            Assert.AreEqual(Tone.High, b[2].Tone);
        }

        // ---------- striking a skin ----------

        const float R = 0.25f;

        static bool Hit(ref bool armed, Vector3 from, Vector3 to, out float at) => DrumGame.Struck(ref armed, from, to, 1f / 60f, R, out at);

        [Test] public void AHandComingDown_ThroughTheSkin_IsAStroke_TheEdgeIsASlap()
        {
            var armed = true;
            Assert.IsTrue(Hit(ref armed, new Vector3(0f, 0.02f, 0f), new Vector3(0f, -0.01f, 0f), out var at));
            Assert.Less(at, DrumGame.RimFrom, "the middle");
            Assert.IsFalse(armed, "spent until it lifts");
            Assert.IsFalse(Hit(ref armed, new Vector3(0f, 0.02f, 0f), new Vector3(0f, -0.01f, 0f), out _), "no second stroke without lifting");
            Hit(ref armed, new Vector3(0f, 0f, 0f), new Vector3(0f, 0.05f, 0f), out _);
            Assert.IsTrue(armed, "lifted: ready again");
            Assert.IsTrue(Hit(ref armed, new Vector3(0.2f, 0.02f, 0f), new Vector3(0.2f, -0.01f, 0f), out at));
            Assert.Greater(at, DrumGame.RimFrom, "the edge: a slap");
        }

        [Test] public void Resting_Sliding_OrMissingTheDrum_IsNoStroke()
        {
            var armed = true;
            Assert.IsFalse(Hit(ref armed, new Vector3(0f, 0.001f, 0f), new Vector3(0f, -0.001f, 0f), out _), "too slow: resting a hand on it");
            armed = true;
            Assert.IsFalse(Hit(ref armed, new Vector3(0.5f, 0.02f, 0f), new Vector3(0.5f, -0.01f, 0f), out _), "beside the drum");
            armed = true;
            Assert.IsFalse(Hit(ref armed, new Vector3(0f, -0.02f, 0f), new Vector3(0.05f, -0.04f, 0f), out _), "already under the skin");
        }
    }
}
