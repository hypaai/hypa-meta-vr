using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The walk from the campfire to the hut (ticket 3G), built at runtime inside the camp (see <see cref="HutTrailLayout"/>):
    ///  - a wooden signpost by the fallen log, "HUT" with its arrow pointing down the trail;
    ///  - a dirt trail through the pines, lit by a few lanterns on posts, with fireflies drifting along it;
    ///  - a round mud hut with a thatched roof, a glowing lantern by its door, smoke from the roof and an empty room inside;
    ///  - an owl on a branch that watches you pass and hoots, and a wolf pacing beside the middle of the trail: you see it
    ///    and hear it as you come around the bend; it stops and growls, then runs off into the trees and howls;
    ///  - footsteps while you walk the trail.
    /// Every mesh is made here (vertex colours on the pines' Lambert shader, with fog), every sound is synthesised
    /// (<see cref="ProceduralClips"/>). Lanterns light by glow cards and per-vertex lights, so the Quest budget holds:
    /// the fire stays the only per-pixel light.
    /// </summary>
    public sealed class HutTrail : TrailBuilder
    {
        Material _smoke;

        /// <summary>The hut itself (its door on local +Z, its floor at local y = 0): what is inside it hangs here (see <see cref="HutVisit"/>).</summary>
        public Transform HutRoot { get; private set; }

        // owl
        Transform _owlHead;
        Material _owlEyes;
        AudioSource _owlVoice;
        AudioClip _hoot;
        float _nextHoot, _blinkAt;

        // wolf
        enum WolfState { Pacing, Watching, Running, Gone }
        WolfState _wolfState;
        Transform _wolf, _wolfHead;
        readonly Transform[] _legs = new Transform[4];
        Material _wolfEyes;
        AudioSource _wolfVoice;
        AudioClip _growl, _howl, _snap, _rustle;
        float _wolfT;
        Vector3 _wolfHome, _runDir;
        Vector2 _paceA, _paceB;
        bool _paceToB, _heard;

        public void Build(StoryAssets a, Texture2D glow)
        {
            Init(a, glow, 3, "Trail");
            Ribbon("Trail", new List<Vector2>(HutTrailLayout.Path) { HutTrailLayout.Door }, HutTrailLayout.PathHalf,
                new Color(0.17f, 0.135f, 0.1f), new Color(0.11f, 0.095f, 0.075f));
            Signpost("HUT", HutTrailLayout.Sign, (HutTrailLayout.Path[1] - HutTrailLayout.Path[0]).normalized, 0.78f, 1.5f);
            LanternPosts(new[] { 0.12f, 0.26f, 0.40f, 0.52f, 0.64f, 0.76f, 0.88f }, HutTrailLayout.Along, HutTrailLayout.PathHalf);
            Undergrowth("Trail Undergrowth", HutTrailLayout.Along, HutTrailLayout.PathHalf,
                p => HutTrailLayout.NearHut(p.x, p.y, 0.3f) || p.magnitude < 5.5f || Vector2.Distance(p, HutTrailLayout.Sign) < 0.5f);
            TrailMist(HutTrailLayout.Along, 6, HutTrailLayout.PathHalf);
            BuildHut();
            BuildFireflies();
            BuildOwl();
            BuildWolf();
            BuildFootsteps();
        }

        /// <summary>Footsteps on the trail (beyond the clearing's seats) and inside the hut.</summary>
        protected override bool OnGround(Vector2 v) =>
            (v.magnitude > 5f && HutTrailLayout.DistanceToPath(v) < HutTrailLayout.WalkHalf + 0.3f) || HutTrailLayout.InsideHut(v);

        // ---------- the hut ----------

        void BuildHut()
        {
            var hut = Node("Hut");
            HutRoot = hut;
            hut.localPosition = X0Z(HutTrailLayout.Hut);
            // built with its door on local +Z, then turned to face back down the trail
            hut.localRotation = Quaternion.LookRotation(X0Z(HutTrailLayout.DoorDir));

            float rOut = HutTrailLayout.HutRadius;
            var gap = HutTrailLayout.DoorHalfAngle * Mathf.Deg2Rad;
            const float apexY = HutTrailLayout.RoofApexY;
            RoundHut(hut, new HutShape
            {
                RIn = HutTrailLayout.WallInner, ROut = rOut, WallH = HutTrailLayout.WallHeight, DoorH = HutTrailLayout.DoorHeight,
                DoorHalfAngle = HutTrailLayout.DoorHalfAngle, Eave = HutTrailLayout.RoofEave, EaveY = HutTrailLayout.RoofEaveY, ApexY = apexY,
                Mud = new Color(0.47f, 0.33f, 0.22f), MudDark = new Color(0.3f, 0.21f, 0.14f), Thatch = new Color(0.74f, 0.55f, 0.29f), Floor = true,
            });
            Vector3 At(float ang, float r, float y) => new Vector3(Mathf.Sin(ang) * r, y, Mathf.Cos(ang) * r);

            // the lantern by the door, warm light on the walls (per vertex) and a glow on the ground
            var doorLantern = hut.TransformPoint(At(gap + 0.16f, rOut + 0.22f, 1.75f));
            Lantern(transform.InverseTransformPoint(doorLantern), 1.1f, 0.4f, 6.5f);
            GroundPool(HutTrailLayout.Door + HutTrailLayout.DoorDir * 0.8f, 3.6f, 3.6f, new Color(1f, 0.56f, 0.2f), 0.18f, 0.4f);
            // a dim glow inside, so the empty room isn't pitch black
            var inside = Node("Hut Inside Light", hut).gameObject.AddComponent<Light>();
            inside.transform.localPosition = new Vector3(0f, 1.6f, -0.3f);
            inside.type = LightType.Point; inside.range = 3.6f; inside.color = new Color(1f, 0.62f, 0.32f); inside.intensity = 0.7f;
            inside.shadows = LightShadows.None; inside.renderMode = LightRenderMode.ForceVertex;
            _lamps.Add((inside, 0.7f, 2.2f));
            GroundPool(HutTrailLayout.Hut, 3.4f, 3.4f, new Color(1f, 0.5f, 0.2f), 0.08f, 2.2f);

            // smoke curling up from the roof's peak
            _smoke = new Material(_a.particles) { name = "Hut Smoke", color = new Color(0.55f, 0.58f, 0.6f) };
            _smoke.SetFloat("_Size", 0.5f); _smoke.SetFloat("_Alpha", 0.16f); _smoke.SetFloat("_Speed", 0.22f);
            _smoke.SetFloat("_Blink", 0f); _smoke.SetFloat("_Ember", 1f); _smoke.SetFloat("_Scale", 1.6f);
            _smoke.SetVector("_EmberBase", new Vector4(HutTrailLayout.Hut.x, apexY + 0.1f, HutTrailLayout.Hut.y, 0f));
            MeshNode("Hut Smoke", StoryMeshes.Billboards(22, 23, r => Vector3.zero), _smoke);
        }

        // ---------- fireflies along the trail ----------

        void BuildFireflies()
        {
            var m = new Material(_a.particles) { name = "Trail Fireflies", color = new Color(0.78f, 1f, 0.48f) };
            m.SetFloat("_Size", 0.06f); m.SetFloat("_Alpha", 1f); m.SetFloat("_Speed", 0.8f);
            m.SetFloat("_Blink", 1f); m.SetFloat("_Ember", 0f);
            var r = new System.Random(77);
            var mesh = StoryMeshes.Billboards(34, 78, rr =>
            {
                var (p, dir) = HutTrailLayout.Along(0.1f + (float)r.NextDouble() * 0.85f);
                var side = new Vector2(-dir.y, dir.x) * (((float)r.NextDouble() - 0.5f) * 5f);
                return X0Z(p + side, 0.3f + (float)r.NextDouble() * 1.8f);
            }, 3f);
            MeshNode("Trail Fireflies", mesh, m);
        }

        // ---------- the owl ----------

        void BuildOwl()
        {
            // a dead branch out of a pine beside the trail, the owl at its end
            var (p, dir) = HutTrailLayout.Along(0.3f);
            var side = new Vector2(dir.y, -dir.x); // the right of the trail as you walk
            var trunk = p + side * 3.4f;
            var perch = p + side * 2.1f;
            var mk = new MeshKit();
            var bark = new Color(0.11f, 0.085f, 0.065f);
            mk.Box(X0Z(trunk, 2.6f), new Vector3(0.28f, 5.2f, 0.28f), Quaternion.Euler(0f, 20f, 0f), bark);
            var mid = (trunk + perch) * 0.5f;
            mk.Box(X0Z(mid, 3.15f), new Vector3(0.09f, 0.09f, Vector2.Distance(trunk, perch) + 0.3f), Quaternion.LookRotation(X0Z(perch - trunk)) * Quaternion.Euler(-8f, 0f, 0f), bark * 1.2f);
            MeshNode("Owl Branch", mk.Build("Owl Branch"), _vc);

            var owl = Node("Owl");
            owl.localPosition = X0Z(perch, 3.24f);
            var plumage = new Color(0.33f, 0.26f, 0.2f);
            var body = new MeshKit();
            body.Box(new Vector3(0f, 0.14f, 0f), new Vector3(0.2f, 0.28f, 0.17f), Quaternion.identity, plumage);
            body.Box(new Vector3(0f, 0.1f, 0.075f), new Vector3(0.14f, 0.18f, 0.04f), Quaternion.identity, new Color(0.5f, 0.42f, 0.32f)); // pale breast
            body.Box(new Vector3(-0.05f, 0f, 0.02f), new Vector3(0.04f, 0.03f, 0.06f), Quaternion.identity, new Color(0.25f, 0.22f, 0.12f)); // talons
            body.Box(new Vector3(0.05f, 0f, 0.02f), new Vector3(0.04f, 0.03f, 0.06f), Quaternion.identity, new Color(0.25f, 0.22f, 0.12f));
            MeshNode("Owl Body", body.Build("Owl Body"), _vc, owl);
            _owlHead = Node("Owl Head", owl);
            _owlHead.localPosition = new Vector3(0f, 0.34f, 0f);
            var head = new MeshKit();
            head.Box(Vector3.zero, new Vector3(0.19f, 0.15f, 0.16f), Quaternion.identity, plumage * 1.05f);
            head.Box(new Vector3(-0.06f, 0.09f, 0f), new Vector3(0.04f, 0.06f, 0.04f), Quaternion.Euler(0f, 0f, 15f), plumage * 0.9f);  // ear tufts
            head.Box(new Vector3(0.06f, 0.09f, 0f), new Vector3(0.04f, 0.06f, 0.04f), Quaternion.Euler(0f, 0f, -15f), plumage * 0.9f);
            head.Box(new Vector3(0f, 0f, 0.075f), new Vector3(0.16f, 0.11f, 0.02f), Quaternion.identity, new Color(0.55f, 0.47f, 0.37f)); // face disc
            head.Box(new Vector3(0f, -0.02f, 0.09f), new Vector3(0.02f, 0.03f, 0.02f), Quaternion.identity, new Color(0.2f, 0.18f, 0.12f)); // beak
            MeshNode("Owl Head", head.Build("Owl Head"), _vc, _owlHead);
            _owlEyes = GlowMat(new Color(1f, 0.82f, 0.32f), 0.9f);
            GlowCard("Owl Eye", new Vector3(-0.038f, 0.012f, 0.088f), 0.05f, _owlEyes, _owlHead);
            GlowCard("Owl Eye", new Vector3(0.038f, 0.012f, 0.088f), 0.05f, _owlEyes, _owlHead);
            owl.localRotation = Quaternion.LookRotation(X0Z(p - perch));

            _hoot = ProceduralClips.Clip("hoot", ProceduralClips.HootSamples(41));
            _owlVoice = Voice("Owl", _owlHead, 22f);
            _nextHoot = 3f;
            _blinkAt = 2f;
        }

        // ---------- the wolf ----------

        void BuildWolf()
        {
            _wolf = Node("Wolf");
            _wolfHome = X0Z(HutTrailLayout.WolfAt);
            _wolf.localPosition = _wolfHome;
            var fur = new Color(0.24f, 0.23f, 0.22f);
            var furLight = new Color(0.4f, 0.38f, 0.34f);
            var id = Quaternion.identity;
            var dark = new Color(0.05f, 0.05f, 0.05f);
            var mk = new MeshKit();
            // a lean body along local +Z (its head end), shoulder about 0.75 m: deep chest, tucked belly, narrow hips
            mk.Ellipsoid(new Vector3(0f, 0.67f, 0.22f), new Vector3(0.145f, 0.19f, 0.27f), Quaternion.Euler(-6f, 0f, 0f), fur, 12, 8);       // ribcage
            mk.Ellipsoid(new Vector3(0f, 0.69f, -0.13f), new Vector3(0.11f, 0.13f, 0.24f), id, fur * 0.97f, 12, 8);                         // waist
            mk.Ellipsoid(new Vector3(0f, 0.67f, -0.38f), new Vector3(0.125f, 0.16f, 0.17f), id, fur * 0.95f, 12, 8);                        // haunches
            mk.Ellipsoid(new Vector3(0f, 0.78f, 0.42f), new Vector3(0.165f, 0.18f, 0.15f), Quaternion.Euler(-20f, 0f, 0f), fur * 1.06f, 12, 8); // the ruff
            mk.Ellipsoid(new Vector3(0f, 0.6f, 0.36f), new Vector3(0.1f, 0.12f, 0.12f), id, furLight, 10, 6);                               // pale throat
            mk.Limb(new Vector3(0f, 0.76f, 0.4f), new Vector3(0f, 0.86f, 0.56f), 0.11f, 0.085f, fur, 10);                                   // neck
            mk.Limb(new Vector3(0f, 0.72f, -0.5f), new Vector3(0f, 0.5f, -0.76f), 0.055f, 0.06f, fur * 0.92f, 8);                           // tail, low
            mk.Ellipsoid(new Vector3(0f, 0.44f, -0.83f), new Vector3(0.065f, 0.07f, 0.13f), Quaternion.Euler(40f, 0f, 0f), fur * 0.85f, 10, 6); // its bushy end
            MeshNode("Wolf Body", mk.Build("Wolf Body"), _vc, _wolf);

            _wolfHead = Node("Wolf Head", _wolf);
            _wolfHead.localPosition = new Vector3(0f, 0.86f, 0.58f);
            var hk = new MeshKit();
            hk.Ellipsoid(Vector3.zero, new Vector3(0.1f, 0.09f, 0.11f), id, fur, 12, 8);                                                  // skull
            hk.Ellipsoid(new Vector3(0f, -0.025f, -0.03f), new Vector3(0.125f, 0.09f, 0.08f), id, fur * 1.05f, 12, 7);                     // cheek fur
            hk.Limb(new Vector3(0f, -0.025f, 0.07f), new Vector3(0f, -0.045f, 0.245f), 0.056f, 0.028f, furLight * 0.9f, 10);               // long muzzle
            hk.Ellipsoid(new Vector3(0f, -0.075f, 0.14f), new Vector3(0.04f, 0.02f, 0.08f), id, furLight * 0.75f, 8, 5);                   // jaw
            hk.Ellipsoid(new Vector3(0f, -0.035f, 0.262f), new Vector3(0.022f, 0.018f, 0.016f), id, dark, 8, 5);                           // nose
            foreach (var k in new[] { -1f, 1f })
                hk.Limb(new Vector3(k * 0.055f, 0.06f, -0.01f), new Vector3(k * 0.075f, 0.17f, -0.03f), 0.035f, 0.006f, fur * 0.9f, 6, null, false); // pointed ears
            MeshNode("Wolf Head", hk.Build("Wolf Head"), _vc, _wolfHead);
            _wolfEyes = GlowMat(new Color(1f, 0.78f, 0.28f), 0.95f);
            GlowCard("Wolf Eye", new Vector3(-0.05f, 0.02f, 0.1f), 0.085f, _wolfEyes, _wolfHead);
            GlowCard("Wolf Eye", new Vector3(0.05f, 0.02f, 0.1f), 0.085f, _wolfEyes, _wolfHead);

            // four long legs, each pivoting at the top: straight forelegs, hind legs with a hock
            Vector3[] hips = { new Vector3(-0.09f, 0.58f, 0.3f), new Vector3(0.09f, 0.58f, 0.3f), new Vector3(-0.09f, 0.6f, -0.4f), new Vector3(0.09f, 0.6f, -0.4f) };
            for (var i = 0; i < 4; i++)
            {
                _legs[i] = Node("Wolf Leg", _wolf);
                _legs[i].localPosition = hips[i];
                var lk = new MeshKit();
                var c = fur * 0.9f; c.a = 1f;
                if (i < 2)
                {
                    lk.Limb(Vector3.zero, new Vector3(0f, -0.28f, 0.02f), 0.058f, 0.034f, c, 8);
                    lk.Limb(new Vector3(0f, -0.28f, 0.02f), new Vector3(0f, -0.55f, 0f), 0.032f, 0.026f, c, 8);
                }
                else
                {
                    lk.Limb(Vector3.zero, new Vector3(0f, -0.22f, -0.08f), 0.078f, 0.04f, c, 8);
                    lk.Limb(new Vector3(0f, -0.22f, -0.08f), new Vector3(0f, -0.38f, -0.03f), 0.036f, 0.028f, c, 8);
                    lk.Limb(new Vector3(0f, -0.38f, -0.03f), new Vector3(0f, -0.57f, 0f), 0.028f, 0.025f, c, 8);
                }
                lk.Ellipsoid(new Vector3(0f, i < 2 ? -0.565f : -0.585f, 0.03f), new Vector3(0.035f, 0.022f, 0.052f), id, fur * 0.7f, 8, 5); // paw
                MeshNode("Wolf Leg", lk.Build("Wolf Leg"), _vc, _legs[i]);
            }
            _growl = ProceduralClips.Clip("growl", ProceduralClips.GrowlSamples(51));
            _howl = ProceduralClips.Clip("howl", ProceduralClips.HowlSamples(52));
            _snap = ProceduralClips.Clip("snap", ProceduralClips.SnapSamples(53));
            _rustle = ProceduralClips.Clip("rustle", ProceduralClips.RustleSamples(54));
            _wolfVoice = Voice("Wolf", _wolf, 30f);
            _paceA = HutTrailLayout.WolfAt;
            var (ahead, dir) = HutTrailLayout.Along(0.58f);
            var side = new Vector2(dir.y, -dir.x);
            if (Vector2.Dot(side, _paceA - ahead) < 0f) side = -side;
            _paceB = ahead + side * 1.7f;
            _wolfHome = X0Z(_paceA);
            _wolf.localPosition = _wolfHome;
            _wolf.localRotation = Quaternion.LookRotation(X0Z(_paceB - _paceA).normalized);
            _wolfState = WolfState.Pacing;
        }

        // ---------- every frame ----------

        void Update()
        {
            var cam = Tick();
            if (cam == null) return;
            Owl(cam);
            Wolf(cam, Time.deltaTime);
        }

        void Owl(Transform cam)
        {
            if (_owlHead == null) return;
            // turn the head to follow you (owls turn a long way), blink now and then, hoot when you're near
            var to = cam.position - _owlHead.position;
            var want = Quaternion.LookRotation(new Vector3(to.x, to.y * 0.5f, to.z));
            _owlHead.rotation = Quaternion.Slerp(_owlHead.rotation, want, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            var blink = _t > _blinkAt && _t < _blinkAt + 0.14f;
            if (_t > _blinkAt + 0.14f) _blinkAt = _t + 2.5f + R() * 4f;
            _owlEyes.SetFloat(OpacityId, blink ? 0f : 0.9f);
            if (_t > _nextHoot)
            {
                _nextHoot = _t + 9f + R() * 9f;
                if (to.magnitude < 16f) { _owlVoice.pitch = 0.95f + R() * 0.1f; _owlVoice.PlayOneShot(_hoot, 0.5f); }
            }
        }

        void Wolf(Transform cam, float dt)
        {
            if (_wolf == null) return;
            var here = new Vector2(_wolf.localPosition.x, _wolf.localPosition.z);
            var dist = Vector2.Distance(Viewer, here);
            switch (_wolfState)
            {
                case WolfState.Pacing:
                {
                    var goal = _paceToB ? _paceB : _paceA;
                    var to = goal - here;
                    if (to.magnitude < 0.25f) _paceToB = !_paceToB;
                    else
                    {
                        var step = to.normalized * (0.7f * dt);
                        if (step.magnitude > to.magnitude) step = to;
                        here += step;
                        _wolf.localPosition = X0Z(here);
                        _wolf.localRotation = Quaternion.Slerp(_wolf.localRotation, Quaternion.LookRotation(X0Z(to).normalized), 1f - Mathf.Exp(-4f * dt));
                    }
                    for (var i = 0; i < 4; i++)
                        _legs[i].localRotation = Quaternion.Euler((i < 2 ? 1f : -1f) * Mathf.Sin(_t * 3.2f + (i % 2) * Mathf.PI) * 22f, 0f, 0f);
                    _wolfEyes.SetFloat(OpacityId, 0.95f);
                    if (!_heard && dist < 18f && HutTrailLayout.DistanceToPath(Viewer) < 4f)
                    {
                        _heard = true;
                        _wolfVoice.PlayOneShot(_growl, 0.55f);
                    }
                    if (dist < 9f && HutTrailLayout.DistanceToPath(Viewer) < 3.2f)
                    {
                        _wolfState = WolfState.Watching; _wolfT = 0f;
                        _wolfVoice.PlayOneShot(_snap, 0.85f);
                        _wolfVoice.PlayOneShot(_growl, 0.9f);
                    }
                    break;
                }
                case WolfState.Watching:
                {
                    _wolfT += dt;
                    var to = cam.position - _wolf.position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f)
                        _wolf.rotation = Quaternion.Slerp(_wolf.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-4f * dt));
                    _wolfHead.localRotation = Quaternion.Euler(-6f + Mathf.Sin(_wolfT * 1.7f) * 4f, 0f, Mathf.Sin(_wolfT * 0.9f) * 7f);
                    _wolfEyes.SetFloat(OpacityId, 1f);
                    for (var i = 0; i < 4; i++) _legs[i].localRotation = Quaternion.identity;
                    if (_wolfT > 2.6f || dist < 3.4f)
                    {
                        _wolfState = WolfState.Running; _wolfT = 0f;
                        var away = here - Viewer;
                        if (away.sqrMagnitude < 0.04f) away = _paceB - _paceA;
                        var outward = _paceA - HutTrailLayout.Along(0.48f).point;
                        _runDir = X0Z((away.normalized + outward.normalized * 1.4f).normalized);
                        _wolfVoice.PlayOneShot(_rustle, 0.9f);
                    }
                    break;
                }
                case WolfState.Running:
                {
                    _wolfT += dt;
                    var speed = Mathf.Lerp(1.6f, 5.5f, Mathf.Clamp01(_wolfT / 0.8f));
                    _wolf.localRotation = Quaternion.Slerp(_wolf.localRotation, Quaternion.LookRotation(_runDir), 1f - Mathf.Exp(-8f * dt));
                    _wolf.localPosition += _runDir * speed * dt;
                    for (var i = 0; i < 4; i++)
                        _legs[i].localRotation = Quaternion.Euler((i < 2 ? 1f : -1f) * Mathf.Sin(_wolfT * 14f + (i % 2) * Mathf.PI) * 36f, 0f, 0f);
                    _wolfEyes.SetFloat(OpacityId, Mathf.Clamp01(1.2f - _wolfT / 1.4f));
                    if (_wolfT > 3.2f)
                    {
                        _wolf.gameObject.SetActive(false);
                        _wolfState = WolfState.Gone; _wolfT = 0f;
                    }
                    break;
                }
                case WolfState.Gone:
                    _wolfT += dt;
                    if (_wolfT > 1.6f && _wolfT - dt <= 1.6f && _howl != null)
                    {
                        var s = Voice("Distant Howl", transform, 80f);
                        s.transform.position = _wolf.position + _runDir * 18f + Vector3.up * 1.5f;
                        s.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 2800f;
                        s.clip = _howl; s.volume = 0.7f; s.Play();
                        Destroy(s.gameObject, _howl.length + 0.2f);
                    }
                    break;
            }
        }
    }
}
