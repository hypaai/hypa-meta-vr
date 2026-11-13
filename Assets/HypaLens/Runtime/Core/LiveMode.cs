using System;
using System.Collections.Generic;
using UnityEngine;

namespace HypaLens
{
    /// <summary>
    /// Live mode's trigger: when the aim (hand pointer, or head direction without a hand) rests on one area of the
    /// camera frame for <see cref="DwellSeconds"/>, run recognition on that area. Never more often than
    /// <see cref="MinInterval"/> (the throttle in HYP-164), never while a run is in progress, and a moving aim starts
    /// the dwell again. Quest 3 has no eye tracking, so nothing here depends on gaze. Pure, unit tested.
    /// </summary>
    public sealed class DwellTrigger
    {
        public float DwellSeconds = 1.0f;
        public float MinInterval = 1.0f;
        public float RadiusPx = 120f;   // "the same area", in frame pixels

        Vector2 _anchor; double _anchorAt; double _lastFire = double.NegativeInfinity; bool _has;

        /// <summary>Where the current dwell started (valid while the aim has been steady).</summary>
        public Vector2 Anchor => _anchor;

        /// <summary><paramref name="aim"/> in frame pixels, null = nothing to aim at. True = run recognition now.</summary>
        public bool Update(Vector2? aim, bool busy, double now)
        {
            if (aim == null) { _has = false; return false; }
            var a = aim.Value;
            if (!_has || Vector2.Distance(a, _anchor) > RadiusPx) { _anchor = a; _anchorAt = now; _has = true; return false; }
            if (busy || now - _anchorAt < DwellSeconds || now - _lastFire < MinInterval) return false;
            _lastFire = now;
            return true;
        }

        public void Reset() { _has = false; }
    }

    /// <summary>Cropping a captured frame to the area being looked at, and mapping the recognised boxes back. Pure, unit tested.</summary>
    public static class FrameCrop
    {
        /// <summary>A region of the frame centred on <paramref name="aim"/>, <paramref name="fraction"/> of its size, clamped inside it.</summary>
        public static FrameRect RegionAround(Vector2 aim, int frameWidth, int frameHeight, float fraction)
        {
            var w = Mathf.Clamp(frameWidth * fraction, 1f, frameWidth);
            var h = Mathf.Clamp(frameHeight * fraction, 1f, frameHeight);
            var x = Mathf.Clamp(aim.x - w * 0.5f, 0f, frameWidth - w);
            var y = Mathf.Clamp(aim.y - h * 0.5f, 0f, frameHeight - h);
            return new FrameRect(x, y, w, h);
        }

        public static bool Contains(FrameRect r, Vector2 p) => p.x >= r.X && p.x <= r.X + r.Width && p.y >= r.Y && p.y <= r.Y + r.Height;

        /// <summary>Intrinsics to assume before the first frame has told us the real ones (a Quest passthrough camera is roughly this).</summary>
        public static CameraIntrinsics ApproxIntrinsics(Vector2Int size) => new CameraIntrinsics(size.x * 0.7f, size.x * 0.7f, size.x * 0.5f, size.y * 0.5f);

        /// <summary>
        /// The pixels inside <paramref name="region"/> as their own frame (Unity row order, bottom row first, like the
        /// source). Remembers where it came from in CropX/CropY and FullWidth/FullHeight. Null when the region is empty.
        /// A frame without pixels (the simulator) crops to an empty pixel array but keeps the geometry.
        /// </summary>
        public static CapturedFrame Crop(CapturedFrame f, FrameRect region)
        {
            var x0 = Mathf.Clamp(Mathf.FloorToInt(region.X), 0, f.Width);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(region.Y), 0, f.Height);
            var x1 = Mathf.Clamp(Mathf.CeilToInt(region.X + region.Width), 0, f.Width);
            var y1 = Mathf.Clamp(Mathf.CeilToInt(region.Y + region.Height), 0, f.Height);
            var w = x1 - x0; var h = y1 - y0;
            if (w <= 0 || h <= 0) return null;
            var hasPixels = f.Pixels != null && f.Pixels.Length >= f.Width * f.Height;
            var px = new Color32[hasPixels ? w * h : 0];
            if (hasPixels)
                for (var r = 0; r < h; r++) // dest row r (bottom first) is image row y1-1-r, i.e. source row f.Height-y1+r
                    Array.Copy(f.Pixels, (f.Height - y1 + r) * f.Width + x0, px, r * w, w);
            return new CapturedFrame
            {
                Width = w, Height = h, Pixels = px, CameraPose = f.CameraPose, Intrinsics = f.Intrinsics,
                CropX = x0, CropY = y0,
                FullWidth = f.FullWidth > 0 ? f.FullWidth : f.Width,
                FullHeight = f.FullHeight > 0 ? f.FullHeight : f.Height,
            };
        }

        /// <summary>Moves boxes recognised in a cropped frame back into full-frame pixels.</summary>
        public static void OffsetBack(OcrResult ocr, CapturedFrame cropped)
        {
            if (ocr == null) return;
            float dx = cropped.CropX, dy = cropped.CropY;
            FrameRect Move(FrameRect b) => new FrameRect(b.X + dx, b.Y + dy, b.Width, b.Height);
            foreach (var line in ocr.Lines)
            {
                line.Box = Move(line.Box);
                foreach (var w in line.Words) w.Box = Move(w.Box);
            }
            ocr.FrameWidth = cropped.FullWidth; ocr.FrameHeight = cropped.FullHeight;
        }
    }

    /// <summary>
    /// The labels live mode keeps on the walls. A word recognised where a label with the same text already is reuses
    /// that label (no flicker, no jump); a label the camera looked straight at <see cref="MissLimit"/> times without
    /// finding its word is dropped (the sign changed, or it was a misread); a label out of view for
    /// <see cref="KeepOutOfViewSeconds"/> is dropped. Pure, unit tested; T is the scene object the caller owns.
    /// </summary>
    public sealed class LiveLabelSet<T> where T : class
    {
        public float MatchDistance = 0.06f;
        public float KeepOutOfViewSeconds = 2f;
        public int MissLimit = 3;
        /// <summary>Hard cap: beyond this the label seen longest ago goes, whatever else happens (keeps the frame rate safe).</summary>
        public int MaxLabels = 80;

        sealed class Entry { public T Item; public string Text; public Vector3 Pos; public double LastSeen; public int Misses; public bool Matched; }
        readonly List<Entry> _entries = new List<Entry>();

        public int Count => _entries.Count;
        public IEnumerable<T> Items { get { foreach (var e in _entries) yield return e.Item; } }

        public void BeginRun() { foreach (var e in _entries) e.Matched = false; }

        /// <summary>The existing label for this word, or null (then create one and <see cref="Add"/> it). Marks it seen.</summary>
        public T Match(string text, Vector3 pos, double now)
        {
            Entry best = null; var bestD = MatchDistance;
            foreach (var e in _entries)
            {
                if (e.Matched || !string.Equals(e.Text, text, StringComparison.OrdinalIgnoreCase)) continue;
                var d = Vector3.Distance(e.Pos, pos);
                if (d <= bestD) { bestD = d; best = e; }
            }
            if (best == null) return null;
            best.Matched = true; best.LastSeen = now; best.Misses = 0;
            return best.Item;
        }

        /// <summary>Tracks a new label. Returns the label that had to go to stay under <see cref="MaxLabels"/>, or null.</summary>
        public T Add(T item, string text, Vector3 pos, double now)
        {
            _entries.Add(new Entry { Item = item, Text = text, Pos = pos, LastSeen = now, Matched = true });
            if (_entries.Count <= MaxLabels) return null;
            var oldest = 0;
            for (var i = 1; i < _entries.Count; i++) if (_entries[i].LastSeen < _entries[oldest].LastSeen) oldest = i;
            var gone = _entries[oldest].Item;
            _entries.RemoveAt(oldest);
            return gone;
        }

        /// <summary>End of a run: labels the run looked at (<paramref name="covered"/>) but did not find get a miss. Returns the labels to drop.</summary>
        public List<T> EndRun(Func<Vector3, bool> covered)
        {
            var drop = new List<T>();
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Matched || !covered(e.Pos)) continue;
                if (++e.Misses >= MissLimit) { drop.Add(e.Item); _entries.RemoveAt(i); }
            }
            return drop;
        }

        /// <summary>Every frame: a label in view stays; one out of view for a while is dropped. Returns the labels to drop.</summary>
        public List<T> Tick(Func<Vector3, bool> inView, double now)
        {
            var drop = new List<T>();
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (inView(e.Pos)) { e.LastSeen = now; continue; }
                if (now - e.LastSeen > KeepOutOfViewSeconds) { drop.Add(e.Item); _entries.RemoveAt(i); }
            }
            return drop;
        }

        public void Remove(T item) => _entries.RemoveAll(e => e.Item == item);
        public void Clear() => _entries.Clear();
    }
}
