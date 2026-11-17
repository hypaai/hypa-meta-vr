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
        const float FloorY = 0.017f, CardScale = 0.5f, NearMetres = 7f, BuiltMetres = 30f, LostSeconds = 2.5f, LetGoSeconds = 0.3f;

        sealed class Item
        {
            public StoryInfo Story;
            public StoryCard Card;
            public Transform Anchor;
            public Rigidbody Body;
            public Func<bool> Held;
            public Vector3 HomePos;
            public Quaternion HomeRot;
            public float Lost, Ph;
            public bool WasHeld;
        }

        readonly List<Item> _items = new List<Item>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        Transform _hut, _inside, _ui;
        Village.Figure _woman;
        StoryVoice _voice;
        StoryPlayer _story;
        PlayerView _panel;
        AskFlow _ask;
        Func<StoryLanguage> _lang;
        Action _pauseCamp;
        int _current = -1, _gen;
        float _shown, _letGo = -10f, _t;

        /// <summary>True while the old woman is telling you about an item (or its panel is open).</summary>
        public bool Telling => _current >= 0;

        /// <param name="qa">Who answers a question about an item (the story's chat model, or the campfire's stand-in).</param>
        /// <param name="pauseCamp">Called when an item's card is pinched: the story by the fire, if one is being told, waits.</param>
        public void Init(CampWorld camp, StoryAssets a, MicListener mic, IStoryQa qa, Func<StoryLanguage> lang, Action pauseCamp, Action toggleSound)
        {
            _lang = lang; _pauseCamp = pauseCamp;
            _hut = camp.Trail != null ? camp.Trail.HutRoot : null;
            if (_hut == null) { Debug.LogWarning("Hypa Stories: no hut to furnish."); enabled = false; return; }

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
            _voice = gameObject.AddComponent<StoryVoice>();
            _voice.Init(_woman != null && _woman.Mouth != null ? _woman.Mouth : _inside);
            _story = new StoryPlayer(_voice, lang);

            // the same panel the campfire's story plays in, in front of you (not under the campfire's UI, which is put away out here)
            _ui = new GameObject("Hypa Stories Hut UI").transform;
            _panel = new PlayerView(_ui, () => _story.Jump(-1), () => _story.TogglePause(), () => _story.Jump(1), () => { }, AskTap, Close,
                toggleSound, q => _ask.SubmitText(q));
            _panel.Follow.Height = -0.55f; // lower than by the fire: she sits just beyond it, and her face stays in view over its top edge
            _ask = new AskFlow(_story, _voice, mic, qa, _panel, lang, _ => { });
            _panel.Root.gameObject.SetActive(false);

            var cardMat = StoryCard.NewMaterial(a);
            var quad = StoryMeshes.Quad(StoryCard.W, StoryCard.H);
            for (var i = 0; i < HutItems.Entries.Length; i++)
            {
                StoryInfo story;
                try { story = HutItems.All[i]; }
                catch (Exception e) { Debug.LogWarning($"Hypa Stories: hut item {HutItems.Entries[i].Id} has no content: {e.Message}"); continue; }
                BuildItem(i, HutItems.Entries[i], story, a, vc, cardMat, quad);
            }
            _inside.gameObject.SetActive(false); // shown when you come near (see Update)
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
            const int strips = 14; const float mw = 1.05f, ml = 1.7f;
            for (var i = 0; i < strips; i++)
            {
                var straw = i % 2 == 0 ? new Color(0.72f, 0.6f, 0.34f) : new Color(0.52f, 0.33f, 0.17f);
                mat.Box(X0Z(HutItems.Woman, FloorY + 0.006f) + toRoom * new Vector3(0f, 0f, 0.45f - ml / 2f + (i + 0.5f) * ml / strips),
                    new Vector3(mw, 0.012f, ml / strips), toRoom, straw);
            }
            Mesh(_inside, "Mat", mat.Build("Hut Mat"), vc);
            try
            {
                _woman = camp.Village.MakeElderWoman(_inside, HutItems.Woman, -HutItems.Woman);
                _woman.Root.localPosition = X0Z(HutItems.Woman, FloorY + 0.012f); // on the mat
            }
            catch (Exception e) { Debug.LogWarning("Hypa Stories: couldn't make the old woman: " + e.Message); }
        }

        void BuildItem(int index, HutItems.Entry entry, StoryInfo story, StoryAssets a, Material vc, Material cardMat, Mesh quad)
        {
            var spot = HutItems.Spot(index);
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
            prop.SetLocalPositionAndRotation(X0Z(spot, FloorY) + inward * rest.position, inward * rest.rotation);
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
            anchor.localPosition = X0Z(spot, top + HutItems.CardGap + StoryCard.H * CardScale / 2f);
            anchor.localRotation = inward * Quaternion.Euler(0f, 180f, 0f); // its face to the room
            var i = _items.Count;
            var card = new StoryCard(anchor, story, cardMat, quad, "Hear about it", "volume-2", () => Play(i), () => Play(i));
            // the card shows the thing it is about (drawn from its own shape, standing as it stands here), not a story's scene
            try { card.Picture = HutProps.Picture(shape, rest.rotation); }
            catch (Exception e) { Debug.LogWarning($"Hypa Stories: no picture for {entry.Prop}'s card: {e.Message}"); }
            card.Root.localScale = Vector3.one * CardScale;
            card.SetReveal(0f);

            var item = new Item
            {
                Story = story, Card = card, Anchor = anchor, Body = body, Ph = index * 1.7f,
                HomePos = prop.localPosition, HomeRot = prop.localRotation,
            };
            try { item.Held = StoryGrab.Make?.Invoke(body, a); }
            catch (Exception e) { Debug.LogWarning($"Hypa Stories: {entry.Prop} can't be picked up: {e.Message}"); }
            _items.Add(item);
        }

        // ---------- telling ----------

        /// <summary>A card pinched: the old woman tells you about its item, from the start.</summary>
        void Play(int i)
        {
            if (i < 0 || i >= _items.Count) return;
            if (Time.time - _letGo < LetGoSeconds) return; // the pinch that just put an item down, not one meant for the card
            var item = _items[i];
            item.Card.Press = 1f;
            _pauseCamp?.Invoke();
            _ask.Cancel();
            _story.Stop();
            _current = i;
            var lang = _lang();
            _story.Load(item.Story);
            _panel.Load(item.Story, lang);
            _panel.SetNote("Ask her about it");
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

        /// <summary>The panel's Ask button: start a question, end the recording, or skip the line being said (as by the fire).</summary>
        void AskTap()
        {
            if (!_story.Live) return;
            if (_ask.State == AskState.Idle) _ = _ask.BeginAsync();
            else if (_ask.State == AskState.Listening) _ask.StopListening();
            else _ask.Skip();
        }

        /// <summary>Stops the telling and puts the panel away (its back button, leaving the hut, leaving the camp).</summary>
        public void Close()
        {
            _gen++;
            if (_current < 0) return;
            _current = -1;
            _ask.Cancel();
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
            if (!_hut.gameObject.activeInHierarchy) { Close(); return; } // the camp is put away (another place)

            var local = _hut.InverseTransformPoint(cam.position);
            var dist = new Vector2(local.x, local.z).magnitude;
            var built = dist < BuiltMetres;
            if (_inside.gameObject.activeSelf != built) _inside.gameObject.SetActive(built);
            if (!built) { Close(); return; }
            if (_current >= 0 && dist > HutTrailLayout.HutRadius + 3f) Close(); // you walked away

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
                it.Card.Root.localPosition = new Vector3(0f, Mathf.Sin(_t * 0.6f + it.Ph) * 0.012f, 0f);
                // low over its thing, so it tips up to face you as well as turning (never past 50 degrees)
                var to = it.Anchor.position - cam.position;
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
            }

            Woman(cam, dt);
            Things(dt);
        }

        /// <summary>She looks at you, breathes, blinks, and her mouth moves with her voice.</summary>
        void Woman(Transform cam, float dt)
        {
            if (_woman == null) return;
            var to = _inside.InverseTransformPoint(cam.position) - _woman.Root.localPosition;
            var dir = Quaternion.Inverse(_woman.Root.localRotation) * to;
            var yaw = Mathf.Clamp(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -65f, 65f);
            var pitch = Mathf.Clamp(-Mathf.Atan2(dir.y - 0.75f, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg, -22f, 18f);
            var talk = _voice.Level;
            _woman.Head.localRotation = Quaternion.Slerp(_woman.Head.localRotation,
                Quaternion.Euler(pitch + Mathf.Sin(_t * 5f) * 1.5f * talk, yaw, 0f), 1f - Mathf.Exp(-3f * dt));
            _woman.Torso.localRotation = Quaternion.Euler(9f + Mathf.Sin(_t * 1.1f) * 1.2f, yaw * 0.2f, 0f); // a little bent with her years
            var blink = Mathf.Repeat(_t + 0.7f, 4.1f) < 0.13f;
            _woman.EyeL.localScale = _woman.EyeR.localScale = new Vector3(1f, blink ? 0.12f : 1f, 1f);
            if (_woman.Mouth != null) _woman.Mouth.localScale = new Vector3(1f, 1f + talk * 3.5f, 1f);
            // her hands rest in her lap, and lift a little as she talks
            var lift = Mathf.Clamp01(talk * 2f) * (0.6f + 0.4f * Mathf.Sin(_t * 2.3f));
            _woman.ArmL.localRotation = _woman.ArmR.localRotation = Quaternion.Euler(-28f - lift * 10f, 0f, 0f);
            _woman.ForeL.localRotation = Quaternion.Euler(-55f - lift * 22f, 0f, -14f);
            _woman.ForeR.localRotation = Quaternion.Euler(-55f - lift * 30f, 0f, 14f);
        }

        /// <summary>Held, resting, or gone astray: a thing that has left the room goes back to its place.</summary>
        void Things(float dt)
        {
            foreach (var it in _items)
            {
                var held = it.Held != null && it.Held();
                if (it.WasHeld && !held) _letGo = Time.time;
                it.WasHeld = held;
                if (held) { it.Lost = 0f; continue; }
                var p = _inside.InverseTransformPoint(it.Body.position);
                it.Lost = HutItems.Lost(p) ? it.Lost + dt : 0f;
                if (it.Lost > LostSeconds || p.y < -3f) Home(it);
            }
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
            _cts.Cancel();
            _cts.Dispose();
            _ask?.Cancel();
            _story?.Stop();
            if (_ui != null) Destroy(_ui.gameObject);
        }
    }
}
