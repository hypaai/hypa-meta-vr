using System;
using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Stops drawing what the fog already hides: every renderer and per-vertex light of the camp's world that lies more than
    /// <see cref="Range"/> from you (the far ends of the trails, the hut, the pines round them) is switched off, and back on as
    /// you walk toward it. The fog ends at 36 m, so you never see it happen; the Quest just draws less. Moving things (the
    /// wolf, the owl, the bats, the bushbaby) get extra room. Scripts and sounds keep running: 3D sounds are silent that far
    /// away anyway, and switching objects off would stop their loops.
    /// </summary>
    public sealed class DistanceCull : MonoBehaviour
    {
        /// <summary>How far you can see things (just past where the fog is complete, at 36 m).</summary>
        public static float Range = 37f;
        const float Every = 0.2f;

        struct Entry { public Renderer R; public Bounds B; public float Margin; }
        readonly List<Entry> _renderers = new List<Entry>();
        readonly List<(Light l, Vector3 at)> _lights = new List<(Light, Vector3)>();
        float _next;

        /// <summary>Where to measure from (camp ground space) instead of the camera: the far side of the portal while you look through it (3L).</summary>
        public Vector3? Viewer;

        public int Count => _renderers.Count + _lights.Count;

        /// <summary>Is a thing with these bounds (camp ground space) close enough to draw from <paramref name="viewer"/>? Pure, unit tested.</summary>
        public static bool Visible(Bounds b, Vector3 viewer, float range, float margin = 0f)
        {
            var r = range + margin;
            return b.SqrDistance(viewer) <= r * r;
        }

        /// <summary>
        /// Takes in every renderer and, when <paramref name="includeLights"/> is true, every point light under <paramref name="root"/>
        /// except under <paramref name="skip"/>, leaving out what
        /// sits within <paramref name="always"/> of the fire (the clearing is always drawn). Things under a transform whose name
        /// <paramref name="moving"/> accepts get <paramref name="moveMargin"/> extra.
        /// </summary>
        public void Collect(Transform root, Func<Transform, bool> skip, float always, Func<string, bool> moving, float moveMargin, bool includeLights = true)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (Under(r.transform, root, skip)) continue;
                var b = LocalBounds(r);
                if (b.SqrDistance(Vector3.zero) < always * always) continue;
                _renderers.Add(new Entry { R = r, B = b, Margin = Moving(r.transform, root, moving) ? moveMargin : 0f });
            }
            if (!includeLights) return;
            foreach (var l in root.GetComponentsInChildren<Light>(true))
            {
                if (l.type != LightType.Point || Under(l.transform, root, skip)) continue;
                var at = transform.InverseTransformPoint(l.transform.position);
                if (at.magnitude < always) continue;
                _lights.Add((l, at));
            }
        }

        static bool Under(Transform t, Transform root, Func<Transform, bool> skip)
        {
            for (var p = t; p != null && p != root; p = p.parent) if (skip(p)) return true;
            return false;
        }

        static bool Moving(Transform t, Transform root, Func<string, bool> moving)
        {
            for (var p = t; p != null && p != root; p = p.parent) if (moving(p.name)) return true;
            return false;
        }

        /// <summary>A renderer's bounds in this transform's space (the camp's ground), from its world bounds now.</summary>
        Bounds LocalBounds(Renderer r)
        {
            var w = r.bounds;
            var b = new Bounds(transform.InverseTransformPoint(w.center), Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var c = w.center + Vector3.Scale(w.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(transform.InverseTransformPoint(c));
            }
            return b;
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Every;
            var cam = Camera.main;
            if (cam == null) return;
            var me = Viewer ?? transform.InverseTransformPoint(cam.transform.position);
            foreach (var e in _renderers)
            {
                if (e.R == null) continue;
                var on = Visible(e.B, me, Range, e.Margin);
                if (e.R.enabled != on) e.R.enabled = on;
            }
            foreach (var (l, at) in _lights)
            {
                if (l == null) continue;
                var on = (at - me).sqrMagnitude <= Range * Range;
                if (l.enabled != on) l.enabled = on;
            }
        }
    }
}
