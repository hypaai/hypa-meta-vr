using UnityEngine;

namespace HypaCore
{
    /// <summary>
    /// Keeps a panel comfortably in front of the viewer, the same way Stories' FollowView does, so every mode moves alike:
    ///
    /// - It sits <see cref="Distance"/> out at <see cref="Height"/> from eye level, in the direction the head faces (yaw).
    /// - It always turns to face the eyes, so looking up or down at it it still reads straight on.
    /// - Looking around a little leaves it still (a hand ray can land on it); past <see cref="Threshold"/> degrees, or after
    ///   walking away, it glides back in front with an exponential ease (<see cref="Speed"/>), swinging round the user.
    /// - While the hand is on it (<c>hold</c>) it doesn't start a glide; a glide that has started always finishes.
    /// - A sudden jump of the head (the XR session starting at eye height, a recentre) snaps it straight in front.
    /// - A long frame (a scene load, a hitch) is treated as a short one, so it never leaps.
    /// Pure, unit tested.
    /// </summary>
    public sealed class HeadFollow
    {
        public float Distance = 1.0f;    // metres from the eyes; beyond the hands so a hand ray crosses the panel
        public float Height = 0f;        // metres from eye level (negative = below)
        public float Threshold = 28f;    // degrees of head yaw before it follows
        public float MoveAway = 0.35f;   // metres of walking before it follows
        public float Speed = 3.5f;       // per second; higher = snappier glide
        public float SnapJump = 0.5f;    // metres the head can move in one frame before it counts as a jump
        const float MaxStep = 1f / 30f;  // seconds; longer frames are treated as this long

        bool _placed, _moving;
        float _yaw;
        Vector3 _pos, _lastHead;

        public bool IsFollowing => _moving;

        /// <summary>The next update snaps the panel straight in front of the eyes.</summary>
        public void Reset() { _placed = false; _moving = false; }

        /// <summary>The pose the panel should have this frame. The panel's +Z points away from the viewer.</summary>
        public Pose Update(Vector3 headPos, Quaternion headRot, bool hold, float dt)
        {
            var headYaw = YawOf(headRot);
            if (!_placed || (headPos - _lastHead).sqrMagnitude > SnapJump * SnapJump)
            {
                _placed = true; _moving = false;
                _yaw = headYaw;
                _pos = Target(headPos);
            }
            _lastHead = headPos;
            dt = Mathf.Clamp(dt, 0f, MaxStep);

            var off = Mathf.Abs(Mathf.DeltaAngle(headYaw, _yaw));
            var flat = _pos - headPos; flat.y = 0f;
            if (!_moving && !hold && (off > Threshold || Mathf.Abs(flat.magnitude - Distance) > MoveAway)) _moving = true;
            if (_moving)
            {
                _yaw = Mathf.LerpAngle(_yaw, headYaw, 1f - Mathf.Exp(-Speed * dt));
                if (Mathf.Abs(Mathf.DeltaAngle(headYaw, _yaw)) < 2f) _moving = false;
            }
            // The position chases the target on the circle round the head (yaw-wise), never cutting through the user.
            var target = Target(headPos);
            var k = 1f - Mathf.Exp(-Speed * 1.5f * dt);
            var from = _pos - headPos; var to = target - headPos;
            var fromFlat = new Vector3(from.x, 0f, from.z); var toFlat = new Vector3(to.x, 0f, to.z);
            var dir = Vector3.Slerp(fromFlat, toFlat, k);
            _pos = headPos + new Vector3(dir.x, Mathf.Lerp(from.y, to.y, k), dir.z);

            var face = _pos - headPos;
            return new Pose(_pos, Quaternion.LookRotation(face.sqrMagnitude > 1e-6f ? face.normalized : Vector3.forward, Vector3.up));
        }

        Vector3 Target(Vector3 headPos) => headPos + Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward * Distance + Vector3.up * Height;

        static float YawOf(Quaternion q)
        {
            var f = q * Vector3.forward;
            if (new Vector2(f.x, f.z).sqrMagnitude < 1e-6f) f = q * Vector3.up * -Mathf.Sign(f.y); // straight up/down: use the top of the head
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
    }
}
