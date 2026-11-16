using NUnit.Framework;

namespace HypaStories.Tests
{
    /// <summary>The hat game after Eshu at the Crossroads: show each friend the other side, three rounds, the cup, the cowrie.</summary>
    public class HatRulesTests
    {
        static void Run(HatRules r, float seconds) { for (var t = 0f; t < seconds; t += 0.05f) r.Tick(0.05f); }

        /// <summary>Holds a colour on the listener until something happens (at most a few seconds).</summary>
        static ShowResult HoldOn(HatRules r, bool red)
        {
            for (var k = 0; k < 100; k++) { var s = r.Face(red, 0.05f); r.Tick(0.05f); if (s != ShowResult.Nothing) return s; }
            return ShowResult.Nothing;
        }

        static void PlayRound(HatRules r)
        {
            for (var n = 0; n < HatRules.Friends; n++)
            {
                var i = r.Listener;
                Assert.GreaterOrEqual(i, 0);
                Assert.AreEqual(ShowResult.Seen, HoldOn(r, !HatRules.ShoutsRed(i)), $"friend {i}");
            }
        }

        [Test] public void ThreeRounds_ThenTheSwap_ThenTheCup_ThenTheCowrie()
        {
            var r = new HatRules();
            Assert.AreEqual(HatPhase.Intro, r.Phase);
            Run(r, HatRules.IntroTime + 0.1f);
            Assert.AreEqual(HatPhase.Round, r.Phase);
            Assert.AreEqual(0, r.Listener, "the left friend listens first");
            PlayRound(r);
            Assert.AreEqual(1, r.Round);
            Assert.AreEqual(1, r.Listener, "round two starts with the other friend");
            PlayRound(r);
            Assert.AreEqual(HatPhase.Swap, r.Phase, "Eshu's trick before the last round");
            Run(r, HatRules.SwapTime + 0.1f);
            Assert.IsTrue(r.Swapped);
            PlayRound(r);
            Assert.AreEqual(HatPhase.Cup, r.Phase);
            Assert.IsTrue(r.CupLanded());
            Assert.AreEqual(HatPhase.Drink, r.Phase, "they come and drink before anything else");
            Run(r, HatRules.DrinkTime + 0.1f);
            Assert.AreEqual(HatPhase.Offering, r.Phase);
            Assert.IsTrue(r.OfferingLeft());
            Assert.IsTrue(r.Over);
        }

        [Test] public void Holding_Counts_AWobbleOnlyDrainsIt()
        {
            var r = new HatRules();
            Run(r, HatRules.IntroTime + 0.1f);
            Assert.AreEqual(ShowResult.Nothing, r.Face(false, HatRules.HoldTime * 0.6f, true), "white on the red friend: counting, even while you hold the hat");
            Assert.AreEqual(ShowResult.Nothing, r.Face(null, 0.05f), "a wobble out of line");
            Assert.Greater(r.Hold, 0.45f, "it only drains a little");
            Assert.AreEqual(ShowResult.Seen, r.Face(false, HatRules.HoldTime * 0.5f), "back in line: done");
            Assert.IsTrue(r.Seen(0));
        }

        [Test] public void HisOwnColour_OnlyCounts_OnceYouLetGo_AndOnlyOnce()
        {
            var r = new HatRules();
            Run(r, HatRules.IntroTime + 0.1f);
            var q = r.Quarrel;
            Assert.AreEqual(ShowResult.Nothing, r.Face(true, 3f, true), "red swings past the red friend while you turn it: no harm");
            Assert.Less(r.Quarrel - q, 0.01f);
            Assert.AreEqual(ShowResult.Wrong, HoldOn(r, true), "left on him");
            Assert.Greater(r.Quarrel, q + 0.2f);
            q = r.Quarrel;
            Assert.AreEqual(ShowResult.Nothing, HoldOn(r, true), "it doesn't shout again and again");
            Assert.Less(r.Quarrel - q, 0.1f);
            Assert.AreEqual(ShowResult.Seen, HoldOn(r, false), "turned round: he sees");
        }

        [Test] public void NoTimer_TheListenerWaits_ButTheQuarrelSimmers()
        {
            var r = new HatRules();
            Run(r, HatRules.IntroTime + 20f);
            Assert.AreEqual(0, r.Listener, "still listening after 20 s");
            Assert.Greater(r.Quarrel, 0.2f);
            Assert.AreEqual(HatPhase.Round, r.Phase, "long before a fight");
            Assert.Greater(HatRules.Simmer[2], HatRules.Simmer[0], "later rounds are hotter");
        }

        [Test] public void TheQuarrelBoilsOver_TheyFight_TheRoundStartsAgain_NoLives()
        {
            var r = new HatRules();
            Run(r, HatRules.IntroTime + 0.1f);
            Assert.AreEqual(ShowResult.Seen, HoldOn(r, false));
            for (var k = 0; k < 10 && r.Phase == HatPhase.Round; k++) { HoldOn(r, HatRules.ShoutsRed(r.Listener)); r.Face(null, 0.05f); } // keep showing the right friend his own colour
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
            Assert.AreEqual(20f, HatGame.OffSquare(110f, right), 1e-3f);
            Assert.AreEqual(20f, HatGame.OffSquare(-70f, right), 1e-3f, "white, 20 degrees off");
            Assert.AreEqual(90f, HatGame.OffSquare(180f, right), 1e-3f, "side-on");
        }
    }
}
