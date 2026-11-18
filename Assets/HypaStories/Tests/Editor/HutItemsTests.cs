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
                    foreach (var l in StoryLanguages.All)
                        Assert.IsTrue(c.Has(l.Code), $"{s.Id}/{c.Id}: told in {l.Code}, like the campfire's stories");
                    Assert.IsFalse(c.Has("zz"), "a language the app doesn't have");
                    Assert.AreEqual(c.Text, c.TextFor("zz"), "is told in English");
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

        [Test] public void TheLamp_StandsOnTheLedge_AndTheOthersKeepTheirFloorPlaces()
        {
            for (var i = 0; i < HutItems.Entries.Length; i++)
            {
                var (spot, baseY) = HutItems.Place(i);
                var e = HutItems.Entries[i];
                Assert.Less(spot.magnitude, HutTrailLayout.WallInner, e.Id + " stands inside the wall");
                if (e.OwnPlace) { Assert.Greater(baseY, 0f, e.Id + " stands on something"); continue; }
                Assert.AreEqual(0f, baseY, e.Id + " stands on the floor");
            }
            var lamp = System.Array.FindIndex(HutItems.Entries, e => e.Prop == "oil-lamp");
            Assert.GreaterOrEqual(lamp, 0, "there is a lamp");
            Assert.AreEqual(HutInterior.TableHeight, HutItems.Place(lamp).baseY, "on its table");
            Assert.Less(HutItems.Place(lamp).spot.magnitude, HutTrailLayout.WallInner - HutInterior.LedgeDepth - 0.3f, "out in the room, where it is seen");
            for (var i = 0; i < HutItems.Entries.Length; i++)
                if (i != lamp) Assert.Greater(Vector2.Distance(HutItems.Place(i).spot, HutItems.Place(lamp).spot), 0.75f, "the table is clear of " + HutItems.Entries[i].Prop);
            Assert.Greater(Vector2.Distance(HutItems.Place(lamp).spot, HutItems.Woman), 0.8f, "and of the old woman");
            Assert.IsTrue(HutProps.Flame("oil-lamp").HasValue, "with a flame");
            Assert.IsFalse(HutItems.Entries[lamp].Heavy, "light: one hand carries it");
            Assert.IsTrue(HutItems.Entries[0].Heavy && HutItems.Entries[1].Heavy, "the pot and the mortar take both hands");
        }

        [Test] public void EveryItem_HasThreeWordsToTranslate_InEveryLanguage()
        {
            // the hut offers Try Hypa at the end of each item's story, as the campfire does
            foreach (var st in HutItems.All)
                foreach (var l in StoryLanguages.All)
                    Assert.AreEqual(3, st.WordsFor(l.Code).Length, $"{st.Id} {l.Code}");
            Assert.AreEqual(new[] { "lamp", "oil", "flame" }, HutItems.All[System.Array.FindIndex(HutItems.All, s => s.Id.EndsWith("oil-lamp"))].WordsFor("en"));
        }

        [Test] public void HerLines_FallBackToEnglish()
        {
            Assert.AreEqual("en", HutLines.For(HutLines.Kind.Care, "zz").lang);
            Assert.AreEqual("fr", HutLines.For(HutLines.Kind.BothHands, "fr").lang);
            foreach (HutLines.Kind k in System.Enum.GetValues(typeof(HutLines.Kind)))
                foreach (var l in StoryLanguages.All) Assert.IsNotEmpty(HutLines.For(k, l.Code).text, $"{k} in {l.Code}");
            // more than one way to pick something up, a line about each thing, and never the same twice running
            Assert.Greater(HutLines.Options(HutLines.Kind.Care, "en").lines.Length, 1);
            foreach (var e in HutItems.Entries)
                Assert.Greater(HutLines.Options(HutLines.Kind.Care, "en", e.Prop).lines.Length, HutLines.Options(HutLines.Kind.Care, "en").lines.Length, e.Prop + " has its own line");
            var first = HutLines.For(HutLines.Kind.Careful, "en", null, null, 0f).text;
            for (var i = 0; i < 10; i++) Assert.AreNotEqual(first, HutLines.For(HutLines.Kind.Careful, "en", null, first, i / 10f).text);
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
