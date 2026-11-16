using NUnit.Framework;

namespace HypaStories.Tests
{
    /// <summary>The hat game after Eshu at the Crossroads: show each friend the other side, three rounds, the cup, the cowrie.</summary>
    public class HatRulesTests
    {
        static void Run(HatRules r, float seconds) { for (var t = 0f; t < seconds; t += 0.05f) r.Tick(0.05f); }

        /// <summary>Waits for friend <paramref name="i"/> to pause (at most a while).</summary>
        static void UntilPausing(HatRules r, int i) { for (var k = 0; k < 400 && !r.Pausing(i); k++) r.Tick(0.05f); Assert.IsTrue(r.Pausing(i), $"friend {i} pauses"); }

        static void PlayRound(HatRules r)
        {
            for (var i = 0; i < HatRules.Friends; i++)
            {
                UntilPausing(r, i);
                Assert.AreEqual(ShowResult.Seen, r.Show(i, !HatRules.ShoutsRed(i)), $"friend {i}");
            }
        }

        [Test] public void ThreeRounds_ThenTheSwap_ThenTheCup_ThenTheCowrie()
        {
            var r = new HatRules();
            Assert.AreEqual(HatPhase.Intro, r.Phase);
            Run(r, HatRules.IntroTime + 0.1f);
            Assert.AreEqual(HatPhase.Round, r.Phase);
            PlayRound(r);
            Assert.AreEqual(1, r.Round);
            Assert.AreEqual(HatPhase.Round, r.Phase);
            PlayRound(r);
            Assert.AreEqual(HatPhase.Swap, r.Phase, "Eshu's trick before the last round");
            Assert.IsFalse(r.Swapped);
            Run(r, HatRules.SwapTime + 0.1f);
            Assert.IsTrue(r.Swapped);
            Assert.AreEqual(2, r.Round);
            PlayRound(r);
            Assert.AreEqual(HatPhase.Cup, r.Phase);
            Assert.IsTrue(r.CupLanded());
            Assert.AreEqual(HatPhase.Offering, r.Phase);
            Assert.IsTrue(r.OfferingLeft());
            Assert.IsTrue(r.Over);
        }

        [Test] public void OwnColour_OrWhileShouting_FeedsTheQuarrel_OtherSideWhilePausing_IsSeen()
        {
            var r = new HatRules();
            Run(r, HatRules.IntroTime + 0.1f);
            var q0 = r.Quarrel;
            Assert.AreEqual(ShowResult.NotLooking, r.Show(0, false), "white to the red friend while he still shouts");
            Assert.Greater(r.Quarrel, q0);
            UntilPausing(r, 0);
            var q1 = r.Quarrel;
            Assert.AreEqual(ShowResult.Wrong, r.Show(0, true), "his own colour");
            Assert.Greater(r.Quarrel, q1 + 0.2f);
            Assert.IsFalse(r.Seen(0));
            Assert.AreEqual(ShowResult.Seen, r.Show(0, false), "the other side, while he holds still");
            Assert.IsTrue(r.Seen(0));
            Assert.AreEqual(ShowResult.Ignored, r.Show(0, false), "once is enough");
        }

        [Test] public void WindowsGetShorter_EachRound()
        {
            Assert.Greater(HatRules.Window[0], HatRules.Window[1]);
            Assert.Greater(HatRules.Window[1], HatRules.Window[2]);
            Assert.Greater(HatRules.Window[2], 1.5f, "still long enough to turn a hat");
        }

        [Test] public void AMissedPause_ComesAgain()
        {
            var r = new HatRules();
            Run(r, HatRules.IntroTime + 0.1f);
            UntilPausing(r, 0);
            Run(r, HatRules.Window[0] + 0.1f);
            Assert.IsFalse(r.Pausing(0), "the window closed");
            UntilPausing(r, 0);
        }

        [Test] public void TheQuarrelBoilsOver_TheyFight_TheRoundStartsAgain_NoLives()
        {
            var r = new HatRules();
            Run(r, HatRules.IntroTime + 0.1f);
            UntilPausing(r, 0);
            r.Show(0, false);
            Assert.IsTrue(r.Seen(0));
            // keep showing the white-shouting friend his own colour whenever he pauses
            for (var k = 0; k < 4000 && r.Phase == HatPhase.Round; k++) { if (r.Pausing(1)) r.Show(1, false); r.Tick(0.05f); }
            Assert.AreEqual(HatPhase.Fight, r.Phase);
            Assert.AreEqual(1, r.Fights);
            Run(r, HatRules.FightTime + 0.1f);
            Assert.AreEqual(HatPhase.Round, r.Phase, "back to the round, never game over");
            Assert.AreEqual(0, r.Round, "the same round");
            Assert.IsFalse(r.Seen(0), "they both have to see again");
            Assert.Less(r.Quarrel, 0.5f);
        }
    
        [Test] public void Hat_WhichSideFacesWhom()
        {
            var right = UnityEngine.Vector3.right;
            Assert.IsTrue(HatGame.RedFaces(90f, right), "red turned to the right friend");
            Assert.IsFalse(HatGame.RedFaces(-90f, right), "white turned to him");
            Assert.IsFalse(HatGame.RedFaces(180f, right) && HatGame.OffSquare(180f, right) < 45f, "side-on: neither faces him squarely");
            Assert.AreEqual(20f, HatGame.OffSquare(110f, right), 1e-3f);
            Assert.AreEqual(20f, HatGame.OffSquare(-70f, right), 1e-3f, "white, 20 degrees off");
        }
}
}
