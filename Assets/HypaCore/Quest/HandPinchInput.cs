using UnityEngine;

namespace HypaCore.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).

    /// <summary>
    /// Quest pointer: hands first (index pinch, OVRHand pointer pose), controllers as the optional fallback
    /// (pointer from the controller, index trigger = pinch). A pulled trigger on any connected controller always
    /// counts as a pinch; while a hand is tracked the hand ray still aims it (the Meta XR Simulator turns a mouse click
    /// into a trigger, and the click must land where the drawn hand ray points, not under the mouse cursor).
    /// Otherwise the pinching hand wins, then the hand held up in front. Hand pinches obey HypaInput.HandPinches.
    /// </summary>
    public sealed class HandPinchInput : IPinchInput
    {
        const float TriggerDown = 0.55f;
        OVRHand[] _hands;
        OVRCameraRig _rig;

        OVRHand ActiveHand()
        {
            if (_hands == null || _hands.Length == 0 || _hands[0] == null)
                _hands = Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Exclude);
            OVRHand best = null;
            var bestScore = float.NegativeInfinity;
            var cam = Camera.main;
            foreach (var h in _hands)
            {
                if (h == null || !h.IsTracked) continue;
                // A pinch always wins, so the click stays on the hand that is pinching.
                if (HypaInput.HandPinches && h.GetFingerIsPinching(OVRHand.HandFinger.Index)) return h;
                // Otherwise the hand you are holding up in front of you, with a small preference for the dominant hand.
                var score = h.IsDominantHand ? 1.5f : 0f;
                if (h.GetHand() == OVRPlugin.Hand.HandRight) score += 0.25f;
                if (cam != null)
                {
                    var to = h.transform.position - cam.transform.position;
                    if (to.sqrMagnitude > 1e-4f)
                        score += Vector3.Dot(cam.transform.forward, to.normalized);
                }
                if (score > bestScore) { bestScore = score; best = h; }
            }
            return best;
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

        /// <summary>
        /// A trigger (or A/X) pulled on any connected controller, tracked or not. The Meta XR Simulator's point-and-click
        /// mode turns a mouse click into exactly this, even while it shows hands, so it must count as a pinch.
        /// </summary>
        static bool AnyControllerPress(out OVRInput.Controller pressed)
        {
            var connected = OVRInput.GetConnectedControllers();
            foreach (var c in new[] { OVRInput.Controller.RTouch, OVRInput.Controller.LTouch })
            {
                if ((connected & c) != c) continue;
                if (OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, c) > TriggerDown || OVRInput.Get(OVRInput.Button.One, c)) { pressed = c; return true; }
            }
            pressed = OVRInput.Controller.None;
            return false;
        }

        public bool IsPinching
        {
            get
            {
                if (AnyControllerPress(out _)) return true;
                if (!HypaInput.HandPinches) return false;
                var h = ActiveHand();
                return h != null && h.GetFingerIsPinching(OVRHand.HandFinger.Index);
            }
        }

        public bool TryGetRay(out Ray ray)
        {
            // The tracked hand aims, even while a controller trigger is the pinch: what you click is what the drawn ray
            // points at. A pressed controller aims only when no hand is tracked (real controllers in use).
            var h = ActiveHand();
            var handAims = h != null && h.IsTracked && h.IsPointerPoseValid && h.PointerPose != null;
            if (!handAims && AnyControllerPress(out var pressed) && OVRInput.GetControllerPositionTracked(pressed)) return ControllerRay(pressed, out ray);

            if (handAims)
            {
                // Aim with Quest's steady pointer, not the index bone. The index bone curls during a pinch and
                // the line dives under the hand. Start the line at the fingertip so it still meets the hand you see.
                var p = h.PointerPose;
                if (_rig == null) _rig = Object.FindAnyObjectByType<OVRCameraRig>();
                var space = _rig != null ? _rig.trackingSpace : null;
                var rot = space != null ? space.rotation * p.localRotation : p.rotation;
                var dir = rot * Vector3.forward;
                var tip = Fingertip(h);
                var origin = tip != null
                    ? tip.position + dir * 0.012f
                    : (space != null ? space.TransformPoint(p.localPosition) : p.position);
                ray = new Ray(origin, dir);
                return true;
            }

            var c = ActiveController();
            if (c != OVRInput.Controller.None) return ControllerRay(c, out ray);

            ray = default;
            return false;
        }

        bool ControllerRay(OVRInput.Controller c, out Ray ray)
        {
            if (_rig == null) _rig = Object.FindAnyObjectByType<OVRCameraRig>();
            var space = _rig != null ? _rig.trackingSpace : null;
            var pos = OVRInput.GetLocalControllerPosition(c);
            var rot = OVRInput.GetLocalControllerRotation(c);
            if (space != null) { pos = space.TransformPoint(pos); rot = space.rotation * rot; }
            ray = new Ray(pos, rot * Vector3.forward);
            return true;
        }

        static Transform Fingertip(OVRHand hand)
        {
            var skel = hand.GetComponent<OVRSkeleton>() ?? hand.GetComponentInChildren<OVRSkeleton>();
            if (skel == null || !skel.IsInitialized || skel.Bones == null) return null;
            // Quest 3 uses the OpenXR hand. The old Hand_IndexTip number is 20, and that is the ring tip
            // on the OpenXR hand (XRHand_IndexTip is 10). Asking for the old id puts the ray on the ring.
            var tip = skel.GetSkeletonType().IsOpenXRHandSkeleton()
                ? OVRSkeleton.BoneId.XRHand_IndexTip
                : OVRSkeleton.BoneId.Hand_IndexTip;
            foreach (var b in skel.Bones)
                if (b.Id == tip) return b.Transform;
            return null;
        }

        // No hover haptics: OVRInput vibration keeps running (up to 2 s) until stopped, and this input has no update
        // loop to stop it. The highlighted button and the pointer colour are the hover feedback.
        public void Pulse() { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => HypaInput.DeviceFactory = () => new HandPinchInput();
    }
}
