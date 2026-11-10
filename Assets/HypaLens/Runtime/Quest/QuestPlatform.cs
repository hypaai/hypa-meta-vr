using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace HypaLens.Quest
{
    // Compiled only when MR Utility Kit is installed (asmdef defineConstraints: HYPA_MRUK).
    // STATUS: written against MRUK's public API; NOT yet compiled against an installed SDK or run on a headset.
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
            LensPlatform.Factory = () => new LensPlatform
            {
                Placer = new MrukSurfacePlacer(),
                // Frames: DeviceFrameSource + Meta Passthrough Camera intrinsics/pose providers (TODO).
                // Recognizer: on-headset OCR with word boxes (decision D2, TODO).
            };
        }
    }
}
