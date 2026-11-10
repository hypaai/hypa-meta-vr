using System;
using UnityEngine;

namespace HypaCore
{
    /// <summary>World-space UI from primitives so modes need no prefabs or extra packages. Sizes are in metres.</summary>
    public static class Ui
    {
        public static readonly Color Accent = new Color(0.20f, 0.55f, 1f, 1f);
        public static readonly Color Panel = new Color(0.08f, 0.09f, 0.12f, 0.92f);
        public static readonly Color Muted = new Color(0.30f, 0.30f, 0.38f, 1f);
        public static readonly Color Hover = new Color(0.45f, 0.72f, 1f, 1f);

        static Font _font;
        static Font DefaultFont => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        static Material _baseMat;

        public static Material NewMaterial(Color c)
        {
            if (_baseMat == null)
                _baseMat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            return new Material(_baseMat) { color = c };
        }

        /// <summary>A coloured quad, readable from the -Z side (the viewer looks along +Z).</summary>
        public static GameObject Quad(string name, Transform parent, Vector3 localPos, Vector2 size, Color color, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.GetComponent<Renderer>().material = NewMaterial(color);
            if (!collider) UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>Text with roughly <paramref name="lineHeight"/> metres per line. Parent should be unscaled.</summary>
        public static TextMesh Text(Transform parent, string text, float lineHeight, Vector3 localPos, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos + new Vector3(0, 0, -0.002f); // just in front of a panel
            var tm = go.AddComponent<TextMesh>();
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
            var bg = Quad("bg", root.transform, Vector3.zero, size, color, true);
            Text(root.transform, label, size.y * 0.45f, Vector3.zero, Color.white);
            var pt = bg.AddComponent<PinchTarget>();
            pt.OnPinch = k => { if (k == PinchKind.Short) onPress?.Invoke(); };
            pt.OnHover = on => pt.SetColor(on ? Hover : (Color?)null);
            return pt;
        }

        /// <summary>The label TextMesh of a button made by <see cref="Button"/>.</summary>
        public static TextMesh LabelOf(PinchTarget button) => button.transform.parent.GetComponentInChildren<TextMesh>();

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
