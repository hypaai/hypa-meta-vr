using UnityEngine;

namespace HypaStories.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).

    /// <summary>
    /// Quest input for Stories. Thumbsticks: left walks, right snap-turns (<see cref="StoryInput"/>).
    /// Raise a hand to ask: a tracked hand (open, not pinching) raised to about face height in front of you, or a controller held
    /// there. Hands first, then controllers, like HypaCore's pointer. Hold time and re-arming are in <see cref="RaiseHandGesture"/>.
    /// Tested on a Quest Pro: "above the eyes, high confidence" never fired; a hand raised that high leaves the cameras' view and
    /// tracking drops it or reports low confidence. Face height is still well above where hands rest or pinch the panels.
    /// </summary>
    public static class QuestStoryInput
    {
        /// <summary>The hand counts as raised from this far below eye level (metres).</summary>
        const float BelowEyes = 0.10f;
        /// <summary>...and within this far in front of or beside the head (a hand reaching far out is pointing, not raised).</summary>
        const float Reach = 0.75f;
        static OVRHand[] _hands;
        static OVRCameraRig _rig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            RaiseHand.Probe = IsRaised;
            StoryInput.Move = () => OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            StoryInput.Turn = () => OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).x;
        }

        static bool IsRaised()
        {
            var cam = Camera.main;
            if (cam == null) return false;
            var head = cam.transform.position;
            var headY = head.y - BelowEyes;

            if (_hands == null || _hands.Length == 0 || _hands[0] == null)
                _hands = Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Exclude);
            var anyTracked = false;
            foreach (var h in _hands)
            {
                if (h == null || !h.IsTracked) continue;
                anyTracked = true;
                var p = h.transform.position;
                var flat = new Vector2(p.x - head.x, p.z - head.z).magnitude;
                if (!h.GetFingerIsPinching(OVRHand.HandFinger.Index) && p.y > headY && flat < Reach) return true;
            }
            if (anyTracked) return false;

            if (_rig == null) _rig = Object.FindAnyObjectByType<OVRCameraRig>();
            var space = _rig != null ? _rig.trackingSpace : null;
            foreach (var c in new[] { OVRInput.Controller.LTouch, OVRInput.Controller.RTouch })
            {
                if (!OVRInput.GetControllerPositionTracked(c)) continue;
                var p = OVRInput.GetLocalControllerPosition(c);
                if (space != null) p = space.TransformPoint(p);
                if (p.y > headY) return true;
            }
            return false;
        }
    }
}
