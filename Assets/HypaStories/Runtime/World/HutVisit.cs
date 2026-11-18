using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;
using MeshKit = HypaStories.TrailBuilder.MeshKit;

namespace HypaStories
{
    /// <summary>
    /// Inside the hut: household things on the earth floor by the wall (the broom leaning on it), a story card hovering over each, and the old woman
    /// on her mat at the back who tells you about them.
    ///
    /// Nothing here is a new card or a new way of telling. The cards are the campfire's <see cref="StoryCard"/>; an item's
    /// content is a story (<see cref="HutItems"/>, the same story.json format); pinching a card plays it through the same
    /// <see cref="StoryPlayer"/>, <see cref="StoryVoice"/> (from the old woman's mouth), <see cref="PlayerView"/> and
    /// <see cref="AskFlow"/> the campfire uses. The hut has its own instances, so the story by the fire is left as it was.
    ///
    /// The items can be picked up (<see cref="StoryGrab"/>: the Meta XR Interaction SDK on the headset). A card hangs from a
    /// fixed anchor in the hut, not from its item, so it stays over the item's place whatever happens to the item, and it can
    /// still be pinched while the item is held or lies somewhere else. An item that leaves the room goes back to its place.
    /// </summary>
    public sealed class HutVisit : MonoBehaviour
    {
        const float FloorY = 0.017f, CardScale = 0.9f, NearMetres = 7f, BuiltMetres = 46f, LostSeconds = 2.5f, LetGoSeconds = 0.3f;

        sealed class Item
        {
            public StoryInfo Story;
            public StoryCard Card;
            public Transform Anchor;
            public Rigidbody Body;
            /// <summary>How many hands (or controllers) hold it (null: it can't be picked up here).</summary>
            public Func<int> Hands;
            public bool Heavy;
            public string Prop;
            public Vector3 HomePos;
            public Quaternion HomeRot;
            public float Lost, Ph, LetGoAt = -10f, OutFor, UpFor;
            /// <summary>Its flame (the oil lamp), carried with it; null for things without one.</summary>
            public HutInterior.Fire Flame;
            public int WasHands;
            public bool WasHeld => WasHands > 0;
        }

        /// <summary>Tells the hut when a thing hits something hard (dropped, or let go in the air).</summary>
        sealed class ImpactProbe : MonoBehaviour
        {
            public Action<float> Hit;
            void OnCollisionEnter(Collision c) => Hit?.Invoke(c.relativeVelocity.magnitude);
        }

        // what she says when you handle her things, shown over her head too
        const float SayAgainSeconds = 5f, HardHit = 2.2f;
        readonly Dictionary<HutLines.Kind, float> _saidAt = new Dictionary<HutLines.Kind, float>();
        readonly Dictionary<HutLines.Kind, string> _lastSaid = new Dictionary<HutLines.Kind, string>();

        // the broom: dust where it brushes the floor, as much as you sweep; a swish; and she has her say about it
        const float SweepHeight = 0.16f, EnoughSeconds = 7f;
        ParticleSystem _dust;
        AudioSource _swish;
        Item _broom;
        Vector3 _tipWas;
        bool _tipKnown, _praised;
        float _sweepEase, _swept;
        TMPro.TextMeshPro _caption;
        KitGroup _captionGroup;
        Transform _captionRoot;
        float _captionUntil;
        int _sayGen;
        bool _resumeAfterRemark;

        readonly List<Item> _items = new List<Item>();
        readonly RaiseHandGesture _raise = new RaiseHandGesture();
        HutInterior.Fire _fire;
        /// <summary>A card's middle above its anchor: the anchor is the card's bottom edge, so it turns and tips about that edge and never swings down over its thing.</summary>
        static float CardHalf => StoryCard.H * CardScale / 2f;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        Transform _hut, _inside, _ui;
        GameObject _langBar;
        FollowView _langFollow;
        readonly List<(StoryLanguage lang, KitButton btn)> _langChips = new List<(StoryLanguage, KitButton)>();
        DistanceCull _distanceCull;
        float _open;
        Vector3 _jawRest, _jawAt;
        Village.Figure _woman;
        StoryVoice _voice;
        StoryPlayer _story;
        PlayerView _panel;
        AskFlow _ask;
        // Try Hypa Translate at the end of an item's story, in the hut's panel (as at the end of a campfire story)
        TryHypaFlow _tryFlow;
        TryHypaView _try;
        bool _tryOpen, _endNoted;
        Func<StoryLanguage> _lang;
        Action<StoryLanguage> _pickLanguage;
        Action _pauseCamp;
        int _current = -1, _gen;
        float _shown, _letGo = -10f, _t;

        /// <summary>True while the old woman is telling you about an item (or its panel is open).</summary>
        public bool Telling => _current >= 0;
        public bool DuckAmbience => (_voice != null && _voice.IsSpeaking) || (_ask != null && _ask.State != AskState.Idle) ||
            (_tryOpen && _tryFlow.Listening);

        /// <param name="qa">Who answers a question about an item (the story's chat model, or the campfire's stand-in).</param>
        /// <param name="pauseCamp">Called when an item's card is pinched: the story by the fire, if one is being told, waits.</param>
        public void Init(CampWorld camp, StoryAssets a, MicListener mic, IStoryQa qa, Func<StoryLanguage> lang, Action<StoryLanguage> pickLanguage, Action pauseCamp, Action toggleSound)
        {
            _lang = lang; _pickLanguage = pickLanguage; _pauseCamp = pauseCamp;
            _hut = camp.Trail != null ? camp.Trail.HutRoot : null;
            if (_hut == null) { Debug.LogWarning("Hypa Stories: no hut to furnish."); enabled = false; return; }
            _distanceCull = camp.GetComponent<DistanceCull>();

            _inside = new GameObject("Hut Inside").transform;
            _inside.SetParent(_hut, false);
            var vc = new Material(a.pine) { name = "Hut Things (vertex colour)" };
            vc.SetFloat("_Sway", 0f);

            // the floor things land on (a little wider than the room, so what rolls out of the door stops there and is brought back)
            var floor = new GameObject("Floor Collider");
            floor.transform.SetParent(_inside, false);
            var fc = floor.AddComponent<BoxCollider>();
            fc.center = new Vector3(0f, -0.1f, 0f);
            fc.size = new Vector3(7f, 0.23f, 7f); // its top at the hut's earth floor (1.5 cm up)

            BuildWoman(camp, vc);
            _fire = HutInterior.Build(_inside, vc, a, FloorY); // rafters, the painted wall, the ledge, the lamp, the things of the house
            BuildCaption();
            _voice = gameObject.AddComponent<StoryVoice>();
            _voice.Init(_woman != null && _woman.Mouth != null ? _woman.Mouth : _inside);
            _story = new StoryPlayer(_voice);

            // the same panel the campfire's story plays in, in front of you (not under the campfire's UI, which is put away out here)
            _ui = new GameObject("Hypa Stories Hut UI").transform;
            _panel = new PlayerView(_ui, () => _story.Jump(-1), () => _story.TogglePause(), () => _story.Jump(1), OpenTry, CornerTap, Close,
                toggleSound, q => _ask.SubmitText(q));
            _panel.Follow.Height = -0.55f; // lower than by the fire: she sits just beyond it, and her face stays in view over its top edge
            _ask = new AskFlow(_story, _voice, mic, qa, _panel, () => _story.Language, asking => { if (asking) CancelRemarks(); });
            _ask.Logged += e => Debug.Log("Hypa Stories hut " + e);
            // its own translate, in this panel: the same recording as Ask (proven on the headset), not the live stream
            _tryFlow = new TryHypaFlow(new HypaTryApi(), mic, HypaSettings.TargetLanguage) { LiveText = false };
            _try = new TryHypaView(_panel.Body, _tryFlow, CloseTry);
            _try.Root.gameObject.SetActive(false);
            _panel.Root.gameObject.SetActive(false);
            BuildLanguages();

            var cardMat = StoryCard.NewMaterial(a);
            var quad = StoryMeshes.Quad(StoryCard.W, StoryCard.H);
            for (var i = 0; i < HutItems.Entries.Length; i++)
            {
                StoryInfo story;
                try { story = HutItems.All[i]; }
                catch (Exception e) { Debug.LogWarning($"Hypa Stories: hut item {HutItems.Entries[i].Id} has no content: {e.Message}"); continue; }
                BuildItem(i, HutItems.Entries[i], story, a, vc, cardMat, quad);
            }
            // The furnishings use the same draw distance as the shell. Cards, captions and lamp lights manage their own visibility.
            _distanceCull?.Collect(_inside, t => t.name.StartsWith("Card") || t.name == "Old Woman Says", 0f, _ => false, 0f, includeLights: false);
            _inside.gameObject.SetActive(false); // shown when the hut is in view (see Update)
        }

        // ---------- building ----------

        static Vector3 X0Z(Vector2 p, float y = 0f) => new Vector3(p.x, y, p.y);

        static Transform Mesh(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        void BuildWoman(CampWorld camp, Material vc)
        {
            if (camp.Village == null) return; // no village, no villagers: the voice comes from the back of the room
            // her mat: woven straw in two colours, long enough for her legs
            var mat = new MeshKit();
            var toRoom = Quaternion.LookRotation(X0Z(-HutItems.Woman).normalized);
            const int strips = 12; const float mw = 1.05f, ml = 1.25f;
            for (var i = 0; i < strips; i++)
            {
                var straw = i % 2 == 0 ? new Color(0.72f, 0.6f, 0.34f) : new Color(0.52f, 0.33f, 0.17f);
                mat.Box(X0Z(HutItems.Woman, FloorY + 0.006f) + toRoom * new Vector3(0f, 0f, 0.22f - ml / 2f + (i + 0.5f) * ml / strips),
                    new Vector3(mw, 0.012f, ml / strips), toRoom, straw);
            }
            Mesh(_inside, "Mat", mat.Build("Hut Mat"), vc);
            try
            {
                _woman = camp.Village.MakeElderWoman(_inside, HutItems.Woman, -HutItems.Woman);
                _woman.Root.localPosition = X0Z(HutItems.Woman, FloorY + 0.012f); // on the mat
                if (_woman.Mouth != null) _jawRest = _woman.Mouth.localPosition;
                if (_woman.Jaw != null) _jawAt = _woman.Jaw.localPosition;
            }
            catch (Exception e) { Debug.LogWarning("Hypa Stories: couldn't make the old woman: " + e.Message); }
        }

        void BuildItem(int index, HutItems.Entry entry, StoryInfo story, StoryAssets a, Material vc, Material cardMat, Mesh quad)
        {
            var (spot, baseY) = HutItems.Place(index);
            var inward = Quaternion.LookRotation(X0Z(-spot).normalized); // +Z into the room, the wall behind

            // the wall behind it (the hut's wall has no collider of its own): what leans there stays, and nothing is pushed through
            var toWall = HutTrailLayout.WallInner - spot.magnitude;
            var wall = new GameObject("Wall Collider " + entry.Prop);
            wall.transform.SetParent(_inside, false);
            wall.transform.SetLocalPositionAndRotation(X0Z(spot, 1.1f) + inward * new Vector3(0f, 0f, -(toWall + 0.15f)), inward);
            wall.AddComponent<BoxCollider>().size = new Vector3(1.3f, 2.2f, 0.3f);

            // the thing itself, on the earth floor by the wall: a rigidbody, never a parent of its card
            var mk = new MeshKit();
            var boxes = new List<Bounds>();
            if (!HutProps.Build(entry.Prop, mk, boxes))
                Debug.Log($"Hypa Stories: no shape for hut item '{entry.Prop}' yet; showing a crate (add it to HutProps).");
            var shape = mk.Build("Hut " + entry.Prop);
            var prop = Mesh(_inside, "Item " + entry.Prop, shape, vc);
            var size = HutProps.Size(entry.Prop);
            prop.localScale = Vector3.one * size;
            var rest = HutProps.Rest(entry.Prop, toWall);
            prop.SetLocalPositionAndRotation(X0Z(spot, baseY > 0f ? baseY : FloorY) + inward * rest.position, inward * rest.rotation);
            if (baseY > 0f)
            {
                // what it stands on (the mud ledge) holds it up, and catches it when it is put back
                var stand = new GameObject("Stand Collider " + entry.Prop);
                stand.transform.SetParent(_inside, false);
                stand.transform.SetLocalPositionAndRotation(X0Z(spot, baseY / 2f), inward);
                var onTable = spot.magnitude < HutTrailLayout.WallInner - HutInterior.LedgeDepth - 0.1f; // out in the room: its table
                stand.AddComponent<BoxCollider>().size = onTable ? new Vector3(HutInterior.TableSize.x, baseY, HutInterior.TableSize.y)
                    : new Vector3(0.8f, baseY, Mathf.Max(0.3f, (toWall + 0.15f) * 2f));
            }
            var flameAt = HutProps.Flame(entry.Prop);
            foreach (var b in boxes)
            {
                var c = prop.gameObject.AddComponent<BoxCollider>();
                c.center = b.center; c.size = b.size;
            }
            var body = prop.gameObject.AddComponent<Rigidbody>();
            body.mass = 1.2f * size;
            body.linearDamping = 0.2f; body.angularDamping = 1.5f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.Sleep(); // it rests until you touch it

            // the card: on a fixed point over the item's place
            var anchor = new GameObject("Card Anchor " + entry.Prop).transform;
            anchor.SetParent(_inside, false);
            // just over the top of the thing as it rests (its mesh's highest corner), so card and thing read as one
            var top = 0f;
            var mb = prop.GetComponent<MeshFilter>().sharedMesh.bounds;
            for (var k = 0; k < 8; k++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                top = Mathf.Max(top, (prop.localPosition + prop.localRotation * (corner * size)).y);
            }
            anchor.localPosition = X0Z(spot, top + HutItems.CardGap); // the card's bottom edge, a hand's width over the top of its thing
            anchor.localRotation = inward * Quaternion.Euler(0f, 180f, 0f); // its face to the room
            var i = _items.Count;
            var card = new StoryCard(anchor, story, cardMat, quad, "Hear about it", "volume-2", () => Play(i), () => Play(i));
            // the card shows the thing it is about (drawn from its own shape, standing as it stands here), not a story's scene
            try { card.Picture = HutProps.Picture(shape, rest.rotation); }
            catch (Exception e) { Debug.LogWarning($"Hypa Stories: no picture for {entry.Prop}'s card: {e.Message}"); }
            card.Root.localScale = Vector3.one * CardScale;
            card.Root.localPosition = new Vector3(0f, CardHalf, 0f);
            card.SetReveal(0f);

            var item = new Item
            {
                Story = story, Card = card, Anchor = anchor, Body = body, Ph = index * 1.7f,
                HomePos = prop.localPosition, HomeRot = prop.localRotation,
            };
            item.Heavy = entry.Heavy;
            item.Prop = entry.Prop;
            if (entry.Prop == "broom") BuildDust(a, item);
            var probe = prop.gameObject.AddComponent<ImpactProbe>();
            probe.Hit = speed => { if (speed > HardHit && !item.WasHeld && Time.time - item.LetGoAt < 3f) Say(HutLines.Kind.Careful); };
            try { item.Hands = StoryGrab.Make?.Invoke(body, a, entry.Heavy); }
            catch (Exception e) { Debug.LogWarning($"Hypa Stories: {entry.Prop} can't be picked up: {e.Message}"); }
            if (flameAt.HasValue) item.Flame = HutInterior.Flame(prop, a, flameAt.Value, 1f / size);
            _items.Add(item);
        }

        /// <summary>
        /// The same language chips as the campfire's list, in front of you once you are in the hut. Her stories already have a
        /// script for each one; this is how you pick it here, not only back by the fire.
        /// </summary>
        void BuildLanguages()
        {
            var root = new GameObject("Hut Languages").transform;
            root.SetParent(_ui, false);
            _langFollow = root.gameObject.AddComponent<FollowView>();
            _langFollow.Distance = 1.05f;
            _langFollow.Height = -0.22f;
            const int perRow = 4;
            const float row = 0.056f;
            var rows = (StoryLanguages.All.Length + perRow - 1) / perRow;
            var extra = (rows - 1) * row;
            Kit.Glass(root, "Language", new Vector3(0f, -extra / 2f, 0.004f), new Vector2(0.56f, 0.07f + extra), 0.035f);
            Kit.Icon(root, "languages", new Vector3(-0.25f, -extra / 2f, 0f), 0.026f, KitTheme.Dim);
            for (var i = 0; i < StoryLanguages.All.Length; i++)
            {
                var lang = StoryLanguages.All[i];
                var pos = new Vector3(-0.165f + i % perRow * 0.118f, -i / perRow * row, 0f);
                var btn = Kit.Button(root, pos, new Vector2(0.112f, 0.048f), lang.Name, null, KitStyle.Ghost, null, null, 0.017f);
                btn.OnPress = () => _pickLanguage?.Invoke(lang);
                _langChips.Add((lang, btn));
            }
            SetLanguage(_lang());
            _langBar = root.gameObject;
            _langBar.SetActive(false);
        }

        public void SetLanguage(StoryLanguage current)
        {
            foreach (var (lang, btn) in _langChips) if (btn != null) btn.Selected = lang == current;
        }

        void ShowLanguages(bool on)
        {
            if (_langBar == null || _langBar.activeSelf == on) return;
            _langBar.SetActive(on);
            if (on) _langFollow.Snap();
        }

        // ---------- telling ----------

        /// <summary>A card pinched: the old woman tells you about its item, from the start.</summary>
        void Play(int i)
        {
            if (i < 0 || i >= _items.Count) return;
            if (Time.time - _letGo < LetGoSeconds) return; // the pinch that just put an item down, not one meant for the card
            foreach (var other in _items) if (other.WasHeld) return; // still in the hand: this pinch is the release
            var item = _items[i];
            item.Card.Press = 1f;
            _pauseCamp?.Invoke();
            CancelRemarks();
            _ask.Cancel();
            HideTryNow();
            _current = i;
            _endNoted = false;
            _story.Load(item.Story, _lang());
            _panel.Load(item.Story, _story.Language);
            _panel.SetNote(""); // the panel's own hint: raise a hand to ask
            _panel.ShowLine(0, 0f);
            _panel.Root.gameObject.SetActive(true);
            _panel.Follow.Snap();
            _panel.Fade.Set(1f);
            MicListener.RequestPermission();
            StartCoroutine(Begin(++_gen, _story.PrepareAsync(_cts.Token)));
        }

        /// <summary>Starts once the first line's voice is ready (or after a few seconds without it: the lines then catch up).</summary>
        IEnumerator Begin(int gen, Task ready)
        {
            var t0 = Time.time;
            while (!ready.IsCompleted && Time.time - t0 < 6f) yield return null;
            if (gen != _gen) yield break;
            _story.Live = true;
            _story.Start(0f);
        }

        // ---------- Try Hypa Translate (as by the fire) ----------

        /// <summary>The corner button: speak while translating, otherwise Ask.</summary>
        void CornerTap()
        {
            if (_tryOpen) { _ = _tryFlow.DictateAsync(); return; }
            AskTap();
        }

        /// <summary>Opens Hypa Translate in the hut's panel once the item's story is told: its words, your voice, the translation spoken back.</summary>
        void OpenTry()
        {
            if (_current < 0 || _tryOpen || !_story.Live || !_story.Ended || _ask.State != AskState.Idle) return;
            CancelRemarks();
            _voice.StopAll();
            _tryOpen = true;
            var lang = _story.Language;
            var story = _items[_current].Story;
            _tryFlow.Clear();
            _tryFlow.BindStory(lang.HypaLabel, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(StoryLanguages.All, l => l.HypaLabel)));
            var words = story.WordsFor(lang.Code);
            _tryFlow.SetSuggestions(words.Length > 0 ? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(words, 3))
                : TryHypaText.Suggest(System.Linq.Enumerable.Select(story.Timeline.Captions, c => c.TextFor(lang.Code)), 3));
            _try.Root.gameObject.SetActive(true);
            _try.Show(story.Accent);
            _panel.SetTranslateOpen(true);
            _ = _tryFlow.LoadLanguagesAsync();
        }

        void CloseTry()
        {
            if (!_tryOpen) return;
            HideTryNow();
        }

        /// <summary>Puts the translation away at once (another thing picked, the story replayed, the panel closed).</summary>
        void HideTryNow()
        {
            if (_tryFlow == null) return;
            var was = _tryOpen;
            _tryOpen = false;
            _tryFlow.Cancel();
            if (_try != null) _try.Root.gameObject.SetActive(false);
            if (was) _panel?.SetTranslateOpen(false);
        }

        /// <summary>The panel's Ask button: start a question, end the recording, or skip the line being said (as by the fire).</summary>
        void AskTap()
        {
            if (!_story.Live || _tryOpen) return;
            if (_ask.State == AskState.Idle) _ = _ask.BeginAsync();
            else if (_ask.State == AskState.Listening) _ask.StopListening();
            else _ask.Skip();
        }

        /// <summary>Stops the telling and puts the panel away (its back button, leaving the hut, leaving the camp).</summary>
        public void Close()
        {
            _gen++;
            if (_current < 0) return;
            CancelRemarks();
            _current = -1;
            _ask.Cancel();
            HideTryNow();
            _panel.SetTryOffer(false);
            _story.Stop();
            _panel.Root.gameObject.SetActive(false);
        }

        public void SetSound(bool muted) => _panel?.SetSound(muted);

        // ---------- every frame ----------

        static float Approach(float cur, float target, float rate, float dt) => cur + (target - cur) * (1f - Mathf.Exp(-rate * dt));

        void Update()
        {
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null || _hut == null) return;
            var dt = Time.deltaTime;
            _t += dt;
            if (!_hut.gameObject.activeInHierarchy) { Close(); ShowLanguages(false); return; } // the camp is put away (another place)

            var eye = ViewerWorld(cam);
            var dist = ViewerDistance(eye);
            var built = dist < BuiltMetres;
            if (_inside.gameObject.activeSelf != built)
            {
                _inside.gameObject.SetActive(built);
                // the distance test may still have them off from when you were far; turn them on, it switches them off again if you are
                if (built)
                    foreach (var r in _inside.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            }
            if (!built) { Close(); ShowLanguages(false); return; }
            if (_current >= 0 && dist > HutTrailLayout.HutRadius + 3f) Close(); // you walked away
            ShowLanguages(ViewerDistance(cam.position) < NearMetres && _current < 0);

            // the cards: there when you come near, hovering over their places, turned to you
            var shown = Approach(_shown, dist < NearMetres ? 1f : 0f, 4f, dt);
            var reveal = Mathf.Abs(shown - _shown) > 1e-4f;
            _shown = shown;
            var hasRay = HypaInput.Current.TryGetRay(out var ray);
            for (var i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                var on = _shown > 0.01f;
                if (it.Card.Root.gameObject.activeSelf != on) it.Card.Root.gameObject.SetActive(on);
                if (!on) continue;
                if (reveal) it.Card.SetReveal(_shown);
                it.Card.Root.localPosition = new Vector3(0f, CardHalf + 0.006f + Mathf.Sin(_t * 0.6f + it.Ph) * 0.006f, 0f); // bobs gently, never down onto its thing
                // low over its thing, so it tips up to face you as well as turning (never past 50 degrees)
                var to = it.Anchor.position - eye;
                var flat = new Vector2(to.x, to.z).magnitude;
                to.y = Mathf.Clamp(to.y, -flat * 1.19f, flat * 1.19f);
                if (flat > 0.1f)
                    it.Anchor.rotation = Quaternion.Slerp(it.Anchor.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-5f * dt));
                it.Card.Aim(hasRay, ray);
                it.Card.Animate(dt, false, _current >= 0 && _current != i ? 0.6f : 0f); // the others dim while one is told
            }

            // the telling: the same clock, panel and words as by the fire
            if (_story.Live)
            {
                var s = _story.Update(dt);
                _panel.ShowLine(s.Index, s.T);
                _panel.SetProgress(s.T, _story.Duration);
                _panel.SetPauseLabel(_story.Ended ? "Replay" : _story.Paused ? "Resume" : "Pause");
                if (_story.Ended && !_endNoted) { _endNoted = true; _panel.SetNote("Try Hypa Translate, or pick up another thing."); }
                if (!_story.Ended) _endNoted = false;
                _panel.SetTryOffer(_story.Ended && !_tryOpen && _ask.State == AskState.Idle);
                if (_tryOpen && !_story.Ended) HideTryNow(); // replayed: back to the story
            }
            if (_tryOpen)
            {
                _try.Update(dt);
                _panel.SetCorner(_tryFlow.Listening ? "Done" : "Speak", _tryFlow.Listening ? "check" : "mic");
            }

            // raise a hand to ask her about the thing, as by the fire
            var raised = RaiseHand.Probe != null && RaiseHand.Probe();
            if (_raise.Update(raised, dt) && _current >= 0 && _story.Live && !_tryOpen && _ask.State == AskState.Idle) _ = _ask.BeginAsync();
            if (Application.isEditor && _current >= 0 && Input.GetKeyDown(KeyCode.H) && !_tryOpen && _ask.State == AskState.Idle) _ = _ask.BeginAsync();

            HutInterior.Tick(_fire, cam, _t);
            Flames(cam, dt);
            Sweeping(dt);
            Caption(cam, dt);
            Woman(eye, dt);
            Things(dt);
        }

        /// <summary>She looks at you, breathes, blinks, and her mouth moves with her voice.</summary>
        void Woman(Vector3 eye, float dt)
        {
            if (_woman == null) return;
            var to = _inside.InverseTransformPoint(eye) - _woman.Root.localPosition;
            var dir = Quaternion.Inverse(_woman.Root.localRotation) * to;
            var yaw = Mathf.Clamp(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -65f, 65f);
            var pitch = Mathf.Clamp(-Mathf.Atan2(dir.y - 0.75f, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg, -22f, 18f);
            var talk = _voice.Level;
            _woman.Head.localRotation = Quaternion.Slerp(_woman.Head.localRotation,
                Quaternion.Euler(pitch + Mathf.Sin(_t * 5f) * 1.5f * talk, yaw, 0f), 1f - Mathf.Exp(-3f * dt));
            _woman.Torso.localRotation = Quaternion.Euler(9f + Mathf.Sin(_t * 1.1f) * 1.2f, yaw * 0.2f, 0f); // a little bent with her years
            var blink = Mathf.Repeat(_t + 0.7f, 4.1f) < 0.13f;
            _woman.EyeL.localScale = _woman.EyeR.localScale = new Vector3(1f, blink ? 0.12f : 1f, 1f);
            // her hands rest in her lap, and lift a little as she talks
            var lift = Mathf.Clamp01(talk * 2f) * (0.6f + 0.4f * Mathf.Sin(_t * 2.3f));
            _woman.ArmL.localRotation = _woman.ArmR.localRotation = Quaternion.Euler(-28f - lift * 10f, 0f, 0f);
            _woman.ForeL.localRotation = Quaternion.Euler(-55f - lift * 22f, 0f, -14f);
            _woman.ForeR.localRotation = Quaternion.Euler(-55f - lift * 30f, 0f, 14f);
        }

        /// <summary>
        /// Where to measure and face from. Through the portal that is the eye on the far side of the doorway (your head is still
        /// by the fire), so the room is furnished in the glass, and she looks at the door, not only after you step through.
        /// </summary>
        Vector3 ViewerWorld(Transform cam)
        {
            if (_distanceCull != null && _distanceCull.Viewer.HasValue)
            {
                var far = _distanceCull.transform.TransformPoint(_distanceCull.Viewer.Value);
                var hut = _hut.position;
                if ((far - hut).sqrMagnitude < (cam.position - hut).sqrMagnitude) return far;
            }
            return cam.position;
        }

        /// <summary>How far <paramref name="eye"/> is from the hut's centre, on the ground.</summary>
        float ViewerDistance(Vector3 eye)
        {
            var local = _hut.InverseTransformPoint(eye);
            return new Vector2(local.x, local.z).magnitude;
        }

        /// <summary>Her mouth opens with her voice, the same smoothing the campfire storyteller uses, after this frame's loudness is known.</summary>
        void LateUpdate()
        {
            if (_woman == null || _woman.Mouth == null || _inside == null || !_inside.gameObject.activeInHierarchy) return;
            var talk = _voice != null ? _voice.Level : 0f;
            _open = Approach(_open, Mathf.Min(1f, talk * 1.2f), 22f, Time.deltaTime);
            _woman.Mouth.localScale = Vector3.one;
            _woman.Mouth.localPosition = _jawRest + new Vector3(0f, -_open * 0.024f, _open * 0.005f);
            _woman.Mouth.localRotation = Quaternion.Euler(_open * 14f, 0f, 0f);
            if (_woman.Jaw != null)
            {
                _woman.Jaw.localPosition = _jawAt + new Vector3(0f, -_open * 0.012f, 0f);
                _woman.Jaw.localScale = new Vector3(1f, Mathf.Lerp(0.15f, 1.5f, _open), 1f);
            }
        }

        /// <summary>Held, resting, or gone astray: a thing that has left the room goes back to its place.</summary>
        void Things(float dt)
        {
            foreach (var it in _items)
            {
                var hands = it.Hands != null ? it.Hands() : 0;
                if (hands != it.WasHands) Handled(it, it.WasHands, hands);
                it.WasHands = hands;
                if (hands > 0) { it.Lost = 0f; continue; }
                var p = _inside.InverseTransformPoint(it.Body.position);
                it.Lost = HutItems.Lost(p) ? it.Lost + dt : 0f;
                if (it.Lost > LostSeconds || p.y < -3f) Home(it);
            }
        }

        /// <summary>
        /// The lamp's flame burns while it is carried, and lights the room wherever it goes. Tip it over and the oil drowns the
        /// flame (she warns you); stood upright again for a moment, it is lit again.
        /// </summary>
        void Flames(Transform cam, float dt)
        {
            foreach (var it in _items)
            {
                if (it.Flame == null) continue;
                HutInterior.Tick(it.Flame, cam, _t);
                var upright = it.Body.transform.up.y > 0.55f;
                if (it.Flame.Lit)
                {
                    it.OutFor = upright ? 0f : it.OutFor + dt;
                    if (it.OutFor > 0.25f) { HutInterior.SetLit(it.Flame, false); it.UpFor = 0f; Say(HutLines.Kind.Careful); }
                }
                else
                {
                    it.UpFor = upright ? it.UpFor + dt : 0f;
                    if (it.UpFor > 1.5f) HutInterior.SetLit(it.Flame, true);
                }
            }
        }

        /// <summary>
        /// Dust from under the broom: built once, a world-space particle system (the dust stays where it was kicked up) that gives
        /// nothing until the broom sweeps, and a looping swish that is silent until then.
        /// </summary>
        void BuildDust(StoryAssets a, Item broom)
        {
            _broom = broom;
            var go = new GameObject("Broom Dust");
            go.transform.SetParent(_inside, false);
            _dust = go.AddComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _dust.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.32f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.18f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.62f, 0.5f, 0.36f, 0.55f), new Color(0.48f, 0.38f, 0.27f, 0.4f));
            main.gravityModifier = -0.015f; // fine dust drifts up a little before it settles
            main.maxParticles = 160;
            main.playOnAwake = false;
            var shape = _dust.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.12f;
            var em = _dust.emission;
            em.rateOverTime = 0f;
            var size = _dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2.2f)); // puffs spread as they hang
            var col = _dust.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var noise = _dust.noise;
            noise.enabled = true; noise.strength = 0.12f; noise.frequency = 0.6f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = a != null && a.dust != null ? new Material(a.dust) { mainTexture = StoryMeshes.Glow(), name = "Broom Dust" } : null;
            _dust.Play();

            _swish = go.AddComponent<AudioSource>();
            _swish.clip = ProceduralClips.Clip("broom swish", ProceduralClips.RustleSamples(61));
            _swish.loop = true; _swish.playOnAwake = false; _swish.volume = 0f;
            _swish.spatialBlend = 1f; _swish.minDistance = 0.6f; _swish.maxDistance = 6f; _swish.dopplerLevel = 0f;
        }

        /// <summary>
        /// While the broom is held and its spread end brushes the floor, dust rises from under it, more the faster it sweeps, and
        /// the swish follows. She says so when you start ("like that, from the back toward the door"), and after a good while
        /// of sweeping, "that's enough, my child".
        /// </summary>
        void Sweeping(float dt)
        {
            if (_broom == null || _dust == null || dt <= 0f) return;
            var held = _broom.Hands != null && _broom.Hands() > 0;
            // the spread end of the broom (HutProps builds it lying along +Z, the bristles at the +Z end)
            var tip = _broom.Body.transform.TransformPoint(new Vector3(0f, 0.03f, 0.27f));
            var local = _inside.InverseTransformPoint(tip);
            var onFloor = held && local.y < FloorY + SweepHeight;
            var speed = 0f;
            if (_tipKnown)
            {
                var d = local - _tipWas; d.y = 0f;
                speed = d.magnitude / dt;
            }
            _tipWas = local; _tipKnown = true;
            var want = onFloor ? Mathf.Clamp01((speed - 0.12f) / 1.3f) : 0f;
            _sweepEase = Approach(_sweepEase, want, want > _sweepEase ? 14f : 5f, dt);

            _dust.transform.position = _inside.TransformPoint(new Vector3(local.x, FloorY + 0.02f, local.z));
            if (!_dust.isPlaying) _dust.Play(); // built while the room was switched off, it doesn't start on its own
            var em = _dust.emission;
            em.rateOverTime = _sweepEase * 90f;
            if (_swish.clip != null)
            {
                _swish.transform.position = _dust.transform.position;
                _swish.volume = _sweepEase * 0.55f;
                _swish.pitch = 0.85f + _sweepEase * 0.4f;
                if (_sweepEase > 0.02f && !_swish.isPlaying) _swish.Play();
                else if (_sweepEase <= 0.02f && _swish.isPlaying) _swish.Stop();
            }

            if (_sweepEase > 0.25f)
            {
                _swept += dt;
                if (!_praised && _swept > 1f) { _praised = true; Say(HutLines.Kind.Sweep); }
                if (_swept > EnoughSeconds) { Say(HutLines.Kind.Enough); _swept = 0f; _praised = true; }
            }
            if (!held) { _praised = false; _swept = Mathf.Max(0f, _swept - dt * 0.5f); }
        }

        /// <summary>A hand took hold or let go: she minds her things.</summary>
        void Handled(Item it, int was, int now)
        {
            if (now == 0) { _letGo = it.LetGoAt = Time.time; return; }
            if (was != 0 && !(it.Heavy && was == 1 && now >= 2)) return;
            if (it.Heavy && now == 1) Say(HutLines.Kind.BothHands);  // one hand on a heavy pot: it won't come up
            else Say(HutLines.Kind.Care, it.Prop);                  // lifted: handle it with care (and a word about this thing)
        }

        /// <summary>
        /// She says a line, in the story language (English where there is none), shown over her head. If she is telling you about
        /// a thing, the telling waits and picks up again from its line. Lines repeat no more often than every few seconds.
        /// </summary>
        async void Say(HutLines.Kind kind, string prop = null)
        {
            if (_saidAt.TryGetValue(kind, out var at) && Time.time - at < SayAgainSeconds) return;
            _saidAt[kind] = Time.time;
            _lastSaid.TryGetValue(kind, out var last);
            var lang = _current >= 0 ? _story.Language : _lang();
            var (text, code) = HutLines.For(kind, lang.Code, prop, last, UnityEngine.Random.value);
            _lastSaid[kind] = text;
            ShowCaption(text);
            if (_ask.State != AskState.Idle || _tryOpen || _resumeAfterRemark) return;
            var gen = ++_sayGen;
            try
            {
                var line = await _voice.PrepareAsync(text, StoryLanguages.Find(code) ?? lang, _cts.Token);
                if (gen != _sayGen || !_inside.gameObject.activeInHierarchy) return;
                if (_story.Live && _story.Running && !_story.Paused && !_story.Ended)
                {
                    _story.Pause();
                    _resumeAfterRemark = true;
                }
                await _voice.PlayAsync(line, null, _cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning("Hypa Stories: the old woman couldn't speak: " + e.Message); }
            if (gen != _sayGen || !_resumeAfterRemark) return;
            _resumeAfterRemark = false;
            if (_story.Live && _story.Paused) _story.TogglePause();
        }

        void CancelRemarks()
        {
            _sayGen++;
            _resumeAfterRemark = false;
        }

        void ShowCaption(string text)
        {
            if (_captionRoot == null || !Kit.Ready) return;
            _caption.text = text;
            _captionGroup.Refresh();
            _captionGroup.Set(1f);
            _captionUntil = Time.time + 2.2f + text.Length * 0.05f;
        }

        /// <summary>The words over her head face you, and fade when they've been read.</summary>
        void Caption(Transform cam, float dt)
        {
            if (_captionRoot == null || _woman == null) return;
            _captionRoot.position = _woman.Head.position + Vector3.up * 0.42f;
            var to = _captionRoot.position - cam.position;
            if (to.sqrMagnitude > 1e-4f) _captionRoot.rotation = Quaternion.LookRotation(to);
            if (_captionGroup.Alpha > 0f && Time.time > _captionUntil) _captionGroup.Set(Mathf.Max(0f, _captionGroup.Alpha - dt * 2f));
        }

        void BuildCaption()
        {
            if (!Kit.Ready || _woman == null) return;
            _captionRoot = new GameObject("Old Woman Says").transform;
            _captionRoot.SetParent(_inside, false);
            Kit.Glass(_captionRoot, "Bubble", new Vector3(0f, 0f, 0.004f), new Vector2(0.62f, 0.1f), 0.04f);
            _caption = Kit.Text(_captionRoot, "", 0.026f, Vector3.zero, new Vector2(0.58f, 0.09f), KitTheme.Paper, TMPro.TextAlignmentOptions.Center, KitFont.SerifItalic);
            _caption.enableAutoSizing = true; _caption.fontSizeMax = _caption.fontSize; _caption.fontSizeMin = _caption.fontSize * 0.6f;
            _captionGroup = _captionRoot.gameObject.AddComponent<KitGroup>();
            _captionGroup.Refresh();
            _captionGroup.Set(0f);
        }

        void Home(Item it)
        {
            it.Lost = 0f;
            if (!it.Body.isKinematic) { it.Body.linearVelocity = Vector3.zero; it.Body.angularVelocity = Vector3.zero; }
            it.Body.transform.SetLocalPositionAndRotation(it.HomePos, it.HomeRot);
            it.Body.Sleep();
        }

        void OnDestroy()
        {
            CancelRemarks();
            _cts.Cancel();
            _cts.Dispose();
            _ask?.Cancel();
            _tryFlow?.Cancel();
            _story?.Stop();
            if (_ui != null) Destroy(_ui.gameObject);
        }
    }
}
