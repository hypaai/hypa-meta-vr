using UnityEngine;

namespace HypaCore.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).
    // STATUS: written against the OVRHand public API; NOT yet compiled against an installed SDK or run on a headset.

    /// <summary>Hand-tracking pinch and pointer ray from OVRHand (index pinch). Picks whichever tracked hand is pinching.</summary>
    public sealed class HandPinchInput : IPinchInput
    {
        const float RayBackOffset = 0.25f; // metres
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
                // Start the ray a little behind the hand: seated UI sits within arm's reach, often level with the
                // hand, and a ray that starts at the fingertips would skip anything at or just behind them.
                var fwd = h.PointerPose.forward;
                ray = new Ray(h.PointerPose.position - fwd * RayBackOffset, fwd);
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
