using System;
using System.Collections.Generic;
using HypaCore;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The playable scene after The King's Magic Drum, played from your seat: you have the king's drums, a big low drum on your
    /// left and a small high one on your right, waist high in front of you. They remember the king's beats. For each dish the
    /// ogene bell counts a bar in and the drums play a one-bar beat by themselves (each skin lights as it sounds); the bell and a
    /// shaker keep the groove while you play it back: a hand down on the big drum for the low note, on the small one for the
    /// high note (the edge of either gives a sharper slap, still that drum). Each beat played back brings a dish of the feast by
    /// the fire, in the story's order: fish, fufu, yams, okra, palm-oil soup, palm wine. A wrong beat makes the guests laugh and
    /// the drums play it again. The last dish brings dawn, and the town comes out of the trees to eat and dance.
    ///
    /// Then the witch song (<see cref="StoryAssets.witchSong"/>, `Art/Audio/witch-song.mp3`)
    /// plays and you drum freely along with it, nothing counted; it fades out over its last seconds. <b>Play again</b> restarts
    /// the game. Without the recording the free drumming still works.
    ///
    /// The bell, the shaker and the drums' own strokes are scheduled on the audio clock (<see cref="AudioSettings.dspTime"/>),
    /// so the groove holds whatever the frame rate; the skins light when their sound plays. Your strokes sound at once.
    /// Hands first (a palm through the skin, from <see cref="StoryInput.Hand"/>), controllers the same way; pinching while
    /// pointing at a skin also strikes it (the editor, and anyone who'd rather point). The rules are <see cref="DrumRules"/>.
    /// </summary>
    public sealed class DrumGame : MonoBehaviour
    {
        /// <summary>The middle of a skin is inside this share of its radius; outside is the edge (the slap).</summary>
        public const float RimFrom = 0.62f;
        /// <summary>A hand re-arms this far above a skin; a strike must come down at least this fast, within this much of its edge.</summary>
        public const float ReArm = 0.03f, MinSpeed = 0.3f, Margin = 0.04f;
        const float Hip = 0.18f, RoomDrum = 0.85f;
        const float Reach = 0.42f, DishRing = 1.3f, DishScale = 1.4f, Lookahead = 0.12f;
        /// <summary>Round the fire, from your left to your right across it (degrees from the far side), clear of your seat.</summary>
        static readonly float[] DishAngles = { -112f, -72f, -34f, 34f, 72f, 112f };
        static readonly Color LowCol = new Color(1f, 0.52f, 0.18f), HighCol = new Color(1f, 0.88f, 0.5f);

        /// <summary>One of the two drums: where it stands (beside the other), how big, how high its skin sits above the low one's.</summary>
        sealed class Drum
        {
            public Tone Tone;
            public float R, Lift, Side;
            public Color Col;
            public Transform Root, Skin;
            public MeshFilter Body;
            public Material Mid, Edge;
            public float MidLit, EdgeLit, Bounce, BuiltY = -1f;
            public AudioClip[] Open, Slap; // two takes of each, taken in turn
            public int Take;
        }

        CampWorld _camp;
        StoryAssets _a;
        StoryAudio _audio;
        Texture2D _glow;
        Action _restartStory, _back, _return, _toggleRoom;

        DrumRules _rules;
        Transform _root, _rig;
        Material _vc, _wood, _hide;
        float _skinY = 0.8f;
        readonly Drum[] _drums = new Drum[2];

        // the feast
        readonly Transform[] _dishes = new Transform[6];
        readonly Material[] _haloM = new Material[6];
        readonly float[] _dishT = new float[6];

        // hands: where each was last frame, and whether it's lifted (armed) over each drum
        readonly Vector3[] _prev = new Vector3[2];
        readonly bool[] _had = new bool[2];
        readonly bool[,] _armed = new bool[2, 2];
        float _lastStrike = -10f;
        bool _wasPinching;
        readonly List<(float at, Tone tone)> _auto = new List<(float, Tone)>();

        // the groove on the audio clock: the bar line the call started on, the next step and call stroke to schedule
        double _grid;
        int _nextStep, _nextCall, _called;
        DrumPhase _was;
        readonly List<(double at, int drum, bool slap, bool call)> _lights = new List<(double, int, bool, bool)>();
        readonly List<(double at, int beat)> _ticks = new List<(double, int)>();
        readonly AudioSource[] _src = new AudioSource[18];
        int _srcNext;
        AudioClip[] _ogene, _shaker;
        readonly float[] _tickLit = new float[4];

        float _flare, _gloom, _dawn;

        // HUD
        Transform _hud;
        TextMeshPro _title, _sub, _count;
        readonly List<KitPanel> _pips = new List<KitPanel>();
        readonly KitPanel[] _beatTicks = new KitPanel[4];
        KitButton _retry, _restart, _leave, _quit, _room;
        GameFit _fit = GameFit.Forest;

        // the witch song after the feast: you drum along freely
        const float SongDelay = 2f, SongFadeIn = 1.5f, SongFadeOut = 14f, SongLevel = 0.9f;
        AudioSource _song;
        bool _songOn;
        double _songAt;

        public bool Active { get; private set; }
        public DrumPhase Phase => _rules?.Phase ?? DrumPhase.Intro;

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
            _root.gameObject.SetActive(true);
            Active = true;
            Fit(_fit);
            _rules = new DrumRules();
            _rules.Changed += OnPhase;
            _was = DrumPhase.Intro;
            _flare = _gloom = _dawn = 0f;
            _auto.Clear(); _lights.Clear(); _ticks.Clear();
            _nextStep = _nextCall = _called = 0;
            for (var i = 0; i < 2; i++) { _had[i] = false; _armed[i, 0] = _armed[i, 1] = false; }
            foreach (var d in _drums) d.MidLit = d.EdgeLit = d.Bounce = 0f;
            for (var i = 0; i < _dishes.Length; i++) { _dishes[i].gameObject.SetActive(false); _dishT[i] = -1f; }
            _audio.Prepare(new[] { "laugh", "cheer", "sparks", "rooster", "birds" });
            StopSong();
            _camp.Village?.Celebrate(false);
            _hud.gameObject.SetActive(true);
            _hud.GetComponent<FollowView>().Snap();
        }

        public void End()
        {
            if (!Active) return;
            Active = false;
            if (_rules != null) _rules.Changed -= OnPhase;
            foreach (var s in _src) if (s != null) s.Stop();
            StopSong();
            _root.gameObject.SetActive(false);
            _hud.gameObject.SetActive(false);
            _camp.Village?.Celebrate(false);
            _camp.SetStory(null);
        }

        // ---------- the scene ----------

        void Build()
        {
            _root = new GameObject("Drum Game").transform;
            _root.SetParent(_camp.transform, false);
            _vc = new Material(_a.pine) { name = "Drum Game" };
            _vc.SetFloat("_Sway", 0f);
            var surface = Resources.Load<Shader>("DrumSurface");
            if (surface != null)
            {
                Destroy(_vc);
                _vc = new Material(surface) { name = "Feast surfaces" };
            }
            _wood = new Material(_vc) { name = "Drum wood grain" };
            _hide = new Material(_vc) { name = "Drum natural hide" };
            if (surface != null)
            {
                var detail = DrumVisuals.SurfaceTexture();
                _vc.mainTexture = detail; _wood.mainTexture = detail; _hide.mainTexture = detail;
                _wood.SetFloat("_Surface", 1f); _hide.SetFloat("_Surface", 2f);
            }

            _rig = new GameObject("King's Drums").transform;
            _rig.SetParent(_root, false);
            _drums[0] = MakeDrum("Low Drum", Tone.Low, 0.25f, 0f, -0.235f, LowCol, 0);
            _drums[1] = MakeDrum("High Drum", Tone.High, 0.165f, 0.05f, 0.215f, HighCol, 1);

            // the sounds: built once, played from a small pool of sources at the drums
            _ogene = new[] { ProceduralClips.Clip("ogene-low", ProceduralClips.OgeneSamples(true)), ProceduralClips.Clip("ogene", ProceduralClips.OgeneSamples(false)) };
            _shaker = new[] { ProceduralClips.Clip("shaker", ProceduralClips.ShakerSamples(false)), ProceduralClips.Clip("shaker-push", ProceduralClips.ShakerSamples(true)) };
            for (var i = 0; i < _src.Length; i++)
            {
                var s = new GameObject("Drum Voice").AddComponent<AudioSource>();
                s.transform.SetParent(_rig, false);
                s.playOnAwake = false; s.spatialBlend = 1f; s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 2f; s.maxDistance = 40f;
                _src[i] = s;
            }
            _song = new GameObject("Witch Song").AddComponent<AudioSource>();
            _song.transform.SetParent(_root, false);
            _song.playOnAwake = false; _song.loop = false; _song.spatialBlend = 0f; _song.volume = 0f;
            _song.clip = _a.witchSong;

            // the feast, round the fire; each dish appears when its beat is played back
            for (var i = 0; i < _dishes.Length; i++)
            {
                var a = DishAngles[i] * Mathf.Deg2Rad;
                var d = Node(DrumRules.Dishes[i], DrumVisuals.Dish(i), _vc);
                d.SetParent(_root, false);
                d.localPosition = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * DishRing;
                d.localRotation = Quaternion.Euler(0f, DishAngles[i] + 180f, 0f);
                _haloM[i] = new Material(_a.additive) { mainTexture = _glow, color = new Color(1f, 0.75f, 0.4f) };
                var h = Node("Halo", StoryMeshes.Quad(0.9f, 0.9f), _haloM[i]);
                h.SetParent(d, false);
                h.localPosition = Vector3.up * 0.02f; h.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _dishes[i] = d;
            }
        }

        Drum MakeDrum(string name, Tone tone, float r, float lift, float side, Color col, int seed)
        {
            var d = new Drum { Tone = tone, R = r, Lift = lift, Side = side, Col = col, Root = new GameObject(name).transform };
            d.Root.SetParent(_rig, false);
            d.Root.localPosition = new Vector3(side, 0f, 0f);
            var body = Node("Body", null, _wood); body.SetParent(d.Root, false);
            d.Body = body.GetComponent<MeshFilter>();
            d.Skin = Node("Skin", DrumVisuals.Skin(r, tone == Tone.Low), _hide); d.Skin.SetParent(d.Root, false);
            // the skin's lights, just above it: the middle, and a ring of small ones round the edge
            d.Mid = new Material(_a.additive) { mainTexture = _glow, color = col };
            var mid = Node("Middle Glow", StoryMeshes.Quad(r * 1.5f, r * 1.5f), d.Mid);
            mid.SetParent(d.Skin, false);
            mid.localPosition = Vector3.up * 0.016f; mid.localRotation = Quaternion.Euler(90f, 0f, 0f);
            d.Edge = new Material(_a.additive) { mainTexture = _glow, color = Color.Lerp(col, Color.white, 0.35f) };
            for (var k = 0; k < 12; k++)
            {
                var a = k / 12f * Mathf.PI * 2f;
                var g = Node("Edge Glow", StoryMeshes.Quad(r * 0.5f, r * 0.5f), d.Edge);
                g.SetParent(d.Skin, false);
                g.localPosition = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r * 0.84f + Vector3.up * 0.016f;
                g.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            var drum = tone == Tone.Low ? 0 : 1;
            d.Open = new[] { ProceduralClips.Clip(name + " open", ProceduralClips.DrumStrokeSamples(drum, false, 240 + seed * 10)), ProceduralClips.Clip(name + " open 2", ProceduralClips.DrumStrokeSamples(drum, false, 241 + seed * 10)) };
            d.Slap = new[] { ProceduralClips.Clip(name + " slap", ProceduralClips.DrumStrokeSamples(drum, true, 242 + seed * 10)), ProceduralClips.Clip(name + " slap 2", ProceduralClips.DrumStrokeSamples(drum, true, 243 + seed * 10)) };
            return d;
        }

        /// <summary>
        /// Forest layout, or the same drums in a real room: the dishes sit just outside the stones (the room's fire is smaller),
        /// and the panel comes in so it stays in front of you.
        /// </summary>
        public void Fit(GameFit fit)
        {
            _fit = fit;
            if (_root == null) return;
            var ring = fit.Room ? GameFit.StoneEdge + 0.3f : DishRing;
            for (var i = 0; i < _dishes.Length; i++)
            {
                var a = DishAngles[i] * Mathf.Deg2Rad;
                _dishes[i].localPosition = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * ring;
            }
            var follow = _hud != null ? _hud.GetComponent<FollowView>() : null;
            if (follow != null) { follow.Distance = fit.Room ? 0.6f : 1.15f; follow.Height = fit.Room ? 0.28f : 0.42f; }
            if (Active) PlaceDrums();
        }

        /// <summary>The same room switch as the story: the fire and the drums in your real room.</summary>
        public void SetInRoom(bool inRoom)
        {
            if (_room == null) return;
            _room.SetLabel(inRoom ? "Back to the forest" : "In my room");
            _room.Style = inRoom ? KitStyle.Primary : KitStyle.Ghost;
            _room.Selected = inRoom;
        }

        /// <summary>
        /// Sets the drums in front of where you are now, the low drum's skin about at your waist (lower when you sit), and
        /// builds their bodies to that height.
        /// </summary>
        void PlaceDrums()
        {
            var cam = Camera.main != null ? Camera.main.transform : null;
            var head = cam != null ? _camp.transform.InverseTransformPoint(cam.position) : CampWorld.StorySeat + Vector3.up * 1.6f;
            var fwd = cam != null ? _camp.transform.InverseTransformDirection(cam.forward) : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) { fwd = -head; fwd.y = 0f; } // looking straight down or up: face the fire
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            _skinY = Mathf.Clamp(head.y - 0.74f, 0.5f, 0.92f);
            var at = new Vector3(head.x, 0f, head.z) + fwd * Reach;
            if (_fit.Room)
            {
                // the room's fire is right in front of you (you sit at its stones): no floor there for drums, so they go either
                // side of you, at your hips, level with where you sit and clear of the stones, facing the fire
                var flat = new Vector3(head.x, 0f, head.z);
                var out_ = flat.sqrMagnitude > 1e-4f ? flat.normalized : -fwd;
                at = out_ * Mathf.Max(flat.magnitude, GameFit.StoneEdge + 0.15f);
                fwd = -out_;
            }
            _rig.localPosition = at;
            _rig.localRotation = Quaternion.LookRotation(fwd);
            foreach (var d in _drums)
            {
                // in the room they flank you: each drum's inner edge just clear of your hips (a seated body is about 0.36 m
                // across), and 15% smaller like the room's fire. Outside the stones: the low drum's centre is ~0.94 m from the
                // fire (needs 0.7 + 0.21), the high one's ~0.91 m (needs 0.7 + 0.14). In the forest, as built.
                var k = _fit.Room ? RoomDrum : 1f;
                d.Root.localScale = new Vector3(k, 1f, k);
                d.Root.localPosition = new Vector3(_fit.Room ? Mathf.Sign(d.Side) * (Hip + d.R * k) : d.Side, 0f, 0f);
            }
            foreach (var d in _drums)
            {
                var y = _skinY + d.Lift;
                if (Mathf.Abs(d.BuiltY - y) > 0.03f)
                {
                    var oldBody = d.Body.sharedMesh;
                    d.Body.sharedMesh = d.Tone == Tone.Low ? LowBody(y, d.R) : HighBody(y, d.R);
                    if (oldBody != null) Destroy(oldBody);
                    d.BuiltY = y;
                }
                d.Skin.localPosition = Vector3.up * y;
            }
        }

        // ---------- the drums' bodies ----------

        /// <summary>
        /// A turned body: <paramref name="prof"/> (height, radius) from top to bottom, with shared vertices and subtle longitudinal
        /// wood variation. Outside faces out.
        /// </summary>
        static void Turned(TrailBuilder.MeshKit mk, (float y, float r)[] prof, int seg, Color wood, int seed)
        {
            var start = mk.V.Count;
            var profile = new (float, float, float)[prof.Length];
            for (var i = 0; i < prof.Length; i++) profile[i] = (prof[i].y, prof[i].r, prof[i].r);
            mk.Lathe(Vector3.zero, Quaternion.identity, profile, wood, seg);
            // Subtle longitudinal variation, with shared vertices for a round carved shell.
            for (var i = start; i < mk.V.Count; i++)
            {
                var v = mk.V[i];
                var grain = Mathf.PerlinNoise(v.x * 55f + seed, v.z * 55f + seed);
                var c = wood * (0.88f + grain * 0.18f); c.a = 1f;
                mk.C[i] = c;
            }
        }

        /// <summary>A carved band of triangles between <paramref name="y0"/> and <paramref name="y1"/>: points up in one colour, down in the other.</summary>
        static void Zigzag(TrailBuilder.MeshKit mk, float y0, float r0, float y1, float r1, int seg, Color up, Color down)
        {
            for (var j = 0; j < seg; j++)
            {
                float a0 = j / (float)seg * Mathf.PI * 2f, a1 = (j + 1) / (float)seg * Mathf.PI * 2f;
                Vector3 P(float a, float y, float r) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                mk.Tri(P(a0, y1, r1), P(a1, y1, r1), P(a0, y0, r0), down, down, down);
                mk.Tri(P(a1, y1, r1), P(a1, y0, r0), P(a0, y0, r0), up, up, up);
            }
        }

        static void Ring(TrailBuilder.MeshKit mk, float y, float h, float r, Color c, int seg)
            => mk.Lathe(Vector3.zero, Quaternion.identity, new (float, float, float)[] { (y + h, r, r), (y, r, r) }, c, seg);

        static float RadiusAt((float y, float r)[] prof, float y)
        {
            for (var i = 0; i + 1 < prof.Length; i++)
                if (y <= prof[i].y && y >= prof[i + 1].y) return Mathf.Lerp(prof[i + 1].r, prof[i].r, (y - prof[i + 1].y) / Mathf.Max(1e-4f, prof[i].y - prof[i + 1].y));
            return prof[prof.Length - 1].r;
        }

        /// <summary>
        /// The big low drum: an Efik standing drum. A barrel of dark wood on four carved feet, a band of red-ochre and black
        /// triangles and one of lighter checks, a rope-bound hoop holding the skin, laced down to pegs.
        /// </summary>
        static Mesh LowBody(float h, float R)
        {
            var mk = new TrailBuilder.MeshKit();
            var wood = new Color(0.38f, 0.21f, 0.11f);
            var dark = new Color(0.1f, 0.06f, 0.04f);
            var ochre = new Color(0.66f, 0.22f, 0.08f);
            var pale = new Color(0.74f, 0.6f, 0.4f);
            var cord = new Color(0.82f, 0.74f, 0.56f);
            const float foot = 0.11f;
            (float y, float r)[] prof =
            {
                (h - 0.008f, R + 0.01f), (h - 0.07f, R + 0.03f), (h * 0.78f, R + 0.042f), (h * 0.55f, R * 0.96f), (h * 0.34f, R * 0.84f),
                (foot + 0.06f, R * 0.82f), (foot + 0.02f, R * 0.9f), (foot, R * 0.9f),
            };
            Turned(mk, prof, 64, wood, 7);
            // the bands, a hair proud of the wood
            float yA = h * 0.6f, yB = h * 0.69f;
            Ring(mk, yB, 0.012f, RadiusAt(prof, yB) + 0.006f, dark, 26);
            Zigzag(mk, yA, RadiusAt(prof, yA) + 0.004f, yB, RadiusAt(prof, yB) + 0.004f, 18, ochre, dark);
            Ring(mk, yA - 0.012f, 0.012f, RadiusAt(prof, yA) + 0.006f, dark, 26);
            float yC = h * 0.32f, yD = h * 0.37f;
            Zigzag(mk, yC, RadiusAt(prof, yC) + 0.004f, yD, RadiusAt(prof, yD) + 0.004f, 24, pale, wood * 0.6f);
            // the base disc and four carved feet under it
            mk.Cylinder(Vector3.up * (foot - 0.025f), R * 0.86f, R * 0.9f, 0.025f, wood * 0.7f, wood * 0.8f, 20);
            for (var k = 0; k < 4; k++)
            {
                var a = (k * 90f + 45f) * Mathf.Deg2Rad;
                var o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                mk.Box(o * R * 0.66f + Vector3.up * (foot * 0.5f), new Vector3(0.07f, foot, 0.05f), Quaternion.LookRotation(o), wood * 0.85f);
                mk.Box(o * R * 0.72f + Vector3.up * 0.012f, new Vector3(0.09f, 0.024f, 0.07f), Quaternion.LookRotation(o), wood * 0.7f);
            }
            // the hoop, bound with rope, and laces down to the pegs
            Ring(mk, h - 0.04f, 0.045f, R + 0.022f, dark, 28);
            for (var k = 0; k < 32; k++)
            {
                var a = k / 32f * Mathf.PI * 2f;
                mk.Ellipsoid(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (R + 0.026f) + Vector3.up * (h - 0.018f), new Vector3(0.012f, 0.02f, 0.012f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), cord * 0.92f, 5, 3);
            }
            var pegY = h - 0.2f;
            for (var k = 0; k < 12; k++)
            {
                var a = k / 12f * Mathf.PI * 2f;
                var o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var root = o * (RadiusAt(prof, pegY) - 0.01f) + Vector3.up * pegY;
                mk.Limb(root, root + o * 0.06f + Vector3.down * 0.03f, 0.014f, 0.01f, pale * 0.9f, 6);
                foreach (var side in new[] { -0.5f, 0.5f })
                {
                    var a2 = (k + side) / 12f * Mathf.PI * 2f;
                    mk.Limb(new Vector3(Mathf.Sin(a2), 0f, Mathf.Cos(a2)) * (R + 0.026f) + Vector3.up * (h - 0.04f), root + o * 0.015f, 0.0035f, 0.0035f, cord, 6, null, false);
                }
            }
            return mk.Build("Low Drum");
        }

        /// <summary>
        /// The small high drum: a goblet carved from one piece, a cone of a bowl narrowing to a waist and flaring to its foot,
        /// rope laced in diamonds from its hoop to a ring at the waist, a row of cowries round the hoop.
        /// </summary>
        static Mesh HighBody(float h, float R)
        {
            var mk = new TrailBuilder.MeshKit();
            var wood = new Color(0.46f, 0.27f, 0.13f);
            var dark = new Color(0.1f, 0.06f, 0.04f);
            var rope = new Color(0.84f, 0.76f, 0.58f);
            var waist = h * 0.56f;
            (float y, float r)[] prof =
            {
                (h - 0.008f, R + 0.008f), (h - 0.04f, R + 0.012f), (waist, R * 0.46f), (h * 0.46f, R * 0.42f), (h * 0.28f, R * 0.55f),
                (h * 0.09f, R * 0.92f), (0.02f, R * 1.0f), (0f, R * 0.98f),
            };
            Turned(mk, prof, 64, wood, 11);
            Zigzag(mk, h * 0.12f, RadiusAt(prof, h * 0.12f) + 0.004f, h * 0.2f, RadiusAt(prof, h * 0.2f) + 0.004f, 16, new Color(0.7f, 0.24f, 0.08f), dark);
            Ring(mk, h * 0.1f - 0.01f, 0.01f, RadiusAt(prof, h * 0.1f) + 0.006f, dark, 22);
            // the hoop, the rope ring at the waist, and the diamonds of lacing between them
            Ring(mk, h - 0.038f, 0.04f, R + 0.02f, dark, 24);
            Ring(mk, waist - 0.012f, 0.022f, R * 0.46f + 0.012f, rope * 0.9f, 18);
            const int laces = 12;
            for (var k = 0; k < laces; k++)
            {
                float a0 = k / (float)laces * Mathf.PI * 2f, a1 = (k + 0.5f) / laces * Mathf.PI * 2f, a2 = (k + 1f) / laces * Mathf.PI * 2f;
                Vector3 Top(float a) => new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (R + 0.024f) + Vector3.up * (h - 0.036f);
                Vector3 Low(float a) => new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (R * 0.46f + 0.016f) + Vector3.up * (waist + 0.006f);
                Vector3 Mid(float a) => new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * ((R + 0.024f + R * 0.46f + 0.016f) * 0.5f + 0.006f) + Vector3.up * ((h - 0.036f + waist) * 0.5f);
                mk.Limb(Top(a0), Mid(a1), 0.003f, 0.003f, rope, 6, null, false);
                mk.Limb(Top(a2), Mid(a1), 0.003f, 0.003f, rope, 6, null, false);
                mk.Limb(Mid(a1), Low(a0), 0.003f, 0.003f, rope, 6, null, false);
                mk.Limb(Mid(a1), Low(a2), 0.003f, 0.003f, rope, 6, null, false);
            }
            for (var k = 0; k < 18; k++)
            {
                var a = k / 18f * Mathf.PI * 2f;
                var o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                mk.Ellipsoid(o * (R + 0.03f) + Vector3.up * (h - 0.056f), new Vector3(0.011f, 0.016f, 0.008f), Quaternion.LookRotation(o), new Color(0.95f, 0.92f, 0.84f), 6, 4);
            }
            return mk.Build("High Drum");
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

        // ---------- striking the drums ----------

        /// <summary>
        /// Does a hand moving from <paramref name="prev"/> to <paramref name="now"/> (relative to a skin's middle, up from the
        /// skin) in <paramref name="dt"/> strike a skin of radius <paramref name="radius"/>? It must come down through the skin,
        /// fast enough, within reach of its edge, and be armed: lifted at least <see cref="ReArm"/> above it since its last strike.
        /// <paramref name="at"/>: how far out it landed, as a share of the radius (past <see cref="RimFrom"/> is the edge). Pure, unit tested.
        /// </summary>
        public static bool Struck(ref bool armed, Vector3 prev, Vector3 now, float dt, float radius, out float at)
        {
            at = 0f;
            if (now.y > ReArm) { armed = true; return false; }
            if (!armed || dt <= 0f || now.y > 0f || prev.y < 0f) return false;
            if ((prev.y - now.y) / dt < MinSpeed) return false;
            var u = prev.y - now.y > 1e-5f ? prev.y / (prev.y - now.y) : 0f;
            var p = Vector3.Lerp(prev, now, u);
            var d = new Vector2(p.x, p.z).magnitude;
            if (d > radius + Margin) return false;
            armed = false;
            at = d / radius;
            return true;
        }

        Vector3 SkinTop(Drum d) => d.Root.TransformPoint(Vector3.up * (_skinY + d.Lift));

        void Hands(float dt)
        {
            // hands (or controllers) coming down through a skin; landing between the two, the drum nearer its middle takes it
            if (StoryInput.Hand != null)
                for (var i = 0; i < 2; i++)
                {
                    var w = StoryInput.Hand(i);
                    if (w == null) { _had[i] = false; continue; }
                    int hit = -1; var best = float.MaxValue;
                    for (var k = 0; k < 2; k++)
                    {
                        var d = _drums[k];
                        var off = Vector3.up * (_skinY + d.Lift);
                        var now = d.Root.InverseTransformPoint(w.Value) - off;
                        if (!_had[i]) { _armed[i, k] = now.y > ReArm; continue; } // a hand that appears under a skin has to lift first
                        var prev = d.Root.InverseTransformPoint(_prev[i]) - off;
                        if (Struck(ref _armed[i, k], prev, now, dt, d.R, out var at) && at < best) { best = at; hit = k; }
                    }
                    if (hit >= 0) { _lastStrike = Time.time; Strike(hit, best > RimFrom); }
                    _prev[i] = w.Value; _had[i] = true;
                }

            // or a pinch while pointing at a skin: whichever the ray meets nearer its middle
            var input = HypaInput.Current;
            var pinch = input.IsPinching;
            if (pinch && !_wasPinching && !DodoGame.OnPanel() && Time.time - _lastStrike > 0.2f && input.TryGetRay(out var ray))
            {
                int hit = -1; var best = float.MaxValue;
                for (var k = 0; k < 2; k++)
                {
                    var top = SkinTop(_drums[k]); var n = _drums[k].Root.up;
                    var dn = Vector3.Dot(ray.direction, n);
                    if (dn > -0.05f) continue;
                    var t = Vector3.Dot(top - ray.origin, n) / dn;
                    var rr = _drums[k].R * _drums[k].Root.localScale.x; // its size as drawn (smaller in the room)
                    var at = Vector3.Distance(ray.origin + ray.direction * t, top) / rr;
                    if (t > 0f && t < 4f && at <= 1f + Margin / rr && at < best) { best = at; hit = k; }
                }
                if (hit >= 0) Strike(hit, best > RimFrom);
            }
            _wasPinching = pinch;
        }

        void Strike(int drum, bool slap)
        {
            Voice(drum, slap, 0d, false);
            var r = _rules.Stroke(_drums[drum].Tone);
            if (r == StrokeResult.Good || _rules.Over) _flare = Mathf.Max(_flare, _rules.Over ? 0.18f : 0.25f); // free drumming stirs the fire too
        }

        /// <summary>A drum sounds (now, or at a time on the audio clock) and its skin lights when it does.</summary>
        void Voice(int drum, bool slap, double at, bool call)
        {
            var d = _drums[drum];
            var clips = slap ? d.Slap : d.Open;
            Play(clips[d.Take++ % clips.Length], SkinTop(d), drum == 0 ? 1f : 0.85f, at);
            if (at <= 0d) Light(drum, slap, call);
            else _lights.Add((at, drum, slap, call));
        }

        void Light(int drum, bool slap, bool call)
        {
            var d = _drums[drum];
            if (slap) d.EdgeLit = 1f; else d.MidLit = 1f;
            d.Bounce = 1f;
            if (call) { _called++; _flare = Mathf.Max(_flare, 0.12f); }
        }

        void Play(AudioClip clip, Vector3 at, float level, double when)
        {
            var s = _src[_srcNext++ % _src.Length];
            s.Stop();
            s.transform.position = at;
            s.clip = clip;
            s.volume = level;
            s.pitch = UnityEngine.Random.Range(0.985f, 1.015f);
            if (when > 0d) s.PlayScheduled(when); else s.Play();
        }

        // ---------- the groove: the bell, the shaker and the call, on the audio clock ----------

        /// <summary>
        /// Schedules whatever falls in the next moment: the shaker on every eighth (pushed on the off-beats), the ogene on every
        /// beat (its low mouth on the bar line), and in the call, the drums' own strokes after the bell's first bar.
        /// </summary>
        void Groove()
        {
            if (!_rules.Keeping) return;
            var now = AudioSettings.dspTime;
            var horizon = now + Lookahead;
            while (_grid + _nextStep * DrumRules.Step < horizon)
            {
                var at = _grid + _nextStep * DrumRules.Step;
                var step = _nextStep++ % DrumRules.StepsPerBar;
                if (at < now - 0.03) continue; // a hitch: never play a beat late
                var pos = _rig.TransformPoint(new Vector3(0f, _skinY + 0.15f, 0.25f));
                Play(_shaker[step % 2], pos, 0.32f, at);
                if (step % 2 == 0)
                {
                    Play(_ogene[step == 0 ? 0 : 1], _rig.TransformPoint(new Vector3(-0.6f, _skinY + 0.3f, 0.3f)), step == 0 ? 0.42f : 0.3f, at);
                    _ticks.Add((at, step / 2));
                }
            }
            if (_rules.Phase != DrumPhase.Call) return;
            var beat = _rules.Current;
            while (_nextCall < beat.Length && _grid + _rules.CallAt(_nextCall) < horizon)
            {
                var at = _grid + _rules.CallAt(_nextCall);
                var b = beat[_nextCall++];
                // the last stroke of a beat that ends on the high drum cracks at its edge: the call's full stop
                var slap = _nextCall == beat.Length && b.Tone == Tone.High;
                if (at >= now - 0.03) Voice(b.Tone == Tone.Low ? 0 : 1, slap, Math.Max(at, now + 0.005), true);
            }
        }

        // ---------- every frame ----------

        void Update()
        {
            if (!Active || _rules == null) return;
            var dt = Time.deltaTime;
            _rules.Tick(dt);
            if (Application.isEditor) EditorKeys();
            Hands(dt);
            Groove();
            Lights(dt);
            Dishes(dt);
            Scene(dt);
            Song();
            Hud();
        }

        // ---------- the witch song, after the feast ----------

        /// <summary>The song from the top, after <paramref name="delay"/> seconds (no recording in Local: nothing, you still drum).</summary>
        void StartSong(float delay)
        {
            if (_song == null || _song.clip == null) return;
            _song.Stop();
            _song.time = 0f;
            _song.volume = 0f;
            _songAt = AudioSettings.dspTime + delay;
            _song.PlayScheduled(_songAt);
            _songOn = true;
        }

        void StopSong()
        {
            if (_song != null) _song.Stop();
            _songOn = false;
        }

        bool HasSong => _song != null && _song.clip != null;

        /// <summary>The song has played to its end (or was never there).</summary>
        bool SongDone => !_songOn || (!_song.isPlaying && AudioSettings.dspTime > _songAt + 0.5);

        /// <summary>Its level: in over a moment at the top, out over its last <see cref="SongFadeOut"/> seconds, so it ends softly.</summary>
        void Song()
        {
            if (!_songOn || !_song.isPlaying) return;
            var t = _song.time;
            var left = _song.clip.length - t;
            _song.volume = SongLevel * Mathf.Clamp01(t / SongFadeIn) * Mathf.Clamp01(left / SongFadeOut);
        }

        void OnPhase(DrumPhase p)
        {
            switch (p)
            {
                case DrumPhase.Call:
                    // straight on from your turn, the bell's grid carries over (two whole bars); otherwise it starts now
                    _grid = _was == DrumPhase.Answer ? _grid + _rules.CallLength + DrumRules.AnswerWait : AudioSettings.dspTime + 0.05;
                    if (_grid < AudioSettings.dspTime) _grid = AudioSettings.dspTime + 0.05;
                    _nextStep = 0; _nextCall = 0; _called = 0;
                    break;
                case DrumPhase.Answer: break; // the bell runs straight on: the call was whole bars
                case DrumPhase.Feast:
                    var i = _rules.Served - 1;
                    if (i >= 0 && i < _dishes.Length)
                    {
                        _dishes[i].gameObject.SetActive(true);
                        _dishT[i] = 0f;
                        _audio.EffectAt("sparks", _dishes[i].position + Vector3.up * 0.3f, 0.8f, 1f, 4f);
                    }
                    _audio.Effect("cheer");
                    _camp.Effect("sparks");
                    _flare = 1f;
                    break;
                case DrumPhase.Miss:
                    _audio.EffectAt("laugh", _camp.transform.TransformPoint(new Vector3(-1.4f, 1.3f, 1.2f)), 0.6f, 1.05f, 5f);
                    _camp.Effect("fall"); // the flames duck
                    _gloom = 1f;
                    break;
                case DrumPhase.Won:
                    _camp.Village?.Celebrate(true, !HasSong); // the song plays instead of the village's drums
                    StartSong(SongDelay);
                    _audio.Effect("rooster"); _audio.Effect("birds");
                    break;
            }
            if (p != DrumPhase.Call && p != DrumPhase.Answer) { _lights.RemoveAll(l => l.call); _ticks.Clear(); }
            _was = p;
        }

        // ---------- the editor: B the low drum, N the high one, P plays the beat back for you ----------

        void EditorKeys()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.B)) Strike(0, false);
            if (UnityEngine.Input.GetKeyDown(KeyCode.N)) Strike(1, false);
            if (UnityEngine.Input.GetKeyDown(KeyCode.P)) AutoPlay();
            for (var i = _auto.Count - 1; i >= 0; i--)
                if (Time.time >= _auto[i].at) { var tone = _auto[i].tone; _auto.RemoveAt(i); Strike(tone == Tone.Low ? 0 : 1, false); }
        }

        /// <summary>Plays the current beat back on time (the editor's stand-in for a pair of hands).</summary>
        public void AutoPlay()
        {
            if (_rules.Phase != DrumPhase.Answer || _rules.Next > 0 || _auto.Count > 0) return;
            foreach (var b in _rules.Current) _auto.Add((Time.time + b.T, b.Tone));
        }

        // ---------- the drums' lights, the dishes, the night ----------

        void Lights(float dt)
        {
            var now = AudioSettings.dspTime;
            for (var i = _lights.Count - 1; i >= 0; i--)
                if (now >= _lights[i].at) { var l = _lights[i]; _lights.RemoveAt(i); Light(l.drum, l.slap, l.call); }
            for (var i = _ticks.Count - 1; i >= 0; i--)
                if (now >= _ticks[i].at) { _tickLit[_ticks[i].beat] = 1f; _ticks.RemoveAt(i); }
            for (var k = 0; k < 4; k++) _tickLit[k] *= Mathf.Exp(-7f * dt);

            var fade = Mathf.Exp(-6f * dt);
            // a faint rest light on both parts of each skin, a little brighter on your turn, so you can see where they are
            var rest = _rules.Phase == DrumPhase.Answer ? 0.14f : 0.07f;
            foreach (var d in _drums)
            {
                d.MidLit *= fade; d.EdgeLit *= fade; d.Bounce *= Mathf.Exp(-14f * dt);
                d.Mid.SetFloat("_Opacity", rest + 0.85f * Mathf.Max(d.MidLit, d.EdgeLit * 0.4f));
                d.Edge.SetFloat("_Opacity", rest * 0.7f + 0.8f * d.EdgeLit);
                d.Skin.localPosition = Vector3.up * (_skinY + d.Lift - 0.008f * d.Bounce);
            }
        }

        void Dishes(float dt)
        {
            for (var i = 0; i < _dishes.Length; i++)
            {
                if (_dishT[i] < 0f) continue;
                _dishT[i] += dt;
                // it pops up out of nothing, overshoots a little, and settles
                var u = Mathf.Clamp01(_dishT[i] / 0.55f);
                var s = 1f + 2.2f * Mathf.Pow(u - 1f, 3f) + 1.2f * Mathf.Pow(u - 1f, 2f);
                _dishes[i].localScale = Vector3.one * DishScale * Mathf.Max(0.001f, s);
                _haloM[i].SetFloat("_Opacity", Mathf.Lerp(0.035f, 0.65f, Mathf.Exp(-_dishT[i] * 2.8f)));
            }
        }

        void Scene(float dt)
        {
            var p = _rules.Phase;
            _flare = Mathf.MoveTowards(_flare, 0f, dt * 0.8f);
            _gloom = Mathf.MoveTowards(_gloom, 0f, dt * 0.6f);
            _dawn = Mathf.MoveTowards(_dawn, p == DrumPhase.Won ? 0.8f : 0f, dt * 0.12f);
            var fed = _rules.Served / (float)_dishes.Length;
            _camp.SetStory(new StorySample
            {
                Fire = 1.1f + 0.35f * fed + 0.55f * _flare - 0.3f * _gloom,
                Dark = Mathf.Max(0f, 0.32f - 0.14f * fed + 0.12f * _gloom) * (1f - _dawn),
                Dawn = _dawn, Wind = 0.1f, Index = -1,
            });
        }

        // ---------- the HUD ----------

        const float SlotW = 0.042f, PipY = -0.042f;

        void BuildHud(Transform uiRoot)
        {
            _hud = new GameObject("Drum HUD").transform;
            _hud.SetParent(uiRoot, false);
            var f = _hud.gameObject.AddComponent<FollowView>();
            f.Distance = 1.15f; f.Height = 0.42f; f.YawOffset = 0f; f.Threshold = 55f; // above the drums and the fire, clear of your hands
            Kit.Glass(_hud, "Panel", new Vector3(0f, 0f, 0.006f), new Vector2(0.5f, 0.17f), 0.04f);
            _title = Kit.Text(_hud, "", 0.028f, new Vector3(0f, 0.046f, 0f), new Vector2(0.46f, 0.045f), KitTheme.Paper, TextAlignmentOptions.Center, KitFont.Serif);
            _sub = Kit.Text(_hud, "", 0.015f, new Vector3(0f, 0.008f, 0f), new Vector2(0.46f, 0.04f), KitTheme.Dim);
            _count = Kit.Text(_hud, "", 0.013f, new Vector3(0.19f, 0.062f, 0f), new Vector2(0.1f, 0.02f), KitTheme.Dim, TextAlignmentOptions.Right);
            // the bar: a tick under each of its four beats (they flash with the bell), and a dot for each stroke on its eighth,
            // a big ember one for the low drum, a small pale one for the high drum
            for (var k = 0; k < 4; k++)
            {
                var w = k == 0 ? 0.006f : 0.004f;
                _beatTicks[k] = Kit.Panel(_hud, "Beat", new Vector3((k * 2 - 3.5f) * SlotW, PipY - 0.024f, 0f), new Vector2(w, 0.012f), w * 0.5f,
                    KitTheme.Dim, KitTheme.Dim, new Color(0f, 0f, 0f, 0f), 12, 0f);
            }
            for (var i = 0; i < 8; i++)
            {
                var pip = Kit.Panel(_hud, "Stroke", new Vector3(0f, PipY, 0f), new Vector2(0.02f, 0.02f), 0.01f, LowCol, LowCol, KitTheme.Hairline, 12, 0f);
                pip.BorderWidth = 0.003f;
                _pips.Add(pip);
            }
            _quit = Kit.Button(_hud, new Vector3(-0.2f, 0.064f, 0f), new Vector2(0.09f, 0.038f), "Back", "arrow-left", KitStyle.Ghost, () => { End(); _return?.Invoke(); }, null, 0.015f);
            _retry = Kit.Button(_hud, new Vector3(-0.15f, -0.052f, 0f), new Vector2(0.14f, 0.046f), "Play again", "rotate-ccw", KitStyle.Primary, () => { End(); Begin(); });
            _restart = Kit.Button(_hud, new Vector3(0f, -0.052f, 0f), new Vector2(0.14f, 0.046f), "The story", "play", KitStyle.Ghost, () => { End(); _restartStory?.Invoke(); });
            _leave = Kit.Button(_hud, new Vector3(0.15f, -0.052f, 0f), new Vector2(0.14f, 0.046f), "Stories", "arrow-left", KitStyle.Ghost, () => { End(); _back?.Invoke(); });
            if (_toggleRoom != null)
            {
                _room = Kit.Button(_hud, new Vector3(0f, -0.115f, 0f), new Vector2(0.28f, 0.04f), "Back to the forest", "house", KitStyle.Ghost, _toggleRoom, null, 0.014f);
                _room.SetLabel("In my room");
            }
            _hud.gameObject.AddComponent<KitGroup>().Refresh();
            _hud.gameObject.SetActive(false);
        }

        void Hud()
        {
            var p = _rules.Phase;
            var hands = StoryInput.Hand != null;
            var how = hands ? "Bring a hand down on the big drum for the low note, on the small drum for the high one."
                : Application.isEditor ? "Pinch the big drum for the low note, the small drum for the high one (or B and N)."
                : "Pinch the big drum for the low note, the small drum for the high one.";
            string title, sub;
            switch (p)
            {
                case DrumPhase.Intro: title = "The king's drums"; sub = how + " The bell keeps time; the drums play a beat, then you play it back."; break;
                case DrumPhase.Call: title = _called == 0 ? "Listen…" : "Listen"; sub = "The bell counts you in, then the drums play the king's beat. Watch the skins."; break;
                case DrumPhase.Answer: title = "Your turn"; sub = _rules.Next == 0 ? "Play it back with the bell. " + how : "Keep the groove…"; break;
                case DrumPhase.Feast: title = DrumRules.Dishes[Mathf.Clamp(_rules.Served - 1, 0, DrumRules.Dishes.Length - 1)] + "!"; sub = "The feast grows by the fire."; break;
                case DrumPhase.Miss:
                    title = "The guests laugh";
                    sub = _rules.Missed == MissReason.WrongTone ? "That stroke wanted the other drum. Listen again."
                        : _rules.Missed == MissReason.Early ? "A little too quick. Listen again." : "A little too slow. Listen again.";
                    break;
                default:
                    if (_rules.T < 3f) { title = "The feast is served"; sub = "Fish and fufu, yams and okra, palm-oil soup and palm wine. The whole town comes to eat."; }
                    else if (HasSong && !SongDone) { title = "Free drum"; sub = "Drum along with the song any way you like: the big drum low, the small drum high, the edges slap."; }
                    else if (HasSong) { title = "The song is over"; sub = "Play the game again, or go back to the story."; }
                    else { title = "Free drum"; sub = "Drum any way you like: the big drum low, the small drum high, the edges slap."; }
                    break;
            }
            _title.text = title; _sub.text = sub;
            _count.text = $"{_rules.Served} of {DrumRules.Dishes.Length} dishes";

            // the beat, written on the bar: lit as the drums play it, then as you play it back
            var beat = _rules.Current;
            var showBeat = p == DrumPhase.Call || p == DrumPhase.Answer || p == DrumPhase.Miss;
            for (var k = 0; k < 4; k++)
            {
                _beatTicks[k].gameObject.SetActive(showBeat);
                var c = Color.Lerp(KitTheme.Dim, KitTheme.Paper, _tickLit[k]);
                if (_beatTicks[k].Top != c) { _beatTicks[k].Top = _beatTicks[k].Bottom = c; _beatTicks[k].Apply(); }
            }
            for (var i = 0; i < _pips.Count; i++)
            {
                var pip = _pips[i];
                var on = showBeat && i < beat.Length;
                pip.gameObject.SetActive(on);
                if (!on) continue;
                var low = beat[i].Tone == Tone.Low;
                var lit = p == DrumPhase.Call ? i < _called : p == DrumPhase.Answer && i < _rules.Next;
                var c = low ? LowCol : HighCol;
                var size = low ? 0.026f : 0.017f;
                pip.transform.localPosition = new Vector3((beat[i].Slot - 3.5f) * SlotW, PipY, 0f);
                if (Mathf.Abs(pip.Size.x - size) > 1e-4f) { pip.Radius = size * 0.5f; pip.Resize(new Vector2(size, size)); }
                var top = lit ? c : new Color(c.r, c.g, c.b, 0.18f);
                var border = p == DrumPhase.Answer && i == _rules.Next ? KitTheme.Ember : KitTheme.Hairline;
                if (pip.Top != top || pip.Border != border) { pip.Top = pip.Bottom = top; pip.Border = border; pip.Apply(); }
            }

            var over = _rules.Over && _rules.T > 3f;
            _retry.gameObject.SetActive(over); _restart.gameObject.SetActive(over); _leave.gameObject.SetActive(over);
            _quit.gameObject.SetActive(!over);
            _count.gameObject.SetActive(!over);
        }
    }
}
