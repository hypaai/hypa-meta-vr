using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Walking inside a place, the same way as at the camp: left thumbstick walks where you look, right thumbstick snap-turns,
    /// WASD in the editor. The place moves, never the camera or the rig, so it works under the shell too. You stay inside the
    /// place's walk area (<see cref="PlaceEntry.WalkCenter"/>, <see cref="PlaceEntry.WalkRadius"/>) and at a sensible eye height.
    /// Sits next to the <see cref="PlaceEntry"/>, which adds it.
    /// </summary>
    [RequireComponent(typeof(PlaceEntry))]
    public sealed class PlaceWalk : MonoBehaviour
    {
        // Same feel as the camp (StoryApp's moveSpeed, snapTurn, maxEyeHeight).
        public float MoveSpeed = 1.4f;
        public float SnapTurn = 30f;
        public const float MinEye = 0.3f, MaxEye = 2.2f;

        PlaceEntry _entry;
        string _placeId;
        readonly List<Transform> _roots = new List<Transform>();
        bool _turnArmed = true;

        void Start()
        {
            _entry = GetComponent<PlaceEntry>();
            _placeId = Places.IdForScene(gameObject.scene.name);
            // Everything in the place moves together, as when you arrive (Places puts every root on the entry); a rig stays put.
            foreach (var root in gameObject.scene.GetRootGameObjects())
                if (root.GetComponentInChildren<Camera>(true) == null) _roots.Add(root.transform);
        }

        void Update()
        {
            if (Places.IsMoving || _placeId == null || Places.Current != _placeId) return;
            var cam = Camera.main;
            if (cam == null) return;
            var head = cam.transform;

            var move = StoryInput.Move?.Invoke() ?? Vector2.zero;
            if (Application.isEditor)
            {
                if (Input.GetKey(KeyCode.W)) move.y += 1f;
                if (Input.GetKey(KeyCode.S)) move.y -= 1f;
                if (Input.GetKey(KeyCode.D)) move.x += 1f;
                if (Input.GetKey(KeyCode.A)) move.x -= 1f;
            }
            if (move.sqrMagnitude > 0.02f)
            {
                move = Vector2.ClampMagnitude(move, 1f);
                var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
                Shift(-(yaw * new Vector3(move.x, 0f, move.y)) * MoveSpeed * Time.deltaTime);
            }

            var turn = StoryInput.Turn?.Invoke() ?? 0f;
            if (_turnArmed && Mathf.Abs(turn) > 0.7f)
            {
                _turnArmed = false;
                var pivot = new Vector3(head.position.x, transform.position.y, head.position.z);
                foreach (var r in _roots) if (r != null) r.RotateAround(pivot, Vector3.up, -Mathf.Sign(turn) * SnapTurn);
            }
            else if (Mathf.Abs(turn) < 0.3f) _turnArmed = true;

            // Every frame: head movement (room-scale, or the simulator) can't leave the place either.
            var local = transform.InverseTransformPoint(head.position);
            var fix = BoundsFix(local, _entry.WalkCenter, _entry.WalkRadius, MinEye, MaxEye);
            if (fix != Vector3.zero) Shift(transform.TransformVector(fix));
        }

        void Shift(Vector3 delta)
        {
            foreach (var r in _roots) if (r != null) r.position += delta;
        }

        /// <summary>
        /// How far to move the place (in the entry's space) so the head at <paramref name="local"/> ends up inside the walk area
        /// and between <paramref name="minEye"/> and <paramref name="maxEye"/> above the floor. Zero when it already is. Pure, unit tested.
        /// </summary>
        public static Vector3 BoundsFix(Vector3 local, Vector3 center, float radius, float minEye, float maxEye)
        {
            var y = local.y - Mathf.Clamp(local.y, minEye, maxEye);
            var d = new Vector2(local.x - center.x, local.z - center.z);
            var r = d.magnitude;
            var xz = r > radius && r > 1e-4f ? d / r * (r - radius) : Vector2.zero;
            return new Vector3(xz.x, y, xz.y);
        }
    }
}
