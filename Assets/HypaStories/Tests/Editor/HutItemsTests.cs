using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    /// <summary>The hut's household things: their content is a story like any other, and their places fit the room.</summary>
    public class HutItemsTests
    {
        [Test] public void EveryItem_HasAStory_InTheCampfiresFormat()
        {
            Assert.GreaterOrEqual(HutItems.Entries.Length, 3, "at least three household things");
            Assert.AreEqual(HutItems.Entries.Length, HutItems.All.Length);
            for (var i = 0; i < HutItems.All.Length; i++)
            {
                var s = HutItems.All[i];
                Assert.AreEqual(HutItems.Entries[i].Id, s.Id);
                Assert.IsNotEmpty(s.Title, s.Id + ": the card's title");
                Assert.IsNotEmpty(s.Desc, s.Id + ": the card's description");
                Assert.GreaterOrEqual(s.Timeline.Captions.Length, 2, s.Id + ": the player fetches the first two lines before it starts");
                Assert.AreEqual(s.Timeline.Captions.Length, s.Timeline.Captions.Select(c => c.Id).Distinct().Count(), s.Id + ": line ids are unique");
                Assert.Greater(s.Timeline.Duration, 0f);
                Assert.IsNotEmpty(s.Suggestions(0, "en"), s.Id + ": a question to suggest from the first line");
            }
        }

        [Test] public void Items_AreNotOnTheCampfiresRow()
        {
            foreach (var e in HutItems.Entries)
                Assert.IsNull(StoryLibrary.Find(e.Id), e.Id + " must not show among the campfire's story cards");
        }

        [Test] public void Translations_UseTheSameLineIds()
        {
            foreach (var s in HutItems.All)
                foreach (var c in s.Timeline.Captions)
                {
                    Assert.AreNotEqual(c.Text, c.TextFor("fr"), $"{s.Id}/{c.Id}: French");
                    Assert.AreNotEqual(c.Text, c.TextFor("es"), $"{s.Id}/{c.Id}: Spanish");
                    Assert.AreEqual(c.Text, c.TextFor("zz"), "an untranslated language falls back to English");
                }
        }

        [Test] public void Places_AreInsideTheRoom_ClearOfTheDoor_AndOfEachOther()
        {
            var spots = Enumerable.Range(0, HutItems.Places).Select(HutItems.Spot).ToList();
            for (var i = 0; i < spots.Count; i++)
            {
                Assert.Less(spots[i].magnitude + 0.45f, HutTrailLayout.WallInner, $"place {i}: the bench stands inside the wall");
                Assert.Greater(Mathf.Abs(HutItems.AngleOf(i)), HutTrailLayout.DoorHalfAngle + 40f, $"place {i}: clear of the doorway and the way in");
                Assert.Greater(Vector2.Distance(spots[i], HutItems.Woman), 0.9f, $"place {i}: not on the old woman");
                for (var j = 0; j < i; j++)
                    Assert.Greater(Vector2.Distance(spots[i], spots[j]), 0.9f, $"places {j} and {i}: benches don't overlap");
            }
            Assert.GreaterOrEqual(HutItems.Places, HutItems.Entries.Length, "every item has a place of its own");
            Assert.Less(HutItems.Woman.magnitude + 0.4f, HutTrailLayout.WallInner, "she sits inside the wall");
            Assert.Less(HutItems.Woman.y, 0f, "at the back: the door is on +Z");
        }

        [Test] public void AnItemThatLeavesTheRoom_IsLost()
        {
            Assert.IsFalse(HutItems.Lost(new Vector3(0.5f, HutItems.BenchHeight, -0.5f)), "on a bench");
            Assert.IsFalse(HutItems.Lost(new Vector3(0f, 0.02f, 0f)), "on the floor");
            Assert.IsFalse(HutItems.Lost(new Vector3(0.3f, 1.4f, 0.2f)), "held up");
            Assert.IsTrue(HutItems.Lost(new Vector3(0f, -1f, 0f)), "through the floor");
            Assert.IsTrue(HutItems.Lost(new Vector3(0f, 0.5f, HutTrailLayout.HutRadius + 1f)), "out of the door");
        }

        [Test] public void Shapes_AreBuilt_AndAnUnknownOneIsACrate()
        {
            foreach (var e in HutItems.Entries)
            {
                var mk = new TrailBuilder.MeshKit();
                var boxes = new System.Collections.Generic.List<Bounds>();
                Assert.IsTrue(HutProps.Build(e.Prop, mk, boxes), e.Prop + " has a shape");
                Assert.Greater(mk.V.Count, 0);
                Assert.IsNotEmpty(boxes, e.Prop + " has a collider");
                foreach (var b in boxes) Assert.GreaterOrEqual(b.min.y, -1e-4f, e.Prop + " stands on its origin");
            }
            var crate = new TrailBuilder.MeshKit();
            var crateBoxes = new System.Collections.Generic.List<Bounds>();
            Assert.IsFalse(HutProps.Build("no-such-thing", crate, crateBoxes));
            Assert.Greater(crate.V.Count, 0);
            Assert.IsNotEmpty(crateBoxes);
        }
    }
}
