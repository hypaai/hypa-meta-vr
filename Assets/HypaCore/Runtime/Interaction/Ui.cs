using System;
using UnityEngine;

namespace HypaCore
{
    /// <summary>World-space UI from primitives so modes need no prefabs or extra packages. Sizes are in metres.</summary>
    public static class Ui
    {
        // Hypa dark theme from hypa-v2 (the same neutrals as the mobile and web apps).
        // One light pill for the main action. Everything else is a dark card, like Horizon OS.
        public static readonly Color Panel = new Color(0.090f, 0.090f, 0.090f, 0.80f);  // card, #171717, a bit more glass than solid
        public static readonly Color Primary = new Color(0.898f, 0.898f, 0.898f, 1f);   // primary, #e5e5e5
        public static readonly Color Secondary = new Color(0.149f, 0.149f, 0.149f, 1f); // secondary, #262626
        public static readonly Color Tertiary = new Color(0.32f, 0.32f, 0.32f, 1f);     // hover lift, not a bright blue

        // Accent = the one light action. Muted = the quiet dark rows. Hover = a row lighting up under the hand.
        public static readonly Color Accent = Primary;
        public static readonly Color Muted = Secondary;
        public static readonly Color Hover = Tertiary;
        static readonly Color DarkText = new Color(0.09f, 0.09f, 0.09f, 1f);

        /// <summary>Dark text on a light background (e.g. Primary), white text on a dark one.</summary>
        static Color TextColorFor(Color bg)
        {
            var luma = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return luma > 0.6f ? DarkText : Color.white;
        }

        static Font _font;
        static Font DefaultFont => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        static Material _baseMat, _panelMat;
        static Texture2D _roundedMask;

        public static Material NewMaterial(Color c)
        {
            if (_baseMat == null)
                _baseMat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            return new Material(_baseMat) { color = c };
        }

        /// <summary>Flat colour, or the shared soft-rounded-corner mask for panels and buttons.</summary>
        static Material NewMaterial(Color c, bool rounded)
        {
            if (!rounded) return NewMaterial(c);
            if (_panelMat == null)
                _panelMat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard"))
                    { mainTexture = RoundedMask };
            return new Material(_panelMat) { color = c };
        }

        /// <summary>
        /// A white square with soft, antialiased rounded corners (alpha only), built once at runtime and shared by
        /// every rounded quad: Horizon OS's cards and buttons are rounded, not hard-edged rectangles.
        /// </summary>
        static Texture2D RoundedMask
        {
            get
            {
                if (_roundedMask != null) return _roundedMask;
                const int size = 128;
                var r = size * 0.46f; // pill corners, the way Horizon buttons are drawn
                var half = size * 0.5f;
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                {
                    var py = y + 0.5f - half;
                    for (var x = 0; x < size; x++)
                    {
                        var px = x + 0.5f - half;
                        // Signed distance to a rounded box (Inigo Quilez's formula): negative inside, 0 at the edge.
                        var qx = Mathf.Abs(px) - (half - r);
                        var qy = Mathf.Abs(py) - (half - r);
                        var dist = Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0))
                                   + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
                        var a = (byte)(Mathf.Clamp01(0.5f - dist) * 255);
                        pixels[y * size + x] = new Color32(255, 255, 255, a);
                    }
                }
                _roundedMask = new Texture2D(size, size, TextureFormat.RGBA32, false)
                    { name = "Hypa Rounded Mask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _roundedMask.SetPixels32(pixels);
                _roundedMask.Apply(false, true);
                return _roundedMask;
            }
        }

        /// <summary>A coloured quad, readable from the -Z side (the viewer looks along +Z).</summary>
        public static GameObject Quad(string name, Transform parent, Vector3 localPos, Vector2 size, Color color, bool collider,
            bool rounded = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = go.GetComponent<Renderer>();
            r.material = NewMaterial(color, rounded);
            // Transparent UI is sorted by distance, so a large panel behind off-centre buttons can be drawn over them.
            // Panel backgrounds always draw first, buttons next, text (sortingOrder 1) last.
            if (color == Panel)
            {
                r.sortingOrder = -1;
                // The glass treatment: a soft edge and a top highlight. Only the big background cards get it —
                // on a small button it reads as a sticker outline rather than glass, so buttons stay plain pills.
                if (rounded) { AddBorder(go.transform, size); AddSheen(go.transform, size); }
            }
            // The primitive's MeshCollider is one-sided: a hand ray arriving from behind (a hand held past the HUD,
            // common when seated) would pass straight through. A thin box is hit from either side.
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            if (collider) go.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.02f);
            return go;
        }

        /// <summary>A soft, thin edge behind a rounded panel, the way Horizon cards are outlined.</summary>
        static void AddBorder(Transform face, Vector2 size)
        {
            const float pad = 0.0022f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "border";
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(face, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.0015f);
            go.transform.localScale = new Vector3(
                (size.x + pad) / Mathf.Max(0.001f, size.x),
                (size.y + pad) / Mathf.Max(0.001f, size.y),
                1f);
            var rend = go.GetComponent<Renderer>();
            rend.material = NewMaterial(new Color(1f, 1f, 1f, 0.10f), true);
            rend.sortingOrder = -2;
        }

        static Texture2D _sheenMask;

        /// <summary>A soft top-to-bottom alpha fade (white, no shape), for the light-catching-glass highlight below.</summary>
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

        static Material _sheenMat;

        /// <summary>
        /// A soft highlight across the top of a panel, like light catching the top of a pane of glass (iOS's "liquid
        /// glass" and Horizon OS cards both lean on this). Subtle on purpose: it should read as depth, not a stripe.
        /// </summary>
        static void AddSheen(Transform face, Vector2 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "sheen";
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(face, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.0012f);
            go.transform.localScale = new Vector3(0.92f, 1f, 1f); // inset from the rounded sides a touch
            if (_sheenMat == null)
                _sheenMat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard"))
                    { mainTexture = SheenMask };
            var rend = go.GetComponent<Renderer>();
            rend.material = new Material(_sheenMat) { color = new Color(1f, 1f, 1f, 0.14f) };
            rend.sortingOrder = 0; // above the card (-1) and its border (-2), below any text or buttons on it (1)
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
            tm.fontSize = 64;
            // A TextMesh line is about characterSize * fontSize * 0.1 units tall. Tune visually if needed.
            tm.characterSize = lineHeight / (tm.fontSize * 0.1f);
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft ? TextAlignment.Left : TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = DefaultFont.material;
            return tm;
        }

        /// <summary>A pinchable button: an unscaled root holding a background quad (collider) and a label.</summary>
        public static PinchTarget Button(string label, Transform parent, Vector3 localPos, Vector2 size, Color color, Action onPress)
        {
            var root = new GameObject("Btn " + label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            var bg = Quad("bg", root.transform, Vector3.zero, size, color, true, rounded: true);
            Text(root.transform, label, size.y * 0.45f, Vector3.zero, TextColorFor(color));
            var pt = bg.AddComponent<PinchTarget>();
            pt.OnPinch = k => { if (k == PinchKind.Short) onPress?.Invoke(); };
            pt.OnHover = on => pt.SetColor(on ? Hover : (Color?)null);
            return pt;
        }

        /// <summary>The label TextMesh of a button made by <see cref="Button"/>.</summary>
        public static TextMesh LabelOf(PinchTarget button) => button.transform.parent.GetComponentInChildren<TextMesh>();

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
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(radius, radius, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = _disc;
            go.AddComponent<MeshRenderer>().material = NewMaterial(color);
            if (collider) go.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, 0.02f);
            return go;
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
            // A thin light ring, like a Horizon icon button. Not a bright coloured halo.
            Disc("ring", root.transform, new Vector3(0, 0, 0.001f), radius * 1.08f, new Color(1f, 1f, 1f, 0.18f), false);
            var bg = Disc("bg", root.transform, Vector3.zero, radius, color, true);
            if (icon != null)
            {
                var img = Quad("icon", root.transform, new Vector3(0, 0, -0.001f), Vector2.one * radius * 1.3f, Color.white, false);
                var m = img.GetComponent<Renderer>();
                m.material.mainTexture = icon;
                m.sortingOrder = 1;
            }
            Text(root.transform, icon != null ? "" : label, radius * 0.55f, Vector3.zero, TextColorFor(color));
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
