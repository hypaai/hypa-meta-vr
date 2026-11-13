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
    /// the hold passes the threshold (no Short on that release). While <paramref name="canFireLong"/> is false the
    /// long pinch waits (it fires later if the hold continues and the flag comes back). Pure, unit tested.
    /// </summary>
    public sealed class PinchTracker
    {
        public float LongPressSeconds = 0.6f;
        bool _down, _longFired;
        double _downAt;

        public PinchKind Update(bool isPinching, double now, bool canFireLong = true)
        {
            if (isPinching && !_down) { _down = true; _longFired = false; _downAt = now; return PinchKind.None; }
            if (isPinching)
            {
                if (!_longFired && canFireLong && now - _downAt >= LongPressSeconds) { _longFired = true; return PinchKind.Long; }
                return PinchKind.None;
            }
            if (_down) { _down = false; return _longFired ? PinchKind.None : PinchKind.Short; }
            return PinchKind.None;
        }

        public void Reset() { _down = false; _longFired = false; }
    }

    /// <summary>
    /// Decides which target a gesture belongs to. A gesture belongs to the target under the ray when the pinch
    /// started, so a hand that drifts during the pinch still hits what it aimed at. Two refinements make a hand menu
    /// usable with shaky tracking: a long pinch only fires while the ray is on that target (or on nothing), so
    /// sliding onto another target never triggers it; and a short pinch released over a target that accepts slides
    /// (a menu item) picks that target instead, so pinch, slide, release works. Pure, unit tested.
    /// </summary>
    public sealed class PinchRouter<T> where T : class
    {
        readonly PinchTracker _tracker = new PinchTracker();
        public float LongPressSeconds { get => _tracker.LongPressSeconds; set => _tracker.LongPressSeconds = value; }
        T _pressed; bool _was;

        /// <summary>The target the current pinch started on (null between pinches).</summary>
        public T Pressed => _pressed;

        public PinchKind Update(bool pinching, double now, T hit, Func<T, bool> acceptsSlide, out T target)
        {
            if (pinching && !_was) _pressed = hit;
            var onPressed = hit == null || hit == _pressed;
            var kind = _tracker.Update(pinching, now, canFireLong: onPressed);
            target = _pressed;
            if (kind == PinchKind.Short && hit != null && hit != _pressed && acceptsSlide != null && acceptsSlide(hit)) target = hit;
            if (!pinching) _pressed = null;
            _was = pinching;
            return kind;
        }
    }

    /// <summary>Anything a pinch ray can hit. Needs a Collider.</summary>
    public sealed class PinchTarget : MonoBehaviour
    {
        public Action<PinchKind> OnPinch;
        public Action<bool> OnHover;
        /// <summary>
        /// A pinch that started elsewhere and is released over this target counts as a short pinch on it (menu items:
        /// pinch the launcher, slide onto an item, release). Off for things a drifting hand must not pick by accident.
        /// </summary>
        public bool AcceptsSlide;

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
        /// In the editor the mouse aims. With no XR device the left button pinches; with the Meta XR Simulator (or
        /// Link) running, a tracked hand's pinch works too (see <see cref="EditorXrInput"/>). Set false to use the
        /// device's own pointer in the editor, as on the headset.
        /// </summary>
        public static bool MouseInEditor = true;

        enum Kind { Mouse, Device, EditorXr }
        static IPinchInput _current; static Kind _kind;

        public static IPinchInput Current
        {
            get
            {
                // Re-check each call: the XR session can start after the first frame.
                var want = DeviceFactory == null || (Application.isEditor && MouseInEditor && !UnityEngine.XR.XRSettings.isDeviceActive) ? Kind.Mouse
                    : Application.isEditor && MouseInEditor ? Kind.EditorXr
                    : Kind.Device;
                if (_current != null && _kind == want) return _current;
                _kind = want;
                return _current = want == Kind.Mouse ? new MouseInput()
                    : want == Kind.EditorXr ? new EditorXrInput(DeviceFactory())
                    : DeviceFactory();
            }
        }
        public static void Reset() => _current = null;

        /// <summary>True when the mouse is doing the aiming (no hand ray drawn, no reach behind the hand).</summary>
        public static bool AimsWithMouse(IPinchInput input) => input is MouseInput || (input is EditorXrInput e && !e.HandRay);
    }

    /// <summary>Mouse as a hand: ray through the cursor, left button = pinch.</summary>
    public sealed class MouseInput : IPinchInput
    {
        public bool IsPinching => Input.GetMouseButton(0);
        public bool TryGetRay(out Ray ray)
        {
            var cam = Camera.main;
            if (cam == null) { ray = default; return false; }
            ray = cam.ScreenPointToRay(Input.mousePosition, Camera.MonoOrStereoscopicEye.Mono);
            return true;
        }
        public void Pulse() { }
    }

    /// <summary>
    /// Editor with the XR Simulator: the tracked hand aims (its ray is drawn, as on the headset) and either its pinch
    /// or the left mouse button pinches. With <see cref="HandAims"/> off, or no hand tracked, the mouse aims instead:
    /// webcam-tracked hands pinch well but can't hold a ray on a 4 cm button, while the mouse aims precisely.
    /// </summary>
    public sealed class EditorXrInput : IPinchInput
    {
        /// <summary>Editor only: false = the mouse aims instead of the hand. Lens toggles this with P.</summary>
        public static bool HandAims = true;

        readonly IPinchInput _device; readonly MouseInput _mouse = new MouseInput();
        public EditorXrInput(IPinchInput device) { _device = device; }
        public bool IsPinching => _mouse.IsPinching || (_device != null && _device.IsPinching);
        /// <summary>True when the hand is aiming right now (tracked, with a pointer pose).</summary>
        public bool HandRay => HandAims && _device != null && _device.TryGetRay(out _);
        public bool TryGetRay(out Ray ray)
        {
            if (HandAims && _device != null && _device.TryGetRay(out ray)) return true;
            return _mouse.TryGetRay(out ray);
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
        readonly PinchRouter<PinchTarget> _router = new PinchRouter<PinchTarget>();
        PinchTarget _hover;
        LineRenderer _line; Transform _cursor; Renderer _cursorR;

        /// <summary>The target under the ray right now (null = nothing pinchable).</summary>
        public PinchTarget Hover => _hover;
        /// <summary>True while a pinch is held.</summary>
        public bool IsPinching => _wasPinching;
        /// <summary>The target the current pinch started on (null between pinches, or if it started on nothing).</summary>
        public PinchTarget Pressed => _router.Pressed;

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
            var device = !HypaInput.AimsWithMouse(input);
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
            var kind = _router.Update(pinching, Time.realtimeSinceStartupAsDouble, hit, t => t.AcceptsSlide, out var target);
            if (kind != PinchKind.None && target != null) target.OnPinch?.Invoke(kind);
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
            var col = pinching ? Ui.Accent : onTarget ? Ui.Hover : new Color(1f, 1f, 1f, 0.9f);
            _line.startColor = new Color(col.r, col.g, col.b, 0.15f); // fades in from the hand
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
            _line.startWidth = 0.006f; _line.endWidth = 0.004f;
            _line.material = Ui.NewMaterial(Color.white);
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.enabled = false;

            var c = Ui.Disc("Pointer Cursor", transform, Vector3.zero, 0.5f, Color.white, false); // diameter = localScale
            _cursor = c.transform;
            _cursorR = c.GetComponent<Renderer>();
            c.SetActive(false);
        }
    }
}
