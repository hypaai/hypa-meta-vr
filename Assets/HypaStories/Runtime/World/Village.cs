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
        public Transform ElderMouth => _local?.Mouth;
        public Transform ElderRoot => _local?.Root;
        Transform _elderParent;
        Vector3 _elderPosition, _elderScale;
        Quaternion _elderRotation;
        bool _elderInRoom;
        Vector3 _villageFireLightPosition;
        Transform _elderMouthMesh;
        Vector3 _elderMouthRestScale;
        Vector3 _elderMouthRestPosition, _elderJawRestPosition, _elderUpperLipRestPosition;
        Transform _elderJaw, _elderUpperLip;
        float _elderMouthOpen;

        /// <summary>Animate only the visible mouth, leaving the head and speech-source anchor unchanged.</summary>
        public void UpdateElderSpeech(float level, bool speaking, float dt)
        {
            if (_elderMouthMesh == null) return;
            var target = speaking ? Mathf.Clamp01(level * 1.2f) : 0f;
            _elderMouthOpen = Mathf.Lerp(_elderMouthOpen, target, 1f - Mathf.Exp(-22f * Mathf.Max(0f, dt)));
            if (target == 0f && _elderMouthOpen < 0.001f) _elderMouthOpen = 0f;
            var scale = _elderMouthRestScale;
            scale.y *= 1f + _elderMouthOpen * 4f;
            _elderMouthMesh.localScale = scale;
            _elderMouthMesh.localPosition = _elderMouthRestPosition + new Vector3(0f, -0.006f, 0.005f) * _elderMouthOpen;
            if (_elderJaw != null)
            {
                _elderJaw.localPosition = _elderJawRestPosition + Vector3.down * (0.004f * _elderMouthOpen);
                _elderJaw.localRotation = Quaternion.Euler(12f * _elderMouthOpen, 0f, 0f);
            }
            if (_elderUpperLip != null)
                _elderUpperLip.localPosition = _elderUpperLipRestPosition + Vector3.up * (0.001f * _elderMouthOpen);
        }

        public void SetElderInRoom(bool on, Transform roomParent)
        {
            if (_elderInRoom == on) return;
            var elder = ElderRoot;
            if (on)
            {
                _elderParent = elder.parent;
                _elderPosition = elder.localPosition; _elderRotation = elder.localRotation; _elderScale = elder.localScale;
                if (_fire != null)
                {
                    var light = _fire.Light.transform;
                    _villageFireLightPosition = light.localPosition;
                    light.SetParent(elder, true);
                }
                elder.SetParent(roomParent, true);
            }
            else
            {
                elder.SetParent(_elderParent, false);
                elder.localPosition = _elderPosition; elder.localRotation = _elderRotation; elder.localScale = _elderScale;
                if (_fire != null)
                {
                    var light = _fire.Light.transform;
                    light.SetParent(_fire.Root, true);
                    light.localPosition = _villageFireLightPosition;
                }
            }
            _elderInRoom = on;
        }

        public void UpdateRoomElder(Transform cam, float dt)
        {
            if (!_elderInRoom) return;
            _t += dt;
            Local(cam, dt);
        }
        bool _narrationFocus;
        float _performanceLevel = 1f;
        AudioSource _crowd;

        public void SetNarrationFocus(bool on) => _narrationFocus = on;

        /// <summary>Fade performance audio without stopping the drum clock used by the dancers.</summary>
        public void UpdatePerformanceAudio(float dt)
        {
            var target = _narrationFocus ? 0.15f : 1f;
            _performanceLevel = Mathf.Lerp(_performanceLevel, target,
                1f - Mathf.Exp(-Mathf.Max(0f, dt) / (_narrationFocus ? 0.2f : 0.7f)));
            if (_drums != null) _drums.volume = 0.85f * _performanceLevel;
            if (_crowd != null) _crowd.volume = 0.45f * _performanceLevel;
            if (_thud != null) _thud.volume = _performanceLevel;
        }

        Transform _root;   // everything you see (hidden when far)
        StoryFire _fire;

        /// <summary>A person of the village: move them by their parts (root, torso, head, arms and forearms).</summary>
        public sealed class Figure
        {
            public Transform Root, Torso, Head, ArmL, ArmR, ForeL, ForeR, EyeL, EyeR, Mouth, Jaw;
            public float Ph;
            public Vector3 Home;
        }

        readonly List<Figure> _sorters = new List<Figure>();
        readonly List<Figure> _pounders = new List<Figure>();
        readonly List<Transform> _pestles = new List<Transform>();
        readonly List<Figure> _drummers = new List<Figure>();
        readonly List<Figure> _dancers = new List<Figure>();
        readonly List<Figure> _kids = new List<Figure>();

        // ---------- the celebration at the campfire (after the Dodo game) ----------

        Transform _party;
        bool _placeWanted = true;
        readonly List<(Figure f, Vector3 from, Vector3 to)> _celebrants = new List<(Figure, Vector3, Vector3)>();
        float _partyT = -1f, _nextCheer;
        AudioSource _partyDrums, _partyCrowd, _partyCheer;
        AudioClip _murmurClip, _cheerClip;

        /// <summary>The huts and their sounds. Off in your room: the script stays awake so a celebration at the fire can still run.</summary>
        public void SetPlaceShown(bool on)
        {
            _placeWanted = on;
            if (!on && _root != null) _root.gameObject.SetActive(false);
            foreach (var s in GetComponentsInChildren<AudioSource>(true))
            {
                if (_party != null && s.transform.IsChildOf(_party)) continue;
                s.mute = !on;
            }
        }

        CampWorld _camp;
        bool InRoom => (_camp != null ? _camp : (_camp = GetComponentInParent<CampWorld>())) is CampWorld camp && camp.Room;

        /// <summary>A forest point round the fire, pulled to just outside the stones when the camp is in a room.</summary>
        static Vector3 NearTheFire(Vector3 forest)
        {
            var flat = new Vector2(forest.x, forest.z);
            var m = flat.magnitude;
            if (m < 1e-3f) return forest;
            var r = Mathf.Lerp(GameFit.StoneEdge + 0.45f, GameFit.StoneEdge + 1.05f, Mathf.InverseLerp(5.6f, 12.5f, m));
            return new Vector3(flat.x / m * r, forest.y, flat.y / m * r);
        }

        /// <summary>
        /// Villagers come out of the trees round the campfire and dance (Dodo is dead). Off: they go. Built once, the first time.
        /// They live at the camp, not in the village, so they show however far away the village is.
        /// <paramref name="drums"/> false: without the village's drums (the drum game's song is playing; the dancers keep their own time).
        /// </summary>
        public void Celebrate(bool on, bool drums = true)
        {
            if (!on)
            {
                if (_party != null) _party.gameObject.SetActive(false);
                if (_masq != null && _masq.parent != _root) { _masq.SetParent(_root, false); }   // the masquerade goes home
                _partyT = -1f;
                return;
            }
            if (_party == null)
            {
                _party = Node("Celebration");
                var keep = _root; _root = _party;   // Person builds under _root
                // a wide arc in front of the seats and round the sides, never on the seats, in the fire, or where Dodo lies
                // (it falls straight across the fire from you, so the middle of the arc stays clear)
                var angles = new List<float>();
                for (var d = -120f; d <= 120.1f; d += 20f) if (Mathf.Abs(d) > 30f) angles.Add(d);
                for (var i = 0; i < angles.Count; i++)
                {
                    var a = angles[i] * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                    var to = dir * (5.6f + (i % 2) * 0.9f);
                    var from = dir * 12.5f;
                    var f = Person("Celebrant", from, -dir, RandomLook(i % 3 != 1), i % 4 == 3 ? 0.65f : 0.95f + R() * 0.1f);
                    f.Ph = R() * 6.28f;
                    _celebrants.Add((f, X0Z(from), X0Z(to)));
                }
                _root = keep;

                // the village's drums and its crowd, close by this time, and the cheering
                _partyDrums = Voice("Celebration Drums", _party, 40f);
                _partyDrums.clip = _drums.clip; _partyDrums.loop = true; _partyDrums.minDistance = 7f; _partyDrums.spatialBlend = 0.5f;
                _partyDrums.transform.localPosition = new Vector3(2.5f, 1f, 6f);
                _partyCrowd = Voice("Celebration Crowd", _party, 40f);
                _partyCrowd.clip = _murmurClip; _partyCrowd.loop = true; _partyCrowd.minDistance = 7f; _partyCrowd.spatialBlend = 0.4f;
                _partyCrowd.transform.localPosition = new Vector3(-2f, 1.4f, 5f);
                _partyCheer = Voice("Celebration Cheers", _party, 40f);
                _partyCheer.minDistance = 7f; _partyCheer.spatialBlend = 0.4f;
                _partyCheer.transform.localPosition = new Vector3(0f, 1.5f, 6f);
                _cheerClip = ProceduralClips.Clip("cheer", ProceduralClips.CheerSamples(213));
            }
            _party.gameObject.SetActive(true);
            _partyT = 0f;
            _partyDrums.volume = 0.9f; _partyDrums.time = 0f;
            if (drums) _partyDrums.Play(); else _partyDrums.Stop();
            _partyCrowd.volume = 0.55f; _partyCrowd.Play();
            _partyCheer.PlayOneShot(_cheerClip, 1f);
            _nextCheer = _t + 6f;
            // and the masquerade comes to dance round the campfire
            if (_masq != null) { _masq.SetParent(_party, false); _masq.gameObject.SetActive(true); }
        }

        void Celebrants(float dt)
        {
            if (_partyT < 0f || _party == null) return;
            _partyT += dt;
            var loopT = _partyDrums != null && _partyDrums.isPlaying ? _partyDrums.time : _t % ProceduralClips.DrumLoopLength;
            var beat = loopT / 0.5f * Mathf.PI;
            if (_t > _nextCheer) { _nextCheer = _t + 9f + R() * 7f; _partyCheer.PlayOneShot(_cheerClip, 0.8f); }
            if (_masq != null && _masq.parent == _party)
            {
                // the masquerade whirls round the campfire (inside the villagers' arc), flaring on the drums' fill
                var fill = loopT > ProceduralClips.DrumLoopLength * 0.75f;
                _masqSpin = Mathf.MoveTowards(_masqSpin, fill ? 1f : 0.3f, dt * (fill ? 1.6f : 0.5f));
                _masqAngle += dt * (0.3f + _masqSpin * 0.3f);
                _masqYaw += dt * _masqSpin * 720f;
                var arrive = Mathf.Clamp01(_partyT / 2.5f);
                // back and forth along the far side of the fire, facing you: never close to your seat
                var swing = Mathf.Sin(_masqAngle * 0.8f) * (InRoom ? 0.65f : 1.15f); // a wide swing at room size would cross the stones
                var reach = InRoom
                    ? Mathf.Lerp(GameFit.StoneEdge + 0.9f, GameFit.StoneEdge + 0.45f, Mathf.SmoothStep(0f, 1f, arrive))
                    : Mathf.Lerp(9f, 2.5f, Mathf.SmoothStep(0f, 1f, arrive));
                var around = new Vector2(Mathf.Sin(swing), Mathf.Cos(swing)) * reach;
                _masq.localPosition = X0Z(around, Mathf.Abs(Mathf.Sin(loopT * Mathf.PI * 2f)) * 0.08f);
                _masq.localRotation = Quaternion.Euler(0f, _masqYaw, 0f);
                UpdateMasqueradeMesh(_masqSpin * 0.85f);
            }
            for (var i = 0; i < _celebrants.Count; i++)
            {
                var (f, from, to) = _celebrants[i];
                var walk = Mathf.Clamp01((_partyT - i * 0.15f) / 3.2f);         // out of the trees, one after another
                var fromP = InRoom ? NearTheFire(from) : from;
                var toP = InRoom ? NearTheFire(to) : to;
                var at = Vector3.Lerp(fromP, toP, Mathf.SmoothStep(0f, 1f, walk));
                var bob = walk < 1f ? Mathf.Abs(Mathf.Sin(_t * 8f + i)) * 0.05f : Mathf.Abs(Mathf.Sin(beat + f.Ph)) * 0.07f;
                f.Root.localPosition = at + Vector3.up * bob;
                var face = -new Vector3(to.x, 0f, to.z);
                if (face.sqrMagnitude > 1e-4f) f.Root.localRotation = Quaternion.LookRotation(face.normalized) * Quaternion.Euler(0f, walk < 1f ? 0f : Mathf.Sin(beat * 0.5f + f.Ph) * 25f, 0f);
                if (walk < 1f)
                {
                    var swing = Mathf.Sin(_t * 8f + i) * 35f;
                    f.ArmL.localRotation = Quaternion.Euler(swing, 0f, -8f); f.ArmR.localRotation = Quaternion.Euler(-swing, 0f, 8f);
                    f.ForeL.localRotation = f.ForeR.localRotation = Quaternion.Euler(-25f, 0f, 0f);
                    f.Torso.localRotation = Quaternion.Euler(6f, 0f, 0f);
                }
                else
                {
                    // hands up and waving with the drums
                    f.Torso.localRotation = Quaternion.Euler(10f + Mathf.Sin(beat + f.Ph) * 8f, 0f, Mathf.Sin(beat * 0.5f + f.Ph) * 6f);
                    f.ArmL.localRotation = Quaternion.Euler(-130f + Mathf.Sin(beat + f.Ph) * 30f, 0f, -25f);
                    f.ArmR.localRotation = Quaternion.Euler(-130f - Mathf.Sin(beat + f.Ph) * 30f, 0f, 25f);
                    f.ForeL.localRotation = Quaternion.Euler(-25f + Mathf.Sin(beat * 2f + f.Ph) * 25f, 0f, 0f);
                    f.ForeR.localRotation = Quaternion.Euler(-25f - Mathf.Sin(beat * 2f + f.Ph) * 25f, 0f, 0f);
                }
            }
        }
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
            _vc.mainTexture = WaxPrints.Atlas; // the villagers' cloth
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
            HangCloth(g);
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
                var rOut = VillageLayout.HutRadius * s;
                RoundHut(hut, new HutShape
                {
                    RIn = rOut - 0.25f, ROut = rOut, WallH = 2f, DoorH = 1.75f, DoorHalfAngle = 15f,
                    Eave = rOut + 0.6f, EaveY = 1.8f, ApexY = 3.9f + R() * 0.6f,
                    Mud = mud, MudDark = mud * 0.62f, Thatch = Jitter(new Color(0.7f, 0.52f, 0.28f), 0.1f), Floor = false,
                }, 32);
                PaintHut(hut, rOut, i);
                // warm light inside the doorway: someone's home
                var inside = GlowMat(new Color(1f, 0.55f, 0.22f), 0.55f);
                GlowCard("Doorway Glow", hut.localPosition + hut.localRotation * new Vector3(0f, 0.9f, VillageLayout.HutRadius * s - 0.6f), 1.5f, inside, _root);
                _flicker.Add((inside, 0.55f, i * 2.1f));
            }
        }

        /// <summary>A pale wash and two thin rings on a hut wall, with a few dots. The door stays clear. One mesh.</summary>
        void PaintHut(Transform hut, float r, int which)
        {
            Color wash, band, dot;
            if (which % 3 == 0) { wash = new Color(0.93f, 0.88f, 0.76f); band = new Color(0.72f, 0.12f, 0.1f); dot = new Color(0.1f, 0.14f, 0.42f); }
            else if (which % 3 == 1) { wash = new Color(0.9f, 0.68f, 0.16f); band = new Color(0.1f, 0.38f, 0.18f); dot = new Color(0.08f, 0.06f, 0.05f); }
            else { wash = new Color(0.94f, 0.92f, 0.86f); band = new Color(0.14f, 0.32f, 0.58f); dot = new Color(0.82f, 0.32f, 0.1f); }
            var mk = new MeshKit();
            WallBand(mk, r + 0.015f, 0.12f, 0.48f, wash);
            WallBand(mk, r + 0.03f, 1.02f, 0.07f, band);
            WallBand(mk, r + 0.03f, 1.16f, 0.04f, dot);
            const int n = 20;
            for (var s = 0; s < n; s++)
            {
                if ((s + which) % 2 != 0) continue;
                var ang = (s + 0.5f) / n * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)) < 22f) continue;
                mk.Box(new Vector3(Mathf.Sin(ang) * (r + 0.04f), 0.36f, Mathf.Cos(ang) * (r + 0.04f)), new Vector3(0.055f, 0.055f, 0.02f),
                    Quaternion.LookRotation(new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang))), band);
            }
            MeshNode("Hut Paint", mk.Build("Hut Paint"), _vc, hut);
        }

        static void WallBand(MeshKit mk, float r, float y, float h, Color col)
        {
            const int seg = 32;
            for (var i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(0f, (a0 + a1) * 0.5f * Mathf.Rad2Deg)) < 18f) continue;
                Vector3 P(float a, float yy) => new Vector3(Mathf.Sin(a) * r, yy, Mathf.Cos(a) * r);
                mk.Quad(P(a0, y), P(a0, y + h), P(a1, y + h), P(a1, y), col);
            }
        }

        /// <summary>A short run of printed cloths under the gate beam, toward the path.</summary>
        void HangCloth(Vector2 gate)
        {
            var mk = new MeshKit();
            var n = Face(VillageLayout.Into);
            var right = Face(VillageLayout.Right);
            for (var i = 0; i < 4; i++)
            {
                var across = (-1.5f + i) * 0.72f;
                var w = 0.42f;
                var drop = 0.55f + (i % 2) * 0.12f;
                var top = X0Z(gate, 2.62f) + right * across + n * 0.18f;
                var a = top - right * w * 0.5f;
                var b = top + right * w * 0.5f;
                var sag = n * 0.06f;
                mk.Cloth(a, b, b - Vector3.up * drop + sag, a - Vector3.up * drop + sag, WaxPrints.Tile(i % 2 == 0 ? 9 : 2), Color.white);
            }
            MeshNode("Gate Cloth", mk.Build("Gate Cloth"), _vc, _root);
        }

        /// <summary>A short strand of beads at the neck: coral for the women, gold for the elder. Part of the body mesh.</summary>
        static void Beads(MeshKit body, bool coral)
        {
            var bead = coral ? new Color(0.84f, 0.22f, 0.14f) : new Color(0.9f, 0.74f, 0.28f);
            var pale = coral ? new Color(0.95f, 0.55f, 0.35f) : new Color(0.98f, 0.9f, 0.55f);
            for (var i = 0; i < 7; i++)
            {
                var a = Mathf.Lerp(-0.7f, 0.7f, i / 6f);
                body.Ellipsoid(new Vector3(Mathf.Sin(a) * 0.075f, 0.57f, 0.055f + Mathf.Cos(a) * 0.04f), Vector3.one * 0.011f, Quaternion.identity, i % 2 == 0 ? bead : pale, 6, 4);
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
            public Color Skin, Top, Wrap, Hat, Hair;
            /// <summary>Wax prints (<see cref="WaxPrints"/> tiles, 0 = plain colour) for the top, the wrapper or trousers, the head wrap or cap.</summary>
            public int TopPrint, WrapPrint, HatPrint;
            public int Headwear;  // 0 short hair, 1 a tall head wrap (gele), 2 a soft cap (fila)
            public bool Wrapper;  // a wrapper or robe to the ankles (else trousers)
            public bool Woman, Elder;
            /// <summary>An older woman of the house (the hut's storyteller): a fuller figure, gold earrings, a shawl over one shoulder.</summary>
            public bool Matron;
        }

        static readonly Color[] Cloth =
        {
            new Color(0.75f, 0.2f, 0.12f), new Color(0.9f, 0.62f, 0.12f), new Color(0.15f, 0.35f, 0.65f), new Color(0.18f, 0.5f, 0.28f),
            new Color(0.55f, 0.18f, 0.45f), new Color(0.92f, 0.85f, 0.7f), new Color(0.85f, 0.4f, 0.12f), new Color(0.2f, 0.55f, 0.6f),
        };

        Color AnyCloth() => Cloth[(int)(R() * Cloth.Length) % Cloth.Length];
        int AnyPrint() => 1 + (int)(R() * WaxPrints.Count) % WaxPrints.Count;

        /// <summary>
        /// A villager for another scene (the hat game's two friends): built like the village's people, under
        /// <paramref name="parent"/> in the camp's ground space, with a head that can turn.
        /// </summary>
        public Figure MakePerson(Transform parent, string name, Vector2 at, Vector2 facing, bool woman, float scale = 1f)
        {
            var keep = _root; _root = parent;
            try { return Person(name, at, facing, RandomLook(woman), scale, false, true); }
            finally { _root = keep; }
        }

        /// <summary>Eshu: small (he is told as short), dressed in his colours, red and black, a dark wrapper, his head bare (his hat floats on its own).</summary>
        public Figure MakeEshu(Transform parent, Vector2 at, Vector2 facing, float scale = 0.7f)
        {
            var keep = _root; _root = parent;
            try
            {
                var look = new Look
                {
                    Skin = new Color(0.24f, 0.15f, 0.1f), Top = new Color(0.62f, 0.08f, 0.06f), Wrap = new Color(0.06f, 0.05f, 0.05f),
                    Hat = new Color(0.62f, 0.08f, 0.06f), Hair = new Color(0.03f, 0.03f, 0.03f), TopPrint = 0, WrapPrint = 0, Headwear = 0,
                    Wrapper = true,
                };
                return Person("Eshu", at, facing, look, scale, false, true);
            }
            finally { _root = keep; }
        }

        /// <summary>
        /// The old woman of the hut (she tells you about its household things): sitting on her mat with her legs folded to one side
        /// under her wrapper, as village women sit, grey under her head
        /// wrap, in an indigo print. Built like the village's people, under <paramref name="parent"/>, in its own space.
        /// </summary>
        public Figure MakeElderWoman(Transform parent, Vector2 at, Vector2 facing, float scale = 0.92f)
        {
            var keep = _root; _root = parent;
            try
            {
                var look = new Look
                {
                    Skin = new Color(0.26f, 0.165f, 0.11f), Top = new Color(0.15f, 0.2f, 0.45f), Wrap = new Color(0.15f, 0.2f, 0.45f),
                    Hat = new Color(0.62f, 0.3f, 0.12f), Hair = new Color(0.6f, 0.58f, 0.55f), TopPrint = 4, WrapPrint = 4, HatPrint = 6,
                    Headwear = 1, Wrapper = true, Woman = true, Matron = true,
                };
                return Person("The Old Woman", at, facing, look, scale, false, true, true, true);
            }
            finally { _root = keep; }
        }

        Look RandomLook(bool woman)
        {
            var wrapPrint = AnyPrint();
            return new Look
            {
                Skin = Jitter(new Color(0.3f, 0.19f, 0.12f), 0.18f),
                Top = AnyCloth(), Wrap = AnyCloth(), Hat = AnyCloth(), Hair = new Color(0.04f, 0.035f, 0.03f),
                TopPrint = R() < 0.75f ? AnyPrint() : 0,
                WrapPrint = woman || R() < 0.55f ? wrapPrint : 0,
                HatPrint = woman ? (R() < 0.5f ? wrapPrint : AnyPrint()) : (R() < 0.7f ? AnyPrint() : 0),
                Headwear = woman ? 1 : (R() < 0.7f ? 2 : 0),
                Wrapper = woman || R() < 0.4f, Woman = woman,
            };
        }

        /// <summary>Cloth: white under a print (the print carries the colour), else the plain colour.</summary>
        static Color ClothColour(int print, Color plain) => print > 0 ? new Color(0.93f, 0.93f, 0.93f) : plain;
        static Rect? PrintTile(int print) => print > 0 ? WaxPrints.Tile(print) : (Rect?)null;

        /// <summary>
        /// A person of smooth rounded parts: legs in trousers (or a wrapper, a robe), feet; a torso with shoulders that bends at
        /// the hips; a neck and a head with a face (eyes, brows, nose, lips, ears) and hair, a head wrap or a cap; arms that
        /// bend at the shoulder and the elbow, and hands. Six meshes, so a crowd stays cheap. <paramref name="ownHead"/> gives
        /// the head (and its eyes and mouth) their own transforms, to turn, blink and, later, talk.
        /// <paramref name="movingMouth"/> leaves the lips off the head mesh and builds a jaw that can open with a voice.
        /// </summary>
        Figure Person(string name, Vector2 at, Vector2 facing, Look look, float scale = 1f, bool seated = false, bool ownHead = false, bool onFloor = false, bool movingMouth = false)
        {
            var f = new Figure { Ph = R() * 6.28f };
            f.Root = Node(name, _root);
            f.Root.localPosition = X0Z(at);
            f.Root.localRotation = Quaternion.LookRotation(Face(facing));
            f.Root.localScale = Vector3.one * scale;
            f.Home = f.Root.localPosition;
            var hipY = onFloor ? 0.13f : seated ? 0.5f : 0.9f; // on the floor (a mat): no stool, the legs straight out in front
            var id = Quaternion.identity;
            var skin = look.Skin;
            var sole = skin * 0.7f; sole.a = 1f;
            var wrap = ClothColour(look.WrapPrint, look.Wrap); var wrapTile = PrintTile(look.WrapPrint);
            var top = ClothColour(look.TopPrint, look.Top); var topTile = PrintTile(look.TopPrint);

            // ---- legs, wrapper or robe, feet (and the stool) ----
            var legs = new MeshKit();
            if (onFloor)
            {
                // sitting on the floor as village women do: the legs folded to one side under the wrapper, which spreads on the mat;
                // only the feet show, tucked behind on that side
                legs.Ellipsoid(new Vector3(0f, 0.12f, 0.03f), new Vector3(0.21f, 0.12f, 0.19f), id, wrap, 14, 8, wrapTile);                           // hips and lap
                legs.Ellipsoid(new Vector3(0.13f, 0.085f, 0.22f), new Vector3(0.25f, 0.085f, 0.17f), Quaternion.Euler(0f, -28f, 0f), wrap, 14, 7, wrapTile); // the folded thighs
                legs.Ellipsoid(new Vector3(0.29f, 0.06f, 0.02f), new Vector3(0.12f, 0.06f, 0.21f), Quaternion.Euler(0f, 18f, 0f), wrap, 12, 6, wrapTile);   // the shins, drawn back
                legs.Lathe(new Vector3(0.07f, 0f, 0.07f), id, new[] { (0.15f, 0.2f, 0.18f), (0.07f, 0.31f, 0.28f), (0.01f, 0.36f, 0.32f) }, wrap, 18, wrapTile); // the hem spread on the mat
                legs.Ellipsoid(new Vector3(0.36f, 0.03f, -0.16f), new Vector3(0.04f, 0.03f, 0.085f), Quaternion.Euler(0f, 65f, 0f), sole, 8, 5);           // feet
                legs.Ellipsoid(new Vector3(0.28f, 0.028f, -0.2f), new Vector3(0.038f, 0.028f, 0.08f), Quaternion.Euler(0f, 80f, 0f), sole, 8, 5);
            }
            else if (seated)
            {
                foreach (var k in new[] { -1f, 1f })
                {
                    legs.Limb(new Vector3(k * 0.1f, 0.5f, 0f), new Vector3(k * 0.11f, 0.49f, 0.4f), 0.088f, 0.066f, wrap, 8, wrapTile);
                    legs.Limb(new Vector3(k * 0.11f, 0.47f, 0.42f), new Vector3(k * 0.11f, 0.09f, 0.45f), 0.062f, 0.046f, wrap, 8, wrapTile);
                    legs.Ellipsoid(new Vector3(k * 0.11f, 0.04f, 0.5f), new Vector3(0.046f, 0.035f, 0.09f), id, sole, 8, 5);
                }
                legs.Cylinder(new Vector3(0f, 0f, -0.02f), 0.17f, 0.16f, 0.42f, new Color(0.3f, 0.2f, 0.12f), new Color(0.36f, 0.25f, 0.15f), 10);
            }
            else if (look.Wrapper)
            {
                var hem = look.Elder ? 0.27f : 0.235f;
                legs.Lathe(Vector3.zero, id, new[]
                {
                    (hipY + 0.08f, 0.165f, 0.125f), (hipY - 0.15f, 0.195f, 0.15f), (0.5f, 0.215f, 0.17f), (0.15f, hem - 0.005f, 0.185f), (0.04f, hem, 0.19f),
                }, wrap, 16, wrapTile);
                foreach (var k in new[] { -1f, 1f }) legs.Ellipsoid(new Vector3(k * 0.08f, 0.035f, 0.11f), new Vector3(0.045f, 0.035f, 0.085f), id, sole, 8, 5);
            }
            else
                foreach (var k in new[] { -1f, 1f })
                {
                    legs.Limb(new Vector3(k * 0.095f, hipY, 0f), new Vector3(k * 0.1f, 0.48f, 0.01f), 0.088f, 0.062f, wrap, 8, wrapTile);
                    legs.Limb(new Vector3(k * 0.1f, 0.48f, 0.01f), new Vector3(k * 0.1f, 0.09f, -0.005f), 0.062f, 0.046f, wrap, 8, wrapTile);
                    legs.Ellipsoid(new Vector3(k * 0.1f, 0.04f, 0.05f), new Vector3(0.046f, 0.04f, 0.1f), id, sole, 8, 5);
                }
            MeshNode(name + " Legs", legs.Build("Legs"), _vc, f.Root);

            // ---- the torso: shoulders, chest, waist; a neck ----
            f.Torso = Node(name + " Torso", f.Root);
            f.Torso.localPosition = new Vector3(0f, hipY, 0f);
            var body = new MeshKit();
            var w = look.Elder ? 1.12f : look.Woman ? 0.94f : 1f;
            body.Lathe(Vector3.zero, id, new[]
            {
                (0.6f, 0.055f, 0.05f), (0.56f, 0.13f * w, 0.085f), (0.5f, 0.205f * w, 0.112f), (0.42f, 0.21f * w, 0.125f),
                (0.31f, 0.19f * w, look.Woman ? 0.142f : 0.125f), (0.17f, 0.162f * w, 0.115f), (0.05f, 0.17f * w, 0.12f), (-0.06f, 0.18f * w, 0.125f),
            }, top, 14, topTile);
            foreach (var k in new[] { -1f, 1f }) body.Ellipsoid(new Vector3(k * 0.2f * w, 0.46f, 0f), new Vector3(0.072f, 0.066f, 0.075f), id, top, 10, 6, topTile);
            if (look.Elder) body.Ellipsoid(new Vector3(0f, 0.44f, 0.105f), new Vector3(0.075f, 0.12f, 0.03f), Quaternion.Euler(-8f, 0f, 0f), new Color(0.85f, 0.65f, 0.25f), 10, 6, WaxPrints.Tile(7)); // embroidery
            if (look.Woman || look.Elder) Beads(body, look.Woman);
            if (look.Matron)
            {
                foreach (var k in new[] { -1f, 1f })
                    body.Ellipsoid(new Vector3(k * 0.072f, 0.335f, 0.11f), new Vector3(0.074f, 0.068f, 0.062f), Quaternion.Euler(-12f, 0f, 0f), top, 10, 6, topTile); // bust
                // an iborun, the shawl worn over one shoulder, across the body to the other hip
                var shawl = ClothColour(look.HatPrint, look.Hat); var shawlTile = PrintTile(look.HatPrint);
                body.Limb(new Vector3(0.16f * w, 0.5f, 0.03f), new Vector3(-0.15f * w, 0.06f, 0.11f), 0.042f, 0.05f, shawl, 10, shawlTile);
                body.Limb(new Vector3(0.16f * w, 0.5f, 0.03f), new Vector3(0.12f * w, 0.2f, -0.12f), 0.04f, 0.035f, shawl, 10, shawlTile); // its end hanging behind
            }
            body.Limb(new Vector3(0f, 0.54f, 0f), new Vector3(0f, 0.68f, 0.008f), 0.049f, 0.046f, skin, 10);
            var hc = new Vector3(0f, 0.78f, 0.012f);
            if (ownHead)
            {
                f.Head = Node(name + " Head", f.Torso);
                f.Head.localPosition = hc;
                var head = new MeshKit();
                HeadMesh(head, Vector3.zero, look, false, !movingMouth, look.Elder);
                MeshNode(name + " Head", head.Build("Head"), _vc, f.Head);
                Transform Eye(float k)
                {
                    var eye = Node(name + " Eye", f.Head);
                    eye.localPosition = new Vector3(k * 0.035f, 0.018f, 0.084f);
                    var em = new MeshKit();
                    em.Ellipsoid(Vector3.zero, new Vector3(0.018f, 0.011f, 0.012f), id, new Color(0.86f, 0.83f, 0.76f), 8, 5);
                    em.Ellipsoid(new Vector3(0f, 0f, 0.009f), new Vector3(0.0085f, 0.0085f, 0.004f), id, new Color(0.07f, 0.045f, 0.035f), 8, 4);
                    MeshNode(name + " Eye", em.Build("Eye"), _vc, eye);
                    return eye;
                }
                f.EyeL = Eye(-1f); f.EyeR = Eye(1f);
                if (movingMouth && !look.Elder) TalkingMouth(f, name, look);
                else
                {
                    f.Mouth = Node(name + " Mouth", f.Head);
                    f.Mouth.localPosition = new Vector3(0f, -0.06f, 0.087f);
                    var mm = new MeshKit();
                    mm.Ellipsoid(Vector3.zero, new Vector3(0.023f, 0.006f, 0.008f), id, new Color(0.12f, 0.04f, 0.04f), 8, 4);
                    MeshNode(name + " Mouth", mm.Build("Mouth"), _vc, f.Mouth);
                    if (look.Elder) BuildSpeakingMouth(f, look);
                }
            }
            else HeadMesh(body, hc, look, true);
            MeshNode(name + " Body", body.Build("Body"), _vc, f.Torso);

            // ---- arms: shoulder, elbow, hand ----
            Transform Arm(float k, out Transform fore)
            {
                var arm = Node(name + " Arm", f.Torso);
                arm.localPosition = new Vector3(k * 0.215f * w, 0.455f, 0f);
                var am = new MeshKit();
                if (look.Elder)
                    am.Lathe(Vector3.zero, id, new[] { (0.02f, 0.06f, 0.06f), (-0.15f, 0.08f, 0.08f), (-0.3f, 0.115f, 0.1f) }, top, 10, topTile); // a wide sleeve
                else
                    am.Limb(Vector3.zero, new Vector3(0f, -0.27f, 0f), 0.054f, 0.044f, top, 8, topTile);
                MeshNode(name + " Arm", am.Build("Arm"), _vc, arm);
                fore = Node(name + " Forearm", arm);
                fore.localPosition = new Vector3(0f, -0.27f, 0f);
                var fm = new MeshKit();
                fm.Limb(Vector3.zero, new Vector3(0f, -0.23f, 0f), 0.041f, 0.032f, skin, 8);
                fm.Ellipsoid(new Vector3(0f, -0.285f, 0.004f), new Vector3(0.028f, 0.047f, 0.016f), id, skin, 8, 5);            // palm
                fm.Ellipsoid(new Vector3(0f, -0.33f, 0.002f), new Vector3(0.024f, 0.024f, 0.012f), id, skin * 0.95f, 8, 4);    // fingers
                fm.Limb(new Vector3(-k * 0.012f, -0.255f, 0.015f), new Vector3(-k * 0.018f, -0.295f, 0.032f), 0.011f, 0.009f, skin, 6); // thumb
                MeshNode(name + " Forearm", fm.Build("Forearm"), _vc, fore);
                fore.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                return arm;
            }
            f.ArmL = Arm(-1f, out f.ForeL); f.ArmR = Arm(1f, out f.ForeR);
            return f;
        }

        /// <summary>
        /// Lips that open: an upper lip that stays, a lower lip on <see cref="Figure.Mouth"/>, and the dark inside of the mouth
        /// on <see cref="Figure.Jaw"/>. The same places as the lips baked into <see cref="HeadMesh"/>.
        /// </summary>
        void TalkingMouth(Figure f, string name, Look look)
        {
            var id = Quaternion.identity;
            var skin = look.Skin;
            var lip = new Color(skin.r * 1.05f + 0.04f, skin.g * 0.8f, skin.b * 0.8f);
            var upper = Node(name + " Upper Lip", f.Head);
            upper.localPosition = new Vector3(0f, -0.051f, 0.09f);
            var um = new MeshKit();
            um.Ellipsoid(Vector3.zero, new Vector3(0.03f, 0.008f, 0.012f), id, lip, 8, 4);
            MeshNode(name + " Upper Lip", um.Build("Upper Lip"), _vc, upper);

            f.Mouth = Node(name + " Mouth", f.Head);
            f.Mouth.localPosition = new Vector3(0f, -0.067f, 0.086f); // the lower lip, at rest against the upper one
            var lm = new MeshKit();
            lm.Ellipsoid(Vector3.zero, new Vector3(0.027f, 0.009f, 0.012f), id, lip * 0.95f, 8, 4);
            MeshNode(name + " Mouth", lm.Build("Lower Lip"), _vc, f.Mouth);

            f.Jaw = Node(name + " Jaw", f.Head);
            f.Jaw.localPosition = new Vector3(0f, -0.059f, 0.082f); // the dark inside, just behind the closed lips
            var jm = new MeshKit();
            jm.Ellipsoid(Vector3.zero, new Vector3(0.018f, 0.014f, 0.01f), id, new Color(0.1f, 0.03f, 0.03f), 8, 4);
            MeshNode(name + " Jaw", jm.Build("Jaw"), _vc, f.Jaw);
            f.Jaw.localScale = new Vector3(1f, 0.15f, 1f); // a closed mouth: the slit is hidden between the lips
        }

        /// <summary>A head at <paramref name="c"/>: skull and jaw, ears, nose, lips, brows, (eyes), and hair, a head wrap or a cap.</summary>
        void HeadMesh(MeshKit m, Vector3 c, Look look, bool eyes, bool lips = true, bool animatedMouth = false)
        {
            var id = Quaternion.identity;
            var skin = look.Skin;
            var shade = skin * 0.9f; shade.a = 1f;
            var lip = new Color(skin.r * 1.05f + 0.04f, skin.g * 0.8f, skin.b * 0.8f);
            m.Ellipsoid(c + new Vector3(0f, 0.012f, 0f), new Vector3(0.092f, 0.108f, 0.1f), id, skin, 14, 9);
            if (!animatedMouth) m.Ellipsoid(c + new Vector3(0f, -0.05f, 0.02f), new Vector3(0.074f, 0.06f, 0.075f), id, skin, 12, 7); // jaw and chin
            foreach (var k in new[] { -1f, 1f })
            {
                m.Ellipsoid(c + new Vector3(k * 0.09f, -0.004f, -0.006f), new Vector3(0.016f, 0.03f, 0.022f), id, shade, 8, 5); // ears
                if (look.Matron) m.Ellipsoid(c + new Vector3(k * 0.094f, -0.042f, 0f), new Vector3(0.01f, 0.014f, 0.01f), id, new Color(0.95f, 0.72f, 0.22f), 6, 4); // gold earrings
                m.Ellipsoid(c + new Vector3(k * 0.036f, 0.044f, 0.088f), new Vector3(0.023f, 0.0055f, 0.009f), Quaternion.Euler(0f, 0f, k * -8f), look.Elder ? new Color(0.5f, 0.48f, 0.45f) : look.Hair, 8, 4); // brows
                if (eyes)
                {
                    m.Ellipsoid(c + new Vector3(k * 0.035f, 0.018f, 0.084f), new Vector3(0.018f, 0.011f, 0.012f), id, new Color(0.86f, 0.83f, 0.76f), 8, 5);
                    m.Ellipsoid(c + new Vector3(k * 0.035f, 0.018f, 0.093f), new Vector3(0.0085f, 0.0085f, 0.004f), id, new Color(0.07f, 0.045f, 0.035f), 8, 4);
                }
            }
            m.Ellipsoid(c + new Vector3(0f, -0.012f, 0.097f), new Vector3(0.019f, 0.027f, 0.022f), Quaternion.Euler(-10f, 0f, 0f), skin, 8, 5); // nose
            m.Ellipsoid(c + new Vector3(0f, -0.032f, 0.094f), new Vector3(0.031f, 0.013f, 0.018f), id, shade, 8, 5);                                 // its wings
            if (lips && !animatedMouth)
            {
                m.Ellipsoid(c + new Vector3(0f, -0.051f, 0.09f), new Vector3(0.03f, 0.008f, 0.012f), id, lip, 8, 4);
                m.Ellipsoid(c + new Vector3(0f, -0.067f, 0.086f), new Vector3(0.027f, 0.009f, 0.012f), id, lip * 0.95f, 8, 4);
            }

            var hair = look.Elder ? new Color(0.55f, 0.53f, 0.5f) : look.Hair;
            var hat = ClothColour(look.HatPrint, look.Hat); var hatTile = PrintTile(look.HatPrint);
            if (look.Elder)
            {
                if (!animatedMouth) m.Ellipsoid(c + new Vector3(0f, -0.078f, 0.05f), new Vector3(0.062f, 0.05f, 0.05f), id, hair, 10, 6); // beard
                m.Ellipsoid(c + new Vector3(0f, -0.043f, 0.094f), new Vector3(0.033f, 0.008f, 0.01f), id, hair, 8, 4);           // moustache
            }
            if (look.Headwear == 1)
            {
                // a gele: the wrap round the head, and its tall fan
                m.Ellipsoid(c + new Vector3(0f, 0.035f, -0.012f), new Vector3(0.108f, 0.112f, 0.114f), Quaternion.Euler(-20f, 0f, 0f), hat, 14, 7, hatTile, 98f);
                m.Ellipsoid(c + new Vector3(0f, 0.13f, -0.04f), new Vector3(0.165f, 0.075f, 0.1f), Quaternion.Euler(-32f, 0f, 0f), hat, 14, 7, hatTile);
                m.Ellipsoid(c + new Vector3(0.06f, 0.165f, 0.015f), new Vector3(0.095f, 0.045f, 0.06f), Quaternion.Euler(-20f, 0f, 32f), hat, 12, 6, hatTile);
            }
            else if (look.Headwear == 2)
            {
                m.Ellipsoid(c + new Vector3(0f, 0.02f, -0.01f), new Vector3(0.096f, 0.104f, 0.103f), Quaternion.Euler(-28f, 0f, 0f), hair, 12, 5, null, 80f);
                m.Ellipsoid(c + new Vector3(0.012f, 0.06f, -0.012f), new Vector3(0.1f, 0.085f, 0.106f), Quaternion.Euler(-12f, 0f, -16f), hat, 12, 6, hatTile, 96f); // a fila, slouched to one side
            }
            else
                m.Ellipsoid(c + new Vector3(0f, 0.02f, -0.01f), new Vector3(0.097f, 0.106f, 0.104f), Quaternion.Euler(-28f, 0f, 0f), hair, 12, 5, null, 82f);
        }

        // Same resting geometry as HeadMesh, separated so the actual lips, chin and beard can move.
        void BuildSpeakingMouth(Figure figure, Look look)
        {
            var lip = new Color(look.Skin.r * 1.05f + 0.04f, look.Skin.g * 0.8f, look.Skin.b * 0.8f);
            var upper = new MeshKit();
            upper.Ellipsoid(Vector3.zero, new Vector3(0.03f, 0.008f, 0.012f), Quaternion.identity, lip, 8, 4);
            _elderUpperLip = MeshNode("Elder Upper Lip", upper.Build("Upper lip"), _vc, figure.Head);
            _elderUpperLipRestPosition = _elderUpperLip.localPosition = new Vector3(0f, -0.051f, 0.09f);
            _elderJaw = Node("Elder Jaw", figure.Head);
            _elderJawRestPosition = _elderJaw.localPosition = new Vector3(0f, -0.04f, 0.015f);
            var lower = new MeshKit();
            lower.Ellipsoid(new Vector3(0f, -0.05f, 0.02f) - _elderJawRestPosition,
                new Vector3(0.074f, 0.06f, 0.075f), Quaternion.identity, look.Skin, 12, 7);
            lower.Ellipsoid(new Vector3(0f, -0.067f, 0.086f) - _elderJawRestPosition,
                new Vector3(0.027f, 0.009f, 0.012f), Quaternion.identity, lip * 0.95f, 8, 4);
            lower.Ellipsoid(new Vector3(0f, -0.078f, 0.05f) - _elderJawRestPosition,
                new Vector3(0.062f, 0.05f, 0.05f), Quaternion.identity, new Color(0.55f, 0.53f, 0.5f), 10, 6);
            MeshNode("Elder Chin Lip and Beard", lower.Build("Moving lower face"), _vc, _elderJaw);
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
                pile.Ellipsoid(X0Z(VillageLayout.YamPile + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * rr, y), new Vector3(0.055f, 0.05f, 0.17f + R() * 0.07f),
                    Quaternion.Euler((R() - 0.5f) * 30f, R() * 360f, (R() - 0.5f) * 30f), Jitter(yam, 0.2f), 7, 5);
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
            // a carved mortar, waisted like an hourglass, with white pounded yam in its bowl
            mk.Lathe(X0Z(at), Quaternion.identity, new[] { (0.62f, 0.27f, 0.27f), (0.5f, 0.24f, 0.24f), (0.33f, 0.15f, 0.15f), (0.15f, 0.2f, 0.2f), (0f, 0.25f, 0.25f) },
                new Color(0.45f, 0.3f, 0.18f), 14);
            mk.Ellipsoid(X0Z(at, 0.6f), new Vector3(0.23f, 0.05f, 0.23f), Quaternion.identity, new Color(0.9f, 0.87f, 0.76f), 12, 5);
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
                pestle.Limb(new Vector3(0f, -0.85f, 0f), new Vector3(0f, 0.85f, 0f), 0.05f, 0.04f, new Color(0.62f, 0.48f, 0.3f), 8);
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
                drum.Box(front + Vector3.up * h * 0.28f, new Vector3(r * 1.7f, 0.03f, r * 1.7f), Quaternion.identity, new Color(0.9f, 0.72f, 0.16f));  // a gold band
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
            var uv = new Vector2[Strips * 8];
            for (var i = 0; i < Strips; i++)
            {
                // each strip a different wax print, a slice of its tile running down the strip
                var c = new Color(0.95f, 0.95f, 0.95f);
                var back = new Color(0.6f, 0.6f, 0.6f);
                for (var k = 0; k < 4; k++) { _masqC[i * 8 + k] = c; _masqC[i * 8 + 4 + k] = back; }
                var tile = WaxPrints.Tile(1 + i % WaxPrints.Count);
                float u0 = tile.x + tile.width * ((i * 0.37f) % 0.75f), u1 = u0 + tile.width * 0.25f;
                Vector2[] corners = { new Vector2(u0, tile.yMax), new Vector2(u1, tile.yMax), new Vector2(u1, tile.y), new Vector2(u0, tile.y) };
                for (var k = 0; k < 4; k++) { uv[i * 8 + k] = corners[k]; uv[i * 8 + 4 + k] = corners[k]; }
                var v = i * 8; var t = i * 12;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                tris[t + 6] = v + 4; tris[t + 7] = v + 6; tris[t + 8] = v + 5; tris[t + 9] = v + 4; tris[t + 10] = v + 7; tris[t + 11] = v + 6;
            }
            UpdateMasqueradeMesh(0f);
            _masqMesh.colors = _masqC;
            _masqMesh.uv = uv;
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
                Hat = new Color(0.45f, 0.12f, 0.15f), Hair = new Color(0.04f, 0.035f, 0.03f), HatPrint = 2, Headwear = 2, Wrapper = true, Elder = true,
            };
            _local = Person("The Local", VillageLayout.Local, VillageLayout.FrontOfGate - VillageLayout.Local, look, 1.02f, false, true);
            _elderMouthMesh = _local.Mouth.GetComponentInChildren<MeshRenderer>().transform;
            _elderMouthRestScale = _elderMouthMesh.localScale;
            _elderMouthRestPosition = _elderMouthMesh.localPosition;
            // a walking stick in the left hand
            var stick = new MeshKit();
            stick.Limb(new Vector3(-0.35f, 0.01f, 0.37f), new Vector3(-0.32f, 0.99f, 0.35f), 0.02f, 0.017f, new Color(0.3f, 0.2f, 0.11f), 6);
            stick.Ellipsoid(new Vector3(-0.32f, 1.01f, 0.35f), new Vector3(0.035f, 0.03f, 0.035f), Quaternion.identity, new Color(0.25f, 0.16f, 0.09f), 8, 5); // a carved knob
            MeshNode("Walking Stick", stick.Build("Walking Stick"), _vc, _local.Root);
            _local.ArmL.localRotation = Quaternion.Euler(-22f, 0f, -14f); // the hand on the stick's knob
            _local.ForeL.localRotation = Quaternion.Euler(-50f, 0f, 0f);
            // a lantern on a post beside them, so their face shows
            var post = VillageLayout.Local + VillageLayout.Right * -0.85f - VillageLayout.Into * 0.75f;
            var mk = new MeshKit();
            mk.Box(X0Z(post, 0.9f), new Vector3(0.07f, 1.8f, 0.07f), Quaternion.identity, new Color(0.3f, 0.2f, 0.12f));
            MeshNode("Local Lantern Post", mk.Build("Lantern Post"), _vc, _root);
            var lanternLight = Lantern(X0Z(post, 1.88f), 1f, 4.4f, 5f, _root);
            var faceLight = VertexLight(X0Z(VillageLayout.Local - VillageLayout.Into * 1.1f, 1.7f), 2.6f, 0.9f, new Color(1f, 0.7f, 0.45f), 4.9f, _root);
            // The lights follow the elder into the room. The lantern prop stays in the village.
            lanternLight.transform.SetParent(_local.Root, true);
            faceLight.transform.SetParent(_local.Root, true);
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
            _murmurClip = ProceduralClips.Clip("murmur", ProceduralClips.MurmurSamples());
            _crowd = Bed("Crowd", _murmurClip, X0Z(VillageLayout.YamPile, 1.4f), 0.45f, 3f, 22f);
            Bed("Village Fire", ProceduralClips.Clip("village fire", ProceduralClips.FireSamples()), X0Z(VillageLayout.Fire, 0.4f), 0.6f, 1.5f, 12f);
            _thudClip = ProceduralClips.Clip("pestle", ProceduralClips.StepSamples(151));
            _thud = Voice("Pestles", _root, 18f);
            _thud.transform.localPosition = X0Z(VillageLayout.At(3.2f, 2.1f), 0.5f);
        }

        // ---------- every frame ----------

        void Update()
        {
            UpdatePerformanceAudio(Time.unscaledDeltaTime);
            var cam = Tick();
            if (cam == null) return;
            var dt = Time.deltaTime;
            Celebrants(dt);
            var dist = Vector2.Distance(Viewer, VillageLayout.Plaza);
            // the drums come clear as you come out of the trees
            _drumsMuffle.cutoffFrequency = Mathf.Lerp(900f, 22000f, Mathf.Pow(Mathf.Clamp01(1f - (dist - 8f) / 26f), 2f));
            var seen = (_placeWanted || PreviewFrom.HasValue) && dist < ShowWithin; // the portal in your room shows it too
            if (_root.gameObject.activeSelf != seen) _root.gameObject.SetActive(seen);
            if (!seen) return;

            _fire.Update(cam, _t, 1f);
            var beat = _drums.isPlaying ? _drums.time : _t % ProceduralClips.DrumLoopLength;
            Drummers(beat);
            Pounders(beat);
            Crowd(dt);
            Dancers(beat);
            if (_masq.parent != _party) Masquerade(dt, beat);
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
                // the upper arm lifts a little, the forearm a lot: the hand comes down flat on the skin
                float Lift(bool striking) => striking ? Mathf.Clamp01(lift) : 0.35f;
                d.ArmR.localRotation = Quaternion.Euler(-28f - Lift(right) * 14f, 0f, 10f);
                d.ArmL.localRotation = Quaternion.Euler(-28f - Lift(!right) * 14f, 0f, -10f);
                d.ForeR.localRotation = Quaternion.Euler(-58f - Lift(right) * 50f, 0f, 0f);
                d.ForeL.localRotation = Quaternion.Euler(-58f - Lift(!right) * 50f, 0f, 0f);
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
                p.ForeR.localRotation = p.ForeL.localRotation = Quaternion.Euler(-25f + up * 15f, 0f, 0f);
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
                f.ForeR.localRotation = Quaternion.Euler(-20f - (1f - bend) * 60f, 0f, 0f); // reaching down, then the yam held up to look at
                f.ForeL.localRotation = Quaternion.Euler(-15f - bend * 20f, 0f, 0f);
                f.Root.localRotation = Quaternion.LookRotation(Face(VillageLayout.YamPile - new Vector2(f.Home.x, f.Home.z))) * Quaternion.Euler(0f, 0f, Mathf.Sin(_t * 0.5f + f.Ph) * 1.5f); // weight shifts
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
                k.ForeL.localRotation = k.ForeR.localRotation = Quaternion.Euler(-75f, 0f, 0f);
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
                d.ForeL.localRotation = Quaternion.Euler(-30f + Mathf.Sin(beat * 2f + d.Ph) * 30f, 0f, 0f);
                d.ForeR.localRotation = Quaternion.Euler(-30f - Mathf.Sin(beat * 2f + d.Ph) * 30f, 0f, 0f);
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
            // blink now and then
            var blink = Mathf.Repeat(_t + 1.3f, 3.7f) < 0.13f;
            var lid = new Vector3(1f, blink ? 0.12f : 1f, 1f);
            _local.EyeL.localScale = _local.EyeR.localScale = lid;
            // a wave, the first time you come near
            if (!_waved && flat.magnitude < 5.5f) { _waved = true; _waveT = 0f; }
            if (_waveT >= 0f)
            {
                _waveT += dt;
                var up = Mathf.Clamp01(Mathf.Min(_waveT * 3f, (2.6f - _waveT) * 3f));
                _local.ArmR.localRotation = Quaternion.Euler(-70f * up, 0f, 38f * up);                          // the elbow out to the side
                _local.ForeR.localRotation = Quaternion.Euler(-95f * up - 12f * (1f - up), 0f, Mathf.Sin(_waveT * 9f) * 28f * up); // the hand up, waving
                if (_waveT > 2.6f) { _waveT = -1f; _local.ArmR.localRotation = Quaternion.identity; _local.ForeR.localRotation = Quaternion.Euler(-12f, 0f, 0f); }
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
