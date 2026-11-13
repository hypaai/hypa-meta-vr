using UnityEngine;
using UnityEngine.SceneManagement;

namespace HypaCore.Quest
{
    /// <summary>
    /// The see-through view lives on its own object in HypaShell. Loading Lens used to drop it, so the
    /// headset showed a black room while menus (drawn by the camera) still appeared. This makes a fresh
    /// passthrough layer after every scene load and keeps the camera clear color see-through.
    /// </summary>
    static class PassthroughKeep
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded += (_, __) => Ensure();
            Ensure();
        }

        static void Ensure()
        {
            var manager = Object.FindAnyObjectByType<OVRManager>();
            if (manager == null) return;
            manager.isInsightPassthroughEnabled = true;

            foreach (var old in Object.FindObjectsByType<OVRPassthroughLayer>(FindObjectsInactive.Include))
                Object.DestroyImmediate(old.gameObject);

            var host = new GameObject("Hypa Passthrough");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<OVRPassthroughLayer>();

            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            var cameras = rig != null
                ? rig.GetComponentsInChildren<Camera>(true)
                : new[] { Camera.main };
            foreach (var cam in cameras)
            {
                if (cam == null) continue;
                cam.clearFlags = CameraClearFlags.SolidColor;
                var color = cam.backgroundColor;
                color.a = 0f;
                cam.backgroundColor = color;
            }
        }
    }
}
