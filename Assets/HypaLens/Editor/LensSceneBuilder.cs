using System.IO;
using HypaCore;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HypaLens.EditorTools
{
    /// <summary>Hypa > Lens > Create Lens Scene: the HypaLens scene, playable on its own (simulator) or from the shell.</summary>
    static class LensSceneBuilder
    {
        [MenuItem("Hypa/Lens/Create Lens Scene")]
        static void Create()
        {
            const string dir = "Assets/HypaLens/Scenes";
            Directory.CreateDirectory(dir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(new Vector3(0, 1.4f, 0), Quaternion.identity);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cam.nearClipPlane = 0.05f;
            cam.gameObject.AddComponent<StandaloneOnly>(); // removed when the shell provides the camera rig

            // A plain wall 1 m ahead so the simulated labels have something behind them.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wall.name = "Sim Wall";
            wall.transform.position = new Vector3(0, 1.4f, 1.02f);
            wall.transform.localScale = new Vector3(2.4f, 1.8f, 1f);
            Object.DestroyImmediate(wall.GetComponent<Collider>());
            wall.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.22f, 0.24f, 0.30f) };

            new GameObject("HypaLens").AddComponent<LensApp>();
            EditorSceneManager.SaveScene(scene, $"{dir}/{HypaScenes.Lens}.unity");
            Debug.Log("Hypa: created HypaLens scene. Press Play. Space = capture, mouse = hand, O = offline, N = no text, H = help.");
        }
    }
}
