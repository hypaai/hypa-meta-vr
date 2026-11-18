using System;
using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The campfire soundscape, ported from the prototype's audio.js:
    ///  - recorded beds per scene ("splash" / "story"), crossfaded: night ambience (2D) and the fire as a spatial source at the fire;
    ///  - the ambience ducks to about a third while the storyteller speaks or a question is open;
    ///  - random wolves (exponential gaps, a random 6-15 s slice of the recording, random side, distance and pitch, never the same slice
    ///    twice running) and crickets;
    ///  - a storm layer (rain hiss + rumble) driven by the story, and thunder;
    ///  - the transition chime and UI tap.
    /// Missing recordings fall back to a synthesised wind and fire bed (see <see cref="ProceduralClips"/>).
    /// </summary>
    public sealed class StoryAudio : MonoBehaviour
    {
        public const string Splash = "splash", Story = "story";
        const float DuckedLevel = 0.32f;

        sealed class Bed
        {
            public AudioSource Src;
            public string Scene; // null = every scene
            public float Level, Weight;
        }

        sealed class Howl
        {
            public AudioSource Src;
            public float T, Len, Peak;
        }

        readonly List<Bed> _beds = new List<Bed>();
        readonly List<Howl> _howls = new List<Howl>();
        Bed _rain, _rumble;
        AudioClip _wolf, _chime, _tap, _portalOpen, _portalPass;
        AudioClip[] _thunder, _chirps;
        AudioSource _ui;
        Transform _fire;
        Func<bool> _voiceActive;
        string _scene = Splash;
        float _fadeSeconds = 2.5f, _bus = 1f, _master, _storm, _nextChirp, _nextHowl, _lastHowl = -99f;
        bool _hold;
        readonly System.Random _rng = new System.Random();

        float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        /// <param name="voiceActive">true while the storyteller is speaking (the ambience ducks under the voice).</param>
        public void Init(StoryAssets assets, Transform fire, Func<bool> voiceActive)
        {
            _fire = fire; _voiceActive = voiceActive;
            _ui = NewSource("UI", transform, 0f);

            var ambSplash = assets != null ? assets.ambienceSplash : null;
            var ambStory = assets != null ? assets.ambienceStory : null;
            var fireFar = assets != null ? assets.fireFar : null;
            var fireNear = assets != null ? assets.fireNear : null;
            if (ambSplash != null && ambStory != null && fireFar != null && fireNear != null)
            {
                AddBed(ambSplash, Splash, 0.55f, null);
                AddBed(fireFar, Splash, 0.8f, fire);
                AddBed(ambStory, Story, 0.55f, null);
                AddBed(fireNear, Story, 0.7f, fire);
            }
            else
            {
                Debug.Log("Hypa Stories: recorded beds not found (Assets/HypaStories/Local/Audio); using the synthesised wind and fire.");
                AddBed(ProceduralClips.Clip("wind", ProceduralClips.WindSamples()), null, 0.9f, null);
                AddBed(ProceduralClips.Clip("fire", ProceduralClips.FireSamples()), null, 0.9f, fire);
            }
            _rain = AddBed(ProceduralClips.Clip("rain", ProceduralClips.RainSamples()), "storm", 0.12f, null);
            _rumble = AddBed(ProceduralClips.Clip("rumble", ProceduralClips.RumbleSamples()), "storm", 0.26f, null);

            _wolf = assets != null ? assets.wolf : null;
            _thunder = new[] { ProceduralClips.Clip("thunder-a", ProceduralClips.ThunderSamples(21)), ProceduralClips.Clip("thunder-b", ProceduralClips.ThunderSamples(22)) };
            _chirps = new AudioClip[4];
            for (var i = 0; i < _chirps.Length; i++) _chirps[i] = ProceduralClips.Clip("chirp", ProceduralClips.ChirpSamples(30 + i));
            _chime = ProceduralClips.Clip("cue", ProceduralClips.ChimeSamples());
            _tap = ProceduralClips.Clip("tap", ProceduralClips.TapSamples(262f));
            _portalOpen = ProceduralClips.Clip("portal-open", ProceduralClips.PortalSamples(false));
            _portalPass = ProceduralClips.Clip("portal-pass", ProceduralClips.PortalSamples(true));
            _nextChirp = Time.time + Rand(2.5f, 7.5f);
            _nextHowl = Time.time + Rand(9f, 27f);
        }

        AudioSource NewSource(string name, Transform parent, float spatial)
        {
            var go = new GameObject("Audio " + name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = spatial;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1.5f; s.maxDistance = 40f;
            return s;
        }

        Bed AddBed(AudioClip clip, string scene, float level, Transform at)
        {
            var s = NewSource(clip.name, at != null ? at : transform, at != null ? 1f : 0f);
            s.clip = clip; s.loop = true; s.volume = 0f;
            s.timeSamples = _rng.Next(Mathf.Max(1, clip.samples - 1)); // beds never start in sync
            s.Play();
            var b = new Bed { Src = s, Scene = scene, Level = level };
            _beds.Add(b);
            return b;
        }

        /// <summary>Crossfade the beds to a scene over <paramref name="seconds"/>.</summary>
        public void SetScene(string scene, float seconds = 2.5f) { _scene = scene; _fadeSeconds = Mathf.Max(0.1f, seconds); }

        /// <summary>Storm layer (rain + rumble), 0..1.</summary>
        public void SetStorm(float v) => _storm = Mathf.Clamp01(v);

        /// <summary>Keep the ambience ducked regardless of the voice (while a question is open).</summary>
        public void SetHold(bool on) => _hold = on;

        public void Cue() => _ui.PlayOneShot(_chime, 1f);
        public void Tap() => _ui.PlayOneShot(_tap, 1f);
        /// <summary>The portal to the hut: opening, and stepping through (3L).</summary>
        public void PortalOpen() => _ui.PlayOneShot(_portalOpen, 0.9f);
        public void PortalPass() => _ui.PlayOneShot(_portalPass, 1f);

        public void Thunder()
        {
            var s = NewSource("Thunder", transform, 0f);
            s.clip = _thunder[_rng.Next(_thunder.Length)];
            s.pitch = Rand(0.85f, 1.1f);
            s.volume = _bus * _master;
            s.Play();
            Destroy(s.gameObject, s.clip.length / s.pitch + 0.2f);
        }

        // ---------- story sound cues ----------

        /// <summary>
        /// The sounds a story can cue by name ("sounds" in story.json), each made in code, with where it comes from (camp
        /// ground: the fire at the origin, the seats on -Z; null: all around you) and how loud. Unit tested against the stories.
        /// </summary>
        public static readonly Dictionary<string, (Func<float[]> make, Vector3? at, float level)> Effects = new Dictionary<string, (Func<float[]>, Vector3?, float)>
        {
            { "roar", (() => ProceduralClips.RoarSamples(201), new Vector3(-6f, 3f, 16f), 1f) },
            { "groan", (() => ProceduralClips.RoarSamples(202, true), new Vector3(-3f, 1.5f, 9f), 0.85f) },
            { "stomp", (() => ProceduralClips.StompSamples(203), new Vector3(4f, 0f, 11f), 1f) },
            { "fall", (() => ProceduralClips.StompSamples(204, true), new Vector3(2f, 0f, 9f), 1f) },
            { "door", (() => ProceduralClips.DoorSamples(205), new Vector3(-7f, 1f, -2f), 0.75f) },
            { "sizzle", (() => ProceduralClips.SizzleSamples(206), new Vector3(0f, 0.4f, 0f), 0.6f) },
            { "sparks", (() => ProceduralClips.SparksSamples(207), new Vector3(0f, 0.5f, 0f), 0.55f) },
            { "throw", (() => ProceduralClips.ThrowSamples(208), new Vector3(0f, 1.3f, 2.5f), 0.6f) },
            { "gulp", (() => ProceduralClips.GulpSamples(209), new Vector3(0f, 2f, 7f), 0.85f) },
            { "neigh", (() => ProceduralClips.NeighSamples(210), new Vector3(11f, 1.5f, 6f), 0.7f) },
            { "gallop", (() => ProceduralClips.GallopSamples(211), new Vector3(12f, 0.5f, 4f), 0.75f) },
            { "rooster", (() => ProceduralClips.RoosterSamples(212), new Vector3(14f, 3f, 5f), 0.6f) },
            { "heartbeat", (() => ProceduralClips.HeartbeatSamples(), null, 0.55f) },
            { "cheer", (() => ProceduralClips.CheerSamples(213), new Vector3(0f, 1.5f, 11f), 0.7f) },
            { "drums", (() => ProceduralClips.DrumLoopSamples(), new Vector3(3f, 1f, 12f), 0.4f) },
            { "siren", (() => ProceduralClips.SirenSamples(), new Vector3(-30f, 4f, -20f), 0.5f) },
            { "clink", (() => ProceduralClips.ClinkSamples(), null, 0.45f) },
            { "bricks", (() => ProceduralClips.BricksSamples(214), new Vector3(3f, 1f, 5f), 0.7f) },
            { "steps", (() => ProceduralClips.WetStepsSamples(215), null, 0.45f) },
            { "laugh", (() => ProceduralClips.LaughSamples(216), new Vector3(0.8f, 1.4f, 1.6f), 0.75f) },
            { "rattle", (() => ProceduralClips.RattleSamples(217), new Vector3(-1.5f, 1f, 3f), 0.55f) },
            { "birds", (() => ProceduralClips.BirdsSamples(218), new Vector3(-6f, 6f, 9f), 0.45f) },
            { "shout", (() => ProceduralClips.ShoutSamples(219), null, 0.6f) },
        };

        readonly Dictionary<string, AudioClip> _effects = new Dictionary<string, AudioClip>();

        /// <summary>Makes a story's sounds ahead of time (when it starts), so none is built mid-story.</summary>
        public void Prepare(IEnumerable<string> names)
        {
            foreach (var n in names) Clip(n);
        }

        AudioClip Clip(string name)
        {
            if (_effects.TryGetValue(name, out var c)) return c;
            if (!Effects.TryGetValue(name, out var e)) { Debug.LogWarning($"Hypa Stories: no story sound called '{name}'."); return _effects[name] = null; }
            return _effects[name] = ProceduralClips.Clip(name, e.make());
        }

        /// <summary>Plays a story sound by name, from its place round the fire. It stays audible under the storyteller's voice.</summary>
        public void Effect(string name)
        {
            var clip = Clip(name);
            if (clip == null) return;
            var e = Effects[name];
            var s = NewSource("Story " + name, transform, e.at.HasValue ? 1f : 0f);
            if (e.at.HasValue)
            {
                s.transform.position = _fire != null ? _fire.TransformPoint(e.at.Value) : e.at.Value;
                s.minDistance = 4f; s.maxDistance = 120f; s.rolloffMode = AudioRolloffMode.Logarithmic;
            }
            s.clip = clip;
            s.pitch = Rand(0.96f, 1.04f);
            s.volume = e.level * Mathf.Lerp(_bus, 1f, 0.6f) * _master;
            s.Play();
            Destroy(s.gameObject, clip.length / s.pitch + 0.2f);
        }

        /// <summary>
        /// A story sound from a spot in the world (Dodo's own mouth, its feet), full volume within <paramref name="near"/> metres
        /// and never ducked under the voice: Dodo's roar must be heard.
        /// </summary>
        public void EffectAt(string name, Vector3 world, float level = 1f, float pitch = 1f, float near = 10f)
        {
            var clip = Clip(name);
            if (clip == null) return;
            var s = NewSource("Story " + name, transform, 1f);
            s.transform.position = world;
            s.minDistance = near; s.maxDistance = 150f; s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.clip = clip;
            s.pitch = pitch * Rand(0.97f, 1.03f);
            s.volume = Mathf.Clamp01(level) * _master;
            s.Play();
            Destroy(s.gameObject, clip.length / s.pitch + 0.2f);
        }

        static float Approach(float cur, float target, float tau, float dt) => cur + (target - cur) * (1f - Mathf.Exp(-dt / Mathf.Max(tau, 1e-3f)));

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _master = Approach(_master, 1f, 0.5f, dt); // fades in on start, like the prototype (mute is AudioListener.volume)
            var duck = _hold || (_voiceActive?.Invoke() ?? false);
            _bus = Approach(_bus, duck ? DuckedLevel : 1f, duck ? 0.27f : 0.67f, dt);

            foreach (var b in _beds)
            {
                float target;
                if (b.Scene == "storm") target = _storm;
                else target = b.Scene == null || b.Scene == _scene ? 1f : 0f;
                b.Weight = Approach(b.Weight, target, b.Scene == "storm" ? 0.4f : _fadeSeconds / 3f, dt);
                b.Src.volume = b.Level * b.Weight * _bus * _master;
            }

            Wildlife();
            for (var i = _howls.Count - 1; i >= 0; i--)
            {
                var h = _howls[i];
                h.T += dt;
                var env = h.T < 1.6f ? h.T / 1.6f : h.T > h.Len - 2.2f ? Mathf.Clamp01((h.Len - h.T) / 2.2f) : 1f;
                h.Src.volume = env * h.Peak * _bus * _master;
                if (h.T < h.Len) continue;
                Destroy(h.Src.gameObject);
                _howls.RemoveAt(i);
            }
        }

        void Wildlife()
        {
            var listener = Camera.main != null ? Camera.main.transform : null;
            if (listener == null) return;
            if (Time.time >= _nextChirp)
            {
                _nextChirp = Time.time + Rand(2.5f, 7.5f);
                var a = Rand(0f, Mathf.PI * 2f);
                var s = NewSource("Cricket", transform, 1f);
                s.transform.position = listener.position + new Vector3(Mathf.Cos(a), -1.2f, Mathf.Sin(a)) * Rand(3f, 8f);
                s.clip = _chirps[_rng.Next(_chirps.Length)];
                s.volume = 0.035f * _bus * _master;
                s.Play();
                Destroy(s.gameObject, s.clip.length + 0.1f);
            }
            if (_wolf != null && Time.time >= _nextHowl)
            {
                // exponential gaps (~22 s plus a random tail; the first after 9-27 s), so howls never fall into a rhythm
                _nextHowl = Time.time + 22f + -Mathf.Log(1f - (float)_rng.NextDouble()) * 32f;
                PlayHowl(listener);
            }
        }

        void PlayHowl(Transform listener)
        {
            var len = Mathf.Min(_wolf.length - 1f, Rand(6f, 15f));
            if (len < 3f) return;
            var off = 0f;
            for (var i = 0; i < 6; i++) { off = Rand(0f, _wolf.length - len - 0.5f); if (Mathf.Abs(off - _lastHowl) > 8f) break; }
            _lastHowl = off;
            var side = (_rng.NextDouble() < 0.5 ? -1f : 1f) * Rand(0.35f, 0.9f);   // like the prototype's pan, as a direction
            var dir = Quaternion.Euler(0f, listener.eulerAngles.y + side * 90f, 0f) * Vector3.forward;
            var s = NewSource("Wolf", transform, 0.85f);
            s.maxDistance = 400f;
            s.transform.position = listener.position + dir * Rand(25f, 45f);
            var lp = s.gameObject.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = Rand(2200f, 5700f);   // distance
            s.clip = _wolf; s.pitch = Rand(0.95f, 1.05f); s.volume = 0f;
            s.time = off;
            s.Play();
            _howls.Add(new Howl { Src = s, Len = len / s.pitch, Peak = Rand(0.22f, 0.58f) });
        }
    }
}
