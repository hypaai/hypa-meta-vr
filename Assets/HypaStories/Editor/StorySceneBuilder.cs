using System.IO;
using HypaCore;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HypaStories.EditorTools
{
    static class StorySceneBuilder
    {
        [MenuItem("Hypa/Stories/Create Story Stub Scene")]
        static void Create()
        {
            const string dir = "Assets/HypaStories/Scenes";
            Directory.CreateDirectory(dir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(new Vector3(0, 1.4f, 0), Quaternion.identity);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cam.gameObject.AddComponent<StandaloneOnly>();
            new GameObject("Story").AddComponent<StoryStub>();
            EditorSceneManager.SaveScene(scene, $"{dir}/{HypaScenes.Story}.unity");
            Debug.Log("Hypa: created HypaStory scene. Run Hypa > Sync Build Settings.");
        }
    }
}
