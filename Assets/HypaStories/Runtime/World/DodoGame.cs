using System;
using System.Collections.Generic;
using HypaCore;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The playable scene after Seven Hot Stones: you are Auta. Dodo bursts out of the trees into a blood-red night. You put
    /// seven stones in the fire (each flares the fire and glows red hot); then, each time Dodo roars "Who is able to fight with
    /// me?", you shout back "I am!", it stoops at the fire, and you throw a hot stone into its open mouth. Three misses and
    /// Dodo wins. Seven stones and it falls: dawn, and the villagers come out of the trees to dance.
    ///
    /// Hands first, controllers optional (both through <see cref="HypaInput"/>): point near the pile or the fire and pinch,
    /// and that stone flies to your hand; flick to let go. Throws are helped: one roughly toward the mouth (or the fire)
    /// curves in, and letting go while pointing at it lobs it there. The rules are <see cref="DodoRules"/>.
    /// </summary>
    public sealed class DodoGame : MonoBehaviour
    {
        // where things are on the camp's ground (the fire at the origin, you at the story seat on -Z)
        // Dodo's run in, its spot beyond the fire, and where it stoops over it; the slab by your seat. As laid out in the forest:
        // in your room (GameFit) Dodo is smaller, stoops at the far stones, and charges in from just past the storyteller.
        static readonly Vector3 FSlab = new Vector3(0.62f, 0f, -2.35f);
        GameFit _fit = GameFit.Forest;
        Vector3 Stoop => new Vector3(0f, 0f, _fit.Room ? GameFit.StoneEdge + 0.25f : 2.35f); // in the room: just past the far stones
        Vector3 Home => new Vector3(0f, 0f, _fit.Room ? _fit.Gap + 0.45f : 5.4f);
        Vector3 Lair => new Vector3(0f, 0f, _fit.Room ? Home.z + 1.5f : 15f);
        Vector3 Slab => _fit.At(FSlab);
        Transform _slabT;

        /// <summary>
        /// Lays the game out for the forest or your room (see <see cref="GameFit"/>). Mid-game too: the slab and its stones move,
        /// and Dodo walks to its new spot, the game carrying on where it was.
        /// </summary>
        public void Fit(GameFit fit)
        {
            _fit = fit;
            if (_root == null) return;
            _slabT.localPosition = Slab;
            for (var i = 0; i < _stones.Count; i++)
            {
                if (_stones[i].S == StoneState.Pile && _held != _stones[i]) _stones[i].T.localPosition = PilePos(i);
                if (_stones[i].S == StoneState.Fire) _stones[i].T.localPosition = FirePos(_stones[i].Slot); // onto the ring as it is now
            }
            if (_dodo != null) _dodo.transform.localScale = Vector3.one * _fit.DodoScale;
            _homeZ = Mathf.Clamp(_homeZ, Stoop.z + 0.6f, Home.z);
        }
        const float SlabTop = 0.62f, FireRing = 0.6f;

        CampWorld _camp;
        StoryAssets _a;
        StoryAudio _audio;
        Texture2D _glow;
        Func<string, string> _line;       // a story line in the listener's language, by id
        Action<string> _say;               // the storyteller speaks it
        Action _restartStory, _back, _return, _toggleRoom;

        DodoRules _rules;
        DodoCreature _dodo;
        ShoutDetector _shout;
        Transform _root;
        float _danger, _dawn, _flare, _homeZ, _sayAt = -1f;
        string _sayText;

        enum StoneState { Pile, Fetch, Held, Flying, Fire, Return, Gone }
        sealed class Stone
        {
            public Transform T;
            public Material M, GlowM;
            public Transform Glow;
            public StoneState S;
            public int Slot = -1;     // its place in the fire (the order it went in)
            public Vector3 Vel, From, To;
            public float Time;
            public bool Homing, ToMouth, Thrown;
            public int PileIndex;
        }
        readonly List<Stone> _stones = new List<Stone>();
        Stone _held;
        readonly Queue<(Vector3 p, float dt)> _track = new Queue<(Vector3, float)>();
        Vector3 _lastHold;
        bool _wasPinching;

        // the mouth's target ring and the HUD
        Transform _target;
        Material _targetMat;
        Transform _hud;
        KitGroup _hudGroup;
        TextMeshPro _title, _sub;
        readonly List<KitIcon> _lives = new List<KitIcon>();
        KitButton _iAm, _retry, _restart, _leave, _quit, _room;

        public bool Active { get; private set; }
        public DodoPhase Phase => _rules?.Phase ?? DodoPhase.Intro;

        public void Init(CampWorld camp, StoryAssets a, StoryAudio audio, Texture2D glow, Transform uiRoot,
            Func<string, string> line, Action<string> say, Action restartStory, Action back, Action returnToStory, Action toggleRoom = null)
        {
            _camp = camp; _a = a; _audio = audio; _glow = glow;
            _line = line; _say = say; _restartStory = restartStory; _back = back; _return = returnToStory; _toggleRoom = toggleRoom;
            _shout = gameObject.AddComponent<ShoutDetector>();
            _shout.enabled = false;
            BuildHud(uiRoot);
        }

        // ---------- start and stop ----------

        public void Begin()
        {
            if (_root == null) Build();
            _homeZ = Home.z;
            Fit(_fit); // the slab, and Dodo's size, for the forest or your room
            _root.gameObject.SetActive(true);
            Active = true;
            _rules = new DodoRules();
            _rules.Changed += OnPhase;
            _danger = 0f; _dawn = 0f; _flare = 0f; _homeZ = Home.z; _sayAt = -1f;
            _held = null;
            for (var i = 0; i < _stones.Count; i++) Reset(_stones[i], i);
            _camp.Village?.Celebrate(false);
            _camp.Teller.gameObject.SetActive(false);
            if (_dodo.Built)
            {
                _dodo.gameObject.SetActive(true);
                _dodo.transform.localPosition = Lair;
                _dodo.Play("Run", 0.1f);
            }
            _shout.enabled = true;
            _shout.Rest(1.2f); // the opening roar is not an answer
            _hud.gameObject.SetActive(true);
            _hud.GetComponent<FollowView>().Snap();
            OnPhase(DodoPhase.Intro);
            Roar();
            _audio.Effect("heartbeat");
        }

        /// <summary>Leave the game: the camp as it was.</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            if (_rules != null) _rules.Changed -= OnPhase;
            if (_root != null) _root.gameObject.SetActive(false);
            if (_dodo != null) _dodo.gameObject.SetActive(false);
            _hud.gameObject.SetActive(false);
            _shout.enabled = false;
            _camp.Village?.Celebrate(false);
            _camp.Teller.gameObject.SetActive(true);
            _camp.SetStory(null);
        }

        void Build()
        {
            _root = new GameObject("Dodo Game").transform;
            _root.SetParent(_camp.transform, false);

            var dodo = new GameObject("Dodo").transform;
            dodo.SetParent(_camp.transform, false);
            _dodo = dodo.gameObject.AddComponent<DodoCreature>();
            if (!_dodo.Build(_a.dodo, _a.lit)) Debug.LogWarning("Hypa Stories: Dodo's model is missing (StoryAssets.dodo); the game runs without it.");

            // a flat stone slab by your seat, with the seven cool stones on it
            var slab = new TrailBuilder.MeshKit();
            slab.Cylinder(Vector3.zero, 0.36f, 0.3f, SlabTop, new Color(0.26f, 0.25f, 0.24f), new Color(0.34f, 0.33f, 0.31f), 9);
            _slabT = Node("Stone Slab", slab.Build("Stone Slab"), new Material(_a.pine) { name = "Slab" });
            _slabT.SetParent(_root, false);
            for (var i = 0; i < DodoRules.Stones; i++)
            {
                var mk = new TrailBuilder.MeshKit();
                mk.Ellipsoid(Vector3.zero, new Vector3(0.085f, 0.062f, 0.075f), Quaternion.Euler(0f, i * 40f, 8f), Color.white, 10, 7);
                var m = new Material(_a.lit) { name = "Hot Stone", color = new Color(0.32f, 0.31f, 0.3f) };
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                var t = Node("Stone", mk.Build("Stone"), m);
                t.SetParent(_root, false);
                // a soft red glow round it as it heats, so a hot stone shows from across the fire
                var gm = new Material(_a.additive) { mainTexture = _glow, color = new Color(1f, 0.35f, 0.08f) };
                gm.SetFloat("_Opacity", 0f);
                var g = Node("Stone Glow", StoryMeshes.Quad(0.42f, 0.42f), gm);
                g.SetParent(t, false);
                _stones.Add(new Stone { T = t, M = m, PileIndex = i, Glow = g, GlowM = gm });
            }

            // the glowing ring that shows where Dodo's mouth is while it's open
            _targetMat = new Material(_a.additive) { mainTexture = _glow, color = new Color(1f, 0.55f, 0.15f) };
            _targetMat.SetFloat("_Opacity", 0f);
            _target = Node("Mouth Target", StoryMeshes.Ring(0.36f, 0.42f), _targetMat);
            _target.SetParent(_root, false);
        }

        static Transform Node(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void Reset(Stone s, int i)
        {
            s.S = StoneState.Pile; s.Slot = -1; s.Homing = false;
            s.T.gameObject.SetActive(true);
            s.T.localPosition = PilePos(i);
            SetHeat(s, 0f);
        }

        Vector3 PilePos(int i)
        {
            var a = i / (float)DodoRules.Stones * Mathf.PI * 2f;
            var r = i == 0 ? 0f : 0.16f;
            return Slab + new Vector3(Mathf.Sin(a) * r, SlabTop + 0.055f + (i == 0 ? 0.04f : 0f), Mathf.Cos(a) * r);
        }

        Vector3 FirePos(int slot)
        {
            var a = slot / (float)DodoRules.Stones * Mathf.PI * 2f + 0.3f;
            // on the ring of fire-pit stones, round the flames, where you can see them glow
            var k = _fit.Room ? GameFit.FireScale : 1f; // on the room fire's smaller ring
            return new Vector3(Mathf.Sin(a) * FireRing * k, 0.3f * k, Mathf.Cos(a) * FireRing * k);
        }

        void SetHeat(Stone s, float heat)
        {
            s.M.color = Color.Lerp(new Color(0.32f, 0.31f, 0.3f), new Color(0.55f, 0.12f, 0.05f), heat);
            var pulse = heat >= 1f ? 0.85f + 0.15f * Mathf.Sin(Time.time * 6f + s.PileIndex) : 1f;
            s.M.SetColor("_EmissionColor", new Color(1.6f, 0.35f, 0.05f) * heat * heat * pulse);
            s.GlowM.SetFloat("_Opacity", heat * heat * 0.75f * pulse);
            if (Camera.main != null) s.Glow.rotation = Quaternion.LookRotation(s.Glow.position - Camera.main.transform.position);
        }

        // ---------- every frame ----------

        void Update()
        {
            if (!Active || _rules == null) return;
            var dt = Time.deltaTime;
            _rules.Tick(dt);
            var phase = _rules.Phase;
            if (phase == DodoPhase.Answer || phase == DodoPhase.Roar) { if (_shout.Shouted) IAm(); }
            if (Application.isEditor) EditorKeys();
            if (_sayAt > 0f && Time.time >= _sayAt) { _sayAt = -1f; if (!_rules.Over) _say?.Invoke(_sayText); }

            Hands(dt);
            Stones(dt);
            Creature(dt);
            Scene(dt);
            Hud();
        }

        // ---------- the editor (no hands): P puts a stone in the fire, T throws one into the mouth, M throws one wide ----------

        void EditorKeys()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.P)) AutoPlace();
            if (UnityEngine.Input.GetKeyDown(KeyCode.T)) AutoThrow(true);
            if (UnityEngine.Input.GetKeyDown(KeyCode.M)) AutoThrow(false);
        }

        /// <summary>The next stone from the slab goes into the fire, as if you'd dropped it there.</summary>
        public void AutoPlace()
        {
            foreach (var s in _stones)
            {
                if (s.S != StoneState.Pile) continue;
                s.ToMouth = false; s.Time = 0f; s.Homing = true; s.Thrown = true;
                s.Vel = Lob(s.T.position, _camp.transform.TransformPoint(new Vector3(0f, 0.25f, 0f)), 0.8f);
                s.S = StoneState.Flying;
                return;
            }
        }

        /// <summary>A hot stone from the fire, thrown at Dodo's mouth (or wide of it).</summary>
        public void AutoThrow(bool hit)
        {
            foreach (var s in _stones)
            {
                if (s.S != StoneState.Fire || s.Slot < 0 || _rules.Heat(s.Slot) < 1f) continue;
                var to = hit ? _dodo.Mouth : _dodo.Mouth + _dodo.transform.right * 2.5f;
                s.ToMouth = true; s.Time = 0f; s.Homing = hit; s.Thrown = true;
                s.Vel = Lob(s.T.position, to, 0.7f);
                s.S = StoneState.Flying;
                _audio.Effect("throw");
                return;
            }
        }

        /// <summary>Leave the fight and go back to the end of the story.</summary>
        void ReturnToStory()
        {
            End();
            _return?.Invoke();
        }

        // Dodo's sounds come from Dodo: the roar from its head (deep, full volume), steps from its feet, swallowing from its mouth
        Vector3 DodoHead => _dodo != null && _dodo.Built ? _dodo.Mouth + Vector3.up * 0.3f : _camp.transform.TransformPoint(new Vector3(0f, 2.8f, 5f));
        Vector3 DodoFeet => _dodo != null ? _dodo.transform.position + Vector3.up * 0.2f : _camp.transform.position;
        void Roar() { _audio.EffectAt("roar", DodoHead, 1f, 0.82f, 12f); _audio.EffectAt("groan", DodoHead, 0.55f, 0.7f, 10f); }
        void Stomp() => _audio.EffectAt("stomp", DodoFeet, 1f, 0.9f, 8f);

        /// <summary>You answered Dodo: "I am!"</summary>
        public void IAm()
        {
            if (!_rules.Answer()) return;
            _audio.Tap();
            _say?.Invoke(_line?.Invoke("i-am") ?? "I am!");
        }

        void OnPhase(DodoPhase p)
        {
            switch (p)
            {
                case DodoPhase.Roar:
                    _dodo.Play(_dodo.Has("Yes") ? "Yes" : "Idle", 0.2f, 0.8f);
                    Roar();
                    _shout.Rest(4.2f); // its own roar (and the storyteller saying its line) must not count as "I am!"
                    _sayAt = Time.time + 1.5f; // the roar first, then the storyteller speaks its challenge
                    _sayText = _line?.Invoke("challenge") ?? "“In this town, who is able to fight with me?”";
                    break;
                case DodoPhase.Approach: _dodo.Play("Walk", 0.3f); break;
                case DodoPhase.Window: _dodo.Play("Duck", 0.2f); break;
                case DodoPhase.Swallow:
                    _dodo.Play("HitReact", 0.1f);
                    _audio.EffectAt("sizzle", _dodo.Mouth, 0.8f);
                    _flare = 1f;
                    break;
                case DodoPhase.Missed:
                    _homeZ = Mathf.Max(Stoop.z + 0.6f, _homeZ - 0.9f); // it steps closer
                    _dodo.Play("Punch", 0.15f);
                    Roar();
                    Stomp();
                    break;
                case DodoPhase.Lost:
                    _dodo.Play("Run", 0.15f);
                    Roar();
                    break;
                case DodoPhase.Won:
                    _dodo.Play("Death", 0.15f);
                    _audio.EffectAt("groan", DodoHead, 1f, 0.8f, 12f);
                    break;
            }
        }

        // ---------- hands (or controllers): grab, carry, throw ----------

        void Hands(float dt)
        {
            var input = HypaInput.Current;
            var pinch = input.IsPinching;
            var hasRay = input.TryGetRay(out var ray);
            var mouse = HypaInput.AimsWithMouse(input);
            var hold = hasRay ? ray.origin + ray.direction * (mouse ? 0.7f : 0.06f) : _lastHold;
            if (dt > 0f && hasRay)
            {
                _track.Enqueue((hold, dt));
                while (_track.Count > 6) _track.Dequeue();
            }
            _lastHold = hold;

            // a pinch on a panel button ("I am!", Back, the room switch) is for the button: it never picks up or throws a stone
            if (pinch && !_wasPinching) _pinchOnUi = OnPanel();
            if (pinch && !_wasPinching && _held == null && hasRay && !_pinchOnUi) TryGrab(ray, hold);
            if (_held != null && _held.S == StoneState.Held) _held.T.position = hold;
            if (!pinch && _wasPinching && _held != null) Release(ray, hasRay);
            _wasPinching = pinch;
        }

        bool _pinchOnUi;

        /// <summary>The hand's ray is on a panel button, or the pinch started on one. Shared with the hat game.</summary>
        public static bool OnPanel()
        {
            var p = PinchPointer.Instance;
            return p != null && (p.Hover != null || p.Pressed != null);
        }

        bool Grabbable(Stone s)
        {
            var p = _rules.Phase;
            if (s.S == StoneState.Pile) return p == DodoPhase.Heat || p == DodoPhase.Intro;
            if (s.S == StoneState.Fire) return (p == DodoPhase.Roar || p == DodoPhase.Answer || p == DodoPhase.Approach || p == DodoPhase.Window) && _rules.Heat(s.Slot) >= 1f;
            return false;
        }

        /// <summary>
        /// Pinch roughly toward the slab (or, later, the fire) and the nearest stone you can take comes to your hand: the one
        /// closest to where you point, anywhere within a wide cone. No need to aim at a particular stone. Pure, unit tested.
        /// </summary>
        public static int PickStone(Ray ray, Vector3 hand, IList<Vector3> stones, IList<bool> takeable, float coneDegrees = 55f)
        {
            var best = -1; var bestAngle = float.MaxValue;
            for (var i = 0; i < stones.Count; i++)
            {
                if (!takeable[i]) continue;
                if (Vector3.Distance(stones[i], hand) < 0.25f) return i;           // already in reach
                var angle = Vector3.Angle(ray.direction, stones[i] - ray.origin);
                if (angle < coneDegrees && angle < bestAngle) { best = i; bestAngle = angle; }
            }
            return best;
        }

        void TryGrab(Ray ray, Vector3 hold)
        {
            var pos = new List<Vector3>(); var ok = new List<bool>();
            foreach (var s in _stones) { pos.Add(s.T.position); ok.Add(Grabbable(s)); }
            var i = PickStone(ray, hold, pos, ok);
            if (i < 0) return;
            var best = _stones[i];
            _held = best;
            best.S = StoneState.Fetch; best.From = best.T.position; best.Time = 0f;
            _audio.Tap();
        }

        void Release(Ray ray, bool hasRay)
        {
            var s = _held;
            _held = null;
            var aiming = _rules.Phase == DodoPhase.Heat || _rules.Phase == DodoPhase.Intro ? Aim.Fire : Aim.Mouth;
            var target = aiming == Aim.Fire ? _camp.transform.TransformPoint(new Vector3(0f, 0.25f, 0f)) : _dodo.Mouth;

            // the hand's speed over the last few frames
            var v = Vector3.zero; Vector3? first = null; var total = 0f; Vector3 last = default;
            foreach (var (p, dt) in _track) { if (first == null) first = p; last = p; total += dt; }
            if (first != null && total > 1e-3f) v = (last - first.Value) / total;

            var pos = s.T.position;
            s.ToMouth = aiming == Aim.Mouth;
            s.Time = 0f;
            if (Flicked(v.magnitude, hasRay ? Vector3.Angle(ray.direction, target - ray.origin) : 180f, aiming == Aim.Fire))
            {
                // a flick: it flies, guided all the way, into the fire (heating) or Dodo's mouth (fighting)
                s.Vel = Lob(pos, target, Mathf.Clamp(Vector3.Distance(pos, target) * 0.16f, 0.45f, 0.9f));
                s.Homing = true;
                s.Thrown = true;
                s.S = StoneState.Flying;
                _audio.Effect("throw");
            }
            else
            {
                // a fumble, not a throw: it goes back, and it does not cost a life
                s.Vel = v;
                s.Homing = false;
                s.Thrown = false;
                s.S = StoneState.Flying;
            }
        }

        /// <summary>
        /// Does letting go send the stone? Heating: always (it goes in the fire). Fighting: a flick of the hand, or letting go
        /// while pointing roughly at the mouth. A slow release pointing away is a fumble (no life lost). Pure, unit tested.
        /// </summary>
        public static bool Flicked(float handSpeed, float pointAngle, bool toFire) => toFire || handSpeed > 0.45f || pointAngle < 40f;

        enum Aim { Fire, Mouth }

        static Vector3 Lob(Vector3 from, Vector3 to, float time)
        {
            var g = new Vector3(0f, -Gravity, 0f);
            return (to - from - 0.5f * g * time * time) / time;
        }

        const float Gravity = 7f;

        // ---------- stones ----------

        void Stones(float dt)
        {
            foreach (var s in _stones)
            {
                if (s.Slot >= 0 && s.S != StoneState.Gone) SetHeat(s, _rules.Heat(s.Slot));
                switch (s.S)
                {
                    case StoneState.Fetch:
                    {
                        s.Time += dt / 0.22f;
                        var to = _lastHold;
                        s.T.position = Vector3.Lerp(s.From, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s.Time))) + Vector3.up * Mathf.Sin(Mathf.Clamp01(s.Time) * Mathf.PI) * 0.15f;
                        if (s.Time >= 1f) s.S = StoneState.Held;
                        break;
                    }
                    case StoneState.Flying:
                    {
                        s.Time += dt;
                        var target = s.ToMouth ? _dodo.Mouth : _camp.transform.TransformPoint(new Vector3(0f, 0.2f, 0f));
                        s.Vel += Vector3.down * Gravity * dt;
                        if (s.Homing)
                        {
                            var want = (target - s.T.position).normalized * Mathf.Max(s.Vel.magnitude, 5f);
                            s.Vel = Vector3.Lerp(s.Vel, want, 1f - Mathf.Exp(-5f * dt));
                        }
                        s.T.position += s.Vel * dt;
                        s.T.Rotate(s.Vel.normalized, 540f * dt, Space.World);
                        var hitR = s.ToMouth ? 0.5f : 0.55f;
                        if (Vector3.Distance(s.T.position, target) < hitR) { Landed(s, true); break; }
                        var local = _camp.transform.InverseTransformPoint(s.T.position);
                        if (local.y < 0.02f || s.Time > 2.5f) Landed(s, false);
                        break;
                    }
                    case StoneState.Return:
                    {
                        s.Time += dt / 0.7f;
                        var u = Mathf.Clamp01(s.Time);
                        s.T.localPosition = Vector3.Lerp(s.From, s.To, Mathf.SmoothStep(0f, 1f, u)) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.4f;
                        if (u >= 1f) s.S = s.Slot >= 0 ? StoneState.Fire : StoneState.Pile;
                        break;
                    }
                }
            }
        }

        void Landed(Stone s, bool onTarget)
        {
            if (!s.ToMouth)
            {
                if (onTarget && _rules.Place())
                {
                    s.Slot = _rules.Placed - 1;
                    s.S = StoneState.Return; s.From = s.T.localPosition; s.To = FirePos(s.Slot); s.Time = 0.6f;
                    _flare = 1f;
                    _camp.Effect("sparks");
                    _audio.Effect("sparks");
                }
                else Back(s);
                return;
            }
            var window = _rules.Phase == DodoPhase.Window;
            if (onTarget && window && _rules.Hit())
            {
                s.S = StoneState.Gone;
                s.T.gameObject.SetActive(false);
                return;
            }
            if (window && !onTarget && s.Thrown) _rules.Miss();
            Back(s);   // early, late or wide: it rolls back into the fire
        }

        void Back(Stone s)
        {
            s.S = StoneState.Return; s.From = s.T.localPosition; s.Time = 0f; s.Homing = false;
            s.To = s.Slot >= 0 ? FirePos(s.Slot) : PilePos(s.PileIndex);
        }

        // ---------- Dodo ----------

        void Creature(float dt)
        {
            if (!_dodo.Built) return;
            var t = _dodo.transform;
            var cam = Camera.main != null ? _camp.transform.InverseTransformPoint(Camera.main.transform.position) : new Vector3(0f, 1.2f, -3.1f);
            var p = _rules.Phase; var T = _rules.T;
            Vector3 want;
            switch (p)
            {
                case DodoPhase.Intro:
                    want = Vector3.Lerp(Lair, Home, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(T / 3.2f)));
                    if (T > 3.2f && T - dt <= 3.2f) { _dodo.Play("Idle", 0.3f); Stomp(); Roar(); }
                    break;
                case DodoPhase.Approach:
                    want = Vector3.Lerp(new Vector3(0f, 0f, _homeZ), Stoop, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(T / (DodoRules.ApproachTime - 0.3f))));
                    if (Mathf.Repeat(T, 0.6f) < dt) Stomp();
                    break;
                case DodoPhase.Window:
                    want = Stoop;
                    _dodo.Hold("Duck", 0.45f); // stays stooped, mouth open, while the window lasts
                    break;
                case DodoPhase.Swallow:
                    want = T < 1.6f ? Stoop : Vector3.Lerp(Stoop, new Vector3(0f, 0f, _homeZ), Mathf.Clamp01((T - 1.6f) / 1.4f));
                    if (T > 0.9f && T - dt <= 0.9f) { _audio.EffectAt("gulp", _dodo.Mouth, 1f, 0.85f); _dodo.Play("Yes", 0.2f); }
                    if (T > 1.6f && T - dt <= 1.6f) _dodo.Play("Walk", 0.3f);
                    break;
                case DodoPhase.Lost:
                    // it comes for you
                    var me = new Vector3(cam.x, 0f, cam.z);
                    want = Vector3.Lerp(t.localPosition, me + (t.localPosition - me).normalized * 1.4f, 1f - Mathf.Exp(-2.5f * dt));
                    if (T > 1.1f && T - dt <= 1.1f) { _dodo.Play("Punch", 0.1f); _audio.EffectAt("fall", DodoFeet, 1f); }
                    break;
                case DodoPhase.Won:
                    want = t.localPosition;
                    if (T > 0.8f && T - dt <= 0.8f) _audio.EffectAt("fall", DodoFeet, 1f, 0.9f, 10f);
                    break;
                default:
                    want = new Vector3(0f, 0f, _homeZ);
                    if ((p == DodoPhase.Answer || p == DodoPhase.Heat) && !_dodo.IsPlaying("Idle") && T > 1.2f) _dodo.Play("Idle", 0.4f);
                    if (p == DodoPhase.Missed && T > 1.4f && T - dt <= 1.4f) _dodo.Play("Idle", 0.4f);
                    break;
            }
            if (p != DodoPhase.Lost) t.localPosition = Vector3.Lerp(t.localPosition, want, p == DodoPhase.Intro ? 1f : 1f - Mathf.Exp(-4f * dt));
            else t.localPosition = want;
            // always facing you
            var to = new Vector3(cam.x, 0f, cam.z) - t.localPosition; to.y = 0f;
            if (to.sqrMagnitude > 1e-3f && p != DodoPhase.Won) t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.LookRotation(to.normalized), 1f - Mathf.Exp(-5f * dt));

            // the mouth target: a ring of fire that shrinks as the window closes
            var open = p == DodoPhase.Window ? Mathf.Clamp01(_rules.WindowLeft / DodoRules.WindowTime) : 0f;
            _targetMat.SetFloat("_Opacity", p == DodoPhase.Window ? 0.55f + 0.25f * Mathf.Sin(Time.time * 10f) : 0f);
            _target.position = _dodo.Mouth;
            if (Camera.main != null) _target.rotation = Quaternion.LookRotation(_target.position - Camera.main.transform.position);
            _target.localScale = Vector3.one * (0.6f + open * 0.8f);
        }

        // ---------- the night ----------

        void Scene(float dt)
        {
            var p = _rules.Phase;
            _flare = Mathf.Max(0f, _flare - dt * 0.8f);
            var dangerWant = p == DodoPhase.Won ? 0f : p == DodoPhase.Lost ? 1f : 0.85f;
            _danger = Mathf.MoveTowards(_danger, dangerWant, dt * (p == DodoPhase.Won ? 0.25f : 0.5f));
            _dawn = Mathf.MoveTowards(_dawn, p == DodoPhase.Won && _rules.T > 2f ? 1f : 0f, dt * 0.2f);
            var heated = 0f;
            for (var i = 0; i < _rules.Placed; i++) heated += _rules.Heat(i);
            var fire = p == DodoPhase.Won ? 0.9f : 1f + heated / DodoRules.Stones * 0.35f + _flare * 0.9f;
            _camp.SetStory(new StorySample
            {
                Fire = fire, Dark = p == DodoPhase.Won ? 0f : 0.45f * _danger, Danger = _danger, Dawn = _dawn,
                Wind = p == DodoPhase.Window ? 0.5f : 0.25f * _danger, Index = -1,
            });

            if (p == DodoPhase.Won && _rules.T > 3f && _rules.T - dt <= 3f)
            {
                _audio.Effect("rooster");
                _audio.Effect("birds");
                _camp.Village?.Celebrate(true);
            }
        }

        // ---------- the HUD: what to do now, lives, and the end ----------

        void BuildHud(Transform uiRoot)
        {
            _hud = new GameObject("Dodo HUD").transform;
            _hud.SetParent(uiRoot, false);
            var f = _hud.gameObject.AddComponent<FollowView>();
            f.Distance = 1.1f; f.Height = 0.2f; f.YawOffset = -38f; f.Threshold = 55f; // up and to the left: clear of the stones, the fire and Dodo
            Kit.Glass(_hud, "Panel", new Vector3(0f, 0f, 0.006f), new Vector2(0.5f, 0.15f), 0.04f);
            _title = Kit.Text(_hud, "", 0.03f, new Vector3(0f, 0.035f, 0f), new Vector2(0.46f, 0.045f), KitTheme.Paper, TextAlignmentOptions.Center, KitFont.Serif);
            _sub = Kit.Text(_hud, "", 0.016f, new Vector3(0f, -0.006f, 0f), new Vector2(0.46f, 0.04f), KitTheme.Dim);
            for (var i = 0; i < DodoRules.Lives; i++) _lives.Add(Kit.Icon(_hud, "flame", new Vector3(0.2f - i * 0.03f, 0.055f, 0f), 0.022f, KitTheme.Ember));
            _iAm = Kit.Button(_hud, new Vector3(0f, -0.048f, 0f), new Vector2(0.16f, 0.05f), "I am!", "hand", KitStyle.Primary, IAm);
            _quit = Kit.Button(_hud, new Vector3(-0.2f, 0.055f, 0f), new Vector2(0.09f, 0.038f), "Back", "arrow-left", KitStyle.Ghost, ReturnToStory, null, 0.015f);
            if (_toggleRoom != null)
            {
                _room = Kit.Button(_hud, new Vector3(0f, -0.115f, 0f), new Vector2(0.28f, 0.04f), "Back to the forest", "house", KitStyle.Ghost, _toggleRoom, null, 0.014f);
                _room.SetLabel("In my room");
            }
            _retry = Kit.Button(_hud, new Vector3(-0.15f, -0.048f, 0f), new Vector2(0.14f, 0.046f), "Try again", "rotate-ccw", KitStyle.Primary, () => { End(); Begin(); });
            _restart = Kit.Button(_hud, new Vector3(0f, -0.048f, 0f), new Vector2(0.14f, 0.046f), "The story", "play", KitStyle.Ghost, () => { End(); _restartStory?.Invoke(); });
            _leave = Kit.Button(_hud, new Vector3(0.15f, -0.048f, 0f), new Vector2(0.14f, 0.046f), "Stories", "arrow-left", KitStyle.Ghost, () => { End(); _back?.Invoke(); });
            _hudGroup = _hud.gameObject.AddComponent<KitGroup>();
            _hudGroup.Refresh();
            _hud.gameObject.SetActive(false);
        }

        void Hud()
        {
            var p = _rules.Phase;
            string title, sub;
            switch (p)
            {
                case DodoPhase.Intro: title = "Dodo!"; sub = "It has come into the town. Only you can answer it."; break;
                case DodoPhase.Heat:
                    title = "Flick the stones into the fire";
                    sub = _rules.Placed < DodoRules.Stones
                        ? $"Pinch toward the slab, then flick.  {_rules.Placed}/{DodoRules.Stones}"
                        : "Wait until they glow red hot…";
                    break;
                case DodoPhase.Roar: title = "“Who is able to fight with me?”"; sub = "Answer it!"; break;
                case DodoPhase.Answer: title = "Say: “I am!”"; sub = _rules.Silences > 0 ? "Louder! It's waiting…" : "Shout it out loud (or pinch the button)."; break;
                case DodoPhase.Approach: title = "Flick a hot stone at Dodo"; sub = "Pinch toward the fire, then flick."; break;
                case DodoPhase.Window: title = "Flick it into its mouth!"; sub = $"{_rules.WindowLeft:0.0} s"; break;
                case DodoPhase.Swallow: title = $"It swallowed it!  {_rules.Hits}/{DodoRules.Stones}"; sub = _rules.Hits < DodoRules.Stones ? "But it is not finished…" : ""; break;
                case DodoPhase.Missed: title = "Missed!"; sub = "Dodo steps closer. Get ready."; break;
                case DodoPhase.Lost: title = "Dodo wins"; sub = "Try again, or hear the story once more."; break;
                default: title = "You defeated Dodo!"; sub = "Seven hot stones, like Auta. The town is safe."; break;
            }
            _title.text = title; _sub.text = sub;
            for (var i = 0; i < _lives.Count; i++) _lives[i].gameObject.SetActive(i < _rules.LivesLeft && !_rules.Over);
            var asking = p == DodoPhase.Answer || p == DodoPhase.Roar;
            _iAm.gameObject.SetActive(asking);
            if (asking)
            {
                var said = (_line?.Invoke("i-am") ?? "I am!").Trim().Trim('“', '”', '"');
                if (said.Length > 18) said = said.Substring(0, 18).TrimEnd() + "…";
                _iAm.SetLabel(said);
            }
            var over = _rules.Over && _rules.T > (p == DodoPhase.Lost ? 2.2f : 4.5f);
            _quit.gameObject.SetActive(!over);
            _retry.gameObject.SetActive(over);
            _retry.Label.text = p == DodoPhase.Won ? "Play again" : "Try again";
            _restart.gameObject.SetActive(over);
            _leave.gameObject.SetActive(over);
        }

        /// <summary>The same room switch as the story: the fire, Dodo and the stones in your real room.</summary>
        public void SetInRoom(bool inRoom)
        {
            if (_room == null) return;
            _room.SetLabel(inRoom ? "Back to the forest" : "In my room");
            _room.Style = inRoom ? KitStyle.Primary : KitStyle.Ghost;
            _room.Selected = inRoom;
        }
    }
}
