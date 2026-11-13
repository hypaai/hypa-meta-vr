using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Hypa Story (tickets 3A-3C), ported from the Three.js prototype (Ideation/). Three screens in one campfire clearing:
    ///  - mode select: you stand back from the fire; the fire is the portal (pinch it);
    ///  - story list: you sit by the fire; story cards to the right, the storyteller across the fire; pick a language;
    ///  - play: the storyteller tells the story (recorded narration or Hypa TTS line by line), with the script beside you, weather
    ///    timed to the words, and raise a hand / Ask to interrupt with a question (Hypa speech-to-text, a Q&amp;A stand-in, TTS answers).
    /// Transitions fade through a warm dark veil and move the world, never the camera. Uses HypaCore only; never references HypaLens.
    /// Editor: right mouse = look; Enter = choose; arrows = cards / lines; Space = pause; Q (hold) = ask; H = raise a hand;
    /// 1-3 = suggested question; Esc = back.
    /// </summary>
    public sealed class StoryApp : MonoBehaviour
    {
        [SerializeField] StoryAssets assets;
        [Tooltip("Q&A stand-in: stub, slow (~3 s to answer: exercises the second filler) or offline (every call fails).")]
        [SerializeField] string qaMode = "stub";
        [Tooltip("Eye height assumed for the scene's own camera when it has no rig (and for eye-level tracking origins).")]
        [SerializeField] float seatedEyeHeight = 1.2f;
        [Tooltip("Walking speed with the left thumbstick (or WASD in the editor), metres per second.")]
        [SerializeField] float moveSpeed = 1.4f;
        [Tooltip("Snap turn with the right thumbstick, degrees.")]
        [SerializeField] float snapTurn = 30f;
        [Tooltip("How far from the fire you can go, metres (walking, or moving your head in the simulator).")]
        [SerializeField] float walkRadius = 18f;
        [Tooltip("Highest your eyes can be above the ground, metres (no flying up out of the forest).")]
        [SerializeField] float maxEyeHeight = 2.2f;

        enum Screen { Splash, List, Play }

        Screen _screen = Screen.Splash;
        bool _busy, _portalHover, _holdToTalk, _endNoted;
        int _active;
        float _boost = 1f, _hover, _gather, _gatherTarget, _restoreReach, _restoreFar;
        Transform _cam, _world, _ui;
        KitGroup _prep;
        bool _turnArmed = true;
        Material _veil;
        Renderer _veilR;
        CampWorld _camp;
        StoryAudio _audio;
        StoryVoice _voice;
        MicListener _mic;
        SplashView _splash;
        StoryListView _list;
        PlayerView _player;
        StoryPlayer _story;
        AskFlow _ask;
        StoryLanguage _lang;
        readonly RaiseHandGesture _raise = new RaiseHandGesture();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();

        void Start()
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("Hypa Stories: no MainCamera."); enabled = false; return; }
            if (assets == null) { Debug.LogError("Hypa Stories: no StoryAssets. Run Hypa > Stories > Create Story Scene."); enabled = false; return; }
            _cam = cam.transform;
            _restoreFar = cam.farClipPlane;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 200f);
            PinchPointer.EnsureExists();
            _restoreReach = PinchPointer.Instance.MaxDistance;
            PinchPointer.Instance.MaxDistance = 12f; // the fire is the portal, a few metres away
            _lang = StoryLanguages.Current;

            _world = new GameObject("Hypa Stories World").transform;
            _camp = _world.gameObject.AddComponent<CampWorld>();
            _camp.Build(assets); // built at the origin, then moved under the viewer (prop sizing reads world bounds)
            _voice = gameObject.AddComponent<StoryVoice>();
            _voice.Init(_camp.Teller.Mouth);
            _audio = gameObject.AddComponent<StoryAudio>();
            _audio.Init(assets, _camp.Fire.Root, () => _voice.IsSpeaking);
            _mic = gameObject.AddComponent<MicListener>();
            _story = new StoryPlayer(_voice, () => _lang);
            _story.Thunder += _audio.Thunder;

            Recenter(CampWorld.SplashSeat);
            Kit.Init(assets);
            _ui = new GameObject("Hypa Stories UI").transform; // follows the viewer, not the world (you can walk about)
            _splash = new SplashView(_world, _ui, _camp.Fire.Root, assets.logo, () => Go(Screen.List), on => _portalHover = on);
            _list = new StoryListView(_ui, assets, Play, () => Go(Screen.Splash), ToggleSound, PickLanguage);
            _player = new PlayerView(_ui, () => _story.Jump(-1), () => _story.TogglePause(), () => _story.Jump(1),
                AskTap, AskHold, ExitPlay, ToggleSound, q => _ask.SubmitText(q));
            _ask = new AskFlow(_story, _voice, _mic, new StubStoryQa { Mode = qaMode }, _player, () => _lang, OnAsking);
            _ask.Logged += e => Debug.Log("Hypa Stories " + e);

            var prep = new GameObject("Preparing").transform;
            prep.SetParent(_ui, false);
            Kit.Text(prep, "Gathering around the fire…", 0.028f, Vector3.zero, new Vector2(0.8f, 0.05f), KitTheme.Dim, TMPro.TextAlignmentOptions.Center, KitFont.SerifItalic);
            var pf = prep.gameObject.AddComponent<FollowView>();
            pf.Distance = 1.1f; pf.Height = -0.05f;
            _prep = prep.gameObject.AddComponent<KitGroup>();
            _prep.Refresh();
            _prep.Set(0f);

            _list.Root.gameObject.SetActive(false);
            _player.Root.gameObject.SetActive(false);
            _camp.Teller.gameObject.SetActive(false);
            BuildVeil();
            StartCoroutine(Intro());
        }

        // ---------- placement ----------

        /// <summary>Moves the world so the viewer is at <paramref name="seat"/>, facing the fire. Returns the eye height above the floor.</summary>
        float Recenter(Vector3 seat)
        {
            var head = _cam.position;
            var fwd = _cam.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            var rot = Quaternion.LookRotation(fwd.normalized);
            // with a rig, the floor is the rig's origin (floor-level tracking); the scene's own camera has none
            var floor = _cam.parent != null ? _cam.root.position.y : head.y - seatedEyeHeight;
            var eye = head.y - floor;
            if (eye < 0.6f) { floor = head.y - seatedEyeHeight; eye = seatedEyeHeight; } // eye-level tracking origin
            _world.SetPositionAndRotation(new Vector3(head.x, floor, head.z) - rot * seat, rot);
            return eye;
        }

        void BuildVeil()
        {
            var go = new GameObject("Story Veil");
            go.transform.SetParent(_cam, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.15f);
            go.AddComponent<MeshFilter>().sharedMesh = StoryMeshes.Quad(1.5f, 1.5f);
            _veilR = go.AddComponent<MeshRenderer>();
            _veil = new Material(assets.veil) { name = "Veil" };
            _veilR.sharedMaterial = _veil;
            _veilR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetVeil(1f);
        }

        void SetVeil(float a)
        {
            _veil.color = new Color(0.024f, 0.016f, 0.012f, Mathf.Clamp01(a));
            _veilR.enabled = a > 0.001f;
        }

        // ---------- tweens (the prototype used GSAP timelines) ----------

        static float In(float u) => u * u * u;
        static float Out(float u) => 1f - (1f - u) * (1f - u) * (1f - u);
        static float InOut(float u) => u * u * (3f - 2f * u);

        void Run(float delay, float seconds, Action<float> f, Func<float, float> ease = null) => StartCoroutine(Tween(delay, seconds, f, ease ?? InOut));
        void At(float delay, Action a) => StartCoroutine(Tween(delay, 0f, _ => a(), null));

        static IEnumerator Tween(float delay, float seconds, Action<float> f, Func<float, float> ease)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                f(ease(t / seconds));
                yield return null;
            }
            f(1f);
        }

        IEnumerator Intro()
        {
            _busy = true;
            _splash.SetAlpha(0f);
            Run(0.2f, 2.2f, u => SetVeil(1f - u), Out);
            Run(1.4f, 1.4f, u => _splash.SetAlpha(u), Out);
            _audio.SetScene(StoryAudio.Splash, 2.5f);
            yield return new WaitForSeconds(1.6f);
            _busy = false;
        }

        // ---------- mode select ↔ story list ----------

        void Go(Screen target)
        {
            if (_busy || _screen == target || target == Screen.Play) return;
            _busy = true;
            var toList = target == Screen.List;
            if (toList) _audio.Cue(); else _audio.Tap();
            _audio.SetScene(toList ? StoryAudio.Story : StoryAudio.Splash, 3f);
            _portalHover = false;

            if (toList) Run(0f, 0.7f, u => _splash.SetAlpha(1f - u), In);
            else
            {
                Run(0f, 0.7f, u => _list.Chrome.Set(1f - u), In);
                for (var i = 0; i < _list.Count; i++) { var c = i; Run(0f, 0.7f, u => _list.SetReveal(c, 1f - u), In); }
            }
            if (toList) Run(0f, 1f, u => _camp.RingBurst = u, Out);
            var peak = toList ? 1.9f : 1.6f;
            Run(0f, 1.1f, u => _boost = Mathf.Lerp(1f, peak, u), In);
            Run(1.1f, 1.6f, u => _boost = Mathf.Lerp(peak, 1f, u), Out);
            var dim0 = _camp.Dim;
            Run(0.6f, 2f, u => _camp.Dim = Mathf.Lerp(dim0, toList ? 1f : 0f, u));
            Run(0.9f, 0.8f, u => SetVeil(u), In);
            At(1.7f, () =>
            {
                Recenter(toList ? CampWorld.StorySeat : CampWorld.SplashSeat);
                _splash.Brand.gameObject.SetActive(!toList);
                _splash.Prompt.gameObject.SetActive(!toList);
                _camp.PortalRing.gameObject.SetActive(!toList);
                _camp.RingBurst = 0f;
                _camp.Teller.gameObject.SetActive(toList);
                _list.Root.gameObject.SetActive(toList);
                if (toList)
                {
                    _list.Follow.Snap();
                    _list.ResetFocus(0);
                    for (var i = 0; i < _list.Count; i++) _list.SetReveal(i, 0f);
                    _list.Chrome.Set(0f);
                }
                else { _splash.SetAlpha(0f); _splash.Follow.Snap(); }
            });
            Run(1.7f, 1.3f, u => SetVeil(1f - u));
            if (toList)
            {
                for (var i = 0; i < _list.Count; i++) { var c = i; Run(2f + i * 0.35f, 2f, u => _list.SetReveal(c, u), Out); }
                Run(2.1f, 1f, u => _list.Chrome.Set(u), Out);
            }
            else Run(2.1f, 1.2f, u => _splash.SetAlpha(u), Out);
            At(3.4f, () => { _busy = false; _screen = target; });
        }

        // ---------- story list ↔ play ----------

        void Play(int i)
        {
            if (_busy || _screen != Screen.List) return;
            _busy = true; _active = i;
            var st = StoryLibrary.All[i];
            _audio.Tap();
            _story.Load(st);
            _player.Load(st, _lang);
            _endNoted = false;
            MicListener.RequestPermission();
            PrefetchLines();
            var ready = _story.PrepareAsync(_cts.Token);
            for (var c = 0; c < _list.Count; c++) { var k = c; Run(0f, 0.9f, u => _list.SetReveal(k, 1f - u), In); }
            Run(0f, 0.8f, u => _list.Chrome.Set(1f - u), In);
            StartCoroutine(PlaySequence(ready));
        }

        IEnumerator PlaySequence(Task ready)
        {
            yield return new WaitForSeconds(0.9f);
            var t0 = Time.time;
            while (!ready.IsCompleted && Time.time - t0 < 6f)
            {
                if (Time.time - t0 > 0.35f && _prep.Alpha < 1f) _prep.Set(1f); // only if it actually takes a moment
                yield return null;
            }
            _prep.Set(0f);
            _list.Root.gameObject.SetActive(false);
            _player.Root.gameObject.SetActive(true);
            _player.Follow.Snap();
            _player.Fade.Set(0f);
            _story.Live = true;
            Run(0.2f, 0.9f, u => _player.Fade.Set(u), Out);
            yield return new WaitForSeconds(0.8f);
            _story.Start(0f);
            _busy = false;
            _screen = Screen.Play;
        }

        void ExitPlay()
        {
            if (_busy || _screen != Screen.Play) return;
            _busy = true;
            _ask.Cancel();
            _holdToTalk = false;
            _story.Stop();
            _audio.SetStorm(0f);
            _audio.Tap();
            Run(0f, 0.6f, u => _player.Fade.Set(1f - u), In);
            At(0.7f, () =>
            {
                _camp.SetStory(null);
                _player.Root.gameObject.SetActive(false);
                _list.Root.gameObject.SetActive(true);
                _list.Follow.Snap();
                _list.ResetFocus(_active);
                for (var i = 0; i < _list.Count; i++) _list.SetReveal(i, 0f);
                _list.Chrome.Set(0f);
            });
            for (var i = 0; i < _list.Count; i++) { var c = i; Run(0.8f + i * 0.3f, 2f, u => _list.SetReveal(c, u), Out); }
            Run(0.9f, 1f, u => _list.Chrome.Set(u), Out);
            At(2.2f, () => { _busy = false; _screen = Screen.List; });
        }

        // ---------- language, sound ----------

        void PickLanguage(StoryLanguage lang)
        {
            _lang = lang;
            StoryLanguages.Current = lang;
            PrefetchLines();
        }

        /// <summary>The storyteller's stock lines in the current language, so a question's filler plays at once.</summary>
        void PrefetchLines()
        {
            var l = StoryLanguages.Lines(_lang.Code);
            foreach (var s in l.Filler) _voice.Prefetch(s, _lang);
            _voice.Prefetch(l.Resume, _lang);
            _voice.Prefetch(l.Again, _lang);
            _voice.Prefetch(l.Offline, _lang);
        }

        void ToggleSound()
        {
            var muted = AudioListener.volume > 0.5f;
            AudioListener.volume = muted ? 0f : 1f;
            _list.SetSound(muted);
            _player.SetSound(muted);
        }

        // ---------- asking ----------

        void OnAsking(bool on)
        {
            _audio.SetHold(on);
            _gatherTarget = on ? 1f : 0f; // fireflies draw in: the storyteller listens
        }

        /// <summary>Tap Ask: start a question, or end the recording, or skip the line being said.</summary>
        void AskTap()
        {
            if (_screen != Screen.Play) return;
            if (_ask.State == AskState.Idle) _ = _ask.BeginAsync();
            else if (_ask.State == AskState.Listening) _ask.StopListening();
            else _ask.Skip();
        }

        /// <summary>Hold Ask: push-to-talk. The question ends when the pinch is released.</summary>
        void AskHold()
        {
            if (_screen != Screen.Play || _ask.State != AskState.Idle) return;
            _holdToTalk = true;
            _ = _ask.BeginAsync();
        }

        // ---------- frame ----------

        static float Approach(float cur, float target, float rate, float dt) => cur + (target - cur) * (1f - Mathf.Exp(-rate * dt));

        void Update()
        {
            var dt = Time.deltaTime;
            _hover = Approach(_hover, _portalHover && _screen == Screen.Splash && !_busy ? 1f : 0f, 5f, dt);
            _camp.Boost = _boost * (1f + 0.3f * _hover);
            _camp.SetPortalLit(_hover);
            _gather = Approach(_gather, _gatherTarget, 2.5f, dt);
            _camp.Gather = _gather;

            _splash.Update(_cam);
            _list.Update(dt);
            Locomotion(dt);
            if (!_busy) KeepInBounds(); // every frame: head movement (room-scale, or the simulator) can't leave the clearing either

            if (_story.Live)
            {
                var s = _story.Update(dt);
                _camp.SetStory(s);
                _audio.SetStorm(s.Rain);
                _player.ShowLine(s.Index, s.T);
                _player.SetProgress(s.T, _story.Duration);
                _player.SetPauseLabel(_story.Ended ? "Replay" : _story.Paused ? "Resume" : "Pause");
                if (_story.Ended && !_endNoted) { _endNoted = true; _player.SetNote("The end of this story. Replay, or go back to the stories."); }
                if (!_story.Ended) _endNoted = false;
            }
            _camp.Teller.Level = _voice.Level;
            _camp.Teller.Attend = _ask.State != AskState.Idle ? 1f : 0f;

            var raised = RaiseHand.Probe != null && RaiseHand.Probe();
            if (_raise.Update(raised, dt) && _screen == Screen.Play && !_busy && _ask.State == AskState.Idle) _ = _ask.BeginAsync();
            if (_holdToTalk && !HypaInput.Current.IsPinching)
            {
                _holdToTalk = false;
                if (_ask.State == AskState.Listening) _ask.StopListening();
            }
            if (Application.isEditor) EditorKeys();
        }

        void EditorKeys()
        {
            switch (_screen)
            {
                case Screen.Splash:
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) Go(Screen.List);
                    break;
                case Screen.List:
                    if (_busy) break;
                    if (Input.GetKeyDown(KeyCode.RightArrow)) _list.Step(1);
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) _list.Step(-1);
                    if (Input.GetKeyDown(KeyCode.Return)) _list.BeginFocused();
                    if (Input.GetKeyDown(KeyCode.Escape)) Go(Screen.Splash);
                    break;
                case Screen.Play:
                    if (_busy) break;
                    if (Input.GetKeyDown(KeyCode.Space)) _story.TogglePause();
                    if (Input.GetKeyDown(KeyCode.RightArrow)) _story.Jump(1);
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) _story.Jump(-1);
                    if (Input.GetKeyDown(KeyCode.H) && _ask.State == AskState.Idle) _ = _ask.BeginAsync();
                    if (Input.GetKeyDown(KeyCode.Q)) { _qDown = Time.time; AskTap(); }
                    if (Input.GetKeyUp(KeyCode.Q) && Time.time - _qDown > 0.5f && _ask.State == AskState.Listening) _ask.StopListening(); // hold-to-talk
                    for (var k = 0; k < 3; k++)
                        if (Input.GetKeyDown(KeyCode.Alpha1 + k))
                        {
                            var qs = StubStoryQa.Suggestions(_story.StoryId, _lang.Code);
                            if (k < qs.Length) _ask.SubmitText(qs[k]);
                        }
                    if (Input.GetKeyDown(KeyCode.Escape))
                    {
                        if (_ask.State != AskState.Idle) { var line = _story.LineIndex; _ask.Cancel(); _story.Resume(line); }
                        else ExitPlay();
                    }
                    break;
            }
        }

        float _qDown;

        // Headset off, system menu or app switch: pause the story. It never resumes on its own.
        void OnApplicationPause(bool paused) { if (paused) _story?.Pause(); }
        void OnApplicationFocus(bool focus) { if (!focus && !Application.isEditor) _story?.Pause(); }

        void OnDestroy()
        {
            _cts.Cancel();
            _cts.Dispose();
            _ask?.Cancel();
            _story?.Stop();
            AudioListener.volume = 1f;
            if (PinchPointer.Instance != null && _restoreReach > 0f) PinchPointer.Instance.MaxDistance = _restoreReach;
            if (_cam != null)
            {
                var cam = _cam.GetComponent<Camera>();
                if (cam != null && _restoreFar > 0f) cam.farClipPlane = _restoreFar;
            }
            if (_veilR != null) Destroy(_veilR.gameObject); // hangs off the camera, which the shell keeps alive
            if (_world != null) Destroy(_world.gameObject);
            if (_ui != null) Destroy(_ui.gameObject);
        }

        // ---------- walking about ----------

        /// <summary>
        /// Left thumbstick walks (where you look), right thumbstick snap-turns; WASD in the editor. The world moves, not the rig, so
        /// it works under the shell too. You stay within <see cref="walkRadius"/> of the fire and can't walk into it.
        /// </summary>
        void Locomotion(float dt)
        {
            if (_busy) return;
            var move = StoryInput.Move?.Invoke() ?? Vector2.zero;
            if (Application.isEditor)
            {
                if (Input.GetKey(KeyCode.W)) move.y += 1f;
                if (Input.GetKey(KeyCode.S)) move.y -= 1f;
                if (Input.GetKey(KeyCode.D)) move.x += 1f;
                if (Input.GetKey(KeyCode.A)) move.x -= 1f;
            }
            if (move.sqrMagnitude > 0.02f)
            {
                move = Vector2.ClampMagnitude(move, 1f);
                var yaw = Quaternion.Euler(0f, _cam.eulerAngles.y, 0f);
                _world.position -= yaw * new Vector3(move.x, 0f, move.y) * moveSpeed * dt;
            }
            var turn = StoryInput.Turn?.Invoke() ?? 0f;
            if (_turnArmed && Mathf.Abs(turn) > 0.7f)
            {
                _turnArmed = false;
                _world.RotateAround(new Vector3(_cam.position.x, _world.position.y, _cam.position.z), Vector3.up, -Mathf.Sign(turn) * snapTurn);
            }
            else if (Mathf.Abs(turn) < 0.3f) _turnArmed = true;
        }

        /// <summary>
        /// Keeps the viewer inside the clearing: within <see cref="walkRadius"/> of the fire, out of the fire itself, and between
        /// just above the ground and <see cref="maxEyeHeight"/>. Moves the world, so it holds however the head got there.
        /// </summary>
        void KeepInBounds()
        {
            var local = _world.InverseTransformPoint(_cam.position);
            if (local.y > maxEyeHeight) _world.position += Vector3.up * (local.y - maxEyeHeight);
            else if (local.y < 0.3f) _world.position -= Vector3.up * (0.3f - local.y);
            local.y = 0f;
            var r = local.magnitude;
            if (r < 1e-3f) return;
            var limit = r > walkRadius ? walkRadius : r < 1.3f ? 1.3f : r;
            if (Mathf.Approximately(limit, r)) return;
            _world.position += _world.TransformVector(local / r * (r - limit));
        }
    }
}
