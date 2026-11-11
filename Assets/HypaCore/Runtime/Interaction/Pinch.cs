using System;
using UnityEngine;

namespace HypaCore
{
    /// <summary>A pointer that can pinch: a tracked hand on Quest, the mouse in the editor.</summary>
    public interface IPinchInput
    {
        bool IsPinching { get; }
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
        readonly PinchTracker _tracker = new PinchTracker();
        PinchTarget _hover, _pressed;

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
            PinchTarget hit = null;
            if (input.TryGetRay(out var ray) && Physics.Raycast(ray, out var h, MaxDistance))
                hit = h.collider.GetComponentInParent<PinchTarget>();

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
    }
}
