using System.IO;
using System.Linq;
using HypaCore.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HypaCore.EditorTools
{
    /// <summary>Keeps repo hygiene settings on for everyone who opens the project: Force Text and visible .meta files.</summary>
    [InitializeOnLoad]
    static class ProjectHygiene
    {
        static ProjectHygiene()
        {
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
            {
                EditorSettings.serializationMode = SerializationMode.ForceText;
                Debug.Log("Hypa: set Asset Serialization to Force Text.");
            }
            if (VersionControlSettings.mode != "Visible Meta Files")
            {
                VersionControlSettings.mode = "Visible Meta Files";
                Debug.Log("Hypa: set Version Control to Visible Meta Files.");
            }
        }
    }

    static class HypaMenu
    {
        const string CoreScenes = "Assets/HypaCore/Scenes";

        [MenuItem("Hypa/Create Shell and Core Test Scenes")]
        static void CreateCoreScenes()
        {
            Directory.CreateDirectory(CoreScenes);

            // Shell: owns the camera rig. On Quest, replace the plain camera with the Meta camera rig (see README).
            var shell = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            DarkCamera();
            new GameObject("Hypa Shell").AddComponent<HypaShell>();
            EditorSceneManager.SaveScene(shell, $"{CoreScenes}/{HypaScenes.Shell}.unity");

            var test = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            DarkCamera().gameObject.AddComponent<StandaloneOnly>();
            new GameObject("Hypa Core Test").AddComponent<HypaCoreTest>();
            EditorSceneManager.SaveScene(test, $"{CoreScenes}/{HypaScenes.CoreTest}.unity");

            SyncBuildSettings();
            EditorSceneManager.OpenScene($"{CoreScenes}/{HypaScenes.Shell}.unity");
        }

        /// <summary>Shell first, then every other scene named in HypaScenes that exists under Assets/.</summary>
        [MenuItem("Hypa/Sync Build Settings")]
        public static void SyncBuildSettings()
        {
            string[] order = { HypaScenes.Shell, HypaScenes.Lens, HypaScenes.Story, HypaScenes.CoreTest };
            var found = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            var scenes = order
                .Select(n => found.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == n))
                .Where(p => p != null)
                .Select(p => new EditorBuildSettingsScene(p, true))
                .ToArray();
            EditorBuildSettings.scenes = scenes;
            Debug.Log("Hypa: Build Settings = " + string.Join(", ", scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))));
        }

        /// <summary>
        /// HypaLens.unity shipped with its own camera rig and hand tracking, stacked on top of the one Shell already
        /// keeps alive (DontDestroyOnLoad) once you navigate into Lens. Two live rigs is why a hand's ray can go
        /// missing and pinching can feel off. This removes Lens's own copy; Shell's rig already covers every scene.
        /// </summary>
        [MenuItem("Hypa/Lens/Remove Duplicate Camera Rig")]
        static void RemoveDuplicateLensRig()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("Hypa: stop Play mode first."); return; }
            var path = AssetDatabase.FindAssets("t:Scene HypaLens")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == HypaScenes.Lens);
            if (path == null) { Debug.LogError("Hypa: couldn't find the HypaLens scene."); return; }

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            // Not "MR Utility Kit": Shell has no copy of that one, and Lens needs it to pin labels to real surfaces.
            string[] names = { "[BuildingBlock] Camera Rig", "[BuildingBlock] Passthrough" };
            var removed = 0;
            foreach (var root in scene.GetRootGameObjects())
                if (names.Contains(root.name)) { Object.DestroyImmediate(root); removed++; }

            if (removed == 0) { Debug.Log("Hypa: nothing to remove in HypaLens — already clean."); return; }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Hypa: removed {removed} duplicate rig object(s) from HypaLens and saved the scene. " +
                      "Build And Run again to pick it up.");
        }

        /// <summary>Creates the git-ignored key file at the project root, with an empty key to fill in.</summary>
        [MenuItem("Hypa/Create API Key File (git-ignored)")]
        static void CreateKeyFile()
        {
            var path = HypaConfig.LocalFilePath;
            if (!File.Exists(path))
                File.WriteAllText(path, "{\n  \"apiKey\": \"\",\n  \"baseUrl\": \"" + HypaConfig.DefaultBaseUrl + "\"\n}\n");
            EditorUtility.RevealInFinder(path);
            Debug.Log("Hypa: put your key in " + path + " (git-ignored; never commit it).");
        }

        static Camera DarkCamera()
        {
            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(new Vector3(0, 1.4f, 0), Quaternion.identity);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cam.nearClipPlane = 0.05f;
            return cam;
        }
    }
}
