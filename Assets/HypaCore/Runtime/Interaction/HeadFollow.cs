using UnityEngine;

namespace HypaCore
{
    /// <summary>
    /// Keeps a panel comfortably in front of the viewer. Two styles:
    ///
    /// - Yaw only (default, like Stories' FollowView): it sits <see cref="Distance"/> out at <see cref="Height"/> from eye
    ///   level in the direction the head faces, and turns to face the eyes, so it reads straight on from above or below.
    /// - <see cref="FollowPitch"/>: it also follows looking up and down (to <see cref="MaxPitch"/>), staying centred in
    ///   the view and square to the line of sight, like a screen held in front of the eyes.
    ///
    /// Looking around a little (inside <see cref="Threshold"/> degrees) leaves it still, so a hand ray can land on it;
    /// the threshold should be at least half the panel's width, or looking at its edge would move it. It only starts
    /// after the gaze has stayed past the threshold for <see cref="Delay"/> seconds, so a glance never moves it.
    /// Past that it glides back to the centre on a critically damped spring (<see cref="SmoothTime"/>): it eases in and
    /// out, never kicks off at full speed, never overshoots, and swings round the user at a fixed distance. Walking
    /// carries it along at once (it is placed relative to the head every frame).
    ///
    /// While the hand is on it (<c>hold</c>) a glide doesn't start; one that has started always finishes. A sudden jump
    /// of the head (the XR session starting at eye height, a recentre) snaps it in front, and a long frame (a scene load,
    /// a hitch) is treated as a short one so it never leaps. Pure, unit tested.
    /// </summary>
    public sealed class HeadFollow
    {
        public float Distance = 1.0f;    // metres from the eyes; beyond the hands so a hand ray crosses the panel
        public float Height = 0f;        // metres from the line of sight (negative = below)
        public bool FollowPitch;         // also follow looking up/down
        public float MaxPitch = 60f;     // degrees up/down it follows (FollowPitch only)
        public float Threshold = 28f;    // degrees off-centre before it follows
        public float SmoothTime = 0.3f;  // seconds; roughly how long a re-centre takes
        public float Delay = 0f;         // seconds the gaze must stay past the threshold before it follows
        public float SnapJump = 0.5f;    // metres the head can move in one frame before it counts as a jump
        const float MaxStep = 1f / 30f;  // seconds; longer frames are treated as this long
        const float Settled = 0.4f;      // degrees: close enough to stop

        bool _placed, _moving;
        float _yaw, _pitch, _yawVel, _pitchVel, _away;
        Vector3 _lastHead;

        public bool IsFollowing => _moving;

        /// <summary>The next update snaps the panel straight in front of the eyes.</summary>
        public void Reset() { _placed = false; _moving = false; }

        /// <summary>The pose the panel should have this frame. The panel's +Z points away from the viewer.</summary>
        public Pose Update(Vector3 headPos, Quaternion headRot, bool hold, float dt)
        {
            Gaze(headRot, out var yaw, out var pitch);
            if (!FollowPitch) pitch = 0f;
            else pitch = Mathf.Clamp(pitch, -MaxPitch, MaxPitch);

            if (!_placed || (headPos - _lastHead).sqrMagnitude > SnapJump * SnapJump)
            {
                _placed = true; _moving = false;
                _yaw = yaw; _pitch = pitch; _yawVel = _pitchVel = 0f; _away = 0f;
            }
            _lastHead = headPos;
            dt = Mathf.Clamp(dt, 0f, MaxStep);

            var off = Vector3.Angle(Dir(_yaw, _pitch), Dir(yaw, pitch));
            _away = off > Threshold && !hold ? _away + dt : 0f;
            if (!_moving && _away >= Delay && off > Threshold && !hold) _moving = true;
            if (_moving && dt > 0f)
            {
                _yaw = Mathf.SmoothDampAngle(_yaw, yaw, ref _yawVel, SmoothTime, Mathf.Infinity, dt);
                _pitch = Mathf.SmoothDampAngle(_pitch, pitch, ref _pitchVel, SmoothTime, Mathf.Infinity, dt);
                if (Vector3.Angle(Dir(_yaw, _pitch), Dir(yaw, pitch)) < Settled && Mathf.Abs(_yawVel) + Mathf.Abs(_pitchVel) < 5f)
                {
                    _moving = false; _yawVel = _pitchVel = 0f;
                }
            }

            var view = Quaternion.Euler(-_pitch, _yaw, 0f);
            var up = FollowPitch ? view * Vector3.up : Vector3.up;
            var pos = headPos + view * Vector3.forward * Distance + up * Height;
            Quaternion rot;
            if (FollowPitch) rot = view; // square to the line of sight
            else
            {
                var face = pos - headPos;
                rot = Quaternion.LookRotation(face.sqrMagnitude > 1e-6f ? face.normalized : Vector3.forward, Vector3.up);
            }
            return new Pose(pos, rot);
        }

        static Vector3 Dir(float yaw, float pitch) => Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;

        static void Gaze(Quaternion q, out float yaw, out float pitch)
        {
            var f = q * Vector3.forward;
            var flat = new Vector2(f.x, f.z);
            if (flat.sqrMagnitude < 1e-6f) { var u = q * Vector3.up * -Mathf.Sign(f.y); flat = new Vector2(u.x, u.z); } // straight up/down
            yaw = Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
