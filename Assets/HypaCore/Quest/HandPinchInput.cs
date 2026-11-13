using UnityEngine;

namespace HypaCore.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).

    /// <summary>
    /// Quest pointer: hands first (index pinch, OVRHand pointer pose), controllers as the optional fallback
    /// (pointer from the controller, index trigger = pinch). Whichever hand is pinching wins; otherwise the hand
    /// that pinched last; otherwise the right hand; otherwise any tracked hand; otherwise the controller in use.
    /// </summary>
    public sealed class HandPinchInput : IPinchInput
    {
        const float TriggerDown = 0.55f;
        OVRHand[] _hands; OVRHand _lastPinched;
        OVRCameraRig _rig;

        OVRHand ActiveHand()
        {
            if (_hands == null || _hands.Length == 0 || _hands[0] == null)
                _hands = Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Exclude);
            OVRHand tracked = null, right = null;
            foreach (var h in _hands)
            {
                if (h == null || !h.IsTracked) continue;
                if (h.GetFingerIsPinching(OVRHand.HandFinger.Index)) { _lastPinched = h; return h; }
                if (IsRight(h)) right = h;
                tracked = tracked ?? h;
            }
            if (_lastPinched != null && _lastPinched.IsTracked) return _lastPinched;
            return right ?? tracked;
        }

        // OVRHand's hand type is internal; the skeleton it provides says which hand it is.
        static bool IsRight(OVRHand h)
        {
            var t = ((OVRSkeleton.IOVRSkeletonDataProvider)h).GetSkeletonType();
            return t == OVRSkeleton.SkeletonType.HandRight || t == OVRSkeleton.SkeletonType.XRHandRight;
        }

        static OVRInput.Controller ActiveController()
        {
            var connected = OVRInput.GetConnectedControllers();
            bool Has(OVRInput.Controller c) => (connected & c) == c && OVRInput.GetControllerPositionTracked(c);
            var r = Has(OVRInput.Controller.RTouch); var l = Has(OVRInput.Controller.LTouch);
            if (r && l) // the one whose trigger is pulled, else the right
                return OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch) > TriggerDown &&
                       OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) <= TriggerDown
                    ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            return r ? OVRInput.Controller.RTouch : l ? OVRInput.Controller.LTouch : OVRInput.Controller.None;
        }

        public bool IsPinching
        {
            get
            {
                var h = ActiveHand();
                if (h != null) return h.GetFingerIsPinching(OVRHand.HandFinger.Index);
                var c = ActiveController();
                return c != OVRInput.Controller.None && OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, c) > TriggerDown;
            }
        }

        public bool TryGetRay(out Ray ray)
        {
            var h = ActiveHand();
            if (h != null && h.IsPointerPoseValid && h.PointerPose != null)
            {
                ray = new Ray(h.PointerPose.position, h.PointerPose.forward);
                return true;
            }

            var c = ActiveController();
            if (c != OVRInput.Controller.None)
            {
                if (_rig == null) _rig = Object.FindAnyObjectByType<OVRCameraRig>();
                var space = _rig != null ? _rig.trackingSpace : null;
                var pos = OVRInput.GetLocalControllerPosition(c);
                var rot = OVRInput.GetLocalControllerRotation(c);
                if (space != null) { pos = space.TransformPoint(pos); rot = space.rotation * rot; }
                ray = new Ray(pos, rot * Vector3.forward);
                return true;
            }

            ray = default;
            return false;
        }

        // No hover haptics: OVRInput vibration keeps running (up to 2 s) until stopped, and this input has no update
        // loop to stop it. The highlighted button and the pointer colour are the hover feedback.
        public void Pulse() { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => HypaInput.DeviceFactory = () => new HandPinchInput();
    }
}
