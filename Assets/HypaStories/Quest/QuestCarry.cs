using Oculus.Interaction;
using UnityEngine;

namespace HypaStories.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).

    /// <summary>
    /// A heavy thing held in one hand (the clay pot, the mortar): it is too heavy to lift so. It rocks a little toward the hand,
    /// as if tested, and if it was up in the air (the other hand let go while carrying) it sinks back down to where it rests.
    /// The <see cref="Grabbable"/>'s one-grab transformer.
    /// </summary>
    public sealed class TooHeavyOneHand : MonoBehaviour, ITransformer
    {
        const float SinkSpeed = 0.9f, MaxTilt = 5f;
        IGrabbable _g;
        Pose _hand0;
        Quaternion _rot0;
        float _restLocalY, _time;

        public void Initialize(IGrabbable grabbable)
        {
            _g = grabbable;
            _restLocalY = _g.Transform.localPosition.y; // it starts resting on the floor
        }

        public void BeginTransform()
        {
            _hand0 = _g.GrabPoints[0];
            _rot0 = _g.Transform.rotation;
            _time = Time.time;
        }

        public void UpdateTransform()
        {
            var t = _g.Transform;
            // a little rock toward where the hand pulls, no more
            var pull = _g.GrabPoints[0].position - _hand0.position;
            var flat = new Vector3(pull.x, 0f, pull.z);
            var tilt = Mathf.Clamp(flat.magnitude * 40f, 0f, MaxTilt);
            var axis = flat.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            var wobble = Mathf.Sin((Time.time - _time) * 18f) * 0.6f * Mathf.Exp(-(Time.time - _time) * 4f);
            t.rotation = Quaternion.AngleAxis(tilt + wobble, axis) * _rot0;
            // up in the air: sink back to the floor, never below it
            var p = t.localPosition;
            if (p.y > _restLocalY) { p.y = Mathf.Max(_restLocalY, p.y - SinkSpeed * Time.deltaTime); t.localPosition = p; }
        }

        public void EndTransform() { }
    }

    /// <summary>
    /// A heavy thing carried in both hands: it stays upright and follows the point between the hands, turning about the vertical
    /// as the hands turn round it (never tipping or flipping, never scaling), and lags a little behind them, which reads as weight.
    /// The <see cref="Grabbable"/>'s two-grab transformer.
    /// </summary>
    public sealed class CarryTwoHands : MonoBehaviour, ITransformer
    {
        const float Follow = 9f;
        IGrabbable _g;
        Vector3 _offset;       // the thing's position from the hands' midpoint, in the hands' yaw frame
        Quaternion _relRot;    // its rotation in the hands' yaw frame (upright)

        public void Initialize(IGrabbable grabbable) => _g = grabbable;

        static Quaternion Yaw(Vector3 a, Vector3 b)
        {
            var d = b - a; d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
        }

        public void BeginTransform()
        {
            var t = _g.Transform;
            Vector3 a = _g.GrabPoints[0].position, b = _g.GrabPoints[1].position;
            var yaw = Yaw(a, b);
            var inv = Quaternion.Inverse(yaw);
            _offset = inv * (t.position - (a + b) * 0.5f);
            var up = Quaternion.FromToRotation(t.up, Vector3.up) * t.rotation; // set straight as it is lifted
            _relRot = inv * up;
        }

        public void UpdateTransform()
        {
            var t = _g.Transform;
            Vector3 a = _g.GrabPoints[0].position, b = _g.GrabPoints[1].position;
            var yaw = Yaw(a, b);
            var k = 1f - Mathf.Exp(-Follow * Time.deltaTime);
            t.SetPositionAndRotation(Vector3.Lerp(t.position, (a + b) * 0.5f + yaw * _offset, k),
                Quaternion.Slerp(t.rotation, yaw * _relRot, k));
        }

        public void EndTransform() { }
    }
}
