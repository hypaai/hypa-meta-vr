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
        static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(target, value);

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedElder_ExposesTheMouthWhenSpeaking_AndMovesHisVisibleLowerFace(bool inRoom)
        {
            var root = new GameObject("Generated elder mouth test");
            var room = new GameObject("Room parent");
            var assets = AssetDatabase.LoadAssetAtPath<StoryAssets>("Assets/HypaStories/StoryAssets.asset");
            var material = new Material(assets.pine);
            material.SetFloat("_Sway", 0f);
            var ambient = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode; var fog = RenderSettings.fog;
            try
            {
                var village = root.AddComponent<Village>();
                SetField(village, "_r", new System.Random(42));
                SetField(village, "_root", root.transform);
                SetField(village, "_a", assets);
                SetField(village, "_glow", Texture2D.whiteTexture);
                SetField(village, "_vc", material);
                typeof(Village).GetMethod("BuildLocal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(village, null);
                if (inRoom) village.SetElderInRoom(true, room.transform);
                var head = village.ElderMouth.parent;
                var mouthMesh = village.ElderMouth.GetComponentInChildren<MeshRenderer>().transform;
                var jaw = head.Find("Elder Jaw");
                Assert.IsNotNull(jaw, "the visible lower lip, chin and beard must be movable");
                var restJaw = jaw.localPosition; var restRotation = jaw.localRotation;
                var anchor = village.ElderMouth.position;
                foreach (var filter in head.GetComponentsInChildren<MeshFilter>())
                {
                    filter.gameObject.layer = 31;
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
                Transform FrontSurface()
                {
                    Physics.SyncTransforms();
                    var ray = new Ray(head.TransformPoint(new Vector3(0f, -0.067f, 0.3f)), -head.forward);
                    return Physics.RaycastAll(ray, 0.6f, 1 << 31).OrderBy(h => h.distance).First().transform;
                }
                Assert.AreNotSame(mouthMesh, FrontSurface(), "resting lips cover the small mouth interior");

                var camera = new GameObject("Face verification camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.transform.position = head.TransformPoint(new Vector3(0f, 0f, 0.8f));
                camera.transform.rotation = Quaternion.LookRotation(head.position - camera.transform.position, head.up);
                camera.orthographic = true; camera.orthographicSize = 0.17f;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.15f, 0.18f, 0.22f);
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                var light = new GameObject("Face verification light").AddComponent<Light>();
                light.transform.SetParent(root.transform); light.type = LightType.Directional;
                light.transform.rotation = camera.transform.rotation; light.cullingMask = 1 << 31;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white * 0.5f; RenderSettings.fog = false;
                void SaveFrame(string pose)
                {
                    if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
                    var target = new RenderTexture(512, 512, 24);
                    var pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
                    var previous = RenderTexture.active;
                    try
                    {
                        camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply();
                        System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath($"Logs/ElderMouth-{(inRoom ? "Room" : "Village")}-{pose}.png"), pixels.EncodeToPNG());
                    }
                    finally
                    {
                        camera.targetTexture = null; RenderTexture.active = previous;
                        UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
                    }
                }
                SaveFrame("Closed");
                village.UpdateElderSpeech(1f, true, 1f);
                Assert.AreSame(mouthMesh, FrontSurface(), "the actual face must expose the speaking mouth, not hide it behind lips or beard");
                Assert.Less(jaw.localPosition.y, restJaw.y);
                Assert.Greater(Quaternion.Angle(restRotation, jaw.localRotation), 10f);
                Assert.AreEqual(anchor, village.ElderMouth.position, "the audio attachment stays fixed");
                SaveFrame("Open");
                village.UpdateElderSpeech(1f, false, 1f);
                Assert.AreEqual(restJaw, jaw.localPosition);
                Assert.Less(Quaternion.Angle(restRotation, jaw.localRotation), 0.001f);
                Assert.AreNotSame(mouthMesh, FrontSurface());
                if (inRoom) village.SetElderInRoom(false, root.transform);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(room);
                UnityEngine.Object.DestroyImmediate(material);
                RenderSettings.ambientLight = ambient; RenderSettings.ambientMode = mode; RenderSettings.fog = fog;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ElderSpeech_AnimatesOnlyTheMouth_AndClosesWithoutSpeech(bool inRoom)
        {
            var root = new GameObject("Elder speech test");
            var room = new GameObject("Room parent");
            try
            {
                var village = root.AddComponent<Village>();
                var elder = new GameObject("Elder").transform; elder.SetParent(root.transform);
                var mouth = new GameObject("Mouth anchor").transform; mouth.SetParent(elder);
                mouth.localPosition = new Vector3(0f, -0.06f, 0.087f);
                var mesh = new GameObject("Visible mouth").transform; mesh.SetParent(mouth, false);
                var audio = new GameObject("Voice").transform; audio.SetParent(mouth, false);
                var rest = new Vector3(1.1f, 0.9f, 1.2f);
                mesh.localScale = rest;
                SetField(village, "_local", new Village.Figure { Root = elder, Mouth = mouth });
                SetField(village, "_elderMouthMesh", mesh);
                SetField(village, "_elderMouthRestScale", rest);
                if (inRoom) village.SetElderInRoom(true, room.transform);
                var position = mouth.position;
                village.UpdateElderSpeech(1f, true, 0.01f);
                Assert.That(mesh.localScale.y, Is.InRange(rest.y + 0.001f, rest.y * 5f - 0.001f), "opening is smoothed");
                village.UpdateElderSpeech(10f, true, 1f);
                Assert.AreEqual(rest.y * 5f, mesh.localScale.y, 0.0001f, "loud input is bounded");
                Assert.AreEqual(rest.x, mesh.localScale.x);
                Assert.AreEqual(rest.z, mesh.localScale.z);
                Assert.AreEqual(position, mouth.position);
                Assert.AreEqual(Vector3.one, mouth.localScale);
                Assert.AreSame(mouth, audio.parent);
                Assert.AreEqual(Vector3.zero, audio.localPosition);
                village.UpdateElderSpeech(1f, false, 1f); // paused, listening, outside an elder session or stopped
                Assert.AreEqual(rest, mesh.localScale);
                village.UpdateElderSpeech(0f, true, 1f); // a silent interval inside a spoken line
                Assert.AreEqual(rest, mesh.localScale);
                if (inRoom) village.SetElderInRoom(false, root.transform);
                Assert.AreEqual(rest, mesh.localScale);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(room); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RoomPresentation_KeepsTheChosenStoryteller_AndRestoresTheVillage(bool elderSession)
        {
            var root = new GameObject("Room presentation test");
            root.transform.SetPositionAndRotation(new Vector3(15f, 0.4f, -9f), Quaternion.Euler(0f, 27f, 0f));
            var outdoorPosition = root.transform.position; var outdoorRotation = root.transform.rotation;
            var fog = RenderSettings.fog;
            try
            {
                Transform Node(string name, Transform parent)
                { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
                var camp = root.AddComponent<CampWorld>();
                var village = Node("Village", root.transform).gameObject.AddComponent<Village>();
                SetField(camp, "<Village>k__BackingField", village);
                var scenery = Node("Village scenery", village.transform);
                SetField(village, "_root", scenery);
                var elder = Node("Elder", scenery);
                elder.localPosition = new Vector3(40f, 0f, 8f);
                elder.localRotation = Quaternion.Euler(0f, 43f, 0f);
                elder.localScale = Vector3.one * 1.02f;
                var mouth = Node("Mouth", elder);
                SetField(village, "_local", new Village.Figure { Root = elder, Mouth = mouth });
                var teller = Node("Fire teller", root.transform).gameObject.AddComponent<Storyteller>();
                SetField(camp, "<Teller>k__BackingField", teller);
                var ring = Node("Fire stones", root.transform);
                var portal = Node("Portal", root.transform);
                var glow = Node("Fire glow", root.transform);
                var embers = Node("Embers", root.transform);
                SetField(camp, "_ringHolder", ring);
                SetField(camp, "<PortalRing>k__BackingField", portal);
                SetField(camp, "_firePool", glow);
                SetField(camp, "_rainT", Node("Rain", root.transform));
                var originalPosition = elder.localPosition; var originalRotation = elder.localRotation; var originalScale = elder.localScale;
                var elderPosition = elder.position; var elderRotation = elder.rotation;
                camp.SetRoom(true, elderSession);
                Assert.AreEqual(outdoorPosition, root.transform.position);
                Assert.Less(Quaternion.Angle(outdoorRotation, root.transform.rotation), 0.001f);
                Assert.AreEqual(elderSession, camp.RoomWithElder);
                Assert.AreEqual(elderSession, elder.gameObject.activeInHierarchy);
                Assert.IsFalse(scenery.gameObject.activeInHierarchy);
                Assert.AreEqual(0, elder.gameObject.layer);
                Assert.That(Vector3.Distance(elderPosition, elder.position), Is.LessThan(0.0001f));
                Assert.Less(Quaternion.Angle(elderRotation, elder.rotation), 0.001f);
                Assert.AreEqual(!elderSession, village.gameObject.activeInHierarchy);
                foreach (var t in new[] { teller.transform, ring, portal, glow, embers })
                    Assert.AreEqual(!elderSession, t.gameObject.activeInHierarchy, t.name);
                if (elderSession)
                {
                    elder.position = new Vector3(1f, 0.2f, 2f);
                    camp.SetRoom(true, true); // repeated entry must not replace the saved village pose
                }
                camp.SetRoom(false);
                Assert.AreEqual(outdoorPosition, root.transform.position);
                Assert.Less(Quaternion.Angle(outdoorRotation, root.transform.rotation), 0.001f);
                Assert.AreSame(scenery, elder.parent);
                Assert.AreEqual(0, elder.gameObject.layer);
                Assert.AreEqual(originalPosition, elder.localPosition);
                Assert.Less(Quaternion.Angle(originalRotation, elder.localRotation), 0.001f);
                Assert.AreEqual(originalScale, elder.localScale);
                Assert.IsTrue(village.gameObject.activeInHierarchy);
                foreach (var t in new[] { teller.transform, ring, portal, glow, embers }) Assert.IsTrue(t.gameObject.activeInHierarchy);
                Assert.IsFalse(camp.RoomWithElder);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); RenderSettings.fog = fog; }
        }

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
            CollectionAssert.AreEqual(new[] { "seven-hot-stones", "kings-drum", "eshu-crossroads", "marlow-street" }, StoryLibrary.Campfire.Select(s => s.Id));
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
                Assert.AreEqual(4, list.Count);

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
                Assert.AreEqual(4, list.Count);
                Assert.AreEqual("seven-hot-stones", list.StoryAt(0).Id);
                Assert.IsFalse(list.Root.Find("Card girl-who-chose-hare").gameObject.activeSelf);
                Assert.AreEqual(4, list.Root.GetComponentsInChildren<KitPanel>().Count(p => p.name == "Dot"));
                foreach (var button in languageButtons) Assert.IsTrue(button.gameObject.activeInHierarchy);
                Assert.Throws<ArgumentException>(() => list.SetStories(new StoryInfo[0]));
                Assert.AreEqual(4, list.Count, "invalid selections leave the current list intact");
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

        [Test] public void PlaceRows_StayAtTheHutAndGate_WhileTheElderRowHidesForHisStory()
        {
            Assert.IsTrue(StoryApp.PlaceRowShown(false, true, false, true), "hut and village stay while a story is open");
            Assert.IsFalse(StoryApp.PlaceRowShown(false, true, true, true), "Narrate story hides once his list or story is open");
            Assert.IsTrue(StoryApp.PlaceRowShown(false, true, true, false), "Narrate story returns on the splash");
            Assert.IsFalse(StoryApp.PlaceRowShown(true, true, false, false), "no world rows in the room");
            Assert.IsFalse(StoryApp.PlaceRowShown(false, false, false, false), "not when you are away from that door");
        }

        [Test] public void LeavingTheElder_StopsAStoryHeIsStillTelling()
        {
            Assert.IsFalse(VillageLayout.LeftElder(VillageLayout.Local));
            Assert.IsFalse(VillageLayout.LeftElder(VillageLayout.MeetSpot));
            Assert.IsTrue(VillageLayout.LeftElder(VillageLayout.Gate), "the village door is far enough");
            Assert.IsTrue(VillageLayout.LeftElder(VillageLayout.FrontOfGate));
            Assert.IsTrue(StoryApp.ElderTellingShouldStop(true, true, true));
            Assert.IsFalse(StoryApp.ElderTellingShouldStop(true, false, true), "the list, or a finished story, is not him still telling");
            Assert.IsFalse(StoryApp.ElderTellingShouldStop(true, true, false));
            Assert.IsFalse(StoryApp.ElderTellingShouldStop(false, true, true));
            Assert.IsFalse(StoryApp.ElderTellingShouldStop(true, true, true, true), "in the room he is with you");
        }
    }
}
