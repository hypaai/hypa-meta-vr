using System;
using System.Collections.Generic;
using HypaCore;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The story cards on a gentle arc in front of you, a little to the right so the fire and the storyteller stay in view
    /// (port of the prototype's carousel + card.js). The focused card is centred; the others shrink and dim. Pinch a neighbour (or
    /// the arrows / dots) to move, pinch the focused card or Begin story to start. The pinch ray's hit point drives each card's
    /// shader (ripples, parallax, sheen, tilt). Below: the story language, back to start and sound.
    /// </summary>
    public sealed class StoryListView
    {
        const float Arc = 1.3f, StepDeg = 24f, CardW = 0.42f, CardH = 0.63f;

        sealed class Card
        {
            public StoryInfo Story;
            public Transform Root;
            public Renderer Art;
            public Collider Hit;
            public KitGroup Text;
            public float Hover, HoverTarget, Reveal, Press, TiltX, TiltY;
            public Vector2 Pointer = new Vector2(0.5f, 0.35f), PointerTarget = new Vector2(0.5f, 0.35f);
        }

        public readonly Transform Root;
        public readonly KitGroup Chrome;
        public readonly FollowView Follow;
        readonly List<Card> _cards = new List<Card>();
        readonly List<KitPanel> _dots = new List<KitPanel>();
        readonly List<(StoryLanguage lang, KitButton btn)> _chips = new List<(StoryLanguage, KitButton)>();
        readonly Action<int> _begin;
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        readonly Transform _chrome;
        readonly KitButton _sound;
        float _pos, _target;

        static readonly int HoverId = Shader.PropertyToID("_Hover"), PressId = Shader.PropertyToID("_Press"), RevealId = Shader.PropertyToID("_Reveal"),
            DimId = Shader.PropertyToID("_Dim"), ThemeId = Shader.PropertyToID("_Theme"), PointerId = Shader.PropertyToID("_Pointer");

        public int Index => Mathf.RoundToInt(_target);
        public int Count => _cards.Count;

        public StoryListView(Transform uiRoot, StoryAssets a, Action<int> begin, Action back, Action toggleSound, Action<StoryLanguage> pickLanguage)
        {
            _begin = begin;
            Root = new GameObject("Story List").transform;
            Root.SetParent(uiRoot, false);
            Follow = Root.gameObject.AddComponent<FollowView>();
            Follow.Distance = Arc; Follow.Height = -0.06f; Follow.YawOffset = 25f; Follow.Threshold = 40f; // right of the storyteller, who sits just right of the fire
            var cardMat = new Material(a.card) { name = "Story Card" };
            var quad = StoryMeshes.Quad(CardW, CardH);

            for (var i = 0; i < StoryLibrary.All.Length; i++)
            {
                var st = StoryLibrary.All[i];
                var c = new Card { Story = st, Root = new GameObject("Card " + st.Id).transform };
                c.Root.SetParent(Root, false);
                var art = new GameObject("Art");
                art.transform.SetParent(c.Root, false);
                art.transform.localPosition = new Vector3(0, 0, 0.004f);
                art.AddComponent<MeshFilter>().sharedMesh = quad;
                c.Art = art.AddComponent<MeshRenderer>();
                c.Art.sharedMaterial = cardMat;
                c.Art.sortingOrder = 10;
                c.Hit = art.AddComponent<BoxCollider>();
                ((BoxCollider)c.Hit).size = new Vector3(CardW, CardH, 0.02f);
                var index = i;
                var pt = art.AddComponent<PinchTarget>();
                pt.OnHover = on => c.HoverTarget = on ? 1f : 0f;
                pt.OnPinch = k => { if (k == PinchKind.Short) { if (Index == index) Begin(index); else Focus(index); } };

                var text = new GameObject("Text").transform;
                text.SetParent(c.Root, false);
                const float x0 = -0.18f, w = 0.36f;
                var eyebrow = Kit.Text(text, st.Eyebrow.ToUpperInvariant(), 0.0125f, new Vector3(x0 + w / 2f, -0.035f, 0f), new Vector2(w, 0.02f), st.Accent,
                    TextAlignmentOptions.Left, KitFont.UiBold);
                eyebrow.characterSpacing = 12f;
                Kit.Text(text, st.Title.Replace('\n', ' '), 0.04f, new Vector3(x0 + w / 2f, -0.09f, 0f), new Vector2(w, 0.09f), KitTheme.Paper,
                    TextAlignmentOptions.BottomLeft, KitFont.Serif).lineSpacing = -18f;
                Kit.Text(text, st.Desc, 0.0165f, new Vector3(x0 + w / 2f, -0.18f, 0f), new Vector2(w, 0.08f), new Color(0.92f, 0.89f, 0.84f, 0.92f),
                    TextAlignmentOptions.TopLeft);
                Kit.Button(text, new Vector3(0f, -0.268f, -0.002f), new Vector2(0.21f, 0.056f), "Begin story", "play", KitStyle.Primary, () => Begin(index));
                c.Text = text.gameObject.AddComponent<KitGroup>();
                c.Text.Refresh();
                _cards.Add(c);
            }

            // under the row: pager, language, way back and sound
            _chrome = new GameObject("Controls").transform;
            _chrome.SetParent(Root, false);
            _chrome.localPosition = new Vector3(0f, -0.4f, -0.18f); // a little closer and larger than the cards: easy to read and reach
            _chrome.localScale = Vector3.one * 1.15f;
            Kit.Button(_chrome, new Vector3(-0.11f, 0f, 0f), new Vector2(0.05f, 0.05f), null, "chevron-left", KitStyle.Ghost, () => Focus(Index - 1));
            Kit.Button(_chrome, new Vector3(0.11f, 0f, 0f), new Vector2(0.05f, 0.05f), null, "chevron-right", KitStyle.Ghost, () => Focus(Index + 1));
            for (var i = 0; i < _cards.Count; i++)
            {
                var index = i;
                var dot = Kit.Panel(_chrome, "Dot", new Vector3((i - (_cards.Count - 1) / 2f) * 0.028f, 0f, 0f), new Vector2(0.012f, 0.012f), 0.006f,
                    KitTheme.Dim, KitTheme.Dim, KitTheme.Clear, 12, 0f);
                var col = dot.gameObject.AddComponent<BoxCollider>();
                col.size = new Vector3(0.6f, 0.6f, 0.5f); // in the dot's (padded) scale: about 2.5 cm
                dot.gameObject.AddComponent<PinchTarget>().OnPinch = k => { if (k == PinchKind.Short) Focus(index); };
                _dots.Add(dot);
            }

            Kit.Glass(_chrome, "Language", new Vector3(0f, -0.075f, 0.004f), new Vector2(0.52f, 0.07f), 0.035f);
            Kit.Icon(_chrome, "languages", new Vector3(-0.225f, -0.075f, 0f), 0.026f, KitTheme.Dim);
            for (var i = 0; i < StoryLanguages.All.Length; i++)
            {
                var lang = StoryLanguages.All[i];
                var btn = Kit.Button(_chrome, new Vector3(-0.1f + i * 0.13f, -0.075f, 0f), new Vector2(0.122f, 0.05f), lang.Name, null, KitStyle.Ghost, null, null, 0.017f);
                btn.OnPress = () => { pickLanguage(lang); PaintLanguage(lang); };
                _chips.Add((lang, btn));
            }

            Kit.Button(_chrome, new Vector3(-0.19f, -0.155f, 0f), new Vector2(0.15f, 0.052f), "Back", "arrow-left", KitStyle.Ghost, back, null, 0.018f);
            _sound = Kit.Button(_chrome, new Vector3(0.235f, -0.155f, 0f), new Vector2(0.052f, 0.052f), null, "volume-2", KitStyle.Ghost, toggleSound);
            PaintLanguage(StoryLanguages.Current);
            Chrome = _chrome.gameObject.AddComponent<KitGroup>();
            Chrome.Refresh();
            Layout(0f, true);
        }

        void PaintLanguage(StoryLanguage current)
        {
            foreach (var (lang, btn) in _chips) btn.Selected = lang == current;
        }

        public void SetSound(bool muted) => _sound.SetIcon(muted ? "volume-x" : "volume-2");

        public void Focus(int i) => _target = Mathf.Clamp(i, 0, _cards.Count - 1);
        public void Step(int d) => Focus(Index + d);

        public void Begin(int i)
        {
            if (i != Index) { Focus(i); return; }
            _cards[i].Press = 1f;
            _begin(i);
        }

        public void BeginFocused() => Begin(Index);

        /// <summary>0 = hidden (melted into its light), 1 = shown. Staggered per card by the caller.</summary>
        public void SetReveal(int card, float v)
        {
            _cards[card].Reveal = v;
            _cards[card].Text.Set(Mathf.Clamp01(v * 1.5f - 0.3f));
        }

        public void ResetFocus(int i) { _target = _pos = Mathf.Clamp(i, 0, _cards.Count - 1); }

        public void Update(float dt)
        {
            if (!Root.gameObject.activeSelf) return;
            _pos += (_target - _pos) * (1f - Mathf.Exp(-8f * dt));
            Layout(dt, false);

            // the pointer's hit on a card, for its shader (the pinch ray stands in for the prototype's mouse)
            var hasRay = HypaInput.Current.TryGetRay(out var ray);
            foreach (var c in _cards)
            {
                if (!hasRay || c.HoverTarget <= 0f) continue;
                if (!c.Hit.Raycast(new Ray(ray.origin - ray.direction * 0.25f, ray.direction), out var h, 12f)) continue;
                var local = c.Art.transform.InverseTransformPoint(h.point);
                c.PointerTarget = new Vector2(local.x / CardW + 0.5f, local.y / CardH + 0.5f);
            }
        }

        void Layout(float dt, bool snap)
        {
            var t = Time.time;
            float K(float rate) => snap ? 1f : 1f - Mathf.Exp(-rate * dt);
            for (var i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                var o = i - _pos;
                var dim = Mathf.Min(Mathf.Abs(o), 1f);
                var a = o * StepDeg * Mathf.Deg2Rad;
                c.Hover += (c.HoverTarget * (1f - dim) - c.Hover) * K(6f);
                c.Pointer = Vector2.Lerp(c.Pointer, c.PointerTarget, K(10f));
                c.Press = Mathf.Max(0f, c.Press - dt * 0.8f);
                c.TiltX += (-(c.Pointer.y - 0.5f) * 0.22f * c.Hover - c.TiltX) * K(7f);
                c.TiltY += ((c.Pointer.x - 0.5f) * 0.30f * c.Hover - c.TiltY) * K(7f);
                var bob = Mathf.Sin(t * 0.6f + i * 1.3f) * 0.005f;
                // on an arc around the viewer: the row's origin is Arc metres in front, so the centre of the arc is behind it
                c.Root.localPosition = new Vector3(Mathf.Sin(a) * Arc, bob, (Mathf.Cos(a) - 1f) * Arc + dim * 0.06f);
                c.Root.localRotation = Quaternion.Euler(0f, o * StepDeg, 0f);
                c.Root.localScale = Vector3.one * ((1f - 0.14f * dim) * (1f + 0.03f * c.Hover));
                c.Art.transform.localRotation = Quaternion.Euler(-c.TiltX * Mathf.Rad2Deg * 0.5f, c.TiltY * Mathf.Rad2Deg * 0.5f, 0f);

                c.Art.GetPropertyBlock(_mpb);
                _mpb.SetFloat(ThemeId, c.Story.Theme);
                _mpb.SetFloat(HoverId, c.Hover);
                _mpb.SetFloat(PressId, c.Press);
                _mpb.SetFloat(RevealId, c.Reveal);
                _mpb.SetFloat(DimId, dim);
                _mpb.SetVector(PointerId, new Vector4(c.Pointer.x, c.Pointer.y, 0f, 0f));
                c.Art.SetPropertyBlock(_mpb);
            }
            for (var i = 0; i < _dots.Count; i++)
            {
                var on = Mathf.Abs(i - _pos) < 0.5f;
                _dots[i].Top = _dots[i].Bottom = on ? KitTheme.Ember : KitTheme.Faint;
                _dots[i].Resize(on ? new Vector2(0.026f, 0.012f) : new Vector2(0.012f, 0.012f));
            }
        }
    }
}
