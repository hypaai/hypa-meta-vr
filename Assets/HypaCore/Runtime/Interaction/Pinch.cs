using System;
using UnityEngine;

namespace HypaCore
{
    /// <summary>A pointer that can pinch: a tracked hand or a controller on Quest, the mouse in the editor.</summary>
    public interface IPinchInput
    {
        bool IsPinching { get; }
        /// <summary>Pointer ray starting AT the hand/controller (the pointer adds its own reach behind it).</summary>
        bool TryGetRay(out Ray ray);
        /// <summary>Small feedback on hover; no-op where unsupported.</summary>
        void Pulse();
    }

    public enum PinchKind { None, Short, Long }

    /// <summary>
    /// Raw pinch state to gestures. Short = released before <see cref="LongPressSeconds"/>; Long fires once as soon as
    /// the hold passes the threshold (no Short on that release). Pure, unit tested.
    /// </summary>
    public sealed class PinchTracker
    {
        public float LongPressSeconds = 0.6f;
        bool _down, _longFired;
        double _downAt;

        public PinchKind Update(bool isPinching, double now)
        {
            if (isPinching && !_down) { _down = true; _longFired = false; _downAt = now; return PinchKind.None; }
            if (isPinching)
            {
                if (!_longFired && now - _downAt >= LongPressSeconds) { _longFired = true; return PinchKind.Long; }
                return PinchKind.None;
            }
            if (_down) { _down = false; return _longFired ? PinchKind.None : PinchKind.Short; }
            return PinchKind.None;
        }

        public void Reset() { _down = false; _longFired = false; }
    }

    /// <summary>Anything a pinch ray can hit. Needs a Collider.</summary>
    public sealed class PinchTarget : MonoBehaviour
    {
        public Action<PinchKind> OnPinch;
        public Action<bool> OnHover;

        Renderer _r; Color _base; bool _baseSet;
        public void SetColor(Color? c)
        {
            if (_r == null) _r = GetComponent<Renderer>();
            if (_r == null) return;
            if (!_baseSet) { _base = _r.material.color; _baseSet = true; }
            _r.material.color = c ?? _base;
        }
    }

    /// <summary>Picks the input for this platform. HypaCore.Quest registers the hand-tracking factory on device.</summary>
    public static class HypaInput
    {
        public static Func<IPinchInput> DeviceFactory;
        /// <summary>
        /// In the editor, use the mouse while no XR device is running. When the Meta XR Simulator (or Link) is active,
        /// hands are used automatically. Set false to force hands in the editor.
        /// </summary>
        public static bool MouseInEditor = true;

        static IPinchInput _current;
        public static IPinchInput Current
        {
            get
            {
                bool wantMouse = DeviceFactory == null
                                 || (Application.isEditor && MouseInEditor && !UnityEngine.XR.XRSettings.isDeviceActive);
                // Re-check each call: the XR session can start after the first frame.
                if (_current != null && (_current is MouseInput) == wantMouse) return _current;
                return _current = wantMouse ? new MouseInput() : DeviceFactory();
            }
        }
        public static void Reset() => _current = null;
    }

    /// <summary>Mouse as a hand: ray through the cursor, left button = pinch.</summary>
    public sealed class MouseInput : IPinchInput
    {
        public bool IsPinching => Input.GetMouseButton(0);
        public bool TryGetRay(out Ray ray)
        {
            var cam = Camera.main;
            if (cam == null) { ray = default; return false; }
            ray = cam.ScreenPointToRay(Input.mousePosition);
            return true;
        }
        public void Pulse() { }
    }

    /// <summary>
    /// One per app (persists across scenes). Raycasts the pinch ray every frame, sends hover enter/exit and pinch
    /// gestures to the PinchTarget under the ray. Modes never poll input themselves.
    /// </summary>
    public sealed class PinchPointer : MonoBehaviour
    {
        public static PinchPointer Instance { get; private set; }
        public float MaxDistance = 5f;
        /// <summary>
        /// Hand/controller rays are cast from this far behind the hand: seated UI sits within arm's reach, often level
        /// with the hand, and a ray starting at the fingertips would skip anything at or just behind them.
        /// </summary>
        public float BackReach = 0.25f;
        const float IdleRayLength = 1.2f;
        readonly PinchTracker _tracker = new PinchTracker();
        PinchTarget _hover, _pressed;
        LineRenderer _line; Transform _cursor; Renderer _cursorR;

        public static PinchPointer EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Hypa Pinch Pointer");
            DontDestroyOnLoad(go);
            return go.AddComponent<PinchPointer>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Update()
        {
            var input = HypaInput.Current;
            var device = !(input is MouseInput);
            PinchTarget hit = null;
            var hasRay = input.TryGetRay(out var ray);
            var hitPoint = Vector3.zero; var hitSomething = false;
            if (hasRay)
            {
                var back = device ? BackReach : 0f;
                var cast = new Ray(ray.origin - ray.direction * back, ray.direction);
                if (Physics.Raycast(cast, out var h, MaxDistance + back))
                {
                    hit = h.collider.GetComponentInParent<PinchTarget>();
                    hitPoint = h.point; hitSomething = true;
                }
            }
            DrawPointer(device && hasRay, ray, hitSomething, hitPoint, hit != null, input.IsPinching);

            if (hit != _hover)
            {
                if (_hover != null) _hover.OnHover?.Invoke(false);
                _hover = hit;
                if (_hover != null) { _hover.OnHover?.Invoke(true); input.Pulse(); }
            }

            var pinching = input.IsPinching;
            if (pinching && !_wasPinching) _pressed = hit; // the gesture belongs to whatever was under the ray at pinch start
            var kind = _tracker.Update(pinching, Time.realtimeSinceStartupAsDouble);
            if (kind != PinchKind.None && _pressed != null) _pressed.OnPinch?.Invoke(kind);
            if (!pinching) _pressed = null;
            _wasPinching = pinching;
        }

        bool _wasPinching;

        // ---------- Visible pointer (hands and controllers; the mouse has its own cursor) ----------

        void DrawPointer(bool show, Ray ray, bool hitSomething, Vector3 hitPoint, bool onTarget, bool pinching)
        {
            if (_line == null) BuildPointer();
            _line.enabled = show;
            _cursor.gameObject.SetActive(show && hitSomething);
            if (!show) return;

            var end = hitSomething ? hitPoint : ray.GetPoint(IdleRayLength);
            // A target just behind the hand (inside BackReach) gets a short stub instead of a line pointing backwards.
            if (Vector3.Dot(end - ray.origin, ray.direction) < 0.02f) end = ray.origin + ray.direction * 0.02f;
            _line.SetPosition(0, ray.origin);
            _line.SetPosition(1, end);
            var col = pinching ? Ui.Accent : onTarget ? Ui.Hover : new Color(1f, 1f, 1f, 0.55f);
            _line.startColor = new Color(col.r, col.g, col.b, 0f); // fades in from the hand
            _line.endColor = col;

            if (hitSomething)
            {
                var cam = Camera.main;
                _cursor.position = hitPoint - ray.direction * 0.004f;
                if (cam != null) _cursor.rotation = Quaternion.LookRotation(_cursor.position - cam.transform.position);
                var size = (pinching ? 0.010f : 0.014f) * Mathf.Max(0.6f, Vector3.Distance(ray.origin, hitPoint));
                _cursor.localScale = new Vector3(size, size, 1f);
                _cursorR.material.color = col;
            }
        }

        void BuildPointer()
        {
            var lineGo = new GameObject("Pointer Ray");
            lineGo.transform.SetParent(transform, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.startWidth = 0.003f; _line.endWidth = 0.002f;
            _line.material = Ui.NewMaterial(Color.white);
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.enabled = false;

            var c = Ui.Quad("Pointer Cursor", transform, Vector3.zero, Vector2.one, Color.white, false);
            _cursor = c.transform;
            _cursorR = c.GetComponent<Renderer>();
            c.SetActive(false);
        }
    }
}
