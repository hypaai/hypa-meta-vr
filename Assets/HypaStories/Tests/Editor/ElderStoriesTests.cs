using System;
using System.Linq;
using HypaCore;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HypaStories.Tests
{
    public class ElderStoriesTests
    {
        [TestCase(true, true, false, false, false, false, true)] // between lines, buffering or paused
        [TestCase(true, true, true, false, false, false, false)] // ended
        [TestCase(true, true, true, true, false, false, true)] // final audio still finishing
        [TestCase(true, false, false, false, true, false, true)] // microphone / question
        [TestCase(true, true, true, false, false, true, true)] // translation after the end
        [TestCase(true, false, false, false, false, false, false)] // left the player
        [TestCase(false, true, false, true, true, true, false)] // fireside unaffected
        public void VillageQuiet_CoversTheSessionAndPostStorySpeech(bool elder, bool live, bool ended,
            bool speaking, bool asking, bool translating, bool expected)
            => Assert.AreEqual(expected, StoryApp.NeedsVillageQuiet(elder, live, ended, speaking, asking, translating));

        [Test] public void VoiceOrigin_MovesOnlyTheAudio_AndCanReturnToTheFire()
        {
            var root = new GameObject("Voice origin test");
            try
            {
                var fire = new GameObject("Fire mouth").transform; fire.SetParent(root.transform);
                var elder = new GameObject("Elder mouth").transform; elder.SetParent(root.transform);
                elder.position = new Vector3(40f, 1.5f, 8f);
                var voice = root.AddComponent<StoryVoice>(); voice.Init(fire);
                var source = fire.GetComponentInChildren<AudioSource>();
                voice.SetOrigin(elder);
                Assert.AreSame(source, elder.GetComponentInChildren<AudioSource>());
                Assert.AreEqual(elder.position, source.transform.position);
                Assert.AreEqual(0.6f, source.spatialBlend);
                Assert.AreEqual(2f, source.minDistance);
                Assert.AreEqual(30f, source.maxDistance);
                voice.SetOrigin(fire);
                Assert.AreSame(source, fire.GetComponentInChildren<AudioSource>());
                Assert.AreEqual(fire.position, source.transform.position);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test] public void VillagePerformance_FadesOnlySelectedSources_AndRestoresTheirBaseVolumes()
        {
            var root = new GameObject("Village audio test");
            try
            {
                var village = root.AddComponent<Village>();
                AudioSource Source(string field)
                {
                    var go = new GameObject(field); go.transform.SetParent(root.transform);
                    var source = go.AddComponent<AudioSource>(); source.loop = true;
                    typeof(Village).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(village, source);
                    return source;
                }
                var drums = Source("_drums"); var crowd = Source("_crowd"); var pestles = Source("_thud");
                var fire = root.AddComponent<AudioSource>(); fire.volume = 0.6f;
                village.SetNarrationFocus(true); village.UpdatePerformanceAudio(0.1f);
                Assert.That(drums.volume, Is.InRange(0.128f, 0.849f), "fade instead of an abrupt cut");
                village.UpdatePerformanceAudio(10f);
                Assert.AreEqual(0.85f * 0.15f, drums.volume, 0.0001f);
                Assert.AreEqual(0.45f * 0.15f, crowd.volume, 0.0001f);
                Assert.AreEqual(0.15f, pestles.volume, 0.0001f);
                Assert.AreEqual(0.6f, fire.volume, "other ambience is untouched");
                Assert.IsTrue(drums.loop);
                village.SetNarrationFocus(false); village.UpdatePerformanceAudio(10f);
                Assert.AreEqual(0.85f, drums.volume, 0.0001f);
                Assert.AreEqual(0.45f, crowd.volume, 0.0001f);
                Assert.AreEqual(1f, pestles.volume, 0.0001f);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static StoryAssets UiAssets()
        {
            var source = AssetDatabase.LoadAssetAtPath<StoryAssets>("Assets/HypaStories/StoryAssets.asset");
            Assert.IsNotNull(source);
            var assets = UnityEngine.Object.Instantiate(source);
            // These tests exercise routing, not icon textures, whose runtime material instances require Play mode.
            assets.icons = new Texture2D[0];
            return assets;
        }

        [Test] public void ElderProximity_IncludesTheArrivalSpot_ButNotTheGate()
        {
            Assert.IsTrue(VillageLayout.AtLocal(VillageLayout.Local));
            Assert.IsTrue(VillageLayout.AtLocal(VillageLayout.MeetSpot));
            Assert.IsFalse(VillageLayout.AtLocal(VillageLayout.FrontOfGate));
            Assert.IsFalse(VillageLayout.AtGate(VillageLayout.MeetSpot));
            Assert.IsFalse(VillageLayout.AtLocal(VillageLayout.Local + Vector2.right * 2.21f));
            Assert.Greater(Vector2.Distance(VillageLayout.Local, VillageLayout.FrontOfGate), 4.4f,
                "the two proximity regions must not overlap");
        }

        [Test] public void StoryCollections_KeepCameroonTalesSeparateFromTheCampfire()
        {
            CollectionAssert.AreEqual(new[] { "seven-hot-stones", "eshu-crossroads", "marlow-street" }, StoryLibrary.Campfire.Select(s => s.Id));
            CollectionAssert.AreEqual(new[] { "death-of-the-rice-farmer", "girl-who-chose-hare" }, StoryLibrary.Elder.Select(s => s.Id));
            Assert.IsFalse(StoryLibrary.Campfire.Intersect(StoryLibrary.Elder).Any());
            CollectionAssert.AreEquivalent(StoryLibrary.All, StoryLibrary.Campfire.Concat(StoryLibrary.Elder));
        }

        [Test] public void SharedList_ChangesCardsDotsAndPlaybackMapping_WithoutReplacingTheControls()
        {
            var root = new GameObject("Elder list test");
            var assets = UiAssets();
            try
            {
                Kit.Init(assets);
                StoryListView list = null;
                string played = null;
                list = new StoryListView(root.transform, assets, i => played = list.StoryAt(i).Id, () => { }, () => { }, _ => { });
                var languageButtons = list.Root.GetComponentsInChildren<KitButton>(true)
                    .Where(b => b.Label != null && StoryLanguages.All.Any(l => l.Name == b.Label.text)).ToArray();
                Assert.AreEqual(8, languageButtons.Length);
                Assert.AreEqual(3, list.Count);

                list.SetStories(StoryLibrary.Elder);
                Assert.AreEqual(2, list.Count);
                Assert.AreEqual(0, list.Index);
                Assert.AreEqual(2, list.Root.GetComponentsInChildren<KitPanel>().Count(p => p.name == "Dot"));
                Assert.IsFalse(list.Root.Find("Card seven-hot-stones").gameObject.activeSelf);
                list.SetReveal(0, 1f);
                list.Root.Find("Card death-of-the-rice-farmer").GetComponentInChildren<KitButton>().OnPress();
                Assert.AreEqual("death-of-the-rice-farmer", played);
                list.Root.Find("Card girl-who-chose-hare/Art").GetComponent<PinchTarget>().OnPinch(PinchKind.Short);
                Assert.AreEqual(1, list.Index);
                list.SetReveal(1, 1f);
                list.Root.Find("Card girl-who-chose-hare").GetComponentInChildren<KitButton>().OnPress();
                Assert.AreEqual("girl-who-chose-hare", played);

                list.SetStories(StoryLibrary.Campfire);
                Assert.AreEqual(3, list.Count);
                Assert.AreEqual("seven-hot-stones", list.StoryAt(0).Id);
                Assert.IsFalse(list.Root.Find("Card girl-who-chose-hare").gameObject.activeSelf);
                Assert.AreEqual(3, list.Root.GetComponentsInChildren<KitPanel>().Count(p => p.name == "Dot"));
                foreach (var button in languageButtons) Assert.IsTrue(button.gameObject.activeInHierarchy);
                Assert.Throws<ArgumentException>(() => list.SetStories(new StoryInfo[0]));
                Assert.AreEqual(3, list.Count, "invalid selections leave the current list intact");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(assets); }
        }

        [Test] public void ElderRow_OverridesOnlyItsOwnMainAction()
        {
            var root = new GameObject("Elder row test");
            var assets = UiAssets();
            try
            {
                Kit.Init(assets);
                var narrated = false;
                PlaceButtons.Add(root.transform, PlaceIds.Gathering, null, () => { }, () => { }, () => narrated = true, "Narrate story");
                var main = root.GetComponentsInChildren<KitButton>().Single(b => b.Style == KitStyle.Primary);
                Assert.AreEqual("Narrate story", main.Label.text);
                main.OnPress();
                Assert.IsTrue(narrated);
                Assert.AreEqual("Meet the locals", PlaceButtons.Label(PlaceIds.Gathering, true), "the gate keeps its action");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(assets); }
        }
    }
}
