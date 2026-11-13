using UnityEngine;

namespace HypaStories.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).

    /// <summary>
    /// Quest input for Stories. Thumbsticks: left walks, right snap-turns (<see cref="StoryInput"/>).
    /// Raise a hand to ask: a tracked hand (open, not pinching) or a controller held above head height. Hands first, then
    /// controllers, like HypaCore's pointer. Hold time and re-arming are in <see cref="RaiseHandGesture"/>.
    /// </summary>
    public static class QuestStoryInput
    {
        const float AboveHead = 0.02f;
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
            var headY = cam.transform.position.y + AboveHead;

            if (_hands == null || _hands.Length == 0 || _hands[0] == null)
                _hands = Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Exclude);
            var anyTracked = false;
            foreach (var h in _hands)
            {
                if (h == null || !h.IsTracked) continue;
                anyTracked = true;
                if (h.IsDataHighConfidence && !h.GetFingerIsPinching(OVRHand.HandFinger.Index) && h.transform.position.y > headY) return true;
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
