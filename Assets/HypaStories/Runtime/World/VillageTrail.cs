using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The walk from the campfire to the village, on the right (see <see cref="VillageLayout"/>), built at runtime inside the camp:
    ///  - a wooden signpost, "VILLAGE" with its arrow pointing down the trail;
    ///  - a dirt trail through the pines with lanterns on posts;
    ///  - a stream across the trail with a plank bridge, moonlight glinting on the water, glowing mushrooms on its banks and a
    ///    chorus of frogs that falls silent as you come near and starts again once you pass;
    ///  - bats circling over the trail: one swoops low over your head;
    ///  - a bushbaby in a tree, big glowing eyes, that cries like a child and leaps to another branch when you come close;
    ///  - a shooting star now and then over the trees;
    ///  - a fish that jumps in the stream (two, if you wait on the bridge);
    ///  - a duiker grazing off the path that freezes, then bolts across into the trees;
    ///  - the village's drums, faint at the start, louder with every step (they play at the village, see <see cref="Village"/>).
    /// Same budget rules as the hut's trail: vertex colours, glow cards, per-vertex lights, synthesised sounds.
    /// </summary>
    public sealed class VillageTrail : TrailBuilder
    {
        // the stream
        readonly List<(Transform t, float speed, float off)> _glints = new List<(Transform, float, float)>();
        Vector2 _streamAt, _flow;
        readonly List<Material> _shrooms = new List<Material>();

        // frogs
        sealed class Frog { public Transform Sac; public float Next; public AudioSource Voice; public float Puff; public int Clip; }
        readonly List<Frog> _frogs = new List<Frog>();
        AudioClip[] _croaks;
        float _hushUntil;

        // bats
        sealed class Bat
        {
            public Transform Body, WingL, WingR;
            public float Radius, Height, Speed, Phase, Flap;
            public float SwoopT = -1f;
            public Vector3 SwoopFrom, SwoopOver, SwoopTo;
        }
        readonly List<Bat> _bats = new List<Bat>();
        Vector3 _batCentre;
        AudioClip[] _squeaks;
        AudioSource _batVoice;
        float _nextSqueak;
        bool _swooped;

        // the bushbaby
        Transform _galago;
        Material _galagoEyes;
        Vector3 _perchA, _perchB;
        bool _onB;
        float _leapT = -1f, _blinkAt, _calmUntil;
        AudioSource _galagoVoice;
        AudioClip _cry;

        // a fish in the stream
        Transform _fish;
        float _nextJump = 7f, _jumpT = -1f;
        Vector3 _jumpFrom, _jumpTo;
        bool _plopped, _followJump;
        AudioSource _plopVoice;
        AudioClip _plop;

        // a duiker beside the trail
        enum DuikerState { Grazing, Alert, Bolting, Gone }
        DuikerState _duikerState;
        Transform _duiker, _duikerHead;
        readonly Transform[] _duikerLegs = new Transform[4];
        Vector3 _duikerHome, _duikerFrom, _duikerTo;
        float _duikerT;
        AudioSource _duikerVoice;
        AudioClip _rustle;

        // shooting stars
        Transform _star;
        Material _starMat;
        float _nextStar = 14f, _starT = -1f;
        Vector3 _starFrom, _starTo;

        public void Build(StoryAssets a, Texture2D glow)
        {
            Init(a, glow, 31, "Village Trail");
            Ribbon("Village Trail", new List<Vector2>(VillageLayout.Path) { VillageLayout.Gate }, VillageLayout.PathHalf,
                new Color(0.165f, 0.13f, 0.095f), new Color(0.105f, 0.09f, 0.07f));
            Signpost("VILLAGE", VillageLayout.Sign, (VillageLayout.Path[1] - VillageLayout.Path[0]).normalized, 1.55f, 1.02f, 4f);
            LanternPosts(new[] { 0.12f, 0.24f, 0.5f, 0.62f, 0.75f, 0.88f }, VillageLayout.Along, VillageLayout.PathHalf);
            BuildStream();
            Undergrowth("Village Trail Undergrowth", VillageLayout.Along, VillageLayout.PathHalf,
                p => p.magnitude < 5.5f || Vector2.Distance(p, _streamAt) < 2.4f || Vector2.Distance(p, VillageLayout.Sign) < 0.5f || VillageLayout.NearVillage(p.x, p.y, -7f));
            TrailMist(VillageLayout.Along, 6, VillageLayout.PathHalf);
            BuildMushrooms();
            BuildFrogs();
            BuildBats();
            BuildGalago();
            BuildFish();
            BuildDuiker();
            BuildShootingStar();
            BuildFootsteps();
        }

        /// <summary>Footsteps on the trail. The plaza has its own (the village), so they don't double up at the gate.</summary>
        protected override bool OnGround(Vector2 v) =>
            v.magnitude > 5f && VillageLayout.DistanceToPath(v) < VillageLayout.WalkHalf + 0.3f && !VillageLayout.InPlaza(v);

        // ---------- the stream and the bridge ----------

        Vector2 StreamPoint(float s) => _streamAt + _flow * s + new Vector2(-_flow.y, _flow.x) * (Mathf.Sin(s * 0.33f) * 1.1f + Mathf.Sin(s * 0.9f + 1f) * 0.3f);

        void BuildStream()
        {
            (_streamAt, _flow) = VillageLayout.Stream;
            const float reach = 16f, halfW = 0.85f;
            var water = new MeshKit();
            var bank = new MeshKit();
            var wet = new Color(0.035f, 0.05f, 0.06f);
            var deep = new Color(0.02f, 0.03f, 0.04f);
            var mud = new Color(0.1f, 0.08f, 0.06f);
            Vector3 prevL = default, prevR = default, prevBL = default, prevBR = default, prevC = default;
            for (var s = -reach; s <= reach + 0.01f; s += 0.6f)
            {
                var p = StreamPoint(s);
                var d = (StreamPoint(s + 0.1f) - p).normalized;
                var n = new Vector2(-d.y, d.x);
                var w = halfW * (0.85f + 0.3f * Mathf.Sin(s * 0.7f));
                Vector3 L = X0Z(p + n * w, 0.02f), Rr = X0Z(p - n * w, 0.02f), C = X0Z(p, 0.018f);
                Vector3 BL = X0Z(p + n * (w + 0.55f), 0.03f), BR = X0Z(p - n * (w + 0.55f), 0.03f);
                if (s > -reach)
                {
                    water.Quad(prevL, L, C, prevC, wet, wet, deep, deep);
                    water.Quad(prevC, C, Rr, prevR, deep, deep, wet, wet);
                    bank.Quad(prevBL, BL, L + Vector3.up * 0.005f, prevL + Vector3.up * 0.005f, Jitter(mud, 0.2f));
                    bank.Quad(prevR + Vector3.up * 0.005f, Rr + Vector3.up * 0.005f, BR, prevBR, Jitter(mud, 0.2f));
                }
                prevL = L; prevR = Rr; prevC = C; prevBL = BL; prevBR = BR;
                // stones in and by the water
                if (R() < 0.6f)
                {
                    var off = (R() - 0.5f) * 2f * (w + 0.4f);
                    var sz = 0.12f + R() * 0.22f;
                    bank.Box(X0Z(p + n * off, sz * 0.25f), new Vector3(sz, sz * 0.5f, sz * 0.8f), Quaternion.Euler(R() * 10f, R() * 360f, R() * 10f), Jitter(new Color(0.2f, 0.2f, 0.2f), 0.25f));
                }
            }
            MeshNode("Stream", water.Build("Stream"), _vc);
            MeshNode("Stream Banks", bank.Build("Stream Banks"), _vc);

            // moonlight glints drifting downstream
            var glintMat = GlowMat(new Color(0.6f, 0.75f, 0.95f), 0.5f);
            for (var i = 0; i < 26; i++)
            {
                var t = MeshNode("Glint", StoryMeshes.Quad(0.16f + R() * 0.12f, 0.04f), glintMat);
                t.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _glints.Add((t, 0.5f + R() * 0.6f, R() * reach * 2f));
            }

            // the bridge: planks on two logs, along the trail
            var (bp, bdir) = VillageLayout.Along(0.36f);
            var br = new MeshKit();
            var plank = new Color(0.33f, 0.23f, 0.14f);
            var across = new Vector2(-bdir.y, bdir.x);
            foreach (var k in new[] { -1f, 1f })
                br.Beam(X0Z(bp - bdir * 1.9f + across * k * 0.62f, 0.06f), X0Z(bp + bdir * 1.9f + across * k * 0.62f, 0.06f), 0.16f, Jitter(plank * 0.7f, 0.1f));
            for (var i = 0; i < 13; i++)
            {
                var c = bp + bdir * (-1.65f + i * 0.275f);
                br.Box(X0Z(c, 0.16f), new Vector3(1.5f, 0.05f, 0.24f), Quaternion.LookRotation(X0Z(bdir)) * Quaternion.Euler(R() * 3f, (R() - 0.5f) * 6f, 0f), Jitter(plank, 0.14f));
            }
            // a rope rail on stakes on one side
            var rail = bp + across * 0.85f;
            for (var i = -1; i <= 1; i++) br.Box(X0Z(rail + bdir * i * 1.6f, 0.45f), new Vector3(0.07f, 0.9f, 0.07f), Quaternion.Euler(0f, R() * 40f, 0f), plank * 0.8f);
            br.Beam(X0Z(rail - bdir * 1.6f, 0.8f), X0Z(rail + bdir * 1.6f, 0.78f), 0.025f, new Color(0.45f, 0.38f, 0.26f));
            MeshNode("Bridge", br.Build("Bridge"), _vc);
            Lantern(X0Z(rail + bdir * 1.6f, 0.98f), 0.85f, 3.3f, 5f);
            GroundPool(bp, 4.2f, 4.2f, new Color(1f, 0.6f, 0.25f), 0.1f, 3.3f);

            var burble = ProceduralClips.Clip("stream", ProceduralClips.StreamSamples());
            Bed("Stream", burble, X0Z(_streamAt, 0.2f), 0.55f, 2f, 18f);
        }

        // ---------- glowing mushrooms ----------

        void BuildMushrooms()
        {
            var caps = new MeshKit();
            var stem = new Color(0.55f, 0.6f, 0.55f);
            var cap = new Color(0.32f, 0.85f, 0.75f);
            for (var c = 0; c < 16; c++)
            {
                // most on the stream's banks, some at the foot of the trail's trees
                Vector2 at;
                if (c < 9) { var s = (R() - 0.5f) * 18f; var p = StreamPoint(s); at = p + new Vector2(-_flow.y, _flow.x) * ((R() < 0.5f ? 1f : -1f) * (1.4f + R() * 0.8f)); }
                else { var (p, dir) = VillageLayout.Along(0.2f + R() * 0.55f); at = p + new Vector2(-dir.y, dir.x) * ((R() < 0.5f ? 1f : -1f) * (VillageLayout.PathHalf + 0.9f + R() * 1.4f)); }
                if (VillageLayout.DistanceToPath(at) < VillageLayout.PathHalf + 0.3f) continue;
                var count = 3 + (int)(R() * 4f);
                for (var k = 0; k < count; k++)
                {
                    var o = at + new Vector2(R() - 0.5f, R() - 0.5f) * 0.45f;
                    var h = 0.05f + R() * 0.12f;
                    var r = 0.025f + R() * 0.05f;
                    caps.Cylinder(X0Z(o), 0.012f, 0.01f, h, stem, stem, 5);
                    caps.Cylinder(X0Z(o, h), r, 0.004f, r * 0.8f, cap, cap * 1.1f, 7);
                }
                var m = GlowMat(new Color(0.3f, 1f, 0.85f), 0.35f);
                GlowCard("Mushroom Glow", X0Z(at, 0.12f), 0.9f, m);
                _shrooms.Add(m);
                GroundPool(at, 1.3f, 1.3f, new Color(0.25f, 0.9f, 0.75f), 0.12f, c);
            }
            // the caps glow on their own: an unlit copy of the colours
            MeshNode("Glowing Mushrooms", caps.Build("Glowing Mushrooms"), _vc);
        }

        // ---------- frogs ----------

        void BuildFrogs()
        {
            _croaks = new AudioClip[3];
            for (var i = 0; i < _croaks.Length; i++) _croaks[i] = ProceduralClips.Clip("croak", ProceduralClips.CroakSamples(111 + i));
            for (var i = 0; i < 6; i++)
            {
                var s = (i - 2.5f) * 2.4f + (R() - 0.5f) * 1.2f;
                if (Mathf.Abs(s) < 1.4f) s += 2.2f * Mathf.Sign(s + 0.01f); // not on the bridge
                var p = StreamPoint(s) + new Vector2(-_flow.y, _flow.x) * ((i % 2 == 0 ? 1f : -1f) * (0.95f + R() * 0.3f));
                var frog = Node("Frog");
                frog.localPosition = X0Z(p, 0.03f);
                frog.localRotation = Quaternion.Euler(0f, R() * 360f, 0f);
                var skin = Jitter(new Color(0.18f, 0.26f, 0.1f), 0.2f);
                var mk = new MeshKit();
                mk.Box(new Vector3(0f, 0.035f, 0f), new Vector3(0.09f, 0.055f, 0.11f), Quaternion.Euler(-12f, 0f, 0f), skin);
                mk.Box(new Vector3(-0.055f, 0.02f, -0.03f), new Vector3(0.04f, 0.035f, 0.07f), Quaternion.identity, skin * 0.85f); // hind legs
                mk.Box(new Vector3(0.055f, 0.02f, -0.03f), new Vector3(0.04f, 0.035f, 0.07f), Quaternion.identity, skin * 0.85f);
                mk.Box(new Vector3(-0.028f, 0.07f, 0.035f), new Vector3(0.025f, 0.025f, 0.025f), Quaternion.identity, new Color(0.6f, 0.55f, 0.2f)); // eyes
                mk.Box(new Vector3(0.028f, 0.07f, 0.035f), new Vector3(0.025f, 0.025f, 0.025f), Quaternion.identity, new Color(0.6f, 0.55f, 0.2f));
                MeshNode("Frog Body", mk.Build("Frog"), _vc, frog);
                var sac = new MeshKit();
                sac.Box(Vector3.zero, new Vector3(0.05f, 0.04f, 0.04f), Quaternion.identity, new Color(0.62f, 0.6f, 0.42f));
                var sacT = MeshNode("Frog Throat", sac.Build("Frog Throat"), _vc, frog);
                sacT.localPosition = new Vector3(0f, 0.025f, 0.06f);
                sacT.localScale = Vector3.one * 0.3f;
                var v = Voice("Frog", frog, 14f);
                v.minDistance = 0.8f;
                _frogs.Add(new Frog { Sac = sacT, Voice = v, Next = 1f + R() * 4f, Clip = i % _croaks.Length });
            }
        }

        void Frogs(float dt)
        {
            // they hush while you're close to the water, and start again a few seconds after you've crossed
            var near = Vector2.Distance(Viewer, _streamAt) < 4.5f;
            if (near) _hushUntil = _t + 3.5f;
            foreach (var f in _frogs)
            {
                f.Puff = Mathf.Max(0f, f.Puff - dt * 2.5f);
                var k = Mathf.Sin(Mathf.Clamp01(f.Puff) * Mathf.PI);
                f.Sac.localScale = new Vector3(0.3f + k * 1.1f, 0.3f + k * 1.1f, 0.3f + k * 0.9f);
                if (_t < f.Next) continue;
                f.Next = _t + 1.2f + R() * 3.5f;
                if (_t < _hushUntil) continue;
                f.Voice.pitch = 0.9f + R() * 0.25f;
                f.Voice.PlayOneShot(_croaks[f.Clip], 0.55f);
                f.Puff = 1f;
            }
        }

        // ---------- bats ----------

        void BuildBats()
        {
            var (p, _) = VillageLayout.Along(VillageLayout.BatsAt);
            _batCentre = X0Z(p, 0f);
            var fur = new Color(0.07f, 0.06f, 0.06f);
            var wing = new Color(0.1f, 0.085f, 0.08f);
            for (var i = 0; i < 4; i++)
            {
                var body = Node("Bat");
                var mk = new MeshKit();
                mk.Box(Vector3.zero, new Vector3(0.05f, 0.04f, 0.1f), Quaternion.identity, fur);
                mk.Box(new Vector3(0f, 0.01f, 0.06f), new Vector3(0.035f, 0.035f, 0.03f), Quaternion.identity, fur);
                MeshNode("Bat Body", mk.Build("Bat Body"), _vc, body);
                Transform Wing(float side)
                {
                    var w = Node("Bat Wing", body);
                    var wk = new MeshKit();
                    wk.Quad2(new Vector3(0f, 0f, 0.04f), new Vector3(side * 0.16f, 0f, 0.03f), new Vector3(side * 0.13f, 0f, -0.05f), new Vector3(0f, 0f, -0.04f), wing);
                    MeshNode("Wing", wk.Build("Bat Wing"), _vc, w);
                    return w;
                }
                var bat = new Bat
                {
                    Body = body, WingL = Wing(-1f), WingR = Wing(1f),
                    Radius = 2.5f + R() * 3f, Height = 3.5f + R() * 2.5f, Speed = (0.5f + R() * 0.4f) * (i % 2 == 0 ? 1f : -1f), Phase = R() * 6.28f, Flap = 14f + R() * 6f,
                };
                _bats.Add(bat);
            }
            _squeaks = new AudioClip[3];
            for (var i = 0; i < _squeaks.Length; i++) _squeaks[i] = ProceduralClips.Clip("squeak", ProceduralClips.SqueakSamples(121 + i));
            _batVoice = Voice("Bats", transform, 14f);
            _nextSqueak = 2f;
        }

        void Bats(Transform cam, float dt)
        {
            var head = transform.InverseTransformPoint(cam.position);
            // the first time you walk under them, one swoops low over your head
            if (!_swooped && Vector2.Distance(Viewer, new Vector2(_batCentre.x, _batCentre.z)) < 5f)
            {
                _swooped = true;
                var b = _bats[0];
                b.SwoopT = 0f;
                b.SwoopFrom = b.Body.localPosition;
                var fwd = transform.InverseTransformDirection(cam.forward); fwd.y = 0f; fwd.Normalize();
                b.SwoopOver = head + Vector3.up * 0.45f;
                b.SwoopTo = head + fwd * -6f + Vector3.up * 4.5f;
                b.SwoopFrom = head + fwd * 7f + Vector3.up * 3.5f; // it comes at you from ahead
                _batVoice.transform.localPosition = head + fwd * 1.5f;
                _batVoice.PlayOneShot(_squeaks[0], 0.7f);
            }
            for (var i = 0; i < _bats.Count; i++)
            {
                var b = _bats[i];
                Vector3 pos, dir;
                if (b.SwoopT >= 0f)
                {
                    b.SwoopT += dt / 1.3f;
                    var t = Mathf.Clamp01(b.SwoopT);
                    pos = Bezier(b.SwoopFrom, b.SwoopOver, b.SwoopTo, t);
                    dir = Bezier(b.SwoopFrom, b.SwoopOver, b.SwoopTo, Mathf.Min(1f, t + 0.02f)) - pos;
                    if (b.SwoopT >= 1f) b.SwoopT = -1f;
                }
                else
                {
                    var a = b.Phase + _t * b.Speed;
                    pos = _batCentre + new Vector3(Mathf.Cos(a) * b.Radius, b.Height + Mathf.Sin(_t * 1.7f + b.Phase) * 0.6f, Mathf.Sin(a) * b.Radius)
                          + new Vector3(Mathf.Sin(_t * 3.1f + i) * 0.4f, 0f, Mathf.Cos(_t * 2.7f + i) * 0.4f); // the jinking flight of a bat
                    var a2 = a + 0.05f * Mathf.Sign(b.Speed);
                    dir = new Vector3(Mathf.Cos(a2) * b.Radius, 0f, Mathf.Sin(a2) * b.Radius) - new Vector3(Mathf.Cos(a) * b.Radius, 0f, Mathf.Sin(a) * b.Radius);
                }
                b.Body.localPosition = pos;
                if (dir.sqrMagnitude > 1e-6f) b.Body.localRotation = Quaternion.LookRotation(dir.normalized);
                var flap = Mathf.Sin(_t * b.Flap + b.Phase) * 50f;
                b.WingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
                b.WingR.localRotation = Quaternion.Euler(0f, 0f, flap);
            }
            if (_t > _nextSqueak)
            {
                _nextSqueak = _t + 3f + R() * 5f;
                var b = _bats[(int)(R() * _bats.Count) % _bats.Count];
                _batVoice.transform.localPosition = b.Body.localPosition;
                _batVoice.pitch = 0.9f + R() * 0.2f;
                _batVoice.PlayOneShot(_squeaks[(int)(R() * _squeaks.Length) % _squeaks.Length], 0.35f);
            }
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

        // ---------- the bushbaby ----------

        void BuildGalago()
        {
            var (p, dir) = VillageLayout.Along(VillageLayout.BushbabyAt);
            var left = new Vector2(-dir.y, dir.x);
            var trunkA = p + left * 3.2f;
            var trunkB = p + left * 3.6f + dir * 3.2f;
            var bark = new Color(0.1f, 0.08f, 0.06f);
            var mk = new MeshKit();
            mk.Box(X0Z(trunkA, 2.8f), new Vector3(0.26f, 5.6f, 0.26f), Quaternion.Euler(0f, 15f, 1.5f), bark);
            mk.Box(X0Z(trunkB, 2.6f), new Vector3(0.22f, 5.2f, 0.22f), Quaternion.Euler(0f, 40f, -1.5f), bark);
            var tipA = trunkA - left * 1.2f; var tipB = trunkB - left * 1.3f;
            mk.Beam(X0Z(trunkA, 2.6f), X0Z(tipA, 2.75f), 0.08f, bark * 1.2f);
            mk.Beam(X0Z(trunkB, 2.3f), X0Z(tipB, 2.45f), 0.07f, bark * 1.2f);
            MeshNode("Bushbaby Trees", mk.Build("Bushbaby Trees"), _vc);
            _perchA = X0Z(Vector2.Lerp(trunkA, tipA, 0.7f), 2.8f);
            _perchB = X0Z(Vector2.Lerp(trunkB, tipB, 0.7f), 2.5f);

            _galago = Node("Bushbaby");
            _galago.localPosition = _perchA;
            var fur = new Color(0.36f, 0.32f, 0.28f);
            var b = new MeshKit();
            b.Box(new Vector3(0f, 0.07f, 0f), new Vector3(0.09f, 0.13f, 0.09f), Quaternion.Euler(-10f, 0f, 0f), fur);
            b.Box(new Vector3(0f, 0.17f, 0.02f), new Vector3(0.085f, 0.075f, 0.075f), Quaternion.identity, fur * 1.1f);     // head
            b.Box(new Vector3(-0.04f, 0.215f, 0.0f), new Vector3(0.04f, 0.05f, 0.012f), Quaternion.Euler(0f, 0f, 20f), fur * 0.9f); // big ears
            b.Box(new Vector3(0.04f, 0.215f, 0.0f), new Vector3(0.04f, 0.05f, 0.012f), Quaternion.Euler(0f, 0f, -20f), fur * 0.9f);
            b.Beam(new Vector3(0f, 0.04f, -0.04f), new Vector3(0f, -0.2f, -0.12f), 0.025f, fur * 0.85f);                       // long tail
            MeshNode("Bushbaby Body", b.Build("Bushbaby"), _vc, _galago);
            _galagoEyes = GlowMat(new Color(1f, 0.45f, 0.18f), 1f);
            GlowCard("Bushbaby Eye", new Vector3(-0.02f, 0.18f, 0.06f), 0.07f, _galagoEyes, _galago);
            GlowCard("Bushbaby Eye", new Vector3(0.02f, 0.18f, 0.06f), 0.07f, _galagoEyes, _galago);
            _cry = ProceduralClips.Clip("bushbaby", ProceduralClips.GalagoSamples(131));
            _galagoVoice = Voice("Bushbaby", _galago, 30f);
            _blinkAt = 3f;
        }

        void Galago(Transform cam, float dt)
        {
            var to = transform.InverseTransformPoint(cam.position) - _galago.localPosition; to.y = 0f;
            if (_leapT >= 0f)
            {
                // a leap in an arc to the other branch
                _leapT += dt / 0.7f;
                var t = Mathf.Clamp01(_leapT);
                var from = _onB ? _perchA : _perchB; var dest = _onB ? _perchB : _perchA;
                _galago.localPosition = Vector3.Lerp(from, dest, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.1f;
                if (_leapT >= 1f) { _leapT = -1f; _calmUntil = _t + 6f; }
            }
            else
            {
                if (to.sqrMagnitude > 1e-4f) _galago.localRotation = Quaternion.Slerp(_galago.localRotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-3f * dt));
                // when you come close, it cries and leaps away
                if (to.magnitude < 6.5f && _t > _calmUntil)
                {
                    _galagoVoice.pitch = 0.95f + R() * 0.1f;
                    _galagoVoice.PlayOneShot(_cry, 0.8f);
                    _onB = !_onB;
                    _leapT = 0f;
                }
            }
            var blink = _t > _blinkAt && _t < _blinkAt + 0.12f;
            if (_t > _blinkAt + 0.12f) _blinkAt = _t + 2f + R() * 3.5f;
            _galagoEyes.SetFloat(OpacityId, blink ? 0f : 1f);
        }

        // ---------- a fish, and a duiker ----------

        void BuildFish()
        {
            _fish = Node("Fish");
            var mk = new MeshKit();
            var silver = new Color(0.62f, 0.7f, 0.74f);
            mk.Box(Vector3.zero, new Vector3(0.045f, 0.07f, 0.18f), Quaternion.identity, silver);
            mk.Box(new Vector3(0f, 0.01f, -0.11f), new Vector3(0.012f, 0.08f, 0.07f), Quaternion.Euler(0f, 0f, 18f), silver * 0.75f);
            MeshNode("Fish Body", mk.Build("Fish"), _vc, _fish);
            _fish.gameObject.SetActive(false);
            _plop = ProceduralClips.Clip("plop", ProceduralClips.PlopSamples(161));
            _plopVoice = Voice("Fish", transform, 12f);
        }

        void Fish(float dt)
        {
            if (_jumpT >= 0f)
            {
                _jumpT += dt / 0.42f;
                var t = Mathf.Clamp01(_jumpT);
                _fish.localPosition = Vector3.Lerp(_jumpFrom, _jumpTo, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.55f;
                var along = (_jumpTo - _jumpFrom); along.y = 0f;
                if (along.sqrMagnitude > 1e-6f)
                    _fish.localRotation = Quaternion.LookRotation(along.normalized) * Quaternion.Euler(-70f + t * 140f, 0f, 0f);
                if (!_plopped && t >= 0.82f)
                {
                    _plopped = true;
                    _plopVoice.transform.localPosition = _jumpTo;
                    _plopVoice.pitch = 0.9f + R() * 0.2f;
                    _plopVoice.PlayOneShot(_plop, 0.55f);
                }
                if (_jumpT >= 1f) { _jumpT = -1f; _fish.gameObject.SetActive(false); }
                return;
            }
            if (_t < _nextJump) return;
            if (Vector2.Distance(Viewer, _streamAt) > 8f) { _nextJump = _t + 3f; return; }
            var onBridge = Vector2.Distance(Viewer, _streamAt) < 2.4f;
            var s = onBridge ? (R() - 0.5f) * 1.2f : (R() - 0.5f) * 10f;
            var p = StreamPoint(s);
            var across = new Vector2(-_flow.y, _flow.x) * (R() < 0.5f ? 1f : -1f);
            _jumpFrom = X0Z(p - across * 0.35f, 0.02f);
            _jumpTo = X0Z(p + across * 0.45f, 0.02f);
            _jumpT = 0f;
            _plopped = false;
            _fish.gameObject.SetActive(true);
            // stand on the bridge and a second one follows; otherwise they jump now and then
            if (onBridge && !_followJump) { _followJump = true; _nextJump = _t + 0.75f; }
            else { _followJump = false; _nextJump = _t + 8f + R() * 9f; }
        }

        void BuildDuiker()
        {
            var (p, dir) = VillageLayout.Along(0.78f);
            var right = new Vector2(dir.y, -dir.x);
            var home = p + right * 2.5f;
            _duikerHome = X0Z(home);
            _duikerFrom = _duikerHome;
            _duikerTo = X0Z(p - right * 7f + dir * 1.4f);
            _duiker = Node("Duiker");
            _duiker.localPosition = _duikerHome;
            _duiker.localRotation = Quaternion.LookRotation(X0Z(-right));
            var coat = new Color(0.42f, 0.28f, 0.16f);
            var body = new MeshKit();
            body.Box(new Vector3(0f, 0.42f, 0f), new Vector3(0.16f, 0.2f, 0.42f), Quaternion.identity, coat);
            body.Box(new Vector3(0f, 0.52f, -0.22f), new Vector3(0.04f, 0.1f, 0.05f), Quaternion.Euler(-40f, 0f, 0f), coat * 0.8f);
            MeshNode("Duiker Body", body.Build("Duiker"), _vc, _duiker);
            _duikerHead = Node("Duiker Head", _duiker);
            _duikerHead.localPosition = new Vector3(0f, 0.5f, 0.2f);
            var h = new MeshKit();
            h.Box(new Vector3(0f, 0.04f, 0.08f), new Vector3(0.09f, 0.1f, 0.16f), Quaternion.identity, coat * 1.05f);
            h.Box(new Vector3(-0.025f, 0.12f, 0.02f), new Vector3(0.015f, 0.07f, 0.02f), Quaternion.Euler(10f, 0f, -8f), new Color(0.25f, 0.16f, 0.1f));
            h.Box(new Vector3(0.025f, 0.12f, 0.02f), new Vector3(0.015f, 0.07f, 0.02f), Quaternion.Euler(10f, 0f, 8f), new Color(0.25f, 0.16f, 0.1f));
            MeshNode("Duiker Head", h.Build("Duiker Head"), _vc, _duikerHead);
            for (var i = 0; i < 4; i++)
            {
                var leg = Node("Duiker Leg", _duiker);
                var x = i % 2 == 0 ? -0.05f : 0.05f;
                var z = i < 2 ? 0.12f : -0.14f;
                leg.localPosition = new Vector3(x, 0.28f, z);
                var lk = new MeshKit();
                lk.Box(new Vector3(0f, -0.12f, 0f), new Vector3(0.035f, 0.26f, 0.035f), Quaternion.identity, coat * 0.75f);
                MeshNode("Leg", lk.Build("Duiker Leg"), _vc, leg);
                _duikerLegs[i] = leg;
            }
            _rustle = ProceduralClips.Clip("duiker", ProceduralClips.RustleSamples(171));
            _duikerVoice = Voice("Duiker", transform, 16f); // stays on after the duiker hides, so the rustle can finish
        }

        void Duiker(float dt)
        {
            if (_duikerState == DuikerState.Gone) return;
            if (_duikerState == DuikerState.Grazing)
            {
                _duikerHead.localRotation = Quaternion.Slerp(_duikerHead.localRotation, Quaternion.Euler(35f + Mathf.Sin(_t * 1.4f) * 6f, 0f, 0f), 1f - Mathf.Exp(-3f * dt));
                var home = new Vector2(_duikerHome.x, _duikerHome.z);
                if (Vector2.Distance(Viewer, home) < 8f && VillageLayout.DistanceToPath(Viewer) < 3.2f)
                {
                    _duikerState = DuikerState.Alert;
                    _duikerT = 0f;
                }
                return;
            }
            if (_duikerState == DuikerState.Alert)
            {
                _duikerT += dt;
                _duikerHead.localRotation = Quaternion.Slerp(_duikerHead.localRotation, Quaternion.Euler(-8f, 0f, 0f), 1f - Mathf.Exp(-8f * dt));
                if (_duikerT > 0.55f)
                {
                    _duikerState = DuikerState.Bolting;
                    _duikerT = 0f;
                    _duikerFrom = _duiker.localPosition;
                    _duikerVoice.transform.localPosition = _duikerFrom;
                    var flat = _duikerTo - _duikerFrom; flat.y = 0f;
                    if (flat.sqrMagnitude > 1e-4f) _duiker.localRotation = Quaternion.LookRotation(flat.normalized);
                    _duikerVoice.PlayOneShot(_rustle, 0.65f);
                }
                return;
            }
            _duikerT += dt / 1.15f;
            var u = Mathf.Clamp01(_duikerT);
            _duiker.localPosition = Vector3.Lerp(_duikerFrom, _duikerTo, u) + Vector3.up * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 4f)) * 0.28f;
            var swing = Mathf.Sin(u * Mathf.PI * 8f) * 40f;
            for (var i = 0; i < 4; i++) _duikerLegs[i].localRotation = Quaternion.Euler((i % 2 == 0 ? swing : -swing), 0f, 0f);
            if (_duikerT >= 1f)
            {
                _duikerState = DuikerState.Gone;
                _duiker.gameObject.SetActive(false);
            }
        }

        // ---------- a shooting star ----------

        void BuildShootingStar()
        {
            _starMat = GlowMat(new Color(0.85f, 0.92f, 1f), 0f);
            _star = MeshNode("Shooting Star", StoryMeshes.Quad(6f, 0.18f), _starMat);
            _star.gameObject.SetActive(false);
        }

        void ShootingStar(Transform cam, float dt)
        {
            if (_starT >= 0f)
            {
                _starT += dt / 0.9f;
                var t = Mathf.Clamp01(_starT);
                _star.localPosition = Vector3.Lerp(_starFrom, _starTo, t);
                var d = (_starTo - _starFrom).normalized;
                var toCam = (cam.position - _star.position).normalized;
                _star.rotation = Quaternion.LookRotation(-toCam, Vector3.Cross(transform.TransformDirection(d), -toCam)) * Quaternion.Euler(0f, 0f, 90f);
                _starMat.SetFloat(OpacityId, Mathf.Sin(t * Mathf.PI) * 0.9f);
                if (_starT >= 1f) { _starT = -1f; _star.gameObject.SetActive(false); }
                return;
            }
            // now and then, while you're out on the village trail
            if (_t < _nextStar) return;
            _nextStar = _t + 22f + R() * 20f;
            if (Viewer.magnitude < 8f || VillageLayout.DistanceToPath(Viewer) > 4f) return;
            var fwd = transform.InverseTransformDirection(cam.forward); fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();
            var side = new Vector3(fwd.z, 0f, -fwd.x) * (R() < 0.5f ? 1f : -1f);
            var at = X0Z(Viewer) + fwd * 70f + Vector3.up * (34f + R() * 12f);
            _starFrom = at - side * 22f; _starTo = at + side * 22f - Vector3.up * 12f;
            _starT = 0f;
            _star.gameObject.SetActive(true);
        }

        // ---------- every frame ----------

        void Update()
        {
            var cam = Tick();
            if (cam == null) return;
            var dt = Time.deltaTime;
            // the stream's glints drift downstream; the mushrooms breathe
            foreach (var (t, speed, off) in _glints)
            {
                var s = Mathf.Repeat(off + _t * speed, 32f) - 16f;
                var p = StreamPoint(s) + new Vector2(-_flow.y, _flow.x) * Mathf.Sin(off * 7f) * 0.45f;
                t.localPosition = X0Z(p, 0.03f);
                var d = StreamPoint(s + 0.1f) - StreamPoint(s);
                t.localRotation = Quaternion.LookRotation(X0Z(d).normalized) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 90f);
            }
            for (var i = 0; i < _shrooms.Count; i++) _shrooms[i].SetFloat(OpacityId, 0.26f + 0.12f * Mathf.Sin(_t * 0.8f + i * 1.7f));
            Frogs(dt);
            Fish(dt);
            // the far creatures only move when you could see them
            if (Viewer.magnitude > 8f || Vector2.Distance(Viewer, VillageLayout.Path[1]) < 30f)
            {
                Bats(cam, dt);
                Galago(cam, dt);
                Duiker(dt);
            }
            ShootingStar(cam, dt);
        }
    }
}
