using System;
using HypaCore;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// One story card (port of the prototype's card.js): the art quad with the card shader (ripples, parallax, sheen, tilt
    /// under the pinch ray), the eyebrow, title, description and its button, and the pinch target. Whoever owns the card
    /// places its <see cref="Root"/> and calls <see cref="Aim"/> and <see cref="Animate"/> each frame: the campfire's row
    /// (<see cref="StoryListView"/>) and the cards over the hut's household items (<c>HutVisit</c>) are the same card.
    /// </summary>
    public sealed class StoryCard
    {
        public const float W = 0.42f, H = 0.63f;

        public readonly StoryInfo Story;
        public readonly Transform Root;
        public readonly Renderer Art;
        public readonly Collider Hit;
        public readonly KitGroup Text;
        public float Hover, HoverTarget, Reveal, Press, TiltX, TiltY;
        public Vector2 Pointer = new Vector2(0.5f, 0.35f), PointerTarget = new Vector2(0.5f, 0.35f);
        /// <summary>
        /// A picture of what the card is about (with alpha), shown instead of the story's <see cref="StoryInfo.Theme"/> art: the
        /// hut's household items. Null (the campfire's stories) keeps the theme.
        /// </summary>
        public Texture Picture;

        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        static readonly int HoverId = Shader.PropertyToID("_Hover"), PressId = Shader.PropertyToID("_Press"), RevealId = Shader.PropertyToID("_Reveal"),
            DimId = Shader.PropertyToID("_Dim"), ThemeId = Shader.PropertyToID("_Theme"), PointerId = Shader.PropertyToID("_Pointer"),
            PictureId = Shader.PropertyToID("_Picture");
        const float PictureTheme = 2f;

        /// <summary>The material every card of a view shares (per-card values go through a property block).</summary>
        public static Material NewMaterial(StoryAssets a) => new Material(a.card) { name = "Story Card" };

        /// <param name="quad">A <see cref="W"/> x <see cref="H"/> quad, shared by the view's cards.</param>
        /// <param name="action">The button's words ("Begin story").</param>
        /// <param name="onCard">A short pinch on the card itself.</param>
        /// <param name="onButton">The button pressed.</param>
        public StoryCard(Transform parent, StoryInfo st, Material cardMat, Mesh quad, string action, string icon, Action onCard, Action onButton)
        {
            Story = st;
            Root = new GameObject("Card " + st.Id).transform;
            Root.SetParent(parent, false);
            var art = new GameObject("Art");
            art.transform.SetParent(Root, false);
            art.transform.localPosition = new Vector3(0, 0, 0.004f);
            art.AddComponent<MeshFilter>().sharedMesh = quad;
            Art = art.AddComponent<MeshRenderer>();
            Art.sharedMaterial = cardMat;
            Art.sortingOrder = 10;
            Hit = art.AddComponent<BoxCollider>();
            ((BoxCollider)Hit).size = new Vector3(W, H, 0.02f);
            var pt = art.AddComponent<PinchTarget>();
            pt.OnHover = on => HoverTarget = on ? 1f : 0f;
            pt.OnPinch = k => { if (k == PinchKind.Short) onCard?.Invoke(); };

            var text = new GameObject("Text").transform;
            text.SetParent(Root, false);
            const float x0 = -0.18f, w = 0.36f;
            var eyebrow = Kit.Text(text, st.Eyebrow.ToUpperInvariant(), 0.0125f, new Vector3(x0 + w / 2f, -0.035f, 0f), new Vector2(w, 0.02f), st.Accent,
                TextAlignmentOptions.Left, KitFont.UiBold);
            eyebrow.characterSpacing = 12f;
            Kit.Text(text, st.Title.Replace('\n', ' '), 0.04f, new Vector3(x0 + w / 2f, -0.09f, 0f), new Vector2(w, 0.09f), KitTheme.Paper,
                TextAlignmentOptions.BottomLeft, KitFont.Serif).lineSpacing = -18f;
            Kit.Text(text, st.Desc, 0.0165f, new Vector3(x0 + w / 2f, -0.18f, 0f), new Vector2(w, 0.08f), new Color(0.92f, 0.89f, 0.84f, 0.92f),
                TextAlignmentOptions.TopLeft);
            Kit.Button(text, new Vector3(0f, -0.268f, -0.002f), new Vector2(0.21f, 0.056f), action, icon, KitStyle.Primary, onButton);
            Text = text.gameObject.AddComponent<KitGroup>();
            Text.Refresh();
        }

        /// <summary>0 = hidden (melted into its light), 1 = shown.</summary>
        public void SetReveal(float v)
        {
            Reveal = v;
            Text.Set(Mathf.Clamp01(v * 1.5f - 0.3f));
        }

        /// <summary>The pointer's hit on the card, for its shader (the pinch ray stands in for the prototype's mouse).</summary>
        public void Aim(bool hasRay, Ray ray)
        {
            if (!hasRay || HoverTarget <= 0f) return;
            if (!Hit.Raycast(new Ray(ray.origin - ray.direction * 0.25f, ray.direction), out var h, 12f)) return;
            var local = Art.transform.InverseTransformPoint(h.point);
            PointerTarget = new Vector2(local.x / W + 0.5f, local.y / H + 0.5f);
        }

        /// <summary>
        /// Eases hover, pointer, press and tilt, tilts the art and feeds the shader. <paramref name="dim"/> is how far the card is
        /// from being the focused one (0 = focused, 1 = a neighbour).
        /// </summary>
        public void Animate(float dt, bool snap, float dim)
        {
            float K(float rate) => snap ? 1f : 1f - Mathf.Exp(-rate * dt);
            Hover += (HoverTarget * (1f - dim) - Hover) * K(6f);
            Pointer = Vector2.Lerp(Pointer, PointerTarget, K(10f));
            Press = Mathf.Max(0f, Press - dt * 0.8f);
            TiltX += (-(Pointer.y - 0.5f) * 0.22f * Hover - TiltX) * K(7f);
            TiltY += ((Pointer.x - 0.5f) * 0.30f * Hover - TiltY) * K(7f);
            Art.transform.localRotation = Quaternion.Euler(-TiltX * Mathf.Rad2Deg * 0.5f, TiltY * Mathf.Rad2Deg * 0.5f, 0f);

            Art.GetPropertyBlock(_mpb);
            _mpb.SetFloat(ThemeId, Picture != null ? PictureTheme : Story.Theme);
            if (Picture != null) _mpb.SetTexture(PictureId, Picture);
            _mpb.SetFloat(HoverId, Hover);
            _mpb.SetFloat(PressId, Press);
            _mpb.SetFloat(RevealId, Reveal);
            _mpb.SetFloat(DimId, dim);
            _mpb.SetVector(PointerId, new Vector4(Pointer.x, Pointer.y, 0f, 0f));
            Art.SetPropertyBlock(_mpb);
        }
    }
}
