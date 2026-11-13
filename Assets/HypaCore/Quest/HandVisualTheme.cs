using UnityEngine;
using UnityEngine.SceneManagement;

namespace HypaCore.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).

    /// <summary>
    /// Meta's Hand Tracking building block ships a bright blue hand material. This retints every hand mesh to a
    /// neutral light grey at runtime — closer to the Quest system's own hand visual, and consistent with our
    /// monochrome theme — without touching the package's own material asset. Re-applied on every scene load,
    /// since a scene with its own Hand Tracking building block carries its own copy of that material.
    /// </summary>
    static class HandVisualTheme
    {
        static readonly Color GhostColor = new Color(0.85f, 0.85f, 0.85f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded += (_, __) => Apply();
            Apply();
        }

        static void Apply()
        {
            foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.GetComponentInParent<OVRHand>() == null) continue;
                foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color = GhostColor;
            }
        }
    }
}
