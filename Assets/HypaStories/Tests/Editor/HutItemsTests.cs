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
                Assert.Less(spots[i].magnitude + 0.2f, HutTrailLayout.WallInner, $"place {i}: the thing stands inside the wall");
                Assert.Greater(Mathf.Abs(HutItems.AngleOf(i)), HutTrailLayout.DoorHalfAngle + 40f, $"place {i}: clear of the doorway and the way in");
                Assert.Greater(Vector2.Distance(spots[i], HutItems.Woman), 0.9f, $"place {i}: not on the old woman");
                for (var j = 0; j < i; j++)
                    Assert.Greater(Vector2.Distance(spots[i], spots[j]), 0.9f, $"places {j} and {i}: things don't crowd each other");
            }
            Assert.GreaterOrEqual(HutItems.Places, HutItems.Entries.Length, "every item has a place of its own");
            Assert.Less(HutItems.Woman.magnitude + 0.4f, HutTrailLayout.WallInner, "she sits inside the wall");
            Assert.Less(HutItems.Woman.y, 0f, "at the back: the door is on +Z");
        }

        [Test] public void AnItemThatLeavesTheRoom_IsLost()
        {
            Assert.IsFalse(HutItems.Lost(new Vector3(HutItems.Spot(0).x, 0.3f, HutItems.Spot(0).y)), "at its place by the wall");
            Assert.IsFalse(HutItems.Lost(new Vector3(0f, 0.02f, 0f)), "on the floor");
            Assert.IsFalse(HutItems.Lost(new Vector3(0.3f, 1.4f, 0.2f)), "held up");
            Assert.IsTrue(HutItems.Lost(new Vector3(0f, -1f, 0f)), "through the floor");
            Assert.IsTrue(HutItems.Lost(new Vector3(0f, 0.5f, HutTrailLayout.HutRadius + 1f)), "out of the door");
        }

        [Test] public void EveryShape_HasAPictureOfItself_ForItsCard()
        {
            foreach (var e in HutItems.Entries)
            {
                var mk = new TrailBuilder.MeshKit();
                HutProps.Build(e.Prop, mk, new System.Collections.Generic.List<Bounds>());
                var tex = HutProps.Picture(mk.Build(e.Prop), HutProps.Rest(e.Prop, 0.37f).rotation, 128);
                // SetPixels32 + Apply(makeNoLongerReadable) frees the CPU copy, so read it back through a render target
                var rt = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var read = new Texture2D(128, 128, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); read.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                var px = read.GetPixels32();
                var drawn = px.Count(c => c.a > 128);
                Assert.Greater(drawn, 128 * 128 / 40, e.Prop + ": the thing is in its picture");
                Assert.Less(drawn, 128 * 128 * 9 / 10, e.Prop + ": with the wall showing round it");
                Assert.Less(px[0].a, 10, e.Prop + ": the corner is clear");
                Object.DestroyImmediate(tex); Object.DestroyImmediate(read);
            }
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
            var broom = HutProps.Rest("broom", 0.37f);
            Assert.Greater((broom.rotation * Vector3.forward).y, 0.9f, "the broom stands up");
            Assert.Less((broom.rotation * Vector3.forward).z, 0f, "and leans back, on the wall");
            Assert.Greater((broom.position - broom.rotation * Vector3.forward * 0.3f * HutProps.Size("broom")).y, 0f, "its tied end on the floor, not in it");
            var pot = HutProps.Rest("clay-pot", 0.37f);
            Assert.AreEqual(0f, pot.position.y, "the pot stands on the floor");
            Assert.GreaterOrEqual(pot.position.z, 0f, "and never back into the wall");
            Assert.LessOrEqual(0.15f * HutProps.Size("clay-pot") - pot.position.z, 0.37f, "its belly clear of the wall");
            var crate = new TrailBuilder.MeshKit();
            var crateBoxes = new System.Collections.Generic.List<Bounds>();
            Assert.IsFalse(HutProps.Build("no-such-thing", crate, crateBoxes));
            Assert.Greater(crate.V.Count, 0);
            Assert.IsNotEmpty(crateBoxes);
        }
    }
}
