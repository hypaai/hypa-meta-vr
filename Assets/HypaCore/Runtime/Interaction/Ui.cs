using System;
using UnityEngine;

namespace HypaCore
{
    /// <summary>
    /// World-space UI from generated meshes so modes need no prefabs or extra packages. Sizes are in metres.
    /// The look: dark glass panels with a hairline edge, pill buttons (accent = primary action, muted = quiet),
    /// Inter type that shrinks and then trims with an ellipsis rather than spilling out of its button.
    /// </summary>
    public static class Ui
    {
        public static readonly Color Accent = new Color(0.20f, 0.55f, 1f, 1f);
        public static readonly Color Panel = new Color(0.07f, 0.08f, 0.11f, 0.94f);
        /// <summary>Pass as a button colour for the quiet style (translucent white with an edge).</summary>
        public static readonly Color Muted = new Color(0.30f, 0.30f, 0.38f, 1f);
        public static readonly Color Hover = new Color(0.45f, 0.72f, 1f, 1f);
        static readonly Color Quiet = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color QuietHover = new Color(1f, 1f, 1f, 0.24f);
        static readonly Color Edge = new Color(1f, 1f, 1f, 0.14f);

        public const float CornerRadius = 0.012f;
        const float EdgeWidth = 0.0015f;
        const float ButtonPad = 0.014f;

        static Font _font;
        /// <summary>Inter (SIL OFL, Resources/Fonts). Unity's built-in font if it is missing.</summary>
        static Font DefaultFont => _font != null ? _font
            : (_font = Resources.Load<Font>("Fonts/Inter-Regular") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        static Material _baseMat;

        public static Material NewMaterial(Color c)
        {
            if (_baseMat == null)
                _baseMat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            return new Material(_baseMat) { color = c };
        }

        // ---------- Meshes ----------

        /// <summary>A rounded rectangle, w by h metres, readable from the -Z side (the viewer looks along +Z).</summary>
        public static Mesh RoundedRect(float w, float h, float r)
        {
            r = Mathf.Clamp(r, 0f, Mathf.Min(w, h) * 0.5f);
            const int seg = 6; // per corner
            var n = 4 * (seg + 1);
            var v = new Vector3[n + 1]; var uv = new Vector2[n + 1]; var t = new int[n * 3];
            v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f);
            var cx = new[] { w * 0.5f - r, w * 0.5f - r, -w * 0.5f + r, -w * 0.5f + r };
            var cy = new[] { -h * 0.5f + r, h * 0.5f - r, h * 0.5f - r, -h * 0.5f + r };
            var a0 = new[] { -90f, 0f, 90f, 180f };
            var k = 1;
            for (var c = 0; c < 4; c++)
                for (var s = 0; s <= seg; s++)
                {
                    var a = (a0[c] + 90f * s / seg) * Mathf.Deg2Rad;
                    v[k] = new Vector3(cx[c] + r * Mathf.Cos(a), cy[c] + r * Mathf.Sin(a), 0f);
                    uv[k] = new Vector2(v[k].x / w + 0.5f, v[k].y / h + 0.5f);
                    k++;
                }
            for (var i = 0; i < n; i++) { t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % n + 1; t[i * 3 + 2] = i + 1; }
            var m = new Mesh { name = "Hypa Rounded", vertices = v, uv = uv, triangles = t };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static GameObject Shape(string name, Transform parent, Vector3 localPos, Mesh mesh, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.material = NewMaterial(color);
            r.sortingOrder = sortingOrder;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// A coloured rounded rectangle (corner radius <see cref="CornerRadius"/> unless given), unscaled, with an
        /// optional thin box collider that is hit from either side. Panels get a hairline edge behind them.
        /// </summary>
        public static GameObject Quad(string name, Transform parent, Vector3 localPos, Vector2 size, Color color, bool collider, float radius = -1f)
        {
            if (radius < 0f) radius = Mathf.Min(CornerRadius, size.y * 0.5f);
            // Transparent UI is sorted by distance, so a large panel behind off-centre buttons can be drawn over them.
            // Panel edges draw first, panel backgrounds next, buttons after, text (sortingOrder 1) last.
            var panel = color == Panel;
            var go = Shape(name, parent, localPos, RoundedRect(size.x, size.y, radius), color, panel ? -1 : 0);
            if (panel)
                Shape("edge", go.transform, new Vector3(0, 0, 0.0005f), RoundedRect(size.x + EdgeWidth * 2, size.y + EdgeWidth * 2, radius + EdgeWidth), Edge, -2);
            // A thin box, not a mesh collider: a hand ray arriving from behind (a hand held past the HUD, common when
            // seated) must still hit it.
            if (collider) go.AddComponent<BoxCollider>().size = new Vector3(size.x, size.y, 0.02f);
            return go;
        }

        static Mesh _disc;

        /// <summary>A flat coloured circle of <paramref name="radius"/> metres, readable from the -Z side.</summary>
        public static GameObject Disc(string name, Transform parent, Vector3 localPos, float radius, Color color, bool collider)
        {
            if (_disc == null)
            {
                const int n = 48;
                var v = new Vector3[n + 1]; var t = new int[n * 3];
                for (var i = 0; i < n; i++)
                {
                    var a = i * Mathf.PI * 2f / n;
                    v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % n + 1; t[i * 3 + 2] = i + 1;
                }
                _disc = new Mesh { name = "Hypa Disc", vertices = v, triangles = t };
                _disc.RecalculateNormals(); _disc.RecalculateBounds();
            }
            var go = Shape(name, parent, localPos, _disc, color, 0);
            go.transform.localScale = new Vector3(radius, radius, 1f);
            if (collider) go.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, 0.02f);
            return go;
        }

        // ---------- Text ----------

        /// <summary>Average glyph advance as a fraction of the line height (Inter, mixed case). Used to fit labels.</summary>
        public const float CharWidth = 0.52f;
        const int FontPixels = 120; // glyphs rasterised large so small text stays crisp

        public static float TextWidth(string s, float lineHeight) => (s ?? "").Length * lineHeight * CharWidth;

        /// <summary>The line height at which <paramref name="s"/> fits <paramref name="maxWidth"/>: shrinks down to
        /// <paramref name="minScale"/> of <paramref name="lineHeight"/>, never grows. Pure, unit tested.</summary>
        public static float FitLineHeight(string s, float lineHeight, float maxWidth, float minScale = 0.75f)
        {
            var w = TextWidth(s, lineHeight);
            if (w <= maxWidth || w <= 0f) return lineHeight;
            return Mathf.Max(lineHeight * minScale, lineHeight * maxWidth / w);
        }

        /// <summary>Trims <paramref name="s"/> with an ellipsis so it fits <paramref name="maxWidth"/>. Pure, unit tested.</summary>
        public static string Ellipsize(string s, float lineHeight, float maxWidth)
        {
            if (string.IsNullOrEmpty(s) || TextWidth(s, lineHeight) <= maxWidth) return s ?? "";
            var keep = Mathf.FloorToInt(maxWidth / (lineHeight * CharWidth)) - 1;
            return keep <= 0 ? "…" : s.Substring(0, Mathf.Min(keep, s.Length)).TrimEnd() + "…";
        }

        /// <summary>Text with roughly <paramref name="lineHeight"/> metres per line. Parent should be unscaled.</summary>
        public static TextMesh Text(Transform parent, string text, float lineHeight, Vector3 localPos, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos + new Vector3(0, 0, -0.002f); // just in front of a panel
            var tm = go.AddComponent<TextMesh>();
            go.GetComponent<MeshRenderer>().sortingOrder = 1; // above buttons and panels (see Quad)
            tm.font = DefaultFont;
            tm.fontSize = FontPixels;
            // A TextMesh line is about characterSize * fontSize * 0.1 units tall.
            tm.characterSize = lineHeight / (tm.fontSize * 0.1f);
            tm.lineSpacing = 1.08f;
            tm.richText = false;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.LowerLeft ? TextAlignment.Left
                : anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.LowerRight ? TextAlignment.Right
                : TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = DefaultFont.material;
            return tm;
        }

        /// <summary>Sets a TextMesh's line height in metres.</summary>
        public static void SetLineHeight(TextMesh tm, float lineHeight) => tm.characterSize = lineHeight / (tm.fontSize * 0.1f);

        // ---------- Buttons ----------

        /// <summary>
        /// A pinchable pill button: an unscaled root holding a background (collider) and a label that is shrunk and
        /// then trimmed to fit. <see cref="Accent"/> = primary; <see cref="Muted"/> = quiet (translucent, with an edge).
        /// </summary>
        public static PinchTarget Button(string label, Transform parent, Vector3 localPos, Vector2 size, Color color, Action onPress)
        {
            var root = new GameObject("Btn " + label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            var quiet = color == Muted;
            var r = size.y * 0.5f;
            var bg = Quad("bg", root.transform, Vector3.zero, size, quiet ? Quiet : color, true, r);
            if (quiet) Shape("edge", root.transform, new Vector3(0, 0, 0.0005f), RoundedRect(size.x + EdgeWidth * 2, size.y + EdgeWidth * 2, r + EdgeWidth), Edge, -1);
            var pt = bg.AddComponent<PinchTarget>();
            Text(root.transform, "", size.y * 0.42f, Vector3.zero, Color.white);
            SetLabel(pt, label);
            pt.OnPinch = k => { if (k == PinchKind.Short) onPress?.Invoke(); };
            pt.OnHover = on => pt.SetColor(on ? (quiet ? QuietHover : Hover) : (Color?)null);
            return pt;
        }

        /// <summary>The label TextMesh of a button made by <see cref="Button"/> or <see cref="RoundButton"/>.</summary>
        public static TextMesh LabelOf(PinchTarget button) => button.transform.parent.GetComponentInChildren<TextMesh>();

        /// <summary>Changes a button's label, re-fitting it to the button (shrink, then trim).</summary>
        public static void SetLabel(PinchTarget button, string label)
        {
            var tm = LabelOf(button);
            var box = button.GetComponent<BoxCollider>();
            var size = box != null ? new Vector2(box.size.x, box.size.y) : new Vector2(1f, 0.05f); // bg is unscaled: metres
            var maxW = size.x - ButtonPad * 2f;
            var lineH = FitLineHeight(label, size.y * 0.42f, maxW);
            SetLineHeight(tm, lineH);
            tm.text = Ellipsize(label, lineH, maxW);
        }

        /// <summary>
        /// Round button: short pinch = <paramref name="onPress"/>, long pinch = <paramref name="onLongPress"/> (optional).
        /// With an <paramref name="icon"/> (e.g. a logo) the image is shown in a child named "icon" and the label text
        /// starts empty; otherwise the label is shown.
        /// </summary>
        public static PinchTarget RoundButton(string label, Transform parent, Vector3 localPos, float radius, Color color, Action onPress,
            Action onLongPress = null, Texture2D icon = null)
        {
            var root = new GameObject("Btn " + label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            Disc("halo", root.transform, new Vector3(0, 0, 0.002f), radius * 1.30f, new Color(1f, 1f, 1f, 0.10f), false);
            Disc("ring", root.transform, new Vector3(0, 0, 0.001f), radius * 1.10f, new Color(1f, 1f, 1f, 0.95f), false);
            var bg = Disc("bg", root.transform, Vector3.zero, radius, color, true);
            if (icon != null)
            {
                var img = Quad("icon", root.transform, new Vector3(0, 0, -0.001f), Vector2.one * radius * 1.3f, Color.white, false, 0f);
                var m = img.GetComponent<Renderer>();
                m.material.mainTexture = icon;
                m.sortingOrder = 1;
            }
            Text(root.transform, icon != null ? "" : label, radius * 0.5f, Vector3.zero, Color.white);
            var pt = bg.AddComponent<PinchTarget>();
            pt.OnPinch = k =>
            {
                if (k == PinchKind.Short) onPress?.Invoke();
                else if (k == PinchKind.Long) onLongPress?.Invoke();
            };
            pt.OnHover = on => pt.SetColor(on ? Hover : (Color?)null);
            return pt;
        }

        public static string Wrap(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s) || maxChars <= 0) return s ?? "";
            var sb = new System.Text.StringBuilder();
            foreach (var para in s.Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                var len = 0;
                foreach (var w in para.Split(' '))
                {
                    if (len > 0 && len + 1 + w.Length > maxChars) { sb.Append('\n'); len = 0; }
                    else if (len > 0) { sb.Append(' '); len++; }
                    sb.Append(w); len += w.Length;
                }
            }
            return sb.ToString();
        }
    }
}
