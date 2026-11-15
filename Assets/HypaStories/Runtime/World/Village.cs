using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The village at the end of the right-hand trail (ticket 3M, first pass: the place and its people, no talking yet).
    /// See <see cref="VillageLayout"/> for where things stand.
    ///  - A carved gate, a plaza of beaten earth ringed by round thatched huts, warm light in their doorways, strings of
    ///    lights between them.
    ///  - A woven fence across the plaza. On the far side: the village fire with sparks rising, three drummers playing (their
    ///    hands follow the drum loop you hear), a masquerade spinning round the fire with its cloth strips flaring, two dancers,
    ///    a crowd sorting yams round a pile, two women pounding yam in a mortar (you hear each thud), children chasing each other.
    ///  - On your side: the one local (an elder with a stick) who turns to look at you and waves when you come near; a mat with
    ///    a basket of yams, a coil of rope and a calabash (for pinch to learn, later); a dog that barks once and wags; a goat
    ///    on a tether; chickens pecking.
    /// You can't talk to anyone yet. Figures are a few boxes each and move by turning their parts, so the crowd stays cheap.
    /// Everything but the sounds hides when you're far away (the fog hides it anyway).
    /// </summary>
    public sealed class Village : TrailBuilder
    {
        const float ShowWithin = 40f;

        Transform _root;   // everything you see (hidden when far)
        StoryFire _fire;

        sealed class Figure
        {
            public Transform Root, Torso, Head, ArmL, ArmR;
            public float Ph;
            public Vector3 Home;
        }

        readonly List<Figure> _sorters = new List<Figure>();
        readonly List<Figure> _pounders = new List<Figure>();
        readonly List<Transform> _pestles = new List<Transform>();
        readonly List<Figure> _drummers = new List<Figure>();
        readonly List<Figure> _dancers = new List<Figure>();
        readonly List<Figure> _kids = new List<Figure>();
        Figure _local;
        float _waveT = -1f;
        bool _waved;

        // the masquerade: one mesh of cloth strips, rebuilt each frame as they flare
        Transform _masq;
        Mesh _masqMesh;
        Vector3[] _masqV;
        Color[] _masqC;
        float _masqAngle, _masqSpin, _masqYaw;
        const int Strips = 26;

        // drums
        ProceduralClips.DrumHit[] _pattern;
        AudioSource _drums;
        AudioLowPassFilter _drumsMuffle;
        float _lastLoopT;

        // yam pounding
        AudioSource _thud;
        AudioClip _thudClip;
        int _lastPound = -1;

        // animals
        Transform _dog, _dogHead, _dogTail;
        AudioSource _dogVoice;
        AudioClip _bark;
        bool _barked;
        Transform _goat, _goatHead;
        AudioSource _goatVoice;
        AudioClip _bleat;
        float _nextBleat = 6f;
        sealed class Hen { public Transform T, Head; public Vector3 Goal; public float Next; }
        readonly List<Hen> _hens = new List<Hen>();

        public void Build(StoryAssets a, Texture2D glow)
        {
            Init(a, glow, 57, "Village");
            _root = Node("Village (seen)");
            _pattern = ProceduralClips.DrumPattern();
            BuildGround();
            BuildGate();
            BuildHuts();
            BuildLights();
            BuildFence();
            BuildFire();
            BuildCrowd();
            BuildPounders();
            BuildDrummers();
            BuildMasquerade();
            BuildLocal();
            BuildMat();
            BuildAnimals();
            BuildSounds();
            BuildFootsteps();
        }

        protected override bool OnGround(Vector2 v) => VillageLayout.InPlaza(v);

        static Vector3 Face(Vector2 dir) => new Vector3(dir.x, 0f, dir.y);

        // ---------- the place ----------

        void BuildGround()
        {
            var mk = new MeshKit();
            var earth = new Color(0.24f, 0.19f, 0.14f);
            var edge = new Color(0.13f, 0.11f, 0.085f);
            const int seg = 48; const float r = 15f;
            for (var i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                Vector3 P(float a, float rr) => X0Z(VillageLayout.Plaza + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * rr, 0.01f);
                var mid = Jitter(earth, 0.08f);
                mk.Tri(X0Z(VillageLayout.Plaza, 0.012f), P(a1, r * 0.7f), P(a0, r * 0.7f), Jitter(earth, 0.05f), mid, mid);
                mk.Quad(P(a0, r * 0.7f), P(a1, r * 0.7f), P(a1, r * (0.95f + R() * 0.1f)), P(a0, r), mid, mid, edge, edge);
            }
            MeshNode("Plaza", mk.Build("Plaza"), _vc, _root);
        }

        void BuildGate()
        {
            var mk = new MeshKit();
            var wood = new Color(0.3f, 0.2f, 0.12f);
            var paint = new[] { new Color(0.62f, 0.16f, 0.1f), new Color(0.82f, 0.62f, 0.18f), new Color(0.9f, 0.86f, 0.75f) };
            var g = VillageLayout.Gate; var right = VillageLayout.Right;
            foreach (var k in new[] { -1f, 1f })
            {
                var post = g + right * k * 1.35f;
                mk.Box(X0Z(post, 1.4f), new Vector3(0.22f, 2.8f, 0.22f), Quaternion.LookRotation(Face(VillageLayout.Into)), Jitter(wood, 0.06f));
                // carved and painted bands, a little face near the top
                for (var b = 0; b < 4; b++)
                    mk.Box(X0Z(post, 0.5f + b * 0.55f), new Vector3(0.25f, 0.07f, 0.25f), Quaternion.LookRotation(Face(VillageLayout.Into)), paint[b % paint.Length]);
                mk.Box(X0Z(post - VillageLayout.Into * 0.12f, 2.55f), new Vector3(0.16f, 0.2f, 0.04f), Quaternion.LookRotation(Face(VillageLayout.Into)), paint[2] * 0.8f);
            }
            var rot = Quaternion.LookRotation(Face(VillageLayout.Into));
            mk.Box(X0Z(g, 2.88f), new Vector3(3.3f, 0.2f, 0.26f), rot, Jitter(wood, 0.06f));
            mk.Box(X0Z(g, 3.05f), new Vector3(2.4f, 0.14f, 0.2f), rot, paint[0]);
            MeshNode("Gate", mk.Build("Gate"), _vc, _root);
            foreach (var k in new[] { -1f, 1f })
                Lantern(X0Z(g + right * k * 1.35f - VillageLayout.Into * 0.25f, 2.35f), 1.05f, k + 2f, k > 0f ? 7f : 0f, _root);
            GroundPool(g, 4.5f, 4.5f, new Color(1f, 0.56f, 0.2f), 0.16f, 1f, _root);
        }

        void BuildHuts()
        {
            var muds = new[] { new Color(0.5f, 0.34f, 0.22f), new Color(0.56f, 0.4f, 0.27f), new Color(0.45f, 0.3f, 0.2f), new Color(0.6f, 0.45f, 0.3f) };
            for (var i = 0; i < VillageLayout.HutAngles.Length; i++)
            {
                var at = VillageLayout.HutAt(i);
                var hut = Node("Village Hut", _root);
                hut.localPosition = X0Z(at);
                hut.localRotation = Quaternion.LookRotation(Face(VillageLayout.Plaza - at));
                var s = 0.85f + R() * 0.25f;
                var mud = muds[i % muds.Length];
                RoundHut(hut, new HutShape
                {
                    RIn = VillageLayout.HutRadius * s - 0.25f, ROut = VillageLayout.HutRadius * s, WallH = 2f, DoorH = 1.75f, DoorHalfAngle = 15f,
                    Eave = VillageLayout.HutRadius * s + 0.6f, EaveY = 1.8f, ApexY = 3.9f + R() * 0.6f,
                    Mud = mud, MudDark = mud * 0.62f, Thatch = Jitter(new Color(0.7f, 0.52f, 0.28f), 0.1f), Floor = false,
                }, 32);
                // warm light inside the doorway: someone's home
                var inside = GlowMat(new Color(1f, 0.55f, 0.22f), 0.55f);
                GlowCard("Doorway Glow", hut.localPosition + hut.localRotation * new Vector3(0f, 0.9f, VillageLayout.HutRadius * s - 0.6f), 1.5f, inside, _root);
                _flicker.Add((inside, 0.55f, i * 2.1f));
            }
        }

        /// <summary>Strings of little lights from hut to hut round the far side, and over the plaza to a pole by the fire.</summary>
        void BuildLights()
        {
            var warm = new List<Vector3>(); var colour = new List<Vector3>();
            void String(Vector3 a, Vector3 b, float sag, int n)
            {
                for (var k = 1; k < n; k++)
                {
                    var t = k / (float)n;
                    var p = Vector3.Lerp(a, b, t) - Vector3.up * sag * 4f * t * (1f - t);
                    (k % 3 == 0 ? colour : warm).Add(p);
                }
            }
            var pole = VillageLayout.At(5.2f, -0.5f);
            var mk = new MeshKit();
            mk.Box(X0Z(pole, 2.6f), new Vector3(0.12f, 5.2f, 0.12f), Quaternion.identity, new Color(0.28f, 0.19f, 0.11f));
            MeshNode("Light Pole", mk.Build("Light Pole"), _vc, _root);
            var top = X0Z(pole, 5.1f);
            for (var i = 1; i < VillageLayout.HutAngles.Length - 1; i++)
            {
                var a = X0Z(VillageLayout.HutAt(i), 2.3f);
                var b = X0Z(VillageLayout.HutAt(i + 1), 2.3f);
                if (i + 1 < VillageLayout.HutAngles.Length - 1) String(a, b, 0.6f, 14);
                String(a, top, 1.2f, 18);
            }
            Bulbs("Light String", warm, new Color(1f, 0.78f, 0.42f), 0.11f);
            Bulbs("Light String (colour)", colour, new Color(1f, 0.35f, 0.25f), 0.1f);
        }

        /// <summary>Many small glowing dots in one draw: the particles shader, held still.</summary>
        void Bulbs(string name, List<Vector3> at, Color c, float size)
        {
            if (at.Count == 0) return;
            var m = new Material(_a.particles) { name = name, color = c };
            m.SetFloat("_Size", size); m.SetFloat("_Alpha", 1f); m.SetFloat("_Speed", 0f);
            m.SetFloat("_Blink", 0f); m.SetFloat("_Ember", 0f); m.SetFloat("_Scale", 0f);
            var i = 0;
            MeshNode(name, StoryMeshes.Billboards(at.Count, 5, r => at[i++], 1f), m, _root);
        }

        void BuildFence()
        {
            // a woven fence across the plaza: stakes with bands of wattle, rope at the top, a little past the walkable edge
            var mk = new MeshKit();
            var stake = new Color(0.32f, 0.22f, 0.13f);
            var weave = new Color(0.42f, 0.32f, 0.18f);
            var half = Mathf.Sqrt(VillageLayout.PlazaWalk * VillageLayout.PlazaWalk - VillageLayout.FenceAt * VillageLayout.FenceAt) + 1.6f;
            var line = VillageLayout.Plaza + VillageLayout.Into * (VillageLayout.FenceAt + 0.35f);
            var right = VillageLayout.Right;
            var rot = Quaternion.LookRotation(Face(right));
            for (var x = -half; x <= half + 0.01f; x += 0.42f)
                mk.Box(X0Z(line + right * x, 0.55f), new Vector3(0.06f, 1.1f + R() * 0.12f, 0.06f), Quaternion.Euler((R() - 0.5f) * 5f, R() * 90f, (R() - 0.5f) * 5f), Jitter(stake, 0.12f));
            foreach (var y in new[] { 0.3f, 0.58f, 0.86f })
                for (var x = -half; x < half; x += 1.3f)
                    mk.Box(X0Z(line + right * (x + 0.65f), y), new Vector3(0.05f, 0.13f, 1.32f), rot * Quaternion.Euler(0f, 0f, (R() - 0.5f) * 4f), Jitter(weave, 0.15f));
            mk.Beam(X0Z(line - right * half, 1.08f), X0Z(line + right * half, 1.08f), 0.03f, new Color(0.55f, 0.46f, 0.3f));
            MeshNode("Fence", mk.Build("Fence"), _vc, _root);
        }

        void BuildFire()
        {
            _fire = new StoryFire(_root, X0Z(VillageLayout.Fire, 0.02f), _a, _glow);
            _fire.Root.localScale = Vector3.one * 1.15f;
            var sparks = new Material(_a.particles) { name = "Village Sparks", color = new Color(1f, 0.6f, 0.22f) };
            sparks.SetFloat("_Size", 0.05f); sparks.SetFloat("_Alpha", 1f); sparks.SetFloat("_Speed", 0.9f);
            sparks.SetFloat("_Blink", 0f); sparks.SetFloat("_Ember", 1f); sparks.SetFloat("_Scale", 1.1f);
            sparks.SetVector("_EmberBase", new Vector4(VillageLayout.Fire.x, 0.9f, VillageLayout.Fire.y, 0f));
            MeshNode("Village Sparks", StoryMeshes.Billboards(40, 41, r => Vector3.zero), sparks, _root);
            GroundPool(VillageLayout.Fire, 7f, 7f, new Color(1f, 0.5f, 0.18f), 0.2f, 0.7f, _root);
        }

        // ---------- people ----------

        struct Look
        {
            public Color Skin, Top, Wrap, Hat;
            public int Headwear; // 0 none, 1 a tall head wrap (gele), 2 a soft cap (fila)
            public bool Robe;    // a long robe to the ground
        }

        static readonly Color[] Cloth =
        {
            new Color(0.75f, 0.2f, 0.12f), new Color(0.9f, 0.62f, 0.12f), new Color(0.15f, 0.35f, 0.65f), new Color(0.18f, 0.5f, 0.28f),
            new Color(0.55f, 0.18f, 0.45f), new Color(0.92f, 0.85f, 0.7f), new Color(0.85f, 0.4f, 0.12f), new Color(0.2f, 0.55f, 0.6f),
        };

        Look RandomLook(bool woman)
        {
            var skin = Jitter(new Color(0.3f, 0.19f, 0.12f), 0.18f);
            return new Look
            {
                Skin = skin,
                Top = Cloth[(int)(R() * Cloth.Length) % Cloth.Length],
                Wrap = Cloth[(int)(R() * Cloth.Length) % Cloth.Length],
                Hat = Cloth[(int)(R() * Cloth.Length) % Cloth.Length],
                Headwear = woman ? 1 : (R() < 0.6f ? 2 : 0),
                Robe = woman && R() < 0.5f,
            };
        }

        /// <summary>A person of a few boxes: legs (or a wrapper, or a robe), a torso that bends at the hips, a head, two arms from the shoulders.</summary>
        Figure Person(string name, Vector2 at, Vector2 facing, Look look, float scale = 1f, bool seated = false, bool ownHead = false)
        {
            var f = new Figure { Ph = R() * 6.28f };
            f.Root = Node(name, _root);
            f.Root.localPosition = X0Z(at);
            f.Root.localRotation = Quaternion.LookRotation(Face(facing));
            f.Root.localScale = Vector3.one * scale;
            f.Home = f.Root.localPosition;
            var hipY = seated ? 0.48f : 0.86f;

            var legs = new MeshKit();
            if (seated)
            {
                foreach (var k in new[] { -1f, 1f })
                {
                    legs.Box(new Vector3(k * 0.1f, 0.47f, 0.2f), new Vector3(0.13f, 0.13f, 0.42f), Quaternion.identity, look.Wrap);
                    legs.Box(new Vector3(k * 0.1f, 0.22f, 0.4f), new Vector3(0.11f, 0.46f, 0.11f), Quaternion.identity, look.Wrap * 0.9f);
                    legs.Box(new Vector3(k * 0.1f, 0.02f, 0.45f), new Vector3(0.1f, 0.04f, 0.2f), Quaternion.identity, look.Skin * 0.8f);
                }
                legs.Cylinder(new Vector3(0f, 0f, -0.02f), 0.16f, 0.15f, 0.42f, new Color(0.3f, 0.2f, 0.12f), new Color(0.36f, 0.25f, 0.15f), 8); // the stool
            }
            else if (look.Robe)
                legs.Cylinder(Vector3.zero, 0.27f, 0.2f, hipY + 0.05f, look.Wrap, look.Wrap, 8);
            else
                foreach (var k in new[] { -1f, 1f })
                {
                    legs.Box(new Vector3(k * 0.09f, hipY * 0.5f, 0f), new Vector3(0.12f, hipY, 0.13f), Quaternion.identity, look.Wrap);
                    legs.Box(new Vector3(k * 0.09f, 0.025f, 0.04f), new Vector3(0.1f, 0.05f, 0.2f), Quaternion.identity, look.Skin * 0.8f);
                }
            MeshNode(name + " Legs", legs.Build("Legs"), _vc, f.Root);

            f.Torso = Node(name + " Torso", f.Root);
            f.Torso.localPosition = new Vector3(0f, hipY, 0f);
            var body = new MeshKit();
            body.Box(new Vector3(0f, 0.27f, 0f), new Vector3(0.36f, 0.56f, 0.21f), Quaternion.identity, look.Top);
            body.Box(new Vector3(0f, 0.03f, 0f), new Vector3(0.38f, 0.08f, 0.23f), Quaternion.identity, look.Wrap * 0.85f); // a sash
            body.Box(new Vector3(0f, 0.59f, 0f), new Vector3(0.1f, 0.08f, 0.1f), Quaternion.identity, look.Skin);            // neck
            var headMk = ownHead ? new MeshKit() : body;
            var hc = ownHead ? Vector3.zero : new Vector3(0f, 0.74f, 0f);
            headMk.Box(hc, new Vector3(0.19f, 0.23f, 0.21f), Quaternion.identity, look.Skin);
            headMk.Box(hc + new Vector3(0f, -0.02f, 0.105f), new Vector3(0.05f, 0.05f, 0.03f), Quaternion.identity, look.Skin * 0.85f); // nose
            if (look.Headwear == 1)
            {
                headMk.Box(hc + new Vector3(0f, 0.15f, -0.01f), new Vector3(0.26f, 0.12f, 0.25f), Quaternion.Euler(-8f, 0f, 0f), look.Hat);
                headMk.Box(hc + new Vector3(0.05f, 0.24f, -0.03f), new Vector3(0.3f, 0.12f, 0.18f), Quaternion.Euler(-15f, 20f, 18f), look.Hat * 1.1f);
            }
            else if (look.Headwear == 2)
                headMk.Box(hc + new Vector3(0.02f, 0.14f, -0.02f), new Vector3(0.21f, 0.1f, 0.22f), Quaternion.Euler(-10f, 0f, -12f), look.Hat);
            else
                headMk.Box(hc + new Vector3(0f, 0.115f, -0.01f), new Vector3(0.2f, 0.03f, 0.21f), Quaternion.identity, new Color(0.05f, 0.04f, 0.04f)); // short hair
            MeshNode(name + " Body", body.Build("Body"), _vc, f.Torso);
            if (ownHead)
            {
                f.Head = Node(name + " Head", f.Torso);
                f.Head.localPosition = new Vector3(0f, 0.74f, 0f);
                MeshNode(name + " Head", headMk.Build("Head"), _vc, f.Head);
            }
            Transform Arm(float k)
            {
                var arm = Node(name + " Arm", f.Torso);
                arm.localPosition = new Vector3(k * 0.23f, 0.5f, 0f);
                var am = new MeshKit();
                am.Box(new Vector3(0f, -0.15f, 0f), new Vector3(0.1f, 0.32f, 0.11f), Quaternion.identity, look.Top);
                am.Box(new Vector3(0f, -0.43f, 0f), new Vector3(0.08f, 0.26f, 0.09f), Quaternion.identity, look.Skin);
                am.Box(new Vector3(0f, -0.6f, 0.01f), new Vector3(0.08f, 0.09f, 0.05f), Quaternion.identity, look.Skin * 0.9f);
                MeshNode(name + " Arm", am.Build("Arm"), _vc, arm);
                return arm;
            }
            f.ArmL = Arm(-1f); f.ArmR = Arm(1f);
            return f;
        }

        void BuildCrowd()
        {
            // the yam pile and the baskets they sort into
            var pile = new MeshKit();
            var yam = new Color(0.4f, 0.27f, 0.16f);
            for (var i = 0; i < 70; i++)
            {
                var a = R() * Mathf.PI * 2f; var rr = Mathf.Sqrt(R()) * 0.85f;
                var y = (0.85f - rr) * 0.55f * R() + 0.06f;
                pile.Box(X0Z(VillageLayout.YamPile + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * rr, y), new Vector3(0.1f, 0.1f, 0.32f + R() * 0.15f),
                    Quaternion.Euler((R() - 0.5f) * 30f, R() * 360f, (R() - 0.5f) * 30f), Jitter(yam, 0.2f));
            }
            for (var b = 0; b < 4; b++)
            {
                var a = b / 4f * Mathf.PI * 2f + 0.4f;
                var at = VillageLayout.YamPile + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 1.75f;
                pile.Cylinder(X0Z(at), 0.2f, 0.27f, 0.3f, Jitter(new Color(0.6f, 0.45f, 0.25f), 0.1f), new Color(0.35f, 0.24f, 0.14f), 9);
            }
            MeshNode("Yams", pile.Build("Yams"), _vc, _root);

            // the sorters, round the pile, bending to it
            for (var i = 0; i < 7; i++)
            {
                var a = i / 7f * Mathf.PI * 2f + R() * 0.3f;
                var at = VillageLayout.YamPile + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (1.25f + R() * 0.25f);
                _sorters.Add(Person("Villager", at, VillageLayout.YamPile - at, RandomLook(i % 3 != 1), 0.95f + R() * 0.1f));
            }
            // children chasing each other round it
            for (var i = 0; i < 2; i++)
            {
                var kid = Person("Child", VillageLayout.YamPile, Vector2.up, RandomLook(i == 0), 0.62f);
                kid.Ph = i * Mathf.PI * 0.85f;
                _kids.Add(kid);
            }
        }

        void BuildPounders()
        {
            // a tall wooden mortar between two women with long pestles
            var at = VillageLayout.At(3.2f, 2.1f);
            var mk = new MeshKit();
            mk.Cylinder(X0Z(at), 0.24f, 0.3f, 0.62f, new Color(0.34f, 0.22f, 0.13f), new Color(0.88f, 0.84f, 0.7f), 10); // pounded yam, white, in the bowl
            MeshNode("Mortar", mk.Build("Mortar"), _vc, _root);
            var across = VillageLayout.Right;
            for (var i = 0; i < 2; i++)
            {
                var k = i == 0 ? -1f : 1f;
                var spot = at + across * k * 0.62f;
                var p = Person("Pounder", spot, at - spot, RandomLook(true));
                p.Ph = i * Mathf.PI;
                _pounders.Add(p);
                var pestle = new MeshKit();
                pestle.Box(new Vector3(0f, 0f, 0f), new Vector3(0.07f, 1.7f, 0.07f), Quaternion.identity, new Color(0.5f, 0.38f, 0.24f));
                var t = MeshNode("Pestle", pestle.Build("Pestle"), _vc, _root);
                _pestles.Add(t);
            }
        }

        void BuildDrummers()
        {
            var drumCol = new[] { new Color(0.42f, 0.26f, 0.14f), new Color(0.5f, 0.34f, 0.2f), new Color(0.33f, 0.2f, 0.11f) };
            for (var i = 0; i < 3; i++)
            {
                var a = (-40f + i * 40f) * Mathf.Deg2Rad;
                var dir = VillageLayout.Into * Mathf.Cos(a) + VillageLayout.Right * Mathf.Sin(a);
                var at = VillageLayout.Fire + dir * 3.1f;
                var d = Person("Drummer", at, VillageLayout.Fire - at, RandomLook(false), 1f, true);
                _drummers.Add(d);
                var drum = new MeshKit();
                var front = d.Root.localPosition + d.Root.localRotation * new Vector3(0f, 0f, 0.62f);
                var h = i == 2 ? 0.75f : 0.62f;
                var r = i == 2 ? 0.26f : i == 1 ? 0.15f : 0.22f;
                drum.Cylinder(front, r * 0.7f, r, h, drumCol[i], new Color(0.82f, 0.72f, 0.55f), 10);
                drum.Box(front + Vector3.up * h * 0.55f, new Vector3(r * 2.05f, 0.04f, r * 2.05f), Quaternion.identity, new Color(0.7f, 0.15f, 0.1f)); // a red band
                MeshNode("Drum", drum.Build("Drum"), _vc, _root);
            }
            // two dancers between the drummers and the fire
            for (var i = 0; i < 2; i++)
            {
                var at = VillageLayout.Fire + VillageLayout.Into * 1.8f + VillageLayout.Right * (i == 0 ? -1.4f : 1.4f) - VillageLayout.Into * 3.9f;
                var dn = Person("Dancer", at, VillageLayout.Fire - at, RandomLook(true));
                dn.Ph = i * 1.3f;
                _dancers.Add(dn);
            }
        }

        void BuildMasquerade()
        {
            _masq = Node("Masquerade", _root);
            _masq.localPosition = X0Z(VillageLayout.Fire + VillageLayout.Right * 2.6f);
            _masqMesh = new Mesh { name = "Masquerade Cloth" };
            _masqMesh.MarkDynamic();
            _masqV = new Vector3[Strips * 8];
            _masqC = new Color[Strips * 8];
            var tris = new int[Strips * 12];
            var palette = new[] { new Color(0.85f, 0.15f, 0.1f), new Color(0.95f, 0.75f, 0.15f), new Color(0.1f, 0.45f, 0.25f), new Color(0.92f, 0.9f, 0.82f), new Color(0.15f, 0.2f, 0.55f), new Color(0.9f, 0.4f, 0.1f) };
            for (var i = 0; i < Strips; i++)
            {
                var c = palette[i % palette.Length];
                var back = c * 0.6f; back.a = 1f;
                for (var k = 0; k < 4; k++) { _masqC[i * 8 + k] = c; _masqC[i * 8 + 4 + k] = back; }
                var v = i * 8; var t = i * 12;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                tris[t + 6] = v + 4; tris[t + 7] = v + 6; tris[t + 8] = v + 5; tris[t + 9] = v + 4; tris[t + 10] = v + 7; tris[t + 11] = v + 6;
            }
            UpdateMasqueradeMesh(0f);
            _masqMesh.colors = _masqC;
            _masqMesh.triangles = tris;
            _masqMesh.RecalculateNormals();
            _masqMesh.bounds = new Bounds(new Vector3(0f, 1.3f, 0f), Vector3.one * 4f);
            var cloth = MeshNode("Masquerade Cloth", _masqMesh, _vc, _masq);
            cloth.gameObject.name = "Masquerade Cloth";
            // the crown: a carved headpiece with a crest
            var crown = new MeshKit();
            crown.Cylinder(new Vector3(0f, 2.05f, 0f), 0.32f, 0.22f, 0.3f, new Color(0.25f, 0.16f, 0.1f), new Color(0.3f, 0.2f, 0.12f), 10);
            crown.Box(new Vector3(0f, 2.55f, 0f), new Vector3(0.08f, 0.5f, 0.32f), Quaternion.identity, new Color(0.82f, 0.18f, 0.12f));
            crown.Box(new Vector3(0f, 2.25f, 0.24f), new Vector3(0.2f, 0.18f, 0.03f), Quaternion.identity, new Color(0.9f, 0.88f, 0.8f)); // the carved face
            MeshNode("Masquerade Crown", crown.Build("Masquerade Crown"), _vc, _masq);
        }

        /// <summary>The cloth strips hang from a ring under the crown and flare out as the masquerade spins.</summary>
        void UpdateMasqueradeMesh(float flare)
        {
            const float top = 2.05f, ringR = 0.3f, len = 1.95f, w = 0.13f;
            for (var i = 0; i < Strips; i++)
            {
                var a = i / (float)Strips * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var tangent = new Vector3(dir.z, 0f, -dir.x);
                var wave = Mathf.Sin(_t * 6f + i * 0.9f) * 0.06f;
                var out_ = Mathf.Sin(Mathf.Clamp01(flare + wave) * 1.25f);
                var bottom = dir * (ringR + 0.12f + out_ * len) + Vector3.up * (top - Mathf.Cos(Mathf.Clamp01(flare + wave) * 1.25f) * len)
                             - tangent * flare * 0.35f; // they trail behind the spin
                var t0 = dir * ringR + Vector3.up * top;
                var v = i * 8;
                _masqV[v] = t0 - tangent * w; _masqV[v + 1] = t0 + tangent * w; _masqV[v + 2] = bottom + tangent * w * 1.2f; _masqV[v + 3] = bottom - tangent * w * 1.2f;
                for (var k = 0; k < 4; k++) _masqV[v + 4 + k] = _masqV[v + k] - dir * 0.004f;
            }
            _masqMesh.vertices = _masqV;
            if (_masqMesh.triangles.Length > 0) _masqMesh.RecalculateNormals();
        }

        void BuildLocal()
        {
            var look = new Look
            {
                Skin = new Color(0.27f, 0.17f, 0.11f), Top = new Color(0.86f, 0.82f, 0.72f), Wrap = new Color(0.86f, 0.82f, 0.72f),
                Hat = new Color(0.45f, 0.12f, 0.15f), Headwear = 2, Robe = true,
            };
            _local = Person("The Local", VillageLayout.Local, VillageLayout.FrontOfGate - VillageLayout.Local, look, 1.02f, false, true);
            // a walking stick in the left hand
            var stick = new MeshKit();
            stick.Box(new Vector3(-0.32f, 0.62f, 0.12f), new Vector3(0.045f, 1.25f, 0.045f), Quaternion.Euler(0f, 0f, -4f), new Color(0.3f, 0.2f, 0.11f));
            MeshNode("Walking Stick", stick.Build("Walking Stick"), _vc, _local.Root);
            _local.ArmL.localRotation = Quaternion.Euler(-12f, 0f, -8f);
            // a lantern on a post beside them, so their face shows
            var post = VillageLayout.Local + VillageLayout.Right * -0.85f - VillageLayout.Into * 0.75f;
            var mk = new MeshKit();
            mk.Box(X0Z(post, 0.9f), new Vector3(0.07f, 1.8f, 0.07f), Quaternion.identity, new Color(0.3f, 0.2f, 0.12f));
            MeshNode("Local Lantern Post", mk.Build("Lantern Post"), _vc, _root);
            Lantern(X0Z(post, 1.88f), 1f, 4.4f, 5f, _root);
            VertexLight(X0Z(VillageLayout.Local - VillageLayout.Into * 1.1f, 1.7f), 2.6f, 0.9f, new Color(1f, 0.7f, 0.45f), 4.9f, _root); // on their face
            GroundPool(VillageLayout.Local, 3f, 3f, new Color(1f, 0.58f, 0.24f), 0.14f, 4.4f, _root);
        }

        /// <summary>A woven mat by the local with things to learn the words for, later (3J): a basket of yams, a coil of rope, a calabash.</summary>
        void BuildMat()
        {
            var at = VillageLayout.Local + VillageLayout.Right * 1.1f - VillageLayout.Into * 0.25f;
            var rot = Quaternion.LookRotation(Face(VillageLayout.Into));
            var mk = new MeshKit();
            for (var s = 0; s < 6; s++)
                mk.Box(X0Z(at, 0.012f) + rot * new Vector3(-0.45f + s * 0.18f, 0f, 0f), new Vector3(0.17f, 0.012f, 0.8f), rot, s % 2 == 0 ? new Color(0.62f, 0.5f, 0.3f) : new Color(0.5f, 0.38f, 0.22f));
            // the basket of yams
            var basket = X0Z(at, 0.02f) + rot * new Vector3(-0.25f, 0f, 0.1f);
            mk.Cylinder(basket, 0.15f, 0.21f, 0.2f, new Color(0.66f, 0.5f, 0.28f), new Color(0.3f, 0.2f, 0.12f), 10);
            for (var y = 0; y < 4; y++)
                mk.Box(basket + new Vector3((R() - 0.5f) * 0.18f, 0.22f + R() * 0.04f, (R() - 0.5f) * 0.18f), new Vector3(0.08f, 0.08f, 0.26f), Quaternion.Euler(0f, R() * 180f, 10f), Jitter(new Color(0.4f, 0.27f, 0.16f), 0.15f));
            // a coil of rope
            var rope = X0Z(at, 0.03f) + rot * new Vector3(0.2f, 0f, 0.2f);
            for (var k = 0; k < 14; k++)
            {
                float a0 = k / 14f * Mathf.PI * 2f, a1 = (k + 1) / 14f * Mathf.PI * 2f;
                foreach (var rr in new[] { 0.09f, 0.13f })
                    mk.Beam(rope + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * rr, rope + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * rr, 0.03f, new Color(0.6f, 0.5f, 0.32f));
            }
            // a calabash
            var gourd = X0Z(at, 0.02f) + rot * new Vector3(0.22f, 0f, -0.2f);
            mk.Cylinder(gourd, 0.06f, 0.16f, 0.1f, new Color(0.7f, 0.55f, 0.28f), new Color(0.75f, 0.6f, 0.32f), 10);
            mk.Cylinder(gourd + Vector3.up * 0.1f, 0.16f, 0.07f, 0.11f, new Color(0.72f, 0.57f, 0.3f), new Color(0.2f, 0.14f, 0.08f), 10);
            MeshNode("Mat and Things", mk.Build("Mat and Things"), _vc, _root);
        }

        void BuildAnimals()
        {
            BuildDog();
            // a goat on a tether by a hut, on your side
            var stake = VillageLayout.At(-3.2f, 5.6f);
            var mk = new MeshKit();
            mk.Box(X0Z(stake, 0.25f), new Vector3(0.05f, 0.5f, 0.05f), Quaternion.identity, new Color(0.3f, 0.2f, 0.12f));
            MeshNode("Goat Stake", mk.Build("Stake"), _vc, _root);
            _goat = Node("Goat", _root);
            _goat.localPosition = X0Z(stake + VillageLayout.Right * 0.7f);
            _goat.localRotation = Quaternion.LookRotation(Face(-VillageLayout.Right));
            var coat = new Color(0.62f, 0.55f, 0.45f);
            var g = new MeshKit();
            g.Box(new Vector3(0f, 0.45f, 0f), new Vector3(0.24f, 0.24f, 0.55f), Quaternion.identity, coat);
            foreach (var x in new[] { -0.08f, 0.08f }) foreach (var z in new[] { -0.2f, 0.2f })
                g.Box(new Vector3(x, 0.17f, z), new Vector3(0.05f, 0.34f, 0.05f), Quaternion.identity, coat * 0.8f);
            g.Box(new Vector3(0f, 0.56f, -0.29f), new Vector3(0.05f, 0.1f, 0.05f), Quaternion.Euler(-30f, 0f, 0f), coat); // tail
            MeshNode("Goat Body", g.Build("Goat"), _vc, _goat);
            _goatHead = Node("Goat Head", _goat);
            _goatHead.localPosition = new Vector3(0f, 0.55f, 0.27f);
            var h = new MeshKit();
            h.Box(new Vector3(0f, 0.04f, 0.1f), new Vector3(0.12f, 0.13f, 0.22f), Quaternion.Euler(20f, 0f, 0f), coat * 0.95f);
            h.Box(new Vector3(-0.04f, 0.13f, 0.03f), new Vector3(0.025f, 0.12f, 0.025f), Quaternion.Euler(-35f, 0f, -10f), new Color(0.25f, 0.22f, 0.18f)); // horns
            h.Box(new Vector3(0.04f, 0.13f, 0.03f), new Vector3(0.025f, 0.12f, 0.025f), Quaternion.Euler(-35f, 0f, 10f), new Color(0.25f, 0.22f, 0.18f));
            h.Box(new Vector3(0f, -0.07f, 0.17f), new Vector3(0.03f, 0.07f, 0.03f), Quaternion.identity, coat * 0.7f); // beard
            MeshNode("Goat Head", h.Build("Goat Head"), _vc, _goatHead);
            _bleat = ProceduralClips.Clip("bleat", ProceduralClips.BleatSamples(141));
            _goatVoice = Voice("Goat", _goatHead, 18f);

            // chickens pecking about on your side
            for (var i = 0; i < 4; i++)
            {
                var hen = new Hen { T = Node("Chicken", _root) };
                hen.T.localPosition = X0Z(VillageLayout.At(-2.5f + R() * 2f, 2.5f + R() * 2.5f));
                var feathers = i % 2 == 0 ? new Color(0.55f, 0.32f, 0.16f) : new Color(0.85f, 0.82f, 0.75f);
                var c = new MeshKit();
                c.Box(new Vector3(0f, 0.17f, 0f), new Vector3(0.14f, 0.13f, 0.2f), Quaternion.Euler(-10f, 0f, 0f), feathers);
                c.Box(new Vector3(0f, 0.23f, -0.11f), new Vector3(0.08f, 0.1f, 0.06f), Quaternion.Euler(-30f, 0f, 0f), feathers * 0.85f); // tail
                c.Box(new Vector3(-0.025f, 0.05f, 0f), new Vector3(0.015f, 0.1f, 0.015f), Quaternion.identity, new Color(0.8f, 0.6f, 0.2f));
                c.Box(new Vector3(0.025f, 0.05f, 0f), new Vector3(0.015f, 0.1f, 0.015f), Quaternion.identity, new Color(0.8f, 0.6f, 0.2f));
                MeshNode("Chicken Body", c.Build("Chicken"), _vc, hen.T);
                hen.Head = Node("Chicken Head", hen.T);
                hen.Head.localPosition = new Vector3(0f, 0.24f, 0.09f);
                var hk = new MeshKit();
                hk.Box(new Vector3(0f, 0.03f, 0.02f), new Vector3(0.06f, 0.07f, 0.07f), Quaternion.identity, feathers);
                hk.Box(new Vector3(0f, 0.075f, 0.02f), new Vector3(0.015f, 0.04f, 0.05f), Quaternion.identity, new Color(0.8f, 0.12f, 0.1f)); // comb
                hk.Box(new Vector3(0f, 0.025f, 0.07f), new Vector3(0.02f, 0.02f, 0.03f), Quaternion.identity, new Color(0.85f, 0.65f, 0.2f)); // beak
                MeshNode("Chicken Head", hk.Build("Chicken Head"), _vc, hen.Head);
                hen.Goal = hen.T.localPosition;
                hen.Next = R() * 3f;
                _hens.Add(hen);
            }
        }

        void BuildSounds()
        {
            // the drums: heard from halfway down the trail, muffled by the trees until you're near
            _drums = Bed("Drums", ProceduralClips.Clip("drums", ProceduralClips.DrumLoopSamples()), X0Z(VillageLayout.Fire, 0.8f), 0.85f, 5f, 34f);
            _drums.time = 0f;
            _drumsMuffle = _drums.gameObject.AddComponent<AudioLowPassFilter>();
            Bed("Crowd", ProceduralClips.Clip("murmur", ProceduralClips.MurmurSamples()), X0Z(VillageLayout.YamPile, 1.4f), 0.45f, 3f, 22f);
            Bed("Village Fire", ProceduralClips.Clip("village fire", ProceduralClips.FireSamples()), X0Z(VillageLayout.Fire, 0.4f), 0.6f, 1.5f, 12f);
            _thudClip = ProceduralClips.Clip("pestle", ProceduralClips.StepSamples(151));
            _thud = Voice("Pestles", _root, 18f);
            _thud.transform.localPosition = X0Z(VillageLayout.At(3.2f, 2.1f), 0.5f);
        }

        // ---------- every frame ----------

        void Update()
        {
            var cam = Tick();
            if (cam == null) return;
            var dt = Time.deltaTime;
            var dist = Vector2.Distance(Viewer, VillageLayout.Plaza);
            // the drums come clear as you come out of the trees
            _drumsMuffle.cutoffFrequency = Mathf.Lerp(900f, 22000f, Mathf.Pow(Mathf.Clamp01(1f - (dist - 8f) / 26f), 2f));
            var seen = dist < ShowWithin;
            if (_root.gameObject.activeSelf != seen) _root.gameObject.SetActive(seen);
            if (!seen) return;

            _fire.Update(cam, _t, 1f);
            var beat = _drums.isPlaying ? _drums.time : _t % ProceduralClips.DrumLoopLength;
            Drummers(beat);
            Pounders(beat);
            Crowd(dt);
            Dancers(beat);
            Masquerade(dt, beat);
            Local(cam, dt);
            Animals(cam, dt);
        }

        /// <summary>The time since drum <paramref name="drum"/>'s last stroke at <paramref name="loopT"/> in the loop, and which stroke it was.</summary>
        (float since, int index) LastHit(int drum, float loopT)
        {
            var best = -1; var since = 99f;
            for (var i = 0; i < _pattern.Length; i++)
            {
                if (_pattern[i].Drum != drum) continue;
                var d = loopT - _pattern[i].T;
                if (d < 0f) d += ProceduralClips.DrumLoopLength;
                if (d < since) { since = d; best = i; }
            }
            return (since, best);
        }

        void Drummers(float loopT)
        {
            for (var k = 0; k < _drummers.Count; k++)
            {
                var d = _drummers[k];
                var drum = k == 0 ? 0 : k == 1 ? 1 : 2;
                var (since, idx) = LastHit(drum, loopT);
                // a stroke: the hand comes down onto the skin, then lifts; the hands take turns
                var strike = Mathf.Exp(-since * 14f);
                var lift = 1f - strike;
                var right = idx % 2 == 0;
                float Rest(bool striking) => striking ? -55f + 30f * strike - 25f * Mathf.Clamp01(lift) : -60f;
                d.ArmR.localRotation = Quaternion.Euler(Rest(right), 0f, 8f);
                d.ArmL.localRotation = Quaternion.Euler(Rest(!right), 0f, -8f);
                d.Torso.localRotation = Quaternion.Euler(14f + strike * 5f, Mathf.Sin(_t * 1.6f + d.Ph) * 6f, 0f);
            }
        }

        void Pounders(float loopT)
        {
            // each pestle falls every second; the two women take turns, on the beat
            const float period = 1f;
            for (var i = 0; i < _pounders.Count; i++)
            {
                var p = _pounders[i];
                var phase = Mathf.Repeat(loopT / period + i * 0.5f, 1f);           // 0 = the pestle lands
                var up = Mathf.Sin(Mathf.Clamp01(phase / 0.75f) * Mathf.PI * 0.5f); // lifted slowly, dropped fast
                if (phase > 0.75f) up = 1f - (phase - 0.75f) / 0.25f;
                p.ArmR.localRotation = p.ArmL.localRotation = Quaternion.Euler(-95f - up * 70f, 0f, 0f);
                p.Torso.localRotation = Quaternion.Euler(8f - up * 6f, 0f, 0f);
                // the pestle stands upright in the mortar, leaning a little toward her, and rises with her arms
                var bowl = X0Z(VillageLayout.At(3.2f, 2.1f), 0.62f);
                var toward = p.Root.localPosition - bowl; toward.y = 0f; toward.Normalize();
                var pestle = _pestles[i];
                pestle.localPosition = bowl + toward * 0.14f + Vector3.up * (0.85f + up * 0.55f);
                pestle.localRotation = Quaternion.AngleAxis(-7f, Vector3.Cross(Vector3.up, toward));
            }
            var beatNo = Mathf.FloorToInt(loopT / (period * 0.5f));
            if (beatNo != _lastPound)
            {
                if (_lastPound >= 0) { _thud.pitch = 0.5f + R() * 0.08f; _thud.PlayOneShot(_thudClip, 0.8f); }
                _lastPound = beatNo;
            }
        }

        void Crowd(float dt)
        {
            foreach (var f in _sorters)
            {
                // bend to the pile, pick a yam, straighten and turn to drop it in a basket
                var c = Mathf.Repeat(_t * 0.3f + f.Ph, 1f);
                var bend = Mathf.Sin(c * Mathf.PI * 2f) * 0.5f + 0.5f;
                f.Torso.localRotation = Quaternion.Euler(10f + bend * 45f, Mathf.Sin(c * Mathf.PI * 2f + 1f) * 25f, 0f);
                f.ArmR.localRotation = Quaternion.Euler(-20f - bend * 45f, 0f, 5f);
                f.ArmL.localRotation = Quaternion.Euler(-10f - bend * 30f + Mathf.Sin(_t * 1.3f + f.Ph) * 10f, 0f, -5f);
            }
            for (var i = 0; i < _kids.Count; i++)
            {
                var k = _kids[i];
                var a = _t * 0.9f + k.Ph;
                var r = 2.6f + Mathf.Sin(_t * 0.5f + i) * 0.3f;
                var p = VillageLayout.YamPile + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r;
                k.Root.localPosition = X0Z(p, Mathf.Abs(Mathf.Sin(_t * 9f + i)) * 0.06f);
                k.Root.localRotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)));
                var swing = Mathf.Sin(_t * 9f + i) * 45f;
                k.ArmL.localRotation = Quaternion.Euler(swing, 0f, -10f);
                k.ArmR.localRotation = Quaternion.Euler(-swing, 0f, 10f);
                k.Torso.localRotation = Quaternion.Euler(12f, 0f, 0f);
            }
        }

        void Dancers(float loopT)
        {
            var beat = loopT / 0.5f * Mathf.PI;
            foreach (var d in _dancers)
            {
                d.Root.localPosition = d.Home + d.Root.localRotation * new Vector3(Mathf.Sin(beat * 0.5f + d.Ph) * 0.35f, Mathf.Abs(Mathf.Sin(beat + d.Ph)) * 0.05f, 0f);
                d.Torso.localRotation = Quaternion.Euler(18f + Mathf.Sin(beat + d.Ph) * 10f, Mathf.Sin(beat * 0.5f + d.Ph) * 20f, Mathf.Sin(beat * 0.5f) * 8f);
                d.ArmL.localRotation = Quaternion.Euler(-120f + Mathf.Sin(beat + d.Ph) * 35f, 0f, -25f);
                d.ArmR.localRotation = Quaternion.Euler(-120f - Mathf.Sin(beat + d.Ph) * 35f, 0f, 25f);
            }
        }

        void Masquerade(float dt, float loopT)
        {
            // it circles the fire; in the last bar of the drums (their fill) it whirls and the cloth flares wide
            var fill = loopT > ProceduralClips.DrumLoopLength * 0.75f;
            _masqSpin = Mathf.MoveTowards(_masqSpin, fill ? 1f : 0.25f, dt * (fill ? 1.6f : 0.5f));
            _masqAngle += dt * (0.25f + _masqSpin * 0.3f);
            _masqYaw += dt * _masqSpin * 720f;
            var at = VillageLayout.Fire + new Vector2(Mathf.Sin(_masqAngle), Mathf.Cos(_masqAngle)) * 2.6f;
            _masq.localPosition = X0Z(at, Mathf.Abs(Mathf.Sin(loopT * Mathf.PI * 2f)) * 0.08f);
            _masq.localRotation = Quaternion.Euler(0f, _masqYaw, 0f);
            UpdateMasqueradeMesh(_masqSpin * 0.85f);
        }

        void Local(Transform cam, float dt)
        {
            var to = transform.InverseTransformPoint(cam.position) - _local.Root.localPosition;
            var flat = new Vector3(to.x, 0f, to.z);
            var near = flat.magnitude < 9f;
            // the head turns to you when you're near (not past the shoulder); the body breathes
            var localDir = Quaternion.Inverse(_local.Root.localRotation) * to;
            var yaw = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -70f, 70f);
            var pitch = Mathf.Clamp(-Mathf.Atan2(localDir.y - 1.6f, new Vector2(localDir.x, localDir.z).magnitude) * Mathf.Rad2Deg, -20f, 20f);
            var want = near ? Quaternion.Euler(pitch, yaw, 0f) : Quaternion.Euler(0f, Mathf.Sin(_t * 0.3f) * 25f, 0f);
            _local.Head.localRotation = Quaternion.Slerp(_local.Head.localRotation, want, 1f - Mathf.Exp(-3f * dt));
            _local.Torso.localRotation = Quaternion.Euler(4f + Mathf.Sin(_t * 1.1f) * 1.2f, yaw * 0.25f, 0f);
            // a wave, the first time you come near
            if (!_waved && flat.magnitude < 5.5f) { _waved = true; _waveT = 0f; }
            if (_waveT >= 0f)
            {
                _waveT += dt;
                var up = Mathf.Clamp01(Mathf.Min(_waveT * 3f, (2.6f - _waveT) * 3f));
                _local.ArmR.localRotation = Quaternion.Euler(-150f * up, 0f, 20f * up + Mathf.Sin(_waveT * 10f) * 18f * up);
                if (_waveT > 2.6f) { _waveT = -1f; _local.ArmR.localRotation = Quaternion.identity; }
            }
        }

        void BuildDog()
        {
            // on your side of the fence, a little aside from the elder, where you can walk up to it
            var at = VillageLayout.At(-0.35f, -4.1f);
            _dog = Node("Dog", _root);
            _dog.localPosition = X0Z(at);
            _dog.localRotation = Quaternion.LookRotation(Face(-VillageLayout.Into));
            var coat = new Color(0.45f, 0.32f, 0.18f);
            var pale = new Color(0.72f, 0.62f, 0.42f);
            var b = new MeshKit();
            b.Box(new Vector3(0f, 0.32f, 0f), new Vector3(0.16f, 0.16f, 0.38f), Quaternion.identity, coat);
            b.Box(new Vector3(0f, 0.28f, 0.02f), new Vector3(0.14f, 0.1f, 0.22f), Quaternion.identity, pale);
            foreach (var x in new[] { -0.05f, 0.05f }) foreach (var z in new[] { 0.12f, -0.12f })
                b.Box(new Vector3(x, 0.12f, z), new Vector3(0.04f, 0.24f, 0.04f), Quaternion.identity, coat * 0.85f);
            MeshNode("Dog Body", b.Build("Dog"), _vc, _dog);
            _dogHead = Node("Dog Head", _dog);
            _dogHead.localPosition = new Vector3(0f, 0.38f, 0.18f);
            var h = new MeshKit();
            h.Box(new Vector3(0f, 0.02f, 0.06f), new Vector3(0.1f, 0.1f, 0.12f), Quaternion.identity, coat);
            h.Box(new Vector3(0f, -0.02f, 0.1f), new Vector3(0.06f, 0.05f, 0.06f), Quaternion.identity, pale);
            h.Box(new Vector3(-0.045f, 0.08f, 0.02f), new Vector3(0.035f, 0.06f, 0.02f), Quaternion.Euler(0f, 0f, -15f), coat * 0.9f);
            h.Box(new Vector3(0.045f, 0.08f, 0.02f), new Vector3(0.035f, 0.06f, 0.02f), Quaternion.Euler(0f, 0f, 15f), coat * 0.9f);
            MeshNode("Dog Head", h.Build("Dog Head"), _vc, _dogHead);
            _dogTail = Node("Dog Tail", _dog);
            _dogTail.localPosition = new Vector3(0f, 0.38f, -0.18f);
            var tail = new MeshKit();
            tail.Box(new Vector3(0f, 0.06f, -0.06f), new Vector3(0.03f, 0.03f, 0.14f), Quaternion.Euler(-30f, 0f, 0f), coat * 0.8f);
            MeshNode("Dog Tail", tail.Build("Dog Tail"), _vc, _dogTail);
            _bark = ProceduralClips.Clip("bark", ProceduralClips.BarkSamples(181));
            _dogVoice = Voice("Dog", _dogHead, 14f);
        }

        void Dog(Transform cam, float dt)
        {
            var to = transform.InverseTransformPoint(cam.position) - _dog.localPosition;
            var flat = new Vector3(to.x, 0f, to.z);
            var near = flat.magnitude < 6f;
            var localDir = Quaternion.Inverse(_dog.localRotation) * to;
            var yaw = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -60f, 60f);
            var pitch = near ? -12f : 18f + Mathf.Sin(_t * 0.6f) * 4f;
            _dogHead.localRotation = Quaternion.Slerp(_dogHead.localRotation, Quaternion.Euler(pitch, near ? yaw : 0f, 0f), 1f - Mathf.Exp(-4f * dt));
            var wag = (near ? 28f : 10f) * Mathf.Sin(_t * (near ? 10f : 3f));
            _dogTail.localRotation = Quaternion.Euler(0f, 0f, wag);
            if (!_barked && flat.magnitude < 4.5f)
            {
                _barked = true;
                _dogVoice.pitch = 0.95f + R() * 0.1f;
                _dogVoice.PlayOneShot(_bark, 0.7f);
            }
        }

        void Animals(Transform cam, float dt)
        {
            Dog(cam, dt);
            // the goat grazes, looks up, bleats now and then
            var graze = Mathf.Sin(_t * 0.4f) > -0.2f;
            _goatHead.localRotation = Quaternion.Slerp(_goatHead.localRotation, graze ? Quaternion.Euler(55f + Mathf.Sin(_t * 5f) * 4f, 0f, 0f) : Quaternion.Euler(-5f, Mathf.Sin(_t * 0.7f) * 30f, 0f), 1f - Mathf.Exp(-3f * dt));
            if (_t > _nextBleat)
            {
                _nextBleat = _t + 9f + R() * 12f;
                _goatVoice.pitch = 0.92f + R() * 0.16f;
                _goatVoice.PlayOneShot(_bleat, 0.6f);
            }
            // chickens: a few quick steps, then peck, peck
            foreach (var h in _hens)
            {
                var to = h.Goal - h.T.localPosition;
                if (to.magnitude > 0.03f)
                {
                    h.T.localPosition += to.normalized * Mathf.Min(to.magnitude, dt * 0.6f);
                    h.T.localRotation = Quaternion.Slerp(h.T.localRotation, Quaternion.LookRotation(to.normalized), 1f - Mathf.Exp(-8f * dt));
                    h.Head.localRotation = Quaternion.Euler(Mathf.Sin(_t * 14f) * 10f, 0f, 0f);
                }
                else
                {
                    h.Head.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(_t * 7f + h.Next)) * 70f, 0f, 0f);
                    if (_t > h.Next)
                    {
                        h.Next = _t + 2f + R() * 4f;
                        var p = new Vector2(h.T.localPosition.x, h.T.localPosition.z) + new Vector2(R() - 0.5f, R() - 0.5f) * 1.6f;
                        if (!VillageLayout.InPlaza(p) || Vector2.Distance(p, VillageLayout.Plaza) < 2.5f) p = VillageLayout.At(-2.5f + R() * 2f, 2.5f + R() * 2.5f);
                        h.Goal = X0Z(p);
                    }
                }
            }
        }
    }
}
