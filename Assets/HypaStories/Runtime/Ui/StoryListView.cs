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
        const float Arc = 1.3f, StepDeg = 24f;

        public readonly Transform Root;
        public readonly KitGroup Chrome;
        public readonly FollowView Follow;
        readonly List<StoryCard> _cards = new List<StoryCard>();
        readonly List<StoryCard> _catalog = new List<StoryCard>();
        readonly List<KitPanel> _dots = new List<KitPanel>();
        readonly List<(StoryLanguage lang, KitButton btn)> _chips = new List<(StoryLanguage, KitButton)>();
        readonly Action<int> _begin;
        readonly Transform _chrome;
        readonly KitButton _sound;
        float _pos, _target;

        public int Index => Mathf.RoundToInt(_target);
        public int Count => _cards.Count;
        public StoryInfo StoryAt(int index) => _cards[index].Story;

        public StoryListView(Transform uiRoot, StoryAssets a, Action<int> begin, Action back, Action toggleSound, Action<StoryLanguage> pickLanguage)
        {
            _begin = begin;
            Root = new GameObject("Story List").transform;
            Root.SetParent(uiRoot, false);
            Follow = Root.gameObject.AddComponent<FollowView>();
            Follow.Distance = Arc; Follow.Height = -0.06f; Follow.YawOffset = 25f; Follow.Threshold = 40f; // right of the storyteller, who sits just right of the fire
            var cardMat = StoryCard.NewMaterial(a);
            var quad = StoryMeshes.Quad(StoryCard.W, StoryCard.H);

            for (var i = 0; i < StoryLibrary.All.Length; i++)
            {
                StoryCard card = null;
                card = new StoryCard(Root, StoryLibrary.All[i], cardMat, quad, "Begin story", "play",
                    () =>
                    {
                        var index = _cards.IndexOf(card);
                        if (index < 0) return;
                        if (Index == index) Begin(index); else Focus(index);
                    }, () => Begin(_cards.IndexOf(card)));
                _catalog.Add(card);
            }

            // under the row: pager, language, way back and sound
            _chrome = new GameObject("Controls").transform;
            _chrome.SetParent(Root, false);
            _chrome.localPosition = new Vector3(0f, -0.4f, -0.18f); // a little closer and larger than the cards: easy to read and reach
            _chrome.localScale = Vector3.one * 1.15f;
            Kit.Button(_chrome, new Vector3(-0.11f, 0f, 0f), new Vector2(0.05f, 0.05f), null, "chevron-left", KitStyle.Ghost, () => Focus(Index - 1));
            Kit.Button(_chrome, new Vector3(0.11f, 0f, 0f), new Vector2(0.05f, 0.05f), null, "chevron-right", KitStyle.Ghost, () => Focus(Index + 1));
            for (var i = 0; i < _catalog.Count; i++)
            {
                var index = i;
                var dot = Kit.Panel(_chrome, "Dot", Vector3.zero, new Vector2(0.012f, 0.012f), 0.006f,
                    KitTheme.Dim, KitTheme.Dim, KitTheme.Clear, 12, 0f);
                var col = dot.gameObject.AddComponent<BoxCollider>();
                col.size = new Vector3(0.6f, 0.6f, 0.5f); // in the dot's (padded) scale: about 2.5 cm
                dot.gameObject.AddComponent<PinchTarget>().OnPinch = k => { if (k == PinchKind.Short) Focus(index); };
                _dots.Add(dot);
            }

            // languages: four chips a row, as many rows as it takes
            const int perRow = 4;
            const float row = 0.056f;
            var rows = (StoryLanguages.All.Length + perRow - 1) / perRow;
            var extra = (rows - 1) * row;
            Kit.Glass(_chrome, "Language", new Vector3(0f, -0.075f - extra / 2f, 0.004f), new Vector2(0.56f, 0.07f + extra), 0.035f);
            Kit.Icon(_chrome, "languages", new Vector3(-0.25f, -0.075f - extra / 2f, 0f), 0.026f, KitTheme.Dim);
            for (var i = 0; i < StoryLanguages.All.Length; i++)
            {
                var lang = StoryLanguages.All[i];
                var pos = new Vector3(-0.165f + i % perRow * 0.118f, -0.075f - i / perRow * row, 0f);
                var btn = Kit.Button(_chrome, pos, new Vector2(0.112f, 0.048f), lang.Name, null, KitStyle.Ghost, null, null, 0.017f);
                btn.OnPress = () => pickLanguage(lang);
                _chips.Add((lang, btn));
            }

            Kit.Button(_chrome, new Vector3(-0.19f, -0.155f - extra, 0f), new Vector2(0.15f, 0.052f), "Back", "arrow-left", KitStyle.Ghost, back, null, 0.018f);
            _sound = Kit.Button(_chrome, new Vector3(0.235f, -0.155f - extra, 0f), new Vector2(0.052f, 0.052f), null, "volume-2", KitStyle.Ghost, toggleSound);
            SetLanguage(StoryLanguages.Current);
            Chrome = _chrome.gameObject.AddComponent<KitGroup>();
            Chrome.Refresh();
            SetStories(StoryLibrary.Campfire);
        }

        /// <summary>Reuse the same cards and controls for a place's stories. Hidden cards and dots cannot receive pinches.</summary>
        public void SetStories(IReadOnlyList<StoryInfo> stories)
        {
            if (stories == null || stories.Count == 0) throw new ArgumentException("A story list must not be empty.", nameof(stories));
            var selected = new List<StoryCard>();
            foreach (var story in stories)
            {
                var card = _catalog.Find(c => c.Story == story);
                if (card == null || selected.Contains(card)) throw new ArgumentException("Stories must be distinct catalog entries.", nameof(stories));
                selected.Add(card);
            }
            foreach (var card in _catalog)
            {
                card.Root.gameObject.SetActive(false);
                card.Hover = card.HoverTarget = card.Press = card.Reveal = card.TiltX = card.TiltY = 0f;
                card.Pointer = card.PointerTarget = new Vector2(0.5f, 0.35f);
                card.Text.Set(0f);
            }
            _cards.Clear();
            _cards.AddRange(selected);
            foreach (var card in _cards) card.Root.gameObject.SetActive(true);
            for (var i = 0; i < _dots.Count; i++)
            {
                _dots[i].gameObject.SetActive(i < Count);
                _dots[i].transform.localPosition = new Vector3((i - (Count - 1) / 2f) * 0.028f, 0f, 0f);
            }
            _pos = _target = 0f;
            Layout(0f, true);
        }

        public void SetLanguage(StoryLanguage current)
        {
            foreach (var (lang, btn) in _chips) btn.Selected = lang == current;
        }

        public void SetSound(bool muted) => _sound.SetIcon(muted ? "volume-x" : "volume-2");

        public void Focus(int i) => _target = Mathf.Clamp(i, 0, _cards.Count - 1);
        public void Step(int d) => Focus(Index + d);

        public void Begin(int i)
        {
            if (i < 0 || i >= Count) return;
            if (i != Index) { Focus(i); return; }
            _cards[i].Press = 1f;
            _begin(i);
        }

        public void BeginFocused() => Begin(Index);

        /// <summary>0 = hidden (melted into its light), 1 = shown. Staggered per card by the caller.</summary>
        public void SetReveal(int card, float v)
        {
            _cards[card].SetReveal(v);
        }

        public void ResetFocus(int i) { _target = _pos = Mathf.Clamp(i, 0, _cards.Count - 1); }

        public void Update(float dt)
        {
            if (!Root.gameObject.activeSelf) return;
            _pos += (_target - _pos) * (1f - Mathf.Exp(-8f * dt));
            Layout(dt, false);

            // the pointer's hit on a card, for its shader (the pinch ray stands in for the prototype's mouse)
            var hasRay = HypaInput.Current.TryGetRay(out var ray);
            foreach (var c in _cards) c.Aim(hasRay, ray);
        }

        void Layout(float dt, bool snap)
        {
            var t = Time.time;
            for (var i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                var o = i - _pos;
                var dim = Mathf.Min(Mathf.Abs(o), 1f);
                var a = o * StepDeg * Mathf.Deg2Rad;
                c.Animate(dt, snap, dim); // hover, pointer, press, tilt and the shader
                var bob = Mathf.Sin(t * 0.6f + i * 1.3f) * 0.005f;
                // on an arc around the viewer: the row's origin is Arc metres in front, so the centre of the arc is behind it
                c.Root.localPosition = new Vector3(Mathf.Sin(a) * Arc, bob, (Mathf.Cos(a) - 1f) * Arc + dim * 0.06f);
                c.Root.localRotation = Quaternion.Euler(0f, o * StepDeg, 0f);
                c.Root.localScale = Vector3.one * ((1f - 0.14f * dim) * (1f + 0.03f * c.Hover));
            }
            for (var i = 0; i < _dots.Count; i++)
            {
                if (i >= Count) continue;
                var on = Mathf.Abs(i - _pos) < 0.5f;
                _dots[i].Top = _dots[i].Bottom = on ? KitTheme.Ember : KitTheme.Faint;
                _dots[i].Resize(on ? new Vector2(0.026f, 0.012f) : new Vector2(0.012f, 0.012f));
            }
        }
    }
}
