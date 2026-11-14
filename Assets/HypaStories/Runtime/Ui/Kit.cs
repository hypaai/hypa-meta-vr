using System;
using System.Collections.Generic;
using HypaCore;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    public enum KitFont { Ui, UiBold, Serif, SerifItalic }
    public enum KitStyle { Primary, Ghost, Quiet }

    /// <summary>The Stories look: warm dark glass, ember accents, Outfit for the interface and Cormorant Garamond for the story.</summary>
    public static class KitTheme
    {
        public static readonly Color Ember = new Color(1f, 0.66f, 0.32f);
        public static readonly Color EmberDeep = new Color(0.86f, 0.42f, 0.16f);
        public static readonly Color Paper = new Color(0.98f, 0.96f, 0.92f);
        public static readonly Color Dim = new Color(0.78f, 0.73f, 0.66f);
        public static readonly Color Faint = new Color(0.78f, 0.73f, 0.66f, 0.55f);
        public static readonly Color Ink = new Color(0.1f, 0.07f, 0.05f);
        public static readonly Color GlassTop = new Color(0.1f, 0.085f, 0.075f, 0.86f);
        public static readonly Color GlassBottom = new Color(0.045f, 0.038f, 0.034f, 0.9f);
        public static readonly Color Hairline = new Color(1f, 0.86f, 0.7f, 0.16f);
        public static readonly Color GhostTop = new Color(1f, 0.95f, 0.88f, 0.1f);
        public static readonly Color GhostBottom = new Color(1f, 0.95f, 0.88f, 0.06f);
        public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
    }

    interface IKitFade { void SetGroupAlpha(float a); }

    /// <summary>A rounded "glass" rectangle (HypaStories/UIPanel). Sizes in metres; the quad is padded for the shadow and glow.</summary>
    public sealed class KitPanel : MonoBehaviour, IKitFade
    {
        public const float Margin = 0.025f;
        public Color Top, Bottom, Border, Glow = KitTheme.Clear;
        public float Shadow = 0.35f, Radius = 0.03f, BorderWidth = 0.0012f;
        public Vector2 Size { get; private set; }
        float _alpha = 1f, _group = 1f;
        MeshRenderer _r;
        MaterialPropertyBlock _m;

        static readonly int SizeId = Shader.PropertyToID("_Size"), MarginId = Shader.PropertyToID("_Margin"), RadiusId = Shader.PropertyToID("_Radius"),
            TopId = Shader.PropertyToID("_Top"), BottomId = Shader.PropertyToID("_Bottom"), BorderId = Shader.PropertyToID("_Border"),
            BorderWId = Shader.PropertyToID("_BorderWidth"), GlowId = Shader.PropertyToID("_Glow"), ShadowId = Shader.PropertyToID("_Shadow"),
            OpacityId = Shader.PropertyToID("_Opacity");

        internal void Init(MeshRenderer r) { _r = r; _m = new MaterialPropertyBlock(); }

        public float Alpha { get => _alpha; set { _alpha = value; Apply(); } }
        public void SetGroupAlpha(float a) { _group = a; Apply(); }

        public void Resize(Vector2 size)
        {
            Size = size;
            transform.localScale = new Vector3(size.x + Margin * 2f, size.y + Margin * 2f, 1f);
            Apply();
        }

        public void Apply()
        {
            if (_r == null) return;
            var s = transform.localScale;
            _m.SetVector(SizeId, new Vector4(s.x, s.y, 0, 0));
            _m.SetFloat(MarginId, Margin);
            _m.SetFloat(RadiusId, Radius);
            _m.SetColor(TopId, Top); _m.SetColor(BottomId, Bottom); _m.SetColor(BorderId, Border); _m.SetColor(GlowId, Glow);
            _m.SetFloat(BorderWId, BorderWidth);
            _m.SetFloat(ShadowId, Shadow);
            _m.SetFloat(OpacityId, _alpha * _group);
            _r.SetPropertyBlock(_m);
        }
    }

    /// <summary>A tinted icon (HypaStories/UIIcon).</summary>
    public sealed class KitIcon : MonoBehaviour, IKitFade
    {
        public Color Color = Color.white;
        float _alpha = 1f, _group = 1f;
        MeshRenderer _r;
        MaterialPropertyBlock _m;
        static readonly int TexId = Shader.PropertyToID("_MainTex"), ColorId = Shader.PropertyToID("_Color"), OpacityId = Shader.PropertyToID("_Opacity");

        internal void Init(MeshRenderer r, Texture2D tex) { _r = r; _m = new MaterialPropertyBlock(); SetIcon(tex); }

        public void SetIcon(Texture2D tex)
        {
            if (tex != null) _m.SetTexture(TexId, tex);
            _r.enabled = tex != null;
            Apply();
        }

        public float Alpha { get => _alpha; set { _alpha = value; Apply(); } }
        public void SetGroupAlpha(float a) { _group = a; Apply(); }

        public void Apply()
        {
            _m.SetColor(ColorId, Color);
            _m.SetFloat(OpacityId, _alpha * _group);
            _r.SetPropertyBlock(_m);
        }
    }

    /// <summary>
    /// A pinchable button: rounded pill (or circle) with an optional icon and label. Hover lifts it slightly and lights its edge in
    /// ember; a short pinch presses, a long pinch (optional) does the long action. Selected keeps the ember edge (chips, toggles).
    /// </summary>
    public sealed class KitButton : MonoBehaviour
    {
        public KitPanel Bg;
        public KitIcon Icon;
        public TextMeshPro Label;
        public KitStyle Style;
        public Action OnPress, OnLong;
        public bool Selected { get => _selected; set { _selected = value; Paint(); } }
        public PinchTarget Target { get; private set; }
        bool _selected, _hover;
        float _h, _press;
        Vector3 _scale = Vector3.one;

        internal void Init(PinchTarget target)
        {
            Target = target;
            target.OnHover = on => { _hover = on; };
            target.OnPinch = k =>
            {
                if (k == PinchKind.Short) { _press = 1f; OnPress?.Invoke(); }
                else if (k == PinchKind.Long) { _press = 1f; (OnLong ?? OnPress)?.Invoke(); }
            };
            _scale = transform.localScale;
            Paint();
        }

        public void SetLabel(string text) { if (Label != null) Label.text = text; }
        public void SetIcon(string name) { if (Icon != null) Icon.SetIcon(Kit.IconTex(name)); }

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _h += ((_hover ? 1f : 0f) - _h) * (1f - Mathf.Exp(-14f * dt));
            _press = Mathf.Max(0f, _press - dt * 4f);
            transform.localScale = _scale * (1f + 0.045f * _h - 0.05f * _press);
            Paint();
        }

        void Paint()
        {
            if (Bg == null) return;
            var glow = Mathf.Max(_h, _selected ? 0.75f : 0f);
            switch (Style)
            {
                case KitStyle.Primary:
                    Bg.Top = Color.Lerp(KitTheme.Ember, Color.white, 0.12f * _h);
                    Bg.Bottom = Color.Lerp(KitTheme.EmberDeep, KitTheme.Ember, 0.25f * _h);
                    Bg.Border = new Color(1f, 0.9f, 0.75f, 0.35f);
                    break;
                case KitStyle.Ghost:
                    Bg.Top = Color.Lerp(KitTheme.GhostTop, new Color(1f, 0.8f, 0.6f, 0.2f), _h);
                    Bg.Bottom = Color.Lerp(KitTheme.GhostBottom, new Color(1f, 0.7f, 0.45f, 0.14f), _h);
                    Bg.Border = Color.Lerp(new Color(1f, 0.92f, 0.82f, 0.16f), KitTheme.Ember, glow * 0.8f);
                    break;
                default:
                    Bg.Top = new Color(1f, 0.9f, 0.8f, 0.1f * _h);
                    Bg.Bottom = new Color(1f, 0.9f, 0.8f, 0.07f * _h);
                    Bg.Border = new Color(1f, 0.9f, 0.8f, 0.12f * _h);
                    break;
            }
            Bg.Glow = new Color(KitTheme.Ember.r, KitTheme.Ember.g, KitTheme.Ember.b, 0.5f * glow);
            Bg.Apply();
            if (Style == KitStyle.Ghost && Label != null) Label.color = _selected ? KitTheme.Ember : KitTheme.Paper;
            if (Style == KitStyle.Ghost && Icon != null) { Icon.Color = _selected ? KitTheme.Ember : KitTheme.Paper; Icon.Apply(); }
        }
    }

    /// <summary>Fades a whole UI group (panels, icons, TextMeshPro text). Call <see cref="Refresh"/> after adding content.</summary>
    public sealed class KitGroup : MonoBehaviour
    {
        readonly List<IKitFade> _fades = new List<IKitFade>();
        /// <summary>Each text's own alpha, taken the first time it is seen: a later Refresh while faded out must not read the faded value.</summary>
        readonly Dictionary<TMP_Text, float> _texts = new Dictionary<TMP_Text, float>();
        public float Alpha { get; private set; } = 1f;

        public void Refresh()
        {
            _fades.Clear();
            foreach (var f in GetComponentsInChildren<IKitFade>(true)) _fades.Add(f);
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
                if (!_texts.ContainsKey(t)) _texts[t] = t.color.a;
        }

        public void Set(float a)
        {
            Alpha = a;
            foreach (var f in _fades) f.SetGroupAlpha(a);
            foreach (var kv in _texts) if (kv.Key != null) kv.Key.alpha = kv.Value * a;
            gameObject.SetActive(a > 0.001f);
        }
    }

    /// <summary>
    /// Keeps a panel comfortably in front of the viewer without being glued to the head ("lazy follow"): it stays put while you look
    /// around a little, and glides back in front when you turn more than <see cref="Threshold"/> degrees or move away.
    /// It always faces you and sits <see cref="Height"/> metres relative to eye level.
    /// </summary>
    public sealed class FollowView : MonoBehaviour
    {
        public float Distance = 1.2f, Height = -0.1f, YawOffset, Threshold = 32f, Speed = 3.5f;
        float _yaw;
        bool _moving, _placed;

        static Transform Head => Camera.main != null ? Camera.main.transform : null;

        /// <summary>Put it in front of the viewer right now (on transitions, behind the veil).</summary>
        public void Snap()
        {
            var h = Head;
            if (h == null) return;
            _yaw = h.eulerAngles.y;
            transform.position = Target(h);
            Face(h);
            _placed = true; _moving = false;
        }

        Vector3 Target(Transform h)
        {
            var dir = Quaternion.Euler(0f, _yaw + YawOffset, 0f) * Vector3.forward;
            return h.position + dir * Distance + Vector3.up * Height;
        }

        void Face(Transform h)
        {
            var d = transform.position - h.position;
            if (d.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        void LateUpdate()
        {
            var h = Head;
            if (h == null) return;
            if (!_placed) { Snap(); return; }
            var off = Mathf.Abs(Mathf.DeltaAngle(h.eulerAngles.y, _yaw));
            var flat = transform.position - h.position; flat.y = 0f;
            if (!_moving && (off > Threshold || Mathf.Abs(flat.magnitude - Distance) > 0.35f)) _moving = true;
            if (_moving)
            {
                _yaw = Mathf.LerpAngle(_yaw, h.eulerAngles.y, 1f - Mathf.Exp(-Speed * Time.deltaTime));
                if (Mathf.Abs(Mathf.DeltaAngle(h.eulerAngles.y, _yaw)) < 2f) _moving = false;
            }
            transform.position = Vector3.Lerp(transform.position, Target(h), 1f - Mathf.Exp(-Speed * 1.5f * Time.deltaTime));
            Face(h);
        }
    }

    /// <summary>Builds Stories UI elements. <see cref="Init"/> once with the scene's assets.</summary>
    public static class Kit
    {
        static StoryAssets _a;
        static Material _panelMat, _iconMat;
        static Mesh _quad;

        public static void Init(StoryAssets a)
        {
            _a = a;
            _panelMat = new Material(a.uiPanel) { name = "Kit Panel" };
            _iconMat = new Material(a.uiIcon) { name = "Kit Icon" };
            _quad = StoryMeshes.Quad(1f, 1f);
        }

        public static Texture2D IconTex(string name) => string.IsNullOrEmpty(name) ? null : _a.Icon(name);

        static GameObject Node(string name, Transform parent, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go;
        }

        /// <summary>A rounded panel centred at <paramref name="pos"/>. Sorting order: panels 10, buttons 11, content 12.</summary>
        public static KitPanel Panel(Transform parent, string name, Vector3 pos, Vector2 size, float radius, Color top, Color bottom, Color border,
            int order = 10, float shadow = 0.35f)
        {
            var go = Node(name, parent, pos);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _panelMat;
            r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var p = go.AddComponent<KitPanel>();
            p.Init(r);
            p.Top = top; p.Bottom = bottom; p.Border = border; p.Radius = radius; p.Shadow = shadow;
            p.Resize(size);
            return p;
        }

        /// <summary>The standard glass panel.</summary>
        public static KitPanel Glass(Transform parent, string name, Vector3 pos, Vector2 size, float radius = 0.03f)
            => Panel(parent, name, pos, size, radius, KitTheme.GlassTop, KitTheme.GlassBottom, KitTheme.Hairline);

        public static KitIcon Icon(Transform parent, string icon, Vector3 pos, float size, Color c, int order = 12)
        {
            var go = Node("Icon " + icon, parent, pos + new Vector3(0, 0, -0.002f));
            go.transform.localScale = new Vector3(size, size, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _iconMat;
            r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var i = go.AddComponent<KitIcon>();
            i.Color = c;
            i.Init(r, IconTex(icon));
            return i;
        }

        /// <summary>A picture (e.g. the Hypa Reality mark) with alpha, untinted.</summary>
        public static KitIcon Image(Transform parent, Texture2D tex, Vector3 pos, Vector2 size, int order = 12)
        {
            var i = Icon(parent, "", pos, 1f, Color.white, order);
            i.transform.localScale = new Vector3(size.x, size.y, 1f);
            i.SetIcon(tex);
            return i;
        }

        static TMP_FontAsset FontOf(KitFont f) => f == KitFont.UiBold ? _a.uiFontBold : f == KitFont.Serif ? _a.serifFont : f == KitFont.SerifItalic ? _a.serifItalic : _a.uiFont;

        /// <summary>
        /// Text in a <paramref name="box"/> (metres) centred at <paramref name="pos"/>. <paramref name="em"/> is the font size in metres
        /// (TextMeshPro's 3D text is 0.1 units per point).
        /// </summary>
        public static TextMeshPro Text(Transform parent, string text, float em, Vector3 pos, Vector2 box, Color c,
            TextAlignmentOptions align = TextAlignmentOptions.Center, KitFont font = KitFont.Ui, int order = 12, bool wrap = true)
        {
            var go = Node("Text", parent, pos + new Vector3(0, 0, -0.003f));
            var t = go.AddComponent<TextMeshPro>();
            var fa = FontOf(font);
            if (fa != null) t.font = fa;
            t.fontSize = em * 10f;
            t.color = c;
            t.alignment = align;
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;
            t.rectTransform.sizeDelta = box;
            t.text = text;
            var r = go.GetComponent<MeshRenderer>();
            r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }

        /// <summary>
        /// A button. Icon only (no label) = a round button; with a label the icon sits to its left. Primary = ember, Ghost = glass,
        /// Quiet = no fill until hovered.
        /// </summary>
        public static KitButton Button(Transform parent, Vector3 pos, Vector2 size, string label, string icon, KitStyle style,
            Action onPress, Action onLong = null, float em = 0.019f)
        {
            var go = Node("Button " + (label ?? icon), parent, pos);
            var round = string.IsNullOrEmpty(label);
            var bg = Panel(go.transform, "bg", Vector3.zero, size, Mathf.Min(size.x, size.y) * 0.5f, KitTheme.GhostTop, KitTheme.GhostBottom,
                KitTheme.Hairline, 11, style == KitStyle.Quiet ? 0f : 0.25f);
            var ink = style == KitStyle.Primary ? KitTheme.Ink : KitTheme.Paper;
            KitIcon ic = null;
            TextMeshPro lb = null;
            var iconSize = size.y * 0.46f;
            if (round) ic = Icon(go.transform, icon, Vector3.zero, iconSize, ink);
            else
            {
                var textW = size.x - (icon != null ? iconSize + 0.03f : 0.02f);
                if (icon != null)
                {
                    var labelW = Mathf.Min(textW, label.Length * em * 0.5f + 0.004f);
                    var start = -(iconSize + 0.008f + labelW) / 2f;
                    ic = Icon(go.transform, icon, new Vector3(start + iconSize / 2f, 0f, 0f), iconSize, ink);
                    lb = Text(go.transform, label, em, new Vector3(start + iconSize + 0.008f + labelW / 2f, 0.0f, 0f), new Vector2(labelW + 0.02f, size.y), ink,
                        TextAlignmentOptions.Center, style == KitStyle.Primary ? KitFont.UiBold : KitFont.Ui, 12, false);
                }
                else lb = Text(go.transform, label, em, Vector3.zero, new Vector2(textW, size.y), ink, TextAlignmentOptions.Center,
                    style == KitStyle.Primary ? KitFont.UiBold : KitFont.Ui, 12, false);
            }
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.02f);
            var b = go.AddComponent<KitButton>();
            b.Bg = bg; b.Icon = ic; b.Label = lb; b.Style = style; b.OnPress = onPress; b.OnLong = onLong;
            b.Init(go.AddComponent<PinchTarget>());
            return b;
        }
    }
}
