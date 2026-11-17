using System;
using System.Collections.Generic;
using HypaCore;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The playable scene after Eshu at the Crossroads, played from your seat. Eshu's hat floats over a post in front of you:
    /// red on one side, white on the other. A friend stands either side; a badge over each head shows the colour he saw. One
    /// friend at a time listens (his badge glows gold, no timer): pinch the hat and slide your hand to spin it, like a lazy
    /// Susan, until the other colour faces him, and hold it there a moment. He sees it, laughs, and the other friend listens.
    /// Hold his own colour on him and he shouts (the quarrel jumps); the quarrel also grows slowly while you take your time, and
    /// when it boils over they fight and the round starts again. Three rounds; before the last, Eshu swaps their sides. Then
    /// carry the open calabash of palm wine onto the post, under the hat. The friends come, bend, and drink from it with both
    /// hands. After that, a cowrie still goes into the bowl at the crossroads. The rules are <see cref="HatRules"/>.
    /// </summary>
    public sealed class HatGame : MonoBehaviour
    {
        // the camp's ground as laid out in the forest: the fire at the origin, you at the story seat on -Z. In your room the same
        // layout is pulled in to fit (see GameFit), so everything below goes through _fit.
        static readonly Vector3 FPost = new Vector3(0f, 0f, -1.45f);
        static readonly Vector3 FFriendLeft = new Vector3(-1.75f, 0f, -1.25f), FFriendRight = new Vector3(1.75f, 0f, -1.25f);
        static readonly Vector3 FEshu = new Vector3(1.15f, 0f, 1.45f);
        static readonly Vector3 FSlab = new Vector3(0.62f, 0f, -2.35f), FBowl = new Vector3(-0.78f, 0f, -2.15f);
        GameFit _fit = GameFit.Forest;
        // In your room you sit right at the stones, so there's no ground between you and the fire for a post: the hat floats over
        // the fire itself, and Eshu sits across the stones where the storyteller does.
        Vector3 Post => _fit.Room ? Vector3.zero : _fit.At(FPost);
        /// <summary>Just outside the stones on the far side, where the storyteller sits in your room.</summary>
        float FarStones => Mathf.Max(_fit.Gap, GameFit.StoneEdge + 0.15f);
        // in your room the friends stand either side of the fire, just outside the stones, facing the hat over it
        Vector3 FriendLeft => _fit.Room ? new Vector3(-(GameFit.StoneEdge + 0.35f), 0f, 0f) : _fit.At(FFriendLeft);
        Vector3 FriendRight => _fit.Room ? new Vector3(GameFit.StoneEdge + 0.35f, 0f, 0f) : _fit.At(FFriendRight);
        Vector3 EshuAt => _fit.Room ? new Vector3(0.35f, 0f, FarStones) : _fit.At(FEshu);
        Vector3 Slab => _fit.At(FSlab);
        Vector3 Bowl => _fit.At(FBowl);
        /// <summary>Where you sit: the forest seat, or how far from the fire your room put you.</summary>
        Vector3 Seat => _fit.At(new Vector3(0f, 0f, -GameFit.ForestSeat));
        const float SlabTop = 0.62f, PostTop = 1.0f, HatFloat = 0.38f, IdleSpin = 24f, Snap = 65f, ReturnSpeed = 220f;
        /// <summary>Where the open calabash sits: on the post's cap, in sight, under the floating hat.</summary>
        Vector3 CupSpot => Post + new Vector3(0f, PostTop + 0.085f, 0f);
        /// <summary>Tipped toward the seat, so the wine is a face you can see and not a line at eye level.</summary>
        static readonly Quaternion CupSit = Quaternion.Euler(-22f, 0f, 0f);

        CampWorld _camp;
        StoryAssets _a;
        StoryAudio _audio;
        Texture2D _glow;
        Action _restartStory, _back, _return, _toggleRoom;

        HatRules _rules;
        Transform _root, _hat;
        Material _vc;
        Village.Figure _eshu;
        readonly Village.Figure[] _friends = new Village.Figure[2];
        readonly Material[] _ringMat = new Material[2];
        readonly Transform[] _ring = new Transform[2];
        readonly float[] _sitT = { -1f, -1f };
        readonly float[] _poseUntil = new float[2];
        readonly float[] _nextShout = new float[2];
        Transform _cup, _cowrie;
        Material _cupGlow, _cowrieGlow;
        Transform _cupHalo, _cowrieHalo;

        float _yaw = 180f, _holdUntil, _spirit, _eshuLaugh = -1f, _dawn, _nextGulp;
        bool _spinning, _wasPinching;
        float _prevSlide;
        int _stareAt = -1;
        float? _returnTo; // the hat turning itself back side-on (after a friend sees, at a round's start)
        string _roundSub = "";

        // the badges over the friends' heads: the colour each saw, how far the hat's colour is to counting, a check when seen
        sealed class Badge { public Transform Root, Check; public KitPanel Disc, Fill, Inner; public Material Glow; public Transform GlowT; }
        readonly Badge[] _badges = new Badge[2];
        static readonly Color Red = new Color(0.86f, 0.12f, 0.08f), White = new Color(0.96f, 0.94f, 0.88f);

        // a thrown cup or cowrie
        Transform _flying; Vector3 _vel; Vector3 _flyTo; float _flyT; bool _flyingCup;
        Transform _held;

        // HUD
        Transform _hud;
        TextMeshPro _title, _sub;
        KitPanel _meter;
        Transform _meterFill, _meterTrack, _meterLabel;
        KitButton _retry, _restart, _leave, _quit, _room;

        public bool Active { get; private set; }
        public HatPhase Phase => _rules?.Phase ?? HatPhase.Intro;
        public bool AnyListening => _rules != null && _rules.Listener >= 0 && _returnTo == null && Time.time > _holdUntil;

        public void Init(CampWorld camp, StoryAssets a, StoryAudio audio, Texture2D glow, Transform uiRoot, Action restartStory, Action back, Action returnToStory, Action toggleRoom = null)
        {
            _camp = camp; _a = a; _audio = audio; _glow = glow;
            _restartStory = restartStory; _back = back; _return = returnToStory; _toggleRoom = toggleRoom;
            BuildHud(uiRoot);
        }

        // ---------- start and stop ----------

        public void Begin()
        {
            if (_root == null) Build();
            Fit(_fit); // the props at their spots for the forest or your room
            _root.gameObject.SetActive(true);
            Active = true;
            _rules = new HatRules();
            _rules.Changed += OnPhase;
            _yaw = 180f; _returnTo = null; _roundSub = ""; _holdUntil = 0f; _spirit = 0f; _dawn = 0f; _nextGulp = 0f; _stareAt = -1; _spinning = false; _held = null; _flying = null;
            for (var i = 0; i < 2; i++) { _sitT[i] = -1f; _poseUntil[i] = 0f; _nextShout[i] = Time.time + 2.4f + i * 1.6f; Place(_friends[i], Spot(i, false), Post); }
            _cup.gameObject.SetActive(false);
            _cowrie.gameObject.SetActive(false);
            _camp.Village?.Celebrate(false);
            _camp.Teller.gameObject.SetActive(false);
            _hud.gameObject.SetActive(true);
            _hud.GetComponent<FollowView>().Snap();
            Laugh();
        }

        public void End()
        {
            if (!Active) return;
            Active = false;
            if (_rules != null) _rules.Changed -= OnPhase;
            _root.gameObject.SetActive(false);
            _hud.gameObject.SetActive(false);
            _camp.Village?.Celebrate(false);
            _camp.Teller.gameObject.SetActive(true);
            _camp.SetStory(null);
        }

        // ---------- the scene ----------

        void Build()
        {
            _root = new GameObject("Hat Game").transform;
            _root.SetParent(_camp.transform, false);
            _vc = new Material(_a.pine) { name = "Hat Game" };
            _vc.SetFloat("_Sway", 0f);

            // the post, and the hat floating over it
            var post = new TrailBuilder.MeshKit();
            var wood = new Color(0.3f, 0.2f, 0.12f);
            post.Limb(Vector3.zero, Vector3.up * PostTop, 0.07f, 0.055f, wood, 8);
            post.Cylinder(Vector3.up * PostTop, 0.16f, 0.18f, 0.045f, wood * 1.1f, wood * 1.2f, 10); // a platform under the hat, wide enough for the bowl
            _postT = Node("Post", post.Build("Post"), _vc); _postT.SetParent(_root, false);
            var shrine = new TrailBuilder.MeshKit();
            // a little crossroads shrine by your left hand: three paths meeting, a short post, a calabash bowl on top
            var ground = new Color(0.32f, 0.2f, 0.12f);
            for (var k = 0; k < 3; k++)
            {
                var a = (k * 120f + 30f) * Mathf.Deg2Rad;
                shrine.Box(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.35f + Vector3.up * 0.01f, new Vector3(0.14f, 0.01f, 0.7f), Quaternion.Euler(0f, k * 120f + 30f, 0f), ground);
            }
            shrine.Limb(Vector3.zero, Vector3.up * 0.55f, 0.05f, 0.04f, wood, 8);
            shrine.Ellipsoid(Vector3.up * 0.6f, new Vector3(0.13f, 0.06f, 0.13f), Quaternion.identity, new Color(0.62f, 0.46f, 0.24f), 12, 5, null, 100f);
            // the slab by your right hand, where the calabash and the cowrie wait
            _shrineT = Node("Shrine", shrine.Build("Shrine"), _vc); _shrineT.SetParent(_root, false);
            var slab = new TrailBuilder.MeshKit();
            slab.Cylinder(Vector3.zero, 0.36f, 0.3f, SlabTop, new Color(0.26f, 0.25f, 0.24f), new Color(0.34f, 0.33f, 0.31f), 9);
            _slabT = Node("Slab", slab.Build("Slab"), _vc); _slabT.SetParent(_root, false);

            _hat = Node("Eshu's Hat", HatMesh(), _vc);
            _hat.SetParent(_root, false);
            _hat.localPosition = Post + Vector3.up * (PostTop + HatFloat);
            // Eshu's light on the hat: the fire is behind it, so without this the side facing you is in shadow
            var lamp = new GameObject("Hat Light").AddComponent<Light>();
            lamp.type = LightType.Point; lamp.range = 1.6f; lamp.intensity = 1.8f; lamp.color = new Color(1f, 0.9f, 0.75f);
            lamp.shadows = LightShadows.None; lamp.renderMode = LightRenderMode.ForceVertex;
            lamp.transform.SetParent(_postT, false);
            lamp.transform.localPosition = new Vector3(0f, PostTop + HatFloat + 0.25f, -0.55f);

            // the friends, either side of the post, and Eshu by the fire
            var v = _camp.Village;
            var people = new GameObject("People").transform; people.SetParent(_root, false);
            if (v != null)
            {
                _friends[0] = v.MakePerson(people, "Left Friend", new Vector2(FriendLeft.x, FriendLeft.z), Vector2.right, false);
                _friends[1] = v.MakePerson(people, "Right Friend", new Vector2(FriendRight.x, FriendRight.z), Vector2.left, false);
                _eshu = v.MakeEshu(people, new Vector2(EshuAt.x, EshuAt.z), new Vector2(Seat.x - EshuAt.x, Seat.z - EshuAt.z));
                // his staff, strung with cowries: taller than he is (built in his own scale, so ~2.1 m in the world)
                var st = new TrailBuilder.MeshKit();
                var k2 = 1f / _eshu.Root.localScale.y;
                st.Limb(new Vector3(0.32f, 0.02f, 0.18f), new Vector3(0.3f, 2.1f * k2, 0.16f), 0.03f, 0.024f, new Color(0.22f, 0.14f, 0.08f), 6);
                st.Ellipsoid(new Vector3(0.3f, 2.1f * k2 + 0.03f, 0.16f), new Vector3(0.055f, 0.05f, 0.055f), Quaternion.identity, new Color(0.62f, 0.08f, 0.06f), 8, 6); // a red knob
                for (var c = 0; c < 12; c++)
                    st.Ellipsoid(new Vector3(0.33f + Mathf.Sin(c * 1.3f) * 0.05f, 2.1f * k2 - 0.75f + c * 0.055f, 0.2f + Mathf.Cos(c * 1.3f) * 0.05f), new Vector3(0.024f, 0.016f, 0.032f), Quaternion.Euler(0f, c * 40f, 0f), new Color(0.92f, 0.88f, 0.78f), 6, 4);
                Node("Eshu's Staff", st.Build("Eshu's Staff"), _vc).SetParent(_eshu.Root, false);
                _eshu.ArmR.localRotation = Quaternion.Euler(-25f, 0f, 12f);
                _eshu.ForeR.localRotation = Quaternion.Euler(-55f, 0f, 0f);
            }
            for (var i = 0; i < 2; i++)
            {
                _ringMat[i] = new Material(_a.additive) { mainTexture = _glow, color = new Color(1f, 0.78f, 0.3f) };
                _ringMat[i].SetFloat("_Opacity", 0f);
                _ring[i] = Node("Breath Glow", StoryMeshes.Quad(1.3f, 1.3f), _ringMat[i]);
                _ring[i].SetParent(_root, false);
                _ring[i].localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            for (var i = 0; i < 2; i++) _badges[i] = MakeBadge(i);

            // an open calabash of palm wine, and a cowrie, on the slab
            _cup = Node("Calabash", CalabashMesh(), _vc); _cup.SetParent(_root, false);
            var cw = new TrailBuilder.MeshKit();
            cw.Ellipsoid(Vector3.zero, new Vector3(0.035f, 0.022f, 0.05f), Quaternion.identity, new Color(0.95f, 0.92f, 0.84f), 10, 6);
            cw.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.008f, 0.004f, 0.07f), Quaternion.identity, new Color(0.45f, 0.38f, 0.3f)); // its slit
            _cowrie = Node("Cowrie", cw.Build("Cowrie"), _vc); _cowrie.SetParent(_root, false);
            (_cupGlow, _cupHalo) = Halo(_cup, 0.16f);
            _cupGlow.color = new Color(0.72f, 0.94f, 1f); // a cool glint, so the wine doesn't disappear into the firelight
            _cupHalo.localPosition = new Vector3(0f, 0.1f, -0.03f);
            _cupHalo.localRotation = Quaternion.Euler(90f, 0f, 0f);
            (_cowrieGlow, _cowrieHalo) = Halo(_cowrie, 0.35f);
        }

        Badge MakeBadge(int i)
        {
            var b = new Badge { Root = new GameObject(i == 0 ? "Red Badge" : "White Badge").transform };
            b.Root.SetParent(_root, false);
            var own = HatRules.ShoutsRed(i) ? White : Red; // the badge shows the side he still has to see: match it
            b.Glow = new Material(_a.additive) { mainTexture = _glow, color = new Color(1f, 0.8f, 0.35f) };
            b.Glow.SetFloat("_Opacity", 0f);
            b.GlowT = Node("Badge Glow", StoryMeshes.Quad(0.42f, 0.42f), b.Glow);
            b.GlowT.SetParent(b.Root, false);
            b.GlowT.localPosition = new Vector3(0f, 0f, 0.01f);
            b.Fill = Kit.Panel(b.Root, "Badge Fill", new Vector3(0f, 0f, 0.004f), new Vector2(0.17f, 0.17f), 0.085f, White, White, new Color(0f, 0f, 0f, 0f), 11, 0f);
            b.Disc = Kit.Panel(b.Root, "Badge", Vector3.zero, new Vector2(0.12f, 0.12f), 0.06f, own, own * 0.85f, KitTheme.Hairline, 12, 0.2f);
            b.Disc.BorderWidth = 0.007f; b.Disc.Apply();
            b.Inner = Kit.Panel(b.Root, "Badge Both", new Vector3(0f, 0f, -0.002f), new Vector2(0.06f, 0.06f), 0.03f, HatRules.ShoutsRed(i) ? Red : White, HatRules.ShoutsRed(i) ? Red : White, new Color(0f, 0f, 0f, 0f), 13, 0f);
            b.Check = Kit.Icon(b.Root, "check", new Vector3(0.07f, -0.05f, -0.004f), 0.05f, new Color(0.45f, 0.9f, 0.45f), 14).transform;
            return b;
        }

        (Material, Transform) Halo(Transform parent, float size)
        {
            var m = new Material(_a.additive) { mainTexture = _glow, color = new Color(1f, 0.85f, 0.45f) };
            m.SetFloat("_Opacity", 0.5f);
            var t = Node("Halo", StoryMeshes.Quad(size, size), m);
            t.SetParent(parent, false);
            return (m, t);
        }

        /// <summary>
        /// An open calabash, nearly full. The wine is a pale disc just under the rim, drawn from both sides, with a white
        /// glint toward the seat. The pine shader culls back faces, and a low brown surface was reading as more gourd.
        /// </summary>
        static Mesh CalabashMesh()
        {
            var mk = new TrailBuilder.MeshKit();
            var shell = new Color(0.78f, 0.54f, 0.26f);
            var cavity = new Color(0.36f, 0.22f, 0.1f);
            var wet = new Color(0.22f, 0.4f, 0.46f); // the wall under the waterline
            var lip = new Color(0.86f, 0.64f, 0.34f);
            var deep = new Color(0.42f, 0.7f, 0.8f);
            var face = new Color(0.8f, 0.95f, 0.98f);
            var glint = new Color(0.98f, 1f, 1f);
            // outside, top to bottom, so the surface faces out
            mk.Lathe(Vector3.zero, Quaternion.identity, new (float, float, float)[]
            {
                (0.11f, 0.15f, 0.15f), (0.09f, 0.132f, 0.132f), (0.04f, 0.122f, 0.122f), (0f, 0.09f, 0.09f), (-0.04f, 0.042f, 0.042f),
            }, shell, 18);
            // inside below the water, then a dry band up to the lip. Bottom to top, so it faces into the bowl.
            mk.Lathe(Vector3.zero, Quaternion.identity, new (float, float, float)[]
            {
                (-0.005f, 0.045f, 0.045f), (0.03f, 0.09f, 0.09f), (0.07f, 0.116f, 0.116f), (0.088f, 0.124f, 0.124f),
            }, wet, 18);
            mk.Lathe(Vector3.zero, Quaternion.identity, new (float, float, float)[]
            {
                (0.088f, 0.124f, 0.124f), (0.10f, 0.134f, 0.134f),
            }, cavity, 18);
            const int seg = 20;
            const float y = 0.09f, r = 0.12f;
            for (var i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                Vector3 Ring(float a, float rad, float h) => new Vector3(Mathf.Sin(a) * rad, h, Mathf.Cos(a) * rad);
                mk.Quad2(Ring(a0, 0.15f, 0.11f), Ring(a1, 0.15f, 0.11f), Ring(a1, 0.134f, 0.10f), Ring(a0, 0.134f, 0.10f), lip);
                // the surface, both windings: a bright pool filling the mouth, glint shifted toward the seat (-Z)
                var c = new Vector3(0f, y, -0.02f);
                mk.Tri(c, Ring(a1, r, y), Ring(a0, r, y), glint, face, deep);
                mk.Tri(c + Vector3.down * 0.004f, Ring(a0, r, y - 0.004f), Ring(a1, r, y - 0.004f), glint, face, deep);
            }
            return mk.Build("Calabash");
        }

        /// <summary>A tall soft hat: one half red, the other white (red faces local +Z), a dark band at the brim.</summary>
        static Mesh HatMesh()
        {
            var mk = new TrailBuilder.MeshKit();
            const int seg = 32;
            (float y, float r)[] prof = { (0.34f, 0.04f), (0.3f, 0.1f), (0.2f, 0.15f), (0.08f, 0.17f), (0.02f, 0.18f), (0f, 0.18f) };
            Color red = new Color(0.78f, 0.08f, 0.06f), white = new Color(0.94f, 0.92f, 0.86f);
            for (var j = 0; j < seg; j++)
            {
                float a0 = j / (float)seg * Mathf.PI * 2f, a1 = (j + 1) / (float)seg * Mathf.PI * 2f;
                var mid = (a0 + a1) * 0.5f;
                var c = Mathf.Cos(mid) > 0f ? red : white; // the side toward +Z is red
                for (var k = 0; k + 1 < prof.Length; k++)
                {
                    Vector3 P(float a, int i) => new Vector3(Mathf.Sin(a) * prof[i].r, prof[i].y, Mathf.Cos(a) * prof[i].r);
                    var cc = k >= prof.Length - 3 ? new Color(0.12f, 0.08f, 0.06f) : c; // the brim band
                    mk.Quad(P(a0, k), P(a1, k), P(a1, k + 1), P(a0, k + 1), cc);
                }
                mk.Tri(new Vector3(0f, 0.34f, 0f), new Vector3(Mathf.Sin(a1) * 0.04f, 0.34f, Mathf.Cos(a1) * 0.04f), new Vector3(Mathf.Sin(a0) * 0.04f, 0.34f, Mathf.Cos(a0) * 0.04f), c, c, c);
            }
            return mk.Build("Eshu's Hat");
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

        static void Place(Village.Figure f, Vector3 at, Vector3 look)
        {
            if (f == null) return;
            f.Root.localPosition = at;
            var to = look - at; to.y = 0f;
            if (to.sqrMagnitude > 1e-4f) f.Root.localRotation = Quaternion.LookRotation(to.normalized);
            f.Home = at;
        }

        /// <summary>Where friend <paramref name="i"/> (0 shouts red) stands: left or right of the post (swapped in the last round).</summary>
        Vector3 Spot(int i, bool swapped) => (i == 0) != swapped ? FriendLeft : FriendRight;

        Transform _postT, _shrineT, _slabT;

        /// <summary>
        /// Lays the game out for the forest or your room (see <see cref="GameFit"/>). Can be called mid-game when you switch:
        /// the props move, the friends walk to their new spots, and the game carries on where it was.
        /// </summary>
        public void Fit(GameFit fit)
        {
            _fit = fit;
            if (_root == null) return;
            _postT.localPosition = Post; _shrineT.localPosition = Bowl; _slabT.localPosition = Slab;
            _postT.GetComponent<Renderer>().enabled = !_fit.Room; // in the room the hat floats over the flames on its own (its light stays)
            // in the room the hat floats where the panel used to be (over the fire, 1.15 m away): the panel comes nearer, above it
            var follow = _hud.GetComponent<FollowView>();
            follow.Distance = _fit.Room ? 0.6f : 1.15f; follow.Height = _fit.Room ? 0.28f : 0.4f;
            if (_eshu != null) Place(_eshu, EshuAt, Seat);
            if (_cup != null && _rules != null && _rules.Phase >= HatPhase.Drink) _cup.localPosition = CupSpot;
        }

        // ---------- the hat: which side faces whom ----------

        /// <summary>The hat's red side points along yaw <paramref name="yaw"/> (degrees, 0 = +Z). Does red face direction <paramref name="toward"/>? Pure, unit tested.</summary>
        public static bool RedFaces(float yaw, Vector3 toward) => Mathf.Abs(Mathf.DeltaAngle(yaw, Yaw(toward))) < 90f;

        /// <summary>How far (degrees) the colour facing <paramref name="toward"/> is from facing it squarely. Pure.</summary>
        public static float OffSquare(float yaw, Vector3 toward)
        {
            var d = Mathf.Abs(Mathf.DeltaAngle(yaw, Yaw(toward)));
            return Mathf.Min(d, 180f - d);
        }

        static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        // ---------- every frame ----------

        void Update()
        {
            if (!Active || _rules == null) return;
            var dt = Time.deltaTime;
            _rules.Tick(dt);
            if (Application.isEditor) EditorKeys();
            Hands(dt);
            Hat(dt);
            Friends(dt);
            Eshu(dt);
            Things(dt);
            Scene(dt);
            Hud();
        }

        void OnPhase(HatPhase p)
        {
            switch (p)
            {
                case HatPhase.Round:
                    for (var i = 0; i < 2; i++) _sitT[i] = -1f;
                    if (!_spinning) _returnTo = SideOn();
                    break;
                case HatPhase.Fight:
                    _poseUntil[0] = _poseUntil[1] = 0f;
                    _audio.Effect("shout"); _audio.Effect("fall"); _audio.Effect("drums");
                    Laugh();
                    break;
                case HatPhase.Swap: Laugh(); _audio.Effect("rattle"); break;
                case HatPhase.Cup:
                    _cup.gameObject.SetActive(true);
                    _cup.localRotation = Quaternion.identity;
                    _cup.localPosition = Slab + Vector3.up * (SlabTop + 0.05f);
                    break;
                case HatPhase.Drink:
                    _nextGulp = 1.7f;
                    Laugh();
                    break;
                case HatPhase.Offering:
                    _audio.Effect("cheer");
                    _cowrie.gameObject.SetActive(true);
                    _cowrie.localPosition = Slab + Vector3.up * (SlabTop + 0.03f);
                    Laugh();
                    break;
                case HatPhase.Won:
                    _camp.Village?.Celebrate(true);
                    _audio.Effect("rooster"); _audio.Effect("birds");
                    Laugh();
                    break;
            }
        }

        void Laugh()
        {
            _eshuLaugh = 0f;
            if (_eshu != null) _audio.EffectAt("laugh", _eshu.Head != null ? _eshu.Head.position : _eshu.Root.position + Vector3.up * 1.6f, 0.9f, 1f, 6f);
            else _audio.Effect("laugh");
        }

        // ---------- the editor: H shows the hat to whichever friend is pausing, C sends the cup, O the cowrie ----------

        void EditorKeys()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.H)) AutoShow();
            if (UnityEngine.Input.GetKeyDown(KeyCode.C)) AutoSend(true);
            if (UnityEngine.Input.GetKeyDown(KeyCode.O)) AutoSend(false);
        }

        /// <summary>Turns the hat's other side to the friend listening now (the editor's stand-in for a hand); it counts once held.</summary>
        public void AutoShow()
        {
            var i = _rules.Listener;
            if (i < 0 || _friends[i] == null) return;
            var to = _friends[i].Root.localPosition - _hat.localPosition;
            _returnTo = null; _holdUntil = 0f;
            _yaw = Yaw(to) + (HatRules.ShoutsRed(i) ? 180f : 0f);
        }

        /// <summary>Sets the calabash on the post, or sends the cowrie on its way, as if flicked.</summary>
        public void AutoSend(bool cup)
        {
            var t = cup ? _cup : _cowrie;
            if (!t.gameObject.activeSelf) return;
            if (cup) PlaceCup();
            else Launch(t, false);
        }

        // ---------- hands: spin the hat, take the cup or the cowrie and flick ----------

        void Hands(float dt)
        {
            var input = HypaInput.Current;
            var pinch = input.IsPinching;
            if (!input.TryGetRay(out var ray)) { _wasPinching = pinch; return; }
            var mouse = HypaInput.AimsWithMouse(input);
            var hold = ray.origin + ray.direction * (mouse ? 0.7f : 0.06f);
            var cam = Camera.main != null ? Camera.main.transform : null;
            var slide = cam != null ? Vector3.Dot(hold, cam.right) : hold.x;
            Track(hold, dt);

            if (pinch && !_wasPinching && !DodoGame.OnPanel()) // a pinch on a panel button is for the button, not the hat or the cup
            {
                // the cup or the cowrie, if one is waiting; else the hat
                var things = new List<Vector3>(); var ok = new List<bool>();
                things.Add(_cup.position); ok.Add(_rules.Phase == HatPhase.Cup && _cup.gameObject.activeSelf && _flying != _cup);
                things.Add(_cowrie.position); ok.Add(_rules.Phase == HatPhase.Offering && _cowrie.gameObject.activeSelf && _flying != _cowrie);
                var pick = DodoGame.PickStone(ray, hold, things, ok);
                if (pick >= 0) { _held = pick == 0 ? _cup : _cowrie; _audio.Tap(); }
                else if (CanSpin() && (Vector3.Angle(ray.direction, _hat.position - ray.origin) < 32f || Vector3.Distance(hold, _hat.position) < 0.35f))
                {
                    _spinning = true; _prevSlide = slide; _stareAt = -1; _returnTo = null; _holdUntil = 0f; _audio.Tap();
                }
            }
            if (_spinning && pinch)
            {
                // like a lazy Susan: slide your hand right and the near side swings right
                _yaw -= (slide - _prevSlide) * 420f;
                _prevSlide = slide;
                _yawLog.Enqueue((Time.time, _yaw));
                while (_yawLog.Count > 0 && _yawLog.Peek().t < Time.time - 0.3f) _yawLog.Dequeue();
            }
            if (_held != null && pinch)
            {
                if (_held == _cup) CarryCup(ray, dt);
                else _held.position = Vector3.Lerp(_held.position, hold, 1f - Mathf.Exp(-20f * dt));
            }
            if (!pinch && _wasPinching)
            {
                if (_spinning) { _spinning = false; Released(); }
                if (_held != null)
                {
                    var t = _held; _held = null;
                    if (t == _cup)
                    {
                        // letting go near the post sets the bowl down there; a flick is what used to throw it behind the friends
                        if (Vector3.Distance(t.position, _root.TransformPoint(CupSpot)) < 0.34f) PlaceCup();
                        else Return(t);
                    }
                    else
                    {
                        var aim = Vector3.Angle(ray.direction, BowlTarget - ray.origin);
                        if (DodoGame.Flicked(HandSpeed(), aim, false)) Launch(t, false);
                        else Return(t);
                    }
                }
            }
            _wasPinching = pinch;
        }

        readonly Queue<(float t, float yaw)> _yawLog = new Queue<(float, float)>();

        /// <summary>
        /// You let the hat go: undo the last ~0.12 s (opening your fingers drags it), and if the colour he still has to see is
        /// anywhere near the listening friend, lock it squarely onto him so it stays there while it counts.
        /// </summary>
        void Released()
        {
            foreach (var (t, y) in _yawLog) if (t <= Time.time - 0.12f) _yaw = y;
            _yawLog.Clear();
            var i = _rules.Listener;
            if (i < 0 || _friends[i] == null) return;
            var to = _friends[i].Root.localPosition - _hat.localPosition; to.y = 0f;
            var target = Yaw(to) + (HatRules.ShoutsRed(i) ? 180f : 0f); // the hat's red side points at yaw: white to him means red away
            if (Mathf.Abs(Mathf.DeltaAngle(_yaw, target)) <= Snap) _yaw = target;
        }

        readonly Queue<(Vector3 p, float dt)> _track = new Queue<(Vector3, float)>();
        void Track(Vector3 p, float dt)
        {
            if (dt <= 0f) return;
            _track.Enqueue((p, dt));
            while (_track.Count > 6) _track.Dequeue();
        }

        float HandSpeed()
        {
            Vector3? first = null; var last = Vector3.zero; var total = 0f;
            foreach (var (p, dt) in _track) { if (first == null) first = p; last = p; total += dt; }
            return first != null && total > 1e-3f ? (last - first.Value).magnitude / total : 0f;
        }

        bool CanSpin() => _rules.Phase == HatPhase.Round || _rules.Phase == HatPhase.Intro;

        /// <summary>
        /// Every frame in a round: which colour squarely faces the listening friend. Held long enough, the other colour is seen
        /// (the hat snaps to him, holds while he stares, then turns itself back side-on); his own colour makes him shout.
        /// </summary>
        void Facing(float dt)
        {
            var i = _rules.Listener;
            if (i < 0 || _friends[i] == null || _returnTo != null || Time.time < _holdUntil) { _rules.Face(null, 0f); return; }
            var to = _friends[i].Root.localPosition - _hat.localPosition; to.y = 0f;
            bool? red = OffSquare(_yaw, to) <= Snap ? RedFaces(_yaw, to) : (bool?)null;
            var result = _rules.Face(red, dt, _spinning);
            if (result == ShowResult.Seen)
            {
                _yaw = Yaw(to) + (red == true ? 0f : 180f); // squarely to him
                _spinning = false;
                _holdUntil = Time.time + HatRules.StareTime;
                _returnTo = SideOn();
                _stareAt = i;
                _sitT[i] = 0f;
                _poseUntil[i] = Time.time + HatRules.StareTime;
                _spirit = 1f;
                _audio.Effect("sparks");
                _audio.EffectAt("rattle", _hat.position, 0.6f);
                _audio.EffectAt("laugh", _friends[i].Root.position + Vector3.up * 1.6f, 0.7f, i == 0 ? 0.9f : 1.1f, 5f);
            }
            else if (result == ShowResult.Wrong)
            {
                _audio.EffectAt("shout", _friends[i].Root.position + Vector3.up * 1.6f, 0.95f, i == 0 ? 0.95f : 1.1f, 5f);
                Laugh();
            }
        }

        /// <summary>Side-on to both friends: one colour toward you, the other toward the fire (whichever is nearer).</summary>
        float SideOn() => Mathf.Abs(Mathf.DeltaAngle(_yaw, 180f)) < 90f ? 180f : 0f;

        void Hat(float dt)
        {
            var p = _rules.Phase;
            if (_returnTo != null && !_spinning && Time.time > _holdUntil)
            {
                _yaw = Mathf.MoveTowardsAngle(_yaw, _returnTo.Value, ReturnSpeed * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, _returnTo.Value)) < 0.5f) _returnTo = null;
            }
            else if (!_spinning && p != HatPhase.Round && Time.time > _holdUntil) _yaw += IdleSpin * dt * (p == HatPhase.Fight ? 4f : 1f); // in a round it stays where you leave it
            if (p == HatPhase.Round) Facing(dt);
            var bob = Mathf.Sin(Time.time * 1.4f) * 0.035f;
            var away = p == HatPhase.Cup || p == HatPhase.Drink || p == HatPhase.Offering || p == HatPhase.Won;
            _hat.localPosition = Post + Vector3.up * (PostTop + HatFloat + bob + (away ? 0.3f : 0f) - (_fit.Room ? 0.3f : 0f)); // in the room: lower, under the nearer panel
            _hat.localRotation = Quaternion.Slerp(_hat.localRotation, Quaternion.Euler(0f, _yaw, 0f), 1f - Mathf.Exp(-14f * dt));
        }

        /// <summary>Turns a head toward the hat, in the torso's space (the head's own parent), so a lean doesn't throw the look off.</summary>
        void LookAtHat(Village.Figure f, float dt, float pitchAdd = 0f, float yawLimit = 35f)
        {
            if (f.Head == null || f.Torso == null) return;
            var hl = f.Torso.InverseTransformPoint(_hat.position) - f.Head.localPosition;
            var yaw = Mathf.Clamp(Mathf.Atan2(hl.x, Mathf.Max(0.05f, hl.z)) * Mathf.Rad2Deg, -yawLimit, yawLimit);
            var pitch = Mathf.Clamp(Mathf.Atan2(-hl.y, Mathf.Max(0.05f, new Vector2(hl.x, hl.z).magnitude)) * Mathf.Rad2Deg + pitchAdd, -30f, 24f);
            f.Head.localRotation = Quaternion.Slerp(f.Head.localRotation, Quaternion.Euler(pitch, yaw, 0f), 1f - Mathf.Exp(-4f * dt));
        }

        // ---------- the friends ----------

        void Friends(float dt)
        {
            var p = _rules.Phase; var T = _rules.T;
            var drinking = p == HatPhase.Drink || p == HatPhase.Offering || p == HatPhase.Won;
            if (drinking && p == HatPhase.Drink && _nextGulp > 0f && T >= _nextGulp && _cup != null)
            {
                _audio.EffectAt("gulp", _cup.position, 0.85f, _nextGulp < 3f ? 0.95f : 1.05f, 4f);
                _nextGulp = _nextGulp < 3f ? 3.1f : 0f;
            }
            for (var i = 0; i < 2; i++)
            {
                var f = _friends[i];
                if (f == null) continue;
                var spot = Spot(i, _rules.Swapped);
                var t = Time.time + f.Ph;
                var pausing = _rules.Listening(i);
                var seen = p == HatPhase.Round && _rules.Seen(i);

                // where he stands: his spot; two lanes round the post when they swap (not through the hat); at each other in a fight
                Vector3 at;
                if (p == HatPhase.Swap)
                {
                    var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((T - 0.5f) / 2.2f));
                    var from = Spot(i, false); var to = Spot(i, true);
                    // two lanes so they pass each other: one just behind the post (still clear of the fire's stones, ~1 m out), one just in front
                    Vector3 mid;
                    if (_fit.Room) mid = new Vector3(0f, 0f, FarStones + (i == 0 ? 0.4f : 0.8f)); // round the far side of the stones, behind Eshu
                    else
                    {
                        var behind = Post.z + 0.4f < -0.95f; // room between the post and the fire's stones for a lane
                        mid = new Vector3(0f, 0f, i == 0 ? (behind ? Post.z + 0.4f : Post.z - 0.35f) : (behind ? Post.z - 0.4f : Post.z - 0.8f));
                    }
                    at = u < 0.5f ? Vector3.Lerp(from, mid, u * 2f) : Vector3.Lerp(mid, to, (u - 0.5f) * 2f);
                }
                else if (p == HatPhase.Fight && T < HatRules.FightTime - 0.8f && !_fit.Room) at = Vector3.Lerp(spot, new Vector3(spot.x * 0.35f, 0f, spot.z + 0.35f), Mathf.Clamp01(T * 2f));
                else if (drinking)
                {
                    var u = p == HatPhase.Drink ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(T / 1.4f)) : 1f;
                    var side = spot.x < 0f ? -1f : 1f;
                    at = Vector3.Lerp(spot, Post + new Vector3(side * (_fit.Room ? GameFit.StoneEdge + 0.4f : 0.78f), 0f, 0.04f), u); // in from either side, far enough that their heads don't meet over the bowl
                }
                else at = spot;
                var holding = p == HatPhase.Round && Time.time < _poseUntil[i];
                seen = holding || seen;
                var sitting = seen;
                if (seen && _sitT[i] >= 0f) _sitT[i] += dt;
                // seen: he steps back half a pace (never sinks: the figures have no knees, so lowering them looks like shrinking)
                var back = sitting ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(((_sitT[i] >= 0f ? _sitT[i] : 2f) - 0.9f) / 0.7f)) : 0f;
                var away = at - Post; away.y = 0f;
                f.Root.localPosition = Vector3.Lerp(f.Root.localPosition, at + away.normalized * 0.3f * back, 1f - Mathf.Exp(-6f * dt));
                var look = p == HatPhase.Fight ? Spot(1 - i, _rules.Swapped) : Post;
                var dir = look - f.Root.localPosition; dir.y = 0f;
                if (dir.sqrMagnitude > 1e-3f) f.Root.localRotation = Quaternion.Slerp(f.Root.localRotation, Quaternion.LookRotation(dir.normalized), 1f - Mathf.Exp(-6f * dt));

                var bend = !drinking ? 0f : p == HatPhase.Drink ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((T - 1.05f) / 0.7f)) : 1f;
                if (drinking)
                {
                    // no knees, so he leans over the bowl and cups both hands down into the wine
                    var dip = 0.5f + 0.5f * Mathf.Sin(t * 2.3f);
                    f.Torso.localRotation = Quaternion.Euler(18f + bend * 36f + dip * 5f * bend, 0f, 0f);
                    f.ArmL.localRotation = Quaternion.Euler(-25f - bend * 70f - dip * 10f * bend, bend * 16f, -6f);
                    f.ArmR.localRotation = Quaternion.Euler(-25f - bend * 70f - dip * 10f * bend, -bend * 16f, 6f);
                    f.ForeL.localRotation = f.ForeR.localRotation = Quaternion.Euler(-15f - bend * 62f, 0f, 0f);
                }
                else if (p == HatPhase.Fight)
                {
                    var flail = Mathf.Sin(t * 14f) * 60f;
                    f.Torso.localRotation = Quaternion.Euler(18f, Mathf.Sin(t * 7f) * 20f, 0f);
                    f.ArmL.localRotation = Quaternion.Euler(-80f + flail, 0f, -20f); f.ArmR.localRotation = Quaternion.Euler(-80f - flail, 0f, 20f);
                    f.ForeL.localRotation = f.ForeR.localRotation = Quaternion.Euler(-40f, 0f, 0f);
                }
                else if (sitting)
                {
                    // standing back, staring, then laughing (rocking, a hand to the head)
                    var laugh = Mathf.Clamp01((_sitT[i] - 1.6f) / 0.5f);
                    f.Torso.localRotation = Quaternion.Euler(-6f + Mathf.Sin(t * 9f) * 6f * laugh, 0f, 0f);
                    f.ArmL.localRotation = Quaternion.Euler(-30f, 0f, -10f);
                    f.ArmR.localRotation = Quaternion.Euler(-30f - 100f * laugh, 0f, 15f);
                    f.ForeL.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                    f.ForeR.localRotation = Quaternion.Euler(-60f * laugh, 0f, 0f);
                }
                else if (pausing)
                {
                    // listening: still, hands on hips, watching the hat (an angry shake of the head if it shows him his own colour)
                    var cross = _rules.HoldingWrong ? _rules.Hold : 0f;
                    f.Torso.localRotation = Quaternion.Euler(-3f, Mathf.Sin(t * 14f) * 8f * cross, 0f);
                    f.ArmL.localRotation = Quaternion.Euler(-12f, 0f, -38f); f.ArmR.localRotation = Quaternion.Euler(-12f, 0f, 38f);
                    f.ForeL.localRotation = f.ForeR.localRotation = Quaternion.Euler(-95f, 0f, 0f);
                }
                else if (p == HatPhase.Swap)
                {
                    f.Torso.localRotation = Quaternion.Euler(6f, 0f, 0f);
                    f.ArmL.localRotation = Quaternion.Euler(-20f, 0f, -12f); f.ArmR.localRotation = Quaternion.Euler(-20f, 0f, 12f);
                    f.ForeL.localRotation = f.ForeR.localRotation = Quaternion.Euler(-15f, 0f, 0f);
                }
                else
                {
                    // shouting his colour at the hat, an arm jabbing at it
                    var jab = Mathf.Abs(Mathf.Sin(t * 4.5f));
                    f.Torso.localRotation = Quaternion.Euler(4f - jab * 6f, Mathf.Sin(t * 2f) * 8f, 0f);
                    f.ArmR.localRotation = Quaternion.Euler(-70f - jab * 30f, 0f, 10f);
                    f.ForeR.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                    f.ArmL.localRotation = Quaternion.Euler(-10f, 0f, -15f - jab * 20f);
                    f.ForeL.localRotation = Quaternion.Euler(-60f, 0f, 0f);
                    if (p == HatPhase.Round && _rules.Listener >= 0 && Time.time > _nextShout[i])
                    {
                        _nextShout[i] = Time.time + 3.2f + UnityEngine.Random.value * 1.6f;
                        _audio.EffectAt("shout", f.Root.position + Vector3.up * 1.6f, 0.55f, i == 0 ? 0.95f : 1.12f, 4f);
                    }
                }
                if (f.Head != null && drinking && bend > 0.4f)
                    f.Head.localRotation = Quaternion.Slerp(f.Head.localRotation, Quaternion.Euler(24f, 0f, 0f), 1f - Mathf.Exp(-4f * dt));
                else if (f.Head != null && p != HatPhase.Fight) LookAtHat(f, dt);

                // a glow at the listener's feet
                _ring[i].localPosition = new Vector3(f.Root.localPosition.x, 0.03f, f.Root.localPosition.z);
                _ring[i].localScale = Vector3.one * 0.8f;
                _ringMat[i].SetFloat("_Opacity", pausing ? 0.4f + 0.12f * Mathf.Sin(Time.time * 4f) : 0f);
                UpdateBadge(i, f, pausing);
            }
        }

        void UpdateBadge(int i, Village.Figure f, bool listening)
        {
            var b = _badges[i];
            var p = _rules.Phase;
            var show = p == HatPhase.Intro || p == HatPhase.Round || p == HatPhase.Swap || p == HatPhase.Fight;
            b.Root.gameObject.SetActive(show);
            if (!show) return;
            var head = f.Head != null ? f.Head.position : f.Root.position + Vector3.up * 1.6f;
            b.Root.position = head + Vector3.up * 0.34f;
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null) b.Root.rotation = Quaternion.LookRotation(b.Root.position - cam.position);
            var seen = p == HatPhase.Round && _rules.Seen(i);
            var pulse = listening ? 1.45f + 0.07f * Mathf.Sin(Time.time * 5f) : 1.1f;
            b.Root.localScale = Vector3.one * pulse;
            b.Glow.SetFloat("_Opacity", listening ? 0.6f : 0f);
            // the ring behind the badge fills with the colour facing him as it counts
            var hold = listening ? _rules.Hold : 0f;
            b.Fill.gameObject.SetActive(hold > 0.02f);
            if (hold > 0.02f)
            {
                var c = _rules.HoldingWrong == HatRules.ShoutsRed(i) ? Red : White;
                b.Fill.Top = b.Fill.Bottom = c;
                var d = 0.12f + 0.07f * hold;
                b.Fill.Radius = d * 0.5f;
                b.Fill.Resize(new Vector2(d, d));
            }
            var border = listening ? KitTheme.Ember : KitTheme.Hairline;
            if (b.Disc.Border != border) { b.Disc.Border = border; b.Disc.Apply(); }
            b.Inner.gameObject.SetActive(seen);
            b.Check.gameObject.SetActive(seen);
        }

        void Eshu(float dt)
        {
            if (_eshu == null) return;
            var t = Time.time;
            var laughing = _eshuLaugh >= 0f && _eshuLaugh < 2.2f;
            if (_eshuLaugh >= 0f) _eshuLaugh += dt;
            _eshu.Torso.localRotation = laughing
                ? Quaternion.Euler(-10f + Mathf.Sin(t * 16f) * 5f, 0f, Mathf.Sin(t * 7f) * 4f)   // head back, shaking with laughter
                : Quaternion.Euler(2f + Mathf.Sin(t * 1.1f) * 1.5f, Mathf.Sin(t * 0.4f) * 6f, 0f);
            _eshu.ArmL.localRotation = laughing ? Quaternion.Euler(-40f, 0f, -30f) : Quaternion.Euler(-8f, 0f, -8f);
            _eshu.ForeL.localRotation = laughing ? Quaternion.Euler(-70f, 0f, 0f) : Quaternion.Euler(-15f, 0f, 0f);
            if (_eshu.Head != null) LookAtHat(_eshu, dt, laughing ? -12f : 0f, 50f);
            var blink = Mathf.Repeat(t + 0.7f, 4.1f) < 0.12f;
            if (_eshu.EyeL != null) _eshu.EyeL.localScale = _eshu.EyeR.localScale = new Vector3(1f, blink ? 0.12f : 1f, 1f);
        }

        // ---------- the cup and the cowrie ----------

        Vector3 BowlTarget => _camp.transform.TransformPoint(Bowl + Vector3.up * 0.64f);

        /// <summary>
        /// The calabash stays in front of your hand. Point it at the post and it slides onto the platform under the hat,
        /// where you can still see the wine. Letting go anywhere else puts it back on the slab.
        /// </summary>
        void CarryCup(Ray ray, float dt)
        {
            var spot = _root.TransformPoint(CupSpot);
            var to = spot - ray.origin;
            var aim = Vector3.Angle(ray.direction, to);
            var pull = Mathf.Clamp01(1f - (aim - 8f) / 22f);
            var hand = ray.origin + ray.direction * (HypaInput.AimsWithMouse(HypaInput.Current) ? 0.7f : 0.35f);
            var want = Vector3.Lerp(hand, spot, pull * pull);
            _cup.position = Vector3.Lerp(_cup.position, want, 1f - Mathf.Exp(-14f * dt));
            _cup.rotation = Quaternion.Slerp(_cup.rotation, _root.rotation * CupSit, 1f - Mathf.Exp(-10f * dt));
        }

        void PlaceCup()
        {
            _cup.localRotation = CupSit;
            _cup.localPosition = CupSpot;
            if (_rules.CupLanded()) _audio.EffectAt("clink", _cup.position, 0.7f, 1f, 4f);
        }

        void Launch(Transform t, bool cup)
        {
            _flying = t; _flyingCup = cup; _flyT = 0f;
            _flyTo = BowlTarget;
            var time = Mathf.Clamp(Vector3.Distance(t.position, _flyTo) * 0.35f, 0.45f, 0.9f);
            _vel = (_flyTo - t.position - 0.5f * new Vector3(0f, -7f, 0f) * time * time) / time;
            _audio.Effect("throw");
        }

        void Return(Transform t)
        {
            t.localRotation = Quaternion.identity;
            t.localPosition = Slab + Vector3.up * (SlabTop + (t == _cup ? 0.05f : 0.03f));
        }

        void Things(float dt)
        {
            var bob = Mathf.Sin(Time.time * 3f) * 0.01f;
            if (_cup.gameObject.activeSelf && _held != _cup && _flying != _cup && _rules.Phase == HatPhase.Cup) _cup.localPosition = Slab + Vector3.up * (SlabTop + 0.05f + bob);
            if (_cowrie.gameObject.activeSelf && _held != _cowrie && _flying != _cowrie && _rules.Phase == HatPhase.Offering) _cowrie.localPosition = Slab + Vector3.up * (SlabTop + 0.03f + bob);
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null && _cowrieHalo != null) _cowrieHalo.rotation = Quaternion.LookRotation(_cowrieHalo.position - cam.position);
            var wet = _rules.Phase == HatPhase.Cup || _rules.Phase == HatPhase.Drink || _rules.Phase == HatPhase.Offering || _rules.Phase == HatPhase.Won;
            _cupGlow.SetFloat("_Opacity", wet ? 0.62f + 0.16f * Mathf.Sin(Time.time * 2.2f) : 0f);
            _cowrieGlow.SetFloat("_Opacity", _rules.Phase == HatPhase.Offering ? 0.45f + 0.15f * Mathf.Sin(Time.time * 4f) : 0f);

            if (_flying == null) return;
            _flyT += dt;
            _vel += Vector3.down * 7f * dt;
            var want = (_flyTo - _flying.position).normalized * Mathf.Max(_vel.magnitude, 4f);
            _vel = Vector3.Lerp(_vel, want, 1f - Mathf.Exp(-5f * dt)); // guided all the way
            _flying.position += _vel * dt;
            if (Vector3.Distance(_flying.position, _flyTo) < 0.18f || _flyT > 2.5f)
            {
                _flying.position = _flyTo;
                if (_flyingCup) { if (_rules.CupLanded()) _audio.Effect("sizzle"); }
                else if (_rules.OfferingLeft()) { _audio.EffectAt("rattle", _flyTo, 0.8f); _audio.Effect("sparks"); }
                _flying = null;
            }
        }

        // ---------- the shrine's night ----------

        void Scene(float dt)
        {
            var p = _rules.Phase;
            _spirit = Mathf.MoveTowards(_spirit, 0.22f, dt * 0.6f);
            _dawn = Mathf.MoveTowards(_dawn, p == HatPhase.Won ? 0.75f : p == HatPhase.Offering || p == HatPhase.Drink ? 0.3f : 0f, dt * 0.15f);
            var fight = p == HatPhase.Fight ? 1f : 0f;
            _camp.SetStory(new StorySample
            {
                Fire = 1.3f + fight * 0.4f, Dark = (0.3f + fight * 0.15f) * (1f - _dawn), Spirit = p == HatPhase.Won ? 0f : _spirit, Dawn = _dawn,
                Wind = 0.12f + fight * 0.5f, Index = -1,
            });
        }

        // ---------- the HUD ----------

        void BuildHud(Transform uiRoot)
        {
            _hud = new GameObject("Hat HUD").transform;
            _hud.SetParent(uiRoot, false);
            var f = _hud.gameObject.AddComponent<FollowView>();
            f.Distance = 1.15f; f.Height = 0.4f; f.YawOffset = 0f; f.Threshold = 55f; // up in the middle, above the hat: clear of the friends' badges either side
            Kit.Glass(_hud, "Panel", new Vector3(0f, 0f, 0.006f), new Vector2(0.5f, 0.15f), 0.04f);
            _title = Kit.Text(_hud, "", 0.028f, new Vector3(0f, 0.035f, 0f), new Vector2(0.46f, 0.045f), KitTheme.Paper, TextAlignmentOptions.Center, KitFont.Serif);
            _sub = Kit.Text(_hud, "", 0.016f, new Vector3(0f, -0.004f, 0f), new Vector2(0.46f, 0.04f), KitTheme.Dim);
            // the quarrel: a bar that fills
            _meterTrack = Kit.Panel(_hud, "Quarrel", new Vector3(0.06f, -0.04f, 0.002f), new Vector2(0.26f, 0.012f), 0.006f,
                new Color(1f, 1f, 1f, 0.12f), new Color(1f, 1f, 1f, 0.12f), new Color(0f, 0f, 0f, 0f), 11, 0f).transform;
            _meter = Kit.Panel(_hud, "Quarrel Fill", new Vector3(0.06f, -0.04f, 0f), new Vector2(0.26f, 0.012f), 0.006f,
                new Color(0.95f, 0.3f, 0.15f), new Color(0.8f, 0.15f, 0.08f), new Color(0f, 0f, 0f, 0f), 12, 0f);
            _meterFill = _meter.transform;
            _meterLabel = Kit.Text(_hud, "Quarrel", 0.012f, new Vector3(-0.14f, -0.04f, 0f), new Vector2(0.1f, 0.02f), KitTheme.Dim, TextAlignmentOptions.Right).transform;
            _quit = Kit.Button(_hud, new Vector3(-0.2f, 0.055f, 0f), new Vector2(0.09f, 0.038f), "Back", "arrow-left", KitStyle.Ghost, () => { End(); _return?.Invoke(); }, null, 0.015f);
            if (_toggleRoom != null)
            {
                _room = Kit.Button(_hud, new Vector3(0f, -0.115f, 0f), new Vector2(0.28f, 0.04f), "Back to the forest", "house", KitStyle.Ghost, _toggleRoom, null, 0.014f);
                _room.SetLabel("In my room");
            }
            _retry = Kit.Button(_hud, new Vector3(-0.15f, -0.048f, 0f), new Vector2(0.14f, 0.046f), "Play again", "rotate-ccw", KitStyle.Primary, () => { End(); Begin(); });
            _restart = Kit.Button(_hud, new Vector3(0f, -0.048f, 0f), new Vector2(0.14f, 0.046f), "The story", "play", KitStyle.Ghost, () => { End(); _restartStory?.Invoke(); });
            _leave = Kit.Button(_hud, new Vector3(0.15f, -0.048f, 0f), new Vector2(0.14f, 0.046f), "Stories", "arrow-left", KitStyle.Ghost, () => { End(); _back?.Invoke(); });
            _hud.gameObject.AddComponent<KitGroup>().Refresh();
            _hud.gameObject.SetActive(false);
        }

        void Hud()
        {
            var p = _rules.Phase;
            string title, sub;
            switch (p)
            {
                case HatPhase.Intro: title = "Eshu's hat"; sub = "Pinch the hat and slide your hand to spin it. Show each friend the colour on his badge."; break;
                case HatPhase.Round:
                    title = $"Round {_rules.Round + 1} of {HatRules.Rounds}";
                    var l = _rules.Listener;
                    if (l >= 0)
                    {
                        var side = Spot(l, _rules.Swapped).x < 0f ? "left" : "right";
                        string own = HatRules.ShoutsRed(l) ? "red" : "white", other = HatRules.ShoutsRed(l) ? "white" : "red";
                        _roundSub = $"The {side} friend has only seen {own}. Turn the {other} side of the hat to him (it matches his badge) and hold it.";
                    }
                    sub = _roundSub; // kept while the hat turns back, so the words don't flicker
                    break;
                case HatPhase.Fight: title = "They're fighting!"; sub = "The quarrel boiled over. Eshu laughs. Try the round again."; break;
                case HatPhase.Swap: title = "Eshu's trick"; sub = "The friends swap sides of the road. Watch the badges."; break;
                case HatPhase.Cup: title = "One cup"; sub = "Pinch the calabash and set it on the post, under the hat."; break;
                case HatPhase.Drink: title = "Drink"; sub = "They bend and drink from the one cup, with both hands."; break;
                case HatPhase.Offering: title = "For Eshu"; sub = "Pinch the cowrie and flick it into the bowl at the crossroads."; break;
                default: title = "Both sides seen"; sub = "Two friends, one road, one cup. Eshu laughs, softly."; break;
            }
            _title.text = title; _sub.text = sub;
            var q = Mathf.Clamp01(_rules.Quarrel);
            var playing = p == HatPhase.Round || p == HatPhase.Fight || p == HatPhase.Swap;
            _meter.gameObject.SetActive(playing && q > 0.02f);
            _meterTrack.gameObject.SetActive(playing); _meterLabel.gameObject.SetActive(playing);
            if (Mathf.Abs(_meter.Size.x - 0.26f * q) > 0.002f) _meter.Resize(new Vector2(Mathf.Max(0.012f, 0.26f * q), 0.012f));
            _meterFill.localPosition = new Vector3(0.06f - 0.13f * (1f - q), -0.04f, 0f);
            var over = _rules.Over && _rules.T > 3.5f;
            _retry.gameObject.SetActive(over); _restart.gameObject.SetActive(over); _leave.gameObject.SetActive(over);
            _quit.gameObject.SetActive(!over);
        }

        /// <summary>The same room switch as the story: the fire, the hat and the friends in your real room.</summary>
        public void SetInRoom(bool inRoom)
        {
            if (_room == null) return;
            _room.SetLabel(inRoom ? "Back to the forest" : "In my room");
            _room.Style = inRoom ? KitStyle.Primary : KitStyle.Ghost;
            _room.Selected = inRoom;
        }
    }
}
