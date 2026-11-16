using NUnit.Framework;

namespace HypaStories.Tests
{
    /// <summary>The Dodo game after Seven Hot Stones: heat seven stones, answer, throw, three lives.</summary>
    public class DodoRulesTests
    {
        static void Run(DodoRules r, float seconds) { for (var t = 0f; t < seconds; t += 0.05f) r.Tick(0.05f); }

        static DodoRules Heated()
        {
            var r = new DodoRules();
            Run(r, DodoRules.IntroTime + 0.1f);
            Assert.AreEqual(DodoPhase.Heat, r.Phase);
            for (var i = 0; i < DodoRules.Stones; i++) Assert.IsTrue(r.Place());
            Run(r, DodoRules.HeatTime + 0.2f);
            return r;
        }

        [Test] public void Stones_HeatInTheFire_ThenDodoRoars()
        {
            var r = new DodoRules();
            Run(r, DodoRules.IntroTime + 0.1f);
            Assert.IsTrue(r.Place());
            Assert.AreEqual(0f, r.Heat(0), 1e-3f, "cold as it goes in");
            Run(r, DodoRules.HeatTime / 2f);
            Assert.AreEqual(0.5f, r.Heat(0), 0.05f, "warming");
            Run(r, DodoRules.HeatTime);
            Assert.AreEqual(1f, r.Heat(0), 1e-3f, "red hot");
            Assert.AreEqual(DodoPhase.Heat, r.Phase, "Dodo waits for all seven");
            for (var i = 1; i < DodoRules.Stones; i++) r.Place();
            Assert.IsFalse(r.Place(), "only seven");
            Run(r, DodoRules.HeatTime + 0.2f);
            Assert.AreEqual(DodoPhase.Roar, r.Phase, "all seven hot: the challenge");
            Assert.AreEqual(DodoRules.Stones, r.Ready);
        }

        [Test] public void Answer_Stoop_Throw_Swallow_SevenTimes_Wins()
        {
            var r = Heated();
            for (var k = 0; k < DodoRules.Stones; k++)
            {
                Assert.AreEqual(DodoPhase.Roar, r.Phase, $"round {k}");
                Assert.IsFalse(r.Hit(), "no throwing before Dodo stoops");
                Assert.IsTrue(r.Answer(), "I am!");
                Run(r, DodoRules.ApproachTime + 0.1f);
                Assert.AreEqual(DodoPhase.Window, r.Phase);
                Assert.Greater(r.WindowLeft, 0f);
                Assert.IsTrue(r.Hit());
                Assert.AreEqual(DodoPhase.Swallow, r.Phase);
                Run(r, DodoRules.SwallowTime + 0.1f);
            }
            Assert.AreEqual(DodoPhase.Won, r.Phase);
            Assert.AreEqual(DodoRules.Lives, r.LivesLeft, "a clean game");
            Assert.AreEqual(0, r.Ready);
        }

        [Test] public void Misses_CostALife_TheStoneComesBack_ThirdMissLoses()
        {
            var r = Heated();
            r.Answer();
            Run(r, DodoRules.ApproachTime + 0.1f);
            Assert.IsTrue(r.Miss(), "a stone thrown wide");
            Assert.AreEqual(DodoPhase.Missed, r.Phase);
            Assert.AreEqual(DodoRules.Lives - 1, r.LivesLeft);
            Assert.AreEqual(DodoRules.Stones, r.Ready, "the missed stone rolls back into the fire");
            Run(r, DodoRules.MissTime + 0.1f);
            Assert.AreEqual(DodoPhase.Roar, r.Phase, "Dodo roars again");

            r.Answer();
            Run(r, DodoRules.ApproachTime + DodoRules.WindowTime + 0.2f);
            Assert.AreEqual(DodoPhase.Missed, r.Phase, "too slow: the mouth closed");
            Assert.AreEqual(DodoRules.Lives - 2, r.LivesLeft);

            Run(r, DodoRules.MissTime + 0.1f);
            r.Answer();
            Run(r, DodoRules.ApproachTime + 0.1f);
            r.Miss();
            Assert.AreEqual(DodoPhase.Lost, r.Phase);
            Assert.IsTrue(r.Over);
            Run(r, 5f);
            Assert.AreEqual(DodoPhase.Lost, r.Phase, "game over stays over");
        }

        [Test] public void Silence_DodoRoarsAgain_ThenLosesPatience()
        {
            var r = Heated();
            Run(r, DodoRules.RoarTime + DodoRules.AnswerTime + 0.2f);
            Assert.AreEqual(DodoPhase.Roar, r.Phase, "first silence: it calls again");
            Assert.AreEqual(DodoRules.Lives, r.LivesLeft);
            Run(r, DodoRules.RoarTime + DodoRules.AnswerTime + 0.2f);
            Assert.AreEqual(DodoPhase.Missed, r.Phase, "second silence: a life");
            Assert.AreEqual(DodoRules.Lives - 1, r.LivesLeft);
        }

        [Test] public void AnsweringDuringTheRoar_Counts()
        {
            var r = Heated();
            Assert.AreEqual(DodoPhase.Roar, r.Phase);
            Assert.IsTrue(r.Answer(), "shouting back before it finishes is fine");
            Assert.AreEqual(DodoPhase.Approach, r.Phase);
        }

        [Test] public void Shout_IsALoudVoiceOverTheRoom()
        {
            Assert.IsTrue(ShoutDetector.IsShout(0.2f, 0.01f), "a shout in a quiet room");
            Assert.IsFalse(ShoutDetector.IsShout(0.03f, 0.005f), "a murmur");
            Assert.IsFalse(ShoutDetector.IsShout(0.1f, 0.05f), "a noisy room's own level");
        }

        [Test] public void Grab_PointingRoughlyAtThePile_PicksTheNearestStone_OneAtATime()
        {
            var hand = new UnityEngine.Vector3(0f, 1f, -3f);
            var stones = new[] { new UnityEngine.Vector3(0.6f, 0.65f, -2.3f), new UnityEngine.Vector3(0.75f, 0.65f, -2.4f), new UnityEngine.Vector3(0f, 0.3f, 0.6f) };
            var all = new[] { true, true, true };
            // heating (only the slab's stones can be taken): pointing 30° off the pile still finds one
            var dir = UnityEngine.Quaternion.Euler(0f, -30f, 0f) * (stones[0] - hand).normalized;
            var i = DodoGame.PickStone(new UnityEngine.Ray(hand, dir), hand, stones, new[] { true, true, false });
            Assert.That(i == 0 || i == 1, $"picked {i}");
            Assert.AreEqual(2, DodoGame.PickStone(new UnityEngine.Ray(hand, (stones[2] - hand).normalized), hand, stones, all), "toward the fire: the fire's stone");
            Assert.AreEqual(-1, DodoGame.PickStone(new UnityEngine.Ray(hand, UnityEngine.Vector3.back), hand, stones, all), "pointing behind you: nothing");
            Assert.AreEqual(1, DodoGame.PickStone(new UnityEngine.Ray(hand, (stones[0] - hand).normalized), hand, stones, new[] { false, true, false }), "only stones you can take now");
        }

        [Test] public void Release_AFlickOrPointingSendsIt_ASlowFumbleDoesnt()
        {
            Assert.IsTrue(DodoGame.Flicked(0f, 170f, true), "heating: letting go always drops it in the fire");
            Assert.IsTrue(DodoGame.Flicked(0.8f, 120f, false), "a flick goes to the mouth, guided");
            Assert.IsTrue(DodoGame.Flicked(0.1f, 20f, false), "or letting go while pointing at it");
            Assert.IsFalse(DodoGame.Flicked(0.1f, 90f, false), "a slow fumble pointing away goes back to the fire");
        }
    }
}
