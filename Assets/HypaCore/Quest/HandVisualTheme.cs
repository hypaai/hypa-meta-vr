using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HypaCore.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).

    /// <summary>
    /// Gives Meta's tracked hands a clay body and amber rim to match the campfire. The SDK still swaps
    /// in its own material for system gestures.
    /// </summary>
    static class HandVisualTheme
    {
        static readonly Color BodyTop = new Color(0.78f, 0.61f, 0.45f, 1f);
        static readonly Color BodyBottom = new Color(0.58f, 0.43f, 0.29f, 1f);
        static readonly Color Rim = new Color(1f, 0.66f, 0.32f, 1f);
        static readonly Dictionary<Material, Material> TintedMaterials = new Dictionary<Material, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            foreach (var material in TintedMaterials.Values)
                if (material != null) Object.Destroy(material);
            TintedMaterials.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        static void Apply()
        {
            foreach (var hand in Object.FindObjectsByType<OVRMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (hand.ShouldUseSystemGestureMaterial || hand.GetComponentInParent<OVRHand>(true) == null ||
                    !hand.TryGetComponent<SkinnedMeshRenderer>(out var renderer)) continue;

                var original = renderer.sharedMaterial;
                if (original == null || !original.HasProperty("_ColorPrimary") || !original.HasProperty("_ColorTop") ||
                    !original.HasProperty("_ColorBottom") || TintedMaterials.ContainsValue(original)) continue;

                if (!TintedMaterials.TryGetValue(original, out var tinted))
                {
                    tinted = new Material(original);
                    tinted.SetColor("_ColorPrimary", Rim);
                    tinted.SetColor("_ColorTop", BodyTop);
                    tinted.SetColor("_ColorBottom", BodyBottom);
                    TintedMaterials.Add(original, tinted);
                }

                renderer.sharedMaterial = tinted;
                hand.SetMaterial(tinted);
            }
        }
    }
}
