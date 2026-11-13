using System;
using HypaCore;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Mode select (the prototype's splash): Hypa Stories is the only mode and the campfire is the portal. The brand floats above
    /// the fire; a "Gather round" button waits comfortably in front of you. Pinch the fire (a big, forgiving target) or the button.
    /// Hovering the fire swells it and lights the ring; hover is feedback only.
    /// </summary>
    public sealed class SplashView
    {
        public readonly KitGroup Brand, Prompt;
        public readonly FollowView Follow;
        readonly Transform _brand;

        public SplashView(Transform world, Transform uiRoot, Transform fire, Texture2D logo, Action enter, Action<bool> hover)
        {
            // the brand above the fire's ring, facing you
            _brand = new GameObject("Splash Brand").transform;
            _brand.SetParent(world, false);
            _brand.position = fire.position + Vector3.up * 2.55f;
            if (logo != null) Kit.Image(_brand, logo, new Vector3(0f, 0.78f, 0f), new Vector2(0.5f, 0.26f));
            var kicker = Kit.Text(_brand, "GATHER BY THE FIRE", 0.1f, new Vector3(0f, 0.42f, 0f), new Vector2(3f, 0.2f), KitTheme.Dim, TextAlignmentOptions.Center, KitFont.UiBold);
            kicker.characterSpacing = 28f;
            Kit.Text(_brand, "Hypa", 0.36f, new Vector3(-0.63f, 0.02f, 0f), new Vector2(1f, 0.45f), KitTheme.Paper, TextAlignmentOptions.Center, KitFont.UiBold);
            Kit.Panel(_brand, "Pill", new Vector3(0.52f, 0.03f, 0.01f), new Vector2(1.18f, 0.42f), 0.09f, KitTheme.Paper, new Color(0.9f, 0.87f, 0.82f), KitTheme.Clear, 10, 0.5f);
            Kit.Text(_brand, "Stories", 0.36f, new Vector3(0.52f, 0.02f, 0f), new Vector2(1.2f, 0.45f), KitTheme.Ink, TextAlignmentOptions.Center, KitFont.UiBold);
            Brand = _brand.gameObject.AddComponent<KitGroup>();
            Brand.Refresh();

            // the fire itself is the portal
            var portal = new GameObject("Fire Portal");
            portal.transform.SetParent(_brand, false);
            portal.transform.position = fire.position + Vector3.up * 0.9f;
            portal.AddComponent<BoxCollider>().size = new Vector3(2.4f, 2f, 2.4f);
            var pt = portal.AddComponent<PinchTarget>();
            pt.OnPinch = k => { if (k == PinchKind.Short) enter(); };
            pt.OnHover = hover;

            // a near, comfortable button for anyone who doesn't think to pinch the fire
            var near = new GameObject("Splash Prompt").transform;
            near.SetParent(uiRoot, false);
            Kit.Text(near, "Pinch the fire, or", 0.02f, new Vector3(0f, 0.058f, 0f), new Vector2(0.4f, 0.03f), KitTheme.Dim);
            Kit.Button(near, Vector3.zero, new Vector2(0.24f, 0.064f), "Gather round", "flame", KitStyle.Primary, enter, null, 0.022f);
            Follow = near.gameObject.AddComponent<FollowView>();
            Follow.Distance = 0.85f; Follow.Height = -0.34f;
            Prompt = near.gameObject.AddComponent<KitGroup>();
            Prompt.Refresh();
        }

        public void SetAlpha(float a) { Brand.Set(a); Prompt.Set(a); }

        public void Update(Transform viewer)
        {
            if (viewer != null && _brand.gameObject.activeSelf) StoryUi.Face(_brand, viewer.position);
        }
    }
}
