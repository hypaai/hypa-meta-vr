using UnityEngine;

namespace HypaCore.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).
    // STATUS: written against the OVRHand public API; NOT yet compiled against an installed SDK or run on a headset.

    /// <summary>Hand-tracking pinch and pointer ray from OVRHand (index pinch). Picks whichever tracked hand is pinching.</summary>
    public sealed class HandPinchInput : IPinchInput
    {
        OVRHand[] _hands;

        OVRHand Active()
        {
            if (_hands == null || _hands.Length == 0) _hands = Object.FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            OVRHand tracked = null;
            foreach (var h in _hands)
            {
                if (h == null || !h.IsTracked) continue;
                if (h.GetFingerIsPinching(OVRHand.HandFinger.Index)) return h;
                tracked = tracked ?? h;
            }
            return tracked;
        }

        public bool IsPinching { get { var h = Active(); return h != null && h.GetFingerIsPinching(OVRHand.HandFinger.Index); } }

        public bool TryGetRay(out Ray ray)
        {
            var h = Active();
            if (h != null && h.IsPointerPoseValid && h.PointerPose != null)
            {
                ray = new Ray(h.PointerPose.position, h.PointerPose.forward);
                return true;
            }
            ray = default;
            return false;
        }

        public void Pulse() { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => HypaInput.DeviceFactory = () => new HandPinchInput();
    }
}
