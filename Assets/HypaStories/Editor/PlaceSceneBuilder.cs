using System;
using System.Linq;
using HypaCore;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HypaStories.EditorTools
{
    /// <summary>
    /// Place scenes (ticket 3F). Each place is its own scene built from code, one owner per scene. <see cref="CreatePlaceScene"/>
    /// is the shared part: the standalone camera rig (removed under the shell or next to the campfire's rig), a "Place" root
    /// holding the <see cref="PlaceEntry"/> (where you stand, facing +Z), the place's one real-time light, and Build Settings.
    /// The owner's builder adds the rest under the root:
    /// <code>PlaceSceneBuilder.CreatePlaceScene(HypaScenes.StoryHut, root => { /* walls, the local, learnables... */ });</code>
    /// Until then, Hypa > Stories > Create Place Scenes makes stand-ins (a floor, a sign, doors back) for the hut and the gathering.
    /// </summary>
    public static class PlaceSceneBuilder
    {
        const string Scenes = "Assets/HypaStories/Scenes";

        [MenuItem("Hypa/Stories/Create Place Scenes (stubs)")]
        public static void CreateStubs()
        {
            CreateHutStub();
            CreateGatheringStub();
            EditorSceneManager.OpenScene($"{Scenes}/{HypaScenes.Story}.unity");
        }

        [MenuItem("Hypa/Stories/Create Hut Scene (stub)")]
        public static void CreateHutStub() => CreatePlaceScene(HypaScenes.StoryHut, root =>
            Stub(root, HypaScenes.StoryHut, "The hut", "Stand-in until 3H builds the hut.", new Color(0.32f, 0.22f, 0.14f), new[] { PlaceIds.Camp, PlaceIds.Gathering }));

        [MenuItem("Hypa/Stories/Create Gathering Scene (stub)")]
        public static void CreateGatheringStub() => CreatePlaceScene(HypaScenes.StoryGathering, root =>
            Stub(root, HypaScenes.StoryGathering, "The gathering", "Stand-in until 3M builds the gathering.", new Color(0.22f, 0.24f, 0.18f), new[] { PlaceIds.Camp, PlaceIds.Hut }));

        /// <summary>
        /// (Re)builds a place scene: rig, "Place" root with the PlaceEntry at its origin facing +Z, one warm real-time light
        /// without shadows (the Quest budget), then <paramref name="build"/> adds the place itself under the root. Saved to
        /// Assets/HypaStories/Scenes/{sceneName}.unity and added to Build Settings.
        /// </summary>
        public static Scene CreatePlaceScene(string sceneName, Action<Transform> build)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building a place scene.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var sun = UnityEngine.Object.FindAnyObjectByType<Light>();
            if (sun != null) UnityEngine.Object.DestroyImmediate(sun.gameObject); // each place brings its one light
            var cam = Camera.main;
            var rig = StorySceneBuilder.BuildMetaRig();
            if (rig != null)
            {
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
                cam = rig.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.CompareTag("MainCamera")) ?? rig.GetComponentInChildren<Camera>(true);
            }
            else
            {
                cam.transform.SetPositionAndRotation(new Vector3(0, 1.2f, 0), Quaternion.identity);
                cam.gameObject.AddComponent<StandaloneOnly>();
                cam.gameObject.AddComponent<EditorMouseLook>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.04f, 0.035f);
            cam.nearClipPlane = 0.05f;

            var root = new GameObject("Place").transform;
            root.gameObject.AddComponent<PlaceEntry>(); // at the origin, facing +Z: build the place around it
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = new Vector3(0f, 2.6f, 1.2f);
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.82f, 0.62f);
            light.shadows = LightShadows.None;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.13f, 0.11f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            build?.Invoke(root);

            var path = $"{Scenes}/{sceneName}.unity";
            System.IO.Directory.CreateDirectory(Scenes);
            EditorSceneManager.SaveScene(scene, path);
            if (EditorBuildSettings.scenes.All(s => s.path != path)) EditorApplication.ExecuteMenuItem("Hypa/Sync Build Settings");
            Debug.Log($"Hypa Stories: created {sceneName}. Press Play here (1 = camp, 2 = hut, 3 = gathering), or go there from the camp.");
            return scene;
        }

        /// <summary>A stand-in: a round floor and a <see cref="StubPlace"/> sign with doors to the other places.</summary>
        static void Stub(Transform root, string sceneName, string title, string note, Color floor, string[] doors)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ground.name = "Floor";
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>()); // never blocks a pinch ray
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = new Vector3(0f, -0.01f, 1f);
            ground.transform.localScale = new Vector3(7f, 0.01f, 7f);
            var r = ground.GetComponent<Renderer>();
            // A material asset: one made in memory is not saved with the scene.
            var matPath = $"Assets/HypaStories/Materials/{sceneName}Floor.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.color = floor;
            mat.SetFloat("_Glossiness", 0.05f);
            EditorUtility.SetDirty(mat);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var stub = root.gameObject.AddComponent<StubPlace>();
            stub.Title = title;
            stub.Note = note;
            stub.Doors = doors;
        }
    }
}
