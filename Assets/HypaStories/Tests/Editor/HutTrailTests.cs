using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    /// <summary>Ticket 3G: where the trail to the hut runs, and where you can walk (never through the hut's walls).</summary>
    public class HutTrailTests
    {
        const float Clearing = 18f;

        [Test] public void Trail_LeavesOnTheLeft_AndEndsAtTheHutsDoor()
        {
            Assert.Less(HutTrailLayout.Path[0].x, 0f, "the trail starts on the left of the fire (-X), where the Hut button is");
            Assert.Greater(HutTrailLayout.Hut.magnitude, Clearing + HutTrailLayout.HutRadius, "the hut stands beyond the clearing");
            Assert.AreEqual(HutTrailLayout.HutRadius, Vector2.Distance(HutTrailLayout.Door, HutTrailLayout.Hut), 1e-4f);
            Assert.Less(HutTrailLayout.DistanceToPath(HutTrailLayout.Door), 1e-3f, "the trail reaches the door");
            Assert.Less(Vector2.Distance(HutTrailLayout.Sign, HutTrailLayout.Path[0]), 1.5f, "the signpost stands at the trail's mouth");
            Assert.Less(Vector2.Distance(HutTrailLayout.Sign, HutTrailLayout.Log), 1.8f, "the signpost stands by the fallen log");
            Assert.Greater(Vector2.Distance(HutTrailLayout.Sign, HutTrailLayout.Log), 1.05f, "the post is beside the log, not inside it");
        }

        [Test] public void Walk_TheClearingTheTrailAndTheHut()
        {
            Assert.IsTrue(HutTrailLayout.Allowed(new Vector2(0f, -6.2f), Clearing), "the seat");
            Assert.IsFalse(HutTrailLayout.Allowed(new Vector2(0.3f, 0.2f), Clearing), "not in the fire");
            var mid = HutTrailLayout.Along(0.95f).point;
            Assert.Greater(mid.magnitude, Clearing, "this bit of the trail is beyond the clearing");
            Assert.IsTrue(HutTrailLayout.Allowed(mid, Clearing), "on the trail");
            Assert.IsTrue(HutTrailLayout.Allowed(HutTrailLayout.Hut, Clearing), "inside the hut");
            Assert.IsTrue(HutTrailLayout.Allowed(HutTrailLayout.Door, Clearing), "in the doorway");
            Assert.IsFalse(HutTrailLayout.Allowed(new Vector2(-24f, -12f), Clearing), "off in the woods");
        }

        [Test] public void Walk_NeverThroughTheHutsWall()
        {
            // round the wall, away from the door, every point of the wall is out of bounds
            for (var deg = 30f; deg <= 330f; deg += 10f)
            {
                var dir = (Vector2)(Quaternion.Euler(0f, 0f, deg) * HutTrailLayout.DoorDir);
                var inWall = HutTrailLayout.Hut + dir * (HutTrailLayout.WallInner + HutTrailLayout.HutRadius) * 0.5f;
                Assert.IsFalse(HutTrailLayout.Allowed(inWall, Clearing), $"wall at {deg} degrees from the door");
            }
        }

        [Test] public void Clamp_PutsYouSomewhereYouCanStand()
        {
            var r = new System.Random(5);
            for (var i = 0; i < 400; i++)
            {
                var p = new Vector2(-30f + (float)r.NextDouble() * 50f, -20f + (float)r.NextDouble() * 40f);
                var q = HutTrailLayout.Clamp(p, Clearing);
                Assert.IsTrue(HutTrailLayout.Allowed(q, Clearing), $"{p} -> {q}");
                if (HutTrailLayout.Allowed(p, Clearing)) Assert.AreEqual(p, q, "an allowed spot stays put");
            }
        }

        [Test] public void DoorButtons_ShowJustOutsideTheDoor()
        {
            Assert.IsTrue(HutTrailLayout.AtDoor(HutTrailLayout.FrontOfDoor));
            Assert.IsFalse(HutTrailLayout.AtDoor(HutTrailLayout.Hut), "not inside");
            Assert.IsFalse(HutTrailLayout.AtDoor(HutTrailLayout.Path[0]), "not back at the clearing");
        }
    }
}
