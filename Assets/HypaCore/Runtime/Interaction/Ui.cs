using System;
using UnityEngine;

namespace HypaCore
{
    /// <summary>
    /// World-space UI from generated meshes so modes need no prefabs or extra packages. Sizes are in metres.
    /// The look is Hypa's dark theme (the same neutrals as the mobile and web apps), drawn like Horizon OS: dark glass
    /// cards with a hairline edge and a soft top sheen, one light pill for the main action, dark pills for the quiet
    /// rows, Inter type that shrinks and then trims with an ellipsis rather than spilling out of its button.
    /// </summary>
    public static class Ui
    {
        public static readonly Color Panel = new Color(0.090f, 0.090f, 0.090f, 0.92f);  // card, #171717, mostly solid so text reads over a busy room
        public static readonly Color Primary = new Color(0.898f, 0.898f, 0.898f, 1f);   // primary, #e5e5e5
        public static readonly Color Secondary = new Color(0.149f, 0.149f, 0.149f, 1f); // secondary, #262626
        public static readonly Color Tertiary = new Color(0.32f, 0.32f, 0.32f, 1f);     // hover lift, not a bright blue

        /// <summary>Accent = the one light action. Muted = the quiet dark rows. Hover = a dark row lighting up under the hand.</summary>
        public static readonly Color Accent = Primary;
        public static readonly Color Muted = Secondary;
        public static readonly Color Hover = Tertiary;
        static readonly Color PrimaryHover = new Color(0.78f, 0.78f, 0.78f, 1f);  // a light pill dims a touch instead
        static readonly Color DarkText = new Color(0.09f, 0.09f, 0.09f, 1f);
        static readonly Color Edge = new Color(1f, 1f, 1f, 0.12f);

        public const float CornerRadius = 0.012f;
        const float EdgeWidth = 0.0015f;
        const float ButtonPad = 0.014f;

        /// <summary>Dark text on a light background (e.g. Primary), white text on a dark one.</summary>
        public static Color TextColorFor(Color bg)
        {
            var luma = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return luma > 0.6f ? DarkText : Color.white;
        }

        static Color HoverFor(Color bg) => TextColorFor(bg) == DarkText ? PrimaryHover : Hover;

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

        static Texture2D _sheenMask;

        /// <summary>A soft top-to-bottom alpha fade (white, no shape), for the light-catching-glass highlight on cards.</summary>
        static Texture2D SheenMask
        {
            get
            {
                if (_sheenMask != null) return _sheenMask;
                const int h = 64;
                var pixels = new Color32[h];
                for (var y = 0; y < h; y++)
                {
                    var t = y / (float)(h - 1); // 0 at the bottom, 1 at the top
                    var a = (byte)(Mathf.Pow(t, 1.6f) * 255); // most of the brightness sits near the very top
                    pixels[y] = new Color32(255, 255, 255, a);
                }
                _sheenMask = new Texture2D(1, h, TextureFormat.RGBA32, false)
                    { name = "Hypa Sheen", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _sheenMask.SetPixels32(pixels);
                _sheenMask.Apply(false, true);
                return _sheenMask;
            }
        }

        /// <summary>
        /// A coloured rounded rectangle (corner radius <see cref="CornerRadius"/> unless given), unscaled, with an
        /// optional thin box collider that is hit from either side. Panels get the glass treatment: a hairline edge
        /// behind and a soft highlight across the top, like light catching a pane of glass. Subtle on purpose.
        /// <paramref name="rounded"/> = false gives square corners (an icon); everything else is rounded.
        /// </summary>
        public static GameObject Quad(string name, Transform parent, Vector3 localPos, Vector2 size, Color color, bool collider,
            float radius = -1f, bool rounded = true)
        {
            if (!rounded) radius = 0f;
            else if (radius < 0f) radius = Mathf.Min(CornerRadius, size.y * 0.5f);
            // Transparent UI is sorted by distance, so a large panel behind off-centre buttons can be drawn over them.
            // Panel edges draw first, panel backgrounds next, the sheen and buttons after, text (sortingOrder 1) last.
            var panel = color == Panel;
            var go = Shape(name, parent, localPos, RoundedRect(size.x, size.y, radius), color, panel ? -1 : 0);
            if (panel)
            {
                Shape("edge", go.transform, new Vector3(0, 0, 0.0005f), RoundedRect(size.x + EdgeWidth * 2, size.y + EdgeWidth * 2, radius + EdgeWidth), Edge, -2);
                var sheen = Shape("sheen", go.transform, new Vector3(0, 0, -0.0005f), RoundedRect(size.x * 0.94f, size.y * 0.94f, radius), new Color(1f, 1f, 1f, 0.12f), 0);
                sheen.GetComponent<Renderer>().material.mainTexture = SheenMask;
            }
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
        /// then trimmed to fit. <see cref="Accent"/> = the one light primary action; <see cref="Muted"/> = a quiet dark
        /// row. Buttons stay plain pills: on something this small an edge reads as a sticker outline, not glass.
        /// </summary>
        public static PinchTarget Button(string label, Transform parent, Vector3 localPos, Vector2 size, Color color, Action onPress)
        {
            var root = new GameObject("Btn " + label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            var bg = Quad("bg", root.transform, Vector3.zero, size, color, true, size.y * 0.5f);
            var pt = bg.AddComponent<PinchTarget>();
            Text(root.transform, "", size.y * 0.42f, Vector3.zero, TextColorFor(color));
            SetLabel(pt, label);
            pt.OnPinch = k => { if (k == PinchKind.Short) onPress?.Invoke(); };
            var hover = HoverFor(color);
            pt.OnHover = on => pt.SetColor(on ? hover : (Color?)null);
            return pt;
        }

        /// <summary>The label TextMesh of a button made by <see cref="Button"/> or <see cref="RoundButton"/>.</summary>
        // includeInactive: a loading state hides the label, and ending it must find the label again to show it.
        public static TextMesh LabelOf(PinchTarget button) => button.transform.parent.GetComponentInChildren<TextMesh>(true);

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

        /// <summary>A shared Lucide icon from Resources/Icons (white on transparent), or null if it is missing.</summary>
        public static Texture2D Icon(string name) => Resources.Load<Texture2D>("Icons/" + name);

        /// <summary>
        /// Loading state for any button made here: its icon (or label) is swapped for a spinning loader and pinches are
        /// ignored until <paramref name="busy"/> is false again. Safe to call repeatedly and on destroyed buttons.
        /// </summary>
        public static void SetBusy(PinchTarget button, bool busy)
        {
            if (button == null) return;
            button.Busy = busy;
            var root = button.transform.parent;
            var icon = root.Find("icon");
            var label = LabelOf(button);
            var spinner = root.Find("spinner");
            if (busy && spinner == null)
            {
                var box = button.GetComponent<BoxCollider>();
                var h = box != null ? box.size.y * button.transform.lossyScale.y / Mathf.Max(1e-4f, root.lossyScale.y) : 0.04f;
                // A round button's collider is 1.6x its radius each side (3.2 across): the loader sits inside the disc.
                var size = button.GetComponent<MeshFilter>() != null && button.GetComponent<MeshFilter>().sharedMesh == _disc ? h * 0.3f : h * 0.6f;
                var tex = Icon("loader-circle");
                var bgColor = button.GetComponent<Renderer>() != null ? button.GetComponent<Renderer>().material.color : Secondary;
                var go = Quad("spinner", root, new Vector3(0, 0, -0.0015f), Vector2.one * size, TextColorFor(bgColor), false, rounded: false);
                var r = go.GetComponent<Renderer>();
                if (tex != null) r.material.mainTexture = tex;
                r.sortingOrder = 2;
                go.AddComponent<Spin>();
                spinner = go.transform;
            }
            if (spinner != null) spinner.gameObject.SetActive(busy);
            if (icon != null) icon.gameObject.SetActive(!busy);
            if (label != null && icon == null) label.gameObject.SetActive(!busy); // text buttons: the loader replaces the label
        }

        /// <summary>Turns a loader icon steadily.</summary>
        public sealed class Spin : MonoBehaviour
        {
            public float DegreesPerSecond = 360f;
            void Update() => transform.Rotate(0f, 0f, -DegreesPerSecond * Time.unscaledDeltaTime, Space.Self);
        }

        /// <summary>
        /// A round icon button (x, camera, speaker...) with an optional small caption under it. Dark, like the mobile
        /// app's camera controls; <paramref name="onLongPress"/> is optional.
        /// </summary>
        public static PinchTarget IconButton(string iconName, Transform parent, Vector3 localPos, float radius, Action onPress,
            string caption = null, Action onLongPress = null, Color? color = null)
        {
            var icon = Icon(iconName);
            var pt = RoundButton(icon != null ? "" : iconName, parent, localPos, radius, color ?? Secondary, onPress, onLongPress, icon, 1.0f);
            if (!string.IsNullOrEmpty(caption))
                Text(pt.transform.parent, caption, radius * 0.42f, new Vector3(0, -radius * 1.55f, 0), new Color(1f, 1f, 1f, 0.85f));
            return pt;
        }

        /// <summary>
        /// Round button: short pinch = <paramref name="onPress"/>, long pinch = <paramref name="onLongPress"/> (optional).
        /// With an <paramref name="icon"/> (e.g. a logo) the image is shown in a child named "icon", <paramref name="iconScale"/>
        /// times the radius across, and the label text starts empty; otherwise the label is shown.
        /// </summary>
        public static PinchTarget RoundButton(string label, Transform parent, Vector3 localPos, float radius, Color color, Action onPress,
            Action onLongPress = null, Texture2D icon = null, float iconScale = 1.3f)
        {
            var root = new GameObject("Btn " + label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            // A thin light ring, like a Horizon icon button. Not a bright coloured halo.
            var ring = Disc("ring", root.transform, new Vector3(0, 0, 0.001f), radius * 1.08f, new Color(1f, 1f, 1f, 0.18f), false);
            var ringR = ring.GetComponent<Renderer>();
            var bg = Disc("bg", root.transform, Vector3.zero, radius, color, true);
            // A generous hit area, 1.6x the radius: a ray that lands just beside the button still counts (hands wobble).
            bg.GetComponent<BoxCollider>().size = new Vector3(3.2f, 3.2f, 0.02f);
            if (icon != null)
            {
                // Icons are white on transparent; on a light button they take the dark text colour instead.
                var img = Quad("icon", root.transform, new Vector3(0, 0, -0.001f), Vector2.one * radius * iconScale, TextColorFor(color), false, rounded: false);
                var m = img.GetComponent<Renderer>();
                m.material.mainTexture = icon;
                m.sortingOrder = 1;
            }
            Text(root.transform, icon != null ? "" : label, radius * 0.5f, Vector3.zero, TextColorFor(color));
            var pt = bg.AddComponent<PinchTarget>();
            pt.OnPinch = k =>
            {
                if (k == PinchKind.Short) onPress?.Invoke();
                else if (k == PinchKind.Long) onLongPress?.Invoke();
            };
            var hover = HoverFor(color);
            // Unmistakable hover: the fill lifts, the ring lights up and the whole button grows a little.
            pt.OnHover = on =>
            {
                pt.SetColor(on ? hover : (Color?)null);
                ringR.material.color = new Color(1f, 1f, 1f, on ? 0.75f : 0.18f);
                root.transform.localScale = Vector3.one * (on ? 1.08f : 1f);
            };
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
