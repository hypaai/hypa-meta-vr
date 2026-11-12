using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace HypaLens.Quest
{
    // Compiled only when MR Utility Kit is installed (asmdef defineConstraints: HYPA_MRUK).
    // Compiles against MRUK v209 and runs in the Meta XR Simulator; not yet run on a Quest headset.
    // Hands come from HypaCore.Quest (shared by every mode).

    /// <summary>Pins labels to real surfaces using the MRUK room model (walls, tables, etc.).</summary>
    public sealed class MrukSurfacePlacer : ISurfacePlacer
    {
        public bool TryPlace(Ray ray, out Vector3 position, out Vector3 normal)
        {
            var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room != null && room.Raycast(ray, 4f, out var hit, out _))
            {
                position = hit.point; normal = hit.normal;
                return true;
            }
            // TODO: fall back to the depth-based environment raycast when MRUK has no surface for this text.
            position = default; normal = default;
            return false;
        }
    }

    static class QuestRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // Called by LensApp on the main thread when the simulated camera is off.
            LensPlatform.Factory = () => new LensPlatform
            {
                Frames = new PcaFrameSource(),             // MRUK Passthrough Camera Access
                Recognizer = MlKitRecognizer.TryCreate(),   // null off-device (LensApp shows a clear message)
                Placer = new MrukSurfacePlacer(),
            };
        }
    }
}
