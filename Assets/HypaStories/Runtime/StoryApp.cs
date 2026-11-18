using System;
using System.Collections;
using System.Linq;
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
    ///    timed to the words, and raise a hand / Ask to interrupt with a question (Hypa speech-to-text, the story's chat model, TTS answers).
    /// Transitions fade through a warm dark veil and move the world, never the camera. Uses HypaCore only; never references HypaLens.
    /// When the story ends, Try Hypa Translate appears in the panel. It opens there: words from the story, your voice,
    /// and the translation spoken back. Translation and speech go through Hypa's auto router.
    /// Editor: right mouse = look; Enter = choose; arrows = cards / lines; Space = pause; Q = ask; H = raise a hand; T = translate;
    /// 1-3 = suggested question, or a suggested word while translating; V = hide / show the panel; Esc = back (closes Try Hypa first).
    /// </summary>
    public sealed class StoryApp : MonoBehaviour
    {
        [SerializeField] StoryAssets assets;
        [Tooltip("Answers: chat (the story's model through Hypa), or the stand-in: stub, slow (~3 s to answer: exercises the second filler) or offline (every call fails).")]
        [SerializeField] string qaMode = "chat";
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
        bool _busy, _portalHover, _endNoted, _tryOpen;
        bool _elderSession;
        int _active;
        float _boost = 1f, _hover, _gather, _gatherTarget, _restoreReach, _restoreFar;
        Transform _cam, _world, _ui;
        KitGroup _prep;
        TMPro.TextMeshPro _prepText;
        /// <summary>A place row out in the camp's world: at the hut's door (3G), at the village gate (3M).</summary>
        sealed class DoorRow { public Transform Row; public KitGroup Group; public float Shown; public System.Func<Vector2, bool> At; public bool ElderRow; }
        readonly System.Collections.Generic.List<DoorRow> _doorRows = new System.Collections.Generic.List<DoorRow>();
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
        TryHypaView _try;
        TryHypaFlow _tryFlow;
        StoryPlayer _story;
        AskFlow _ask;
        StoryLanguage _lang;     // selection for the next story
        /// <summary>Beside the player, with DEBUG=true only.</summary>
        DebugView _debug;
        readonly RaiseHandGesture _raise = new RaiseHandGesture();
        // Places (3F): away = at another place; the camp stays loaded, hidden and quiet, and picks up when you come back.
        bool _away, _wasPlaying;
        // In my room (3K): the camp in your real room, on the floor you found, with the story going on as before.
        bool _room, _roomBusy;
        // Portal (3L): a doorway on your real door (else a clear wall) that shows where it leads (hut, village or camp); a step in takes you there, a step back brings you home.
        sealed class DoorPortal { public StoryPortal Portal; public PortalPlan Plan; public float Prev = float.NaN; public Transform Chips; public KitButton[] Buttons; }
        readonly System.Collections.Generic.List<DoorPortal> _doors = new System.Collections.Generic.List<DoorPortal>();
        DoorPortal _through;      // the door you stepped through
        RoomScan _roomScan;
        bool _returnToRoom;
        PortalDestination _dest = PortalDestination.Hut;
        float _prevDepth = float.NaN, _portalQuietUntil;
        RoomSpotPlan _roomPlan;
        float _roomFloorY;
        Vector3 _outdoorPosition;
        Quaternion _outdoorRotation;
        KitGroup _roomNote;
        TMPro.TextMeshPro _roomNoteText;
        int _roomNoteId;
        readonly System.Collections.Generic.List<AudioSource> _resumeLoops = new System.Collections.Generic.List<AudioSource>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        DodoGame _game;          // the playable scene after Seven Hot Stones
        KitButton _faceDodo;
        const string DodoStory = "seven-hot-stones";
        HatGame _hatGame;        // the playable scene after Eshu at the Crossroads
        KitButton _turnHat;
        const string EshuStory = "eshu-crossroads";
        DrumGame _drumGame;      // the playable scene after The King's Magic Drum
        KitButton _playDrum;
        const string DrumStory = "kings-drum";
        HutVisit _hut;           // the hut's household things, their cards and the old woman who tells you about them
        bool Playing => (_game != null && _game.Active) || (_hatGame != null && _hatGame.Active) || (_drumGame != null && _drumGame.Active);
        void EndGames() { if (_game != null) _game.End(); if (_hatGame != null) _hatGame.End(); if (_drumGame != null) _drumGame.End(); }

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
            _audio.Init(assets, _camp.Fire.Root, () => _voice.IsSpeaking || (_hut != null && _hut.DuckAmbience));
            _mic = gameObject.AddComponent<MicListener>();
            _story = new StoryPlayer(_voice);
            _story.Thunder += _audio.Thunder;
            _story.Sound += name => { _audio.Effect(name); _camp.Effect(name); };

            Recenter(CampWorld.SplashSeat);
            Kit.Init(assets);
            _ui = new GameObject("Hypa Stories UI").transform; // follows the viewer, not the world (you can walk about)
            // Back (to the Hypa home menu: only under the shell), Hut, Gather round, Village: one row in front of you at the start.
            _splash = new SplashView(_world, _ui, _camp.Fire.Root, assets.logo, () => OpenStories(false), on => _portalHover = on,
                HypaShell.Instance != null ? (System.Action)(() => HypaShell.Instance.ExitToHome()) : null,
                () => Places.GoTo(PlaceIds.Hut), () => Places.GoTo(PlaceIds.Gathering));
            _list = new StoryListView(_ui, assets, Play, () => Go(Screen.Splash), ToggleSound, PickLanguage);
            _player = new PlayerView(_ui, () => _story.Jump(-1), () => _story.TogglePause(), () => _story.Jump(1),
                OpenTry, CornerTap, ExitPlay, ToggleSound, q => _ask.SubmitText(q), ToggleRoom);
            // DEBUG=true in .env only: otherwise the panel is never made and no call is recorded
            if (HypaConfig.Current.DebugMode)
            {
                _debug = new DebugView(_ui, _player, _story, _voice, () => _lang);
            }
            _tryFlow = new TryHypaFlow(new HypaTryApi(), _mic, HypaSettings.TargetLanguage);
            // the same recording as Ask (stops when you stop talking), which is the one proven on the headset; the live
            // stream-until-Done mode gave only silence there
            _tryFlow.LiveText = false;
            _try = new TryHypaView(_player.Body, _tryFlow, CloseTry);
            _ask = new AskFlow(_story, _voice, _mic, qaMode == "chat" ? new ChatStoryQa() : (IStoryQa)new StubStoryQa { Mode = qaMode }, _player, () => _story.Language, OnAsking);
            _ask.Logged += e => Debug.Log("Hypa Stories " + e);

            var prep = new GameObject("Preparing").transform;
            prep.SetParent(_ui, false);
            _prepText = Kit.Text(prep, "Gathering around the fire…", 0.028f, Vector3.zero, new Vector2(0.8f, 0.05f), KitTheme.Dim, TMPro.TextAlignmentOptions.Center, KitFont.SerifItalic);
            var pf = prep.gameObject.AddComponent<FollowView>();
            pf.Distance = 1.1f; pf.Height = -0.05f;
            _prep = prep.gameObject.AddComponent<KitGroup>();
            _prep.Refresh();
            _prep.Set(0f);

            var note = new GameObject("Room Note").transform;
            note.SetParent(_ui, false);
            _roomNoteText = Kit.Text(note, "", 0.02f, Vector3.zero, new Vector2(0.7f, 0.08f), KitTheme.Paper, TMPro.TextAlignmentOptions.Center, KitFont.SerifItalic);
            var nf = note.gameObject.AddComponent<FollowView>();
            nf.Distance = 1.0f; nf.Height = -0.12f;
            _roomNote = note.gameObject.AddComponent<KitGroup>();
            _roomNote.Refresh();
            _roomNote.Set(0f);

            _list.Root.gameObject.SetActive(false);
            _player.Root.gameObject.SetActive(false);
            _try.Root.gameObject.SetActive(false);
            _camp.Teller.gameObject.SetActive(false);
            BuildVeil();
            BuildDoorRows();
            BuildDodoGame();
            BuildHatGame();
            BuildDrumGame();
            BuildHutVisit();
            Places.EnsureRunner();
            Places.Leaving += OnLeavingPlace;
            Places.Arrived += OnArrivedPlace;
            Places.NumberKeysTaken = NumberKeysTaken; // 1-3 pick a suggested question while a story plays
            Places.InCamp = HoldsPlace;
            Places.TravelInCamp = TravelInCamp;
            // Opened from a stand-in (its Hut or Village button): the camp just loaded so you can stand at that place.
            // The places veil is still up, so skip the campfire's own intro.
            if (ApplyCampSpot()) { SetVeil(0f); _splash.SetAlpha(0f); }
            else StartCoroutine(Intro());
        }

        // ---------- places (3F) ----------

        bool NumberKeysTaken() => !_away && _screen == Screen.Play;

        /// <summary>Leaving the camp: pause the story (it never resumes on its own), stop any question, hide and quiet the camp.</summary>
        void OnLeavingPlace(string id)
        {
            if (id != PlaceIds.Camp || _away) return;
            if (_room) ExitRoomNow(); // the hut and the village are out in the forest: back to it, unseen, before the camp is put away
            else if (_returnToRoom) DestroyDoors();
            _wasPlaying = _story.Live && _story.Running && !_story.Paused && !_story.Ended;
            if (_hut != null && _hut.enabled) _hut.Close();
            _ask.Cancel();
            if (_story.Live) _story.Pause();
            _resumeLoops.Clear();
            foreach (var src in _world.GetComponentsInChildren<AudioSource>(true)) if (src.isPlaying && src.loop) _resumeLoops.Add(src);
            foreach (var src in GetComponentsInChildren<AudioSource>(true)) if (src.isPlaying && src.loop) _resumeLoops.Add(src);
            foreach (var src in _resumeLoops) src.Pause();
            _audio.enabled = false; // no wolves or crickets from the camp while you're in the hut
            _away = true;
            if (Playing) { EndGames(); _player.Root.gameObject.SetActive(true); }
            _world.gameObject.SetActive(false);
            _ui.gameObject.SetActive(false);
            foreach (var d in _doorRows) { d.Row.gameObject.SetActive(false); d.Shown = 0f; }
        }

        /// <summary>Back at the camp: show it where you stand, panels in front of you, sound back, story picking up where it was.</summary>
        void OnArrivedPlace(string id)
        {
            if (id != PlaceIds.Camp || !_away) return;
            _away = false;
            _world.gameObject.SetActive(true);
            _ui.gameObject.SetActive(true);
            Recenter(_screen == Screen.Splash ? CampWorld.SplashSeat : CampWorld.StorySeat);
            ApplyCampSpot(); // came back for the hut or the village (a button in another place): straight there
            SnapPanels();
            _audio.enabled = true;
            foreach (var src in _resumeLoops)
            {
                if (src == null || !src.gameObject.activeInHierarchy) continue;
                src.UnPause();
                if (!src.isPlaying) src.Play(); // hiding the camp stopped it outright: start the loop again
            }
            _resumeLoops.Clear();
            if (_wasPlaying && _story.Paused) _story.TogglePause();
            _wasPlaying = false;
            Places.NumberKeysTaken = NumberKeysTaken;
        }

        // ---------- placement ----------

        /// <summary>Moves the world so the viewer is at <paramref name="seat"/>, facing the fire. Returns the eye height above the floor.</summary>
        float Recenter(Vector3 seat)
        {
            if (_room) { PlaceInRoom(); return _cam.position.y - _roomFloorY; } // in your room the fire stays where it is on the real floor
            if (_returnToRoom) return _cam.position.y - _world.position.y; // the destination stays lined up with the real door
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

        Vector2 ViewerOnGround
        {
            get { var p = _world.InverseTransformPoint(_cam.position); return new Vector2(p.x, p.z); }
        }

        void OpenStories(bool elder)
        {
            if (_busy || _screen != Screen.Splash) return;
            if (elder && (_room || !VillageLayout.AtLocal(ViewerOnGround))) return;
            _elderSession = elder;
            UpdateNarrationAudio();
            _list.SetStories(elder ? StoryLibrary.Elder : StoryLibrary.Campfire);
            _prepText.text = elder ? "Preparing the story..." : "Gathering around the fire…";
            Go(Screen.List);
        }

        void Go(Screen target)
        {
            if (_busy || _screen == target || target == Screen.Play) return;
            if (target == Screen.Splash && _room && _elderSession) ExitRoomNow();
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
            if (!_elderSession) Run(0.9f, 0.8f, u => SetVeil(u), In);
            At(1.7f, () =>
            {
                if (!_elderSession) Recenter(toList ? CampWorld.StorySeat : CampWorld.SplashSeat);
                _splash.Brand.gameObject.SetActive(!toList);
                _splash.Prompt.gameObject.SetActive(!toList && (_room || ViewerOnGround.magnitude <= 9f));
                _camp.PortalRing.gameObject.SetActive(!toList);
                _camp.RingBurst = 0f;
                _camp.Teller.gameObject.SetActive(toList && !_elderSession);
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
            if (!_elderSession) Run(1.7f, 1.3f, u => SetVeil(1f - u));
            if (toList)
            {
                for (var i = 0; i < _list.Count; i++) { var c = i; Run(2f + i * 0.35f, 2f, u => _list.SetReveal(c, u), Out); }
                Run(2.1f, 1f, u => _list.Chrome.Set(u), Out);
            }
            else Run(2.1f, 1.2f, u => _splash.SetAlpha(u), Out);
            At(3.4f, () => { _busy = false; _screen = target; });
        }

        // ---------- the Dodo game (after Seven Hot Stones) ----------

        void BuildDodoGame()
        {
            _game = gameObject.AddComponent<DodoGame>();
            _game.Init(_camp, assets, _audio, StoryMeshes.Glow(), transform, DodoLine, Say, () =>
            {
                // the story once more, from the start
                _player.Root.gameObject.SetActive(true);
                _player.Follow.Snap();
                _story.Live = true;
                _endNoted = false;
                _story.Start(0f);
            }, () =>
            {
                _player.Root.gameObject.SetActive(true);
                ExitPlay();
            }, () =>
            {
                _voice.StopAll();
                _player.Root.gameObject.SetActive(true);
                _player.Follow.Snap();
            }, ToggleRoom);
            // above the player panel when Seven Hot Stones ends: step in as Auta
            _faceDodo = Kit.Button(_player.Body, new Vector3(0f, 0.255f, -0.01f), new Vector2(0.22f, 0.064f), "Face Dodo", "flame", KitStyle.Primary, FaceDodo, null, 0.022f);
            _faceDodo.gameObject.SetActive(false);
        }

        void FaceDodo()
        {
            if (_busy || Playing || _screen != Screen.Play) return;
            _ask.Cancel();
            _audio.Tap();
            _player.Root.gameObject.SetActive(false);
            _game.Fit(CurrentFit());
            _game.Begin();
        }

        // ---------- the hut's household things ----------

        /// <summary>
        /// Inside the hut: things to pick up, a story card over each and the old woman who tells you about them, through the same
        /// card, voice, panel and questions as the campfire's stories (its own instances: the story by the fire only pauses).
        /// </summary>
        void BuildHutVisit()
        {
            var go = new GameObject("Hut Visit");
            go.transform.SetParent(transform, false);
            _hut = go.AddComponent<HutVisit>();
            try
            {
                _hut.Init(_camp, assets, _mic, qaMode == "chat" ? new ChatStoryQa() : (IStoryQa)new StubStoryQa { Mode = qaMode }, () => _lang, PickLanguage,
                    () => { if (_story.Live) _story.Pause(); }, ToggleSound);
            }
            catch (Exception e)
            {
                Debug.LogError("Hypa Stories: the hut's things couldn't be built: " + e);
                _hut.enabled = false;
            }
        }

        // ---------- the hat game (after Eshu at the Crossroads) ----------

        void BuildHatGame()
        {
            _hatGame = gameObject.AddComponent<HatGame>();
            _hatGame.Init(_camp, assets, _audio, StoryMeshes.Glow(), transform, () =>
            {
                _player.Root.gameObject.SetActive(true);
                _player.Follow.Snap();
                _story.Live = true;
                _endNoted = false;
                _story.Start(0f);
            }, () =>
            {
                _player.Root.gameObject.SetActive(true);
                ExitPlay();
            }, () =>
            {
                _voice.StopAll();
                _player.Root.gameObject.SetActive(true);
                _player.Follow.Snap();
            }, ToggleRoom);
            _turnHat = Kit.Button(_player.Body, new Vector3(0f, 0.255f, -0.01f), new Vector2(0.22f, 0.064f), "Turn the hat", "rotate-ccw", KitStyle.Primary, TurnHat, null, 0.022f);
            _turnHat.gameObject.SetActive(false);
        }

        void TurnHat()
        {
            if (_busy || Playing || _screen != Screen.Play) return;
            _ask.Cancel();
            _audio.Tap();
            _player.Root.gameObject.SetActive(false);
            _hatGame.Fit(CurrentFit());
            _hatGame.Begin();
        }

        // ---------- the drum game (after The King's Magic Drum) ----------

        void BuildDrumGame()
        {
            _drumGame = gameObject.AddComponent<DrumGame>();
            _drumGame.Init(_camp, assets, _audio, StoryMeshes.Glow(), transform, () =>
            {
                _player.Root.gameObject.SetActive(true);
                _player.Follow.Snap();
                _story.Live = true;
                _endNoted = false;
                _story.Start(0f);
            }, () =>
            {
                _player.Root.gameObject.SetActive(true);
                ExitPlay();
            }, () =>
            {
                _voice.StopAll();
                _player.Root.gameObject.SetActive(true);
                _player.Follow.Snap();
            }, ToggleRoom);
            _playDrum = Kit.Button(_player.Body, new Vector3(0f, 0.255f, -0.01f), new Vector2(0.22f, 0.064f), "Play the drum", "hand", KitStyle.Primary, PlayDrum, null, 0.022f);
            _playDrum.gameObject.SetActive(false);
        }

        void PlayDrum()
        {
            if (_busy || Playing || _screen != Screen.Play) return;
            _ask.Cancel();
            _audio.Tap();
            _player.Root.gameObject.SetActive(false);
            _drumGame.Fit(CurrentFit());
            _drumGame.Begin();
        }

        /// <summary>A line of Seven Hot Stones in the listener's language (Dodo's challenge, Auta's "I am."), by its id.</summary>
        string DodoLine(string id)
        {
            var c = System.Linq.Enumerable.FirstOrDefault(StoryLibrary.Find(DodoStory)?.Timeline.Captions ?? new Caption[0], x => x.Id == id);
            return c?.TextFor((_story.Language ?? _lang).Code);
        }

        /// <summary>The storyteller says a line (in the game: Dodo's challenge). Never throws.</summary>
        int _sayGen;

        async void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var gen = ++_sayGen;
            _voice.StopAll(); // a new line replaces the one still being said
            try
            {
                var line = await _voice.PrepareAsync(text, _story.Language ?? _lang, _cts.Token);
                if (gen != _sayGen) return;
                await _voice.PlayAsync(line, null, _cts.Token);
            }
            catch (System.OperationCanceledException) { }
            catch (System.Exception e) { Debug.LogWarning("Hypa Stories: couldn't speak a game line: " + e.Message); }
        }

        // ---------- story list ↔ play ----------

        void Play(int i)
        {
            if (_busy || _screen != Screen.List) return;
            _busy = true; _active = i;
            var st = _list.StoryAt(i);
            _audio.Tap();
            _story.Load(st, _lang);
            _audio.Prepare(System.Linq.Enumerable.Select(st.Timeline.Sounds, c => c.Name)); // the story's sounds, made now, not mid-story
            _player.Load(st, _story.Language);
            _endNoted = false;
            _player.SetTryOffer(false);
            MicListener.RequestPermission();
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
            if (Playing) { EndGames(); _voice.StopAll(); _player.Root.gameObject.SetActive(true); }
            _busy = true;
            if (_tryOpen) HideTryNow();
            _ask.Cancel();
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
            _list.SetLanguage(lang);
            _hut?.SetLanguage(lang);
        }

        void ToggleSound()
        {
            var muted = AudioListener.volume > 0.5f;
            AudioListener.volume = muted ? 0f : 1f;
            _list.SetSound(muted);
            _player.SetSound(muted);
            if (_hut != null) _hut.SetSound(muted);
        }

        // ---------- asking ----------

        void OnAsking(bool on)
        {
            _audio.SetHold(on);
            _gatherTarget = on ? 1f : 0f; // fireflies draw in: the storyteller listens
        }

        // ---------- Try Hypa ----------

        /// <summary>The corner button: speak while translating, otherwise Ask.</summary>
        void CornerTap()
        {
            if (_tryOpen) { _ = _tryFlow.DictateAsync(); return; }
            AskTap();
        }

        /// <summary>Opens Hypa Translate in the story panel, after the story. Words come from the lines just told.</summary>
        void OpenTry()
        {
            if (_screen != Screen.Play || _busy || _tryOpen || Playing || !_story.Ended || _ask.State != AskState.Idle) return;
            _tryOpen = true;
            _audio.Tap();
            _tryFlow.Clear();
            var lang = _story.Language;
            _tryFlow.BindStory(lang.HypaLabel, StoryLanguages.All.Select(l => l.HypaLabel).ToArray());
            // the story's chosen words in the language it was told in; a story without them offers its first long words
            var words = _story.Story.WordsFor(lang.Code);
            _tryFlow.SetSuggestions(words.Length > 0 ? words.Take(3).ToArray()
                : TryHypaText.Suggest(_story.Story.Timeline.Captions.Select(c => c.TextFor(lang.Code)), 3));
            _try.Root.gameObject.SetActive(true);
            _try.Show(_story.Story.Accent);
            _player.SetTranslateOpen(true);
            _ = _tryFlow.LoadLanguagesAsync();
        }

        void CloseTry()
        {
            if (!_tryOpen) return;
            _tryOpen = false;
            _tryFlow.Cancel();
            _audio.Tap();
            _try.Root.gameObject.SetActive(false);
            _player.SetTranslateOpen(false);
        }

        /// <summary>Closes Try Hypa at once (leaving the story, or the story starting again).</summary>
        void HideTryNow()
        {
            _tryOpen = false;
            _tryFlow.Cancel();
            if (_try != null) _try.Root.gameObject.SetActive(false);
            _player?.SetTranslateOpen(false);
        }

        /// <summary>Tap Ask (Q in the editor): start a question, or end the recording, or skip the line being said.</summary>
        void AskTap()
        {
            if (_screen != Screen.Play || _tryOpen) return;
            if (_ask.State == AskState.Idle) _ = _ask.BeginAsync();
            else if (_ask.State == AskState.Listening) _ask.StopListening();
            else _ask.Skip();
        }

        // ---------- frame ----------

        static float Approach(float cur, float target, float rate, float dt) => cur + (target - cur) * (1f - Mathf.Exp(-rate * dt));

        public static bool NeedsVillageQuiet(bool elder, bool live, bool ended, bool speaking, bool asking, bool translating)
            => elder && ((live && !ended) || speaking || asking || translating);

        /// <summary>Hut and village rows stay while you stand there. Only the elder's row hides once his list or story is open.</summary>
        public static bool PlaceRowShown(bool inRoom, bool atSpot, bool elderRow, bool elderUiOpen)
            => !inRoom && atSpot && !(elderRow && elderUiOpen);

        /// <summary>Walking off outdoors, or stepping back out through the village door, stops a story the elder is still telling. In your room he is already with you.</summary>
        public static bool ElderTellingShouldStop(bool elderSession, bool telling, bool left, bool inRoom = false)
            => elderSession && telling && left && !inRoom;

        void UpdateNarrationAudio()
        {
            _voice.SetOrigin(_elderSession ? _camp.Village.ElderMouth : _camp.Teller.Mouth);
            if (_room && _elderSession) _camp.Village.UpdateRoomElder(_cam, Time.deltaTime);
            _camp.Village.SetNarrationFocus(NeedsVillageQuiet(_elderSession, _story.Live, _story.Ended,
                _voice.IsSpeaking, _ask.State != AskState.Idle,
                _tryOpen && (_tryFlow.Status != TryStatus.Idle || _tryFlow.ActiveAudio != null)));
        }

        void Update()
        {
            if (_away) return; // at another place: the camp waits, hidden
            UpdateNarrationAudio();
            var dt = Time.deltaTime;
            _hover = Approach(_hover, _portalHover && _screen == Screen.Splash && !_busy ? 1f : 0f, 5f, dt);
            _camp.Boost = _boost * (1f + 0.3f * _hover);
            _camp.SetPortalLit(_hover);
            _gather = Approach(_gather, _gatherTarget, 2.5f, dt);
            _camp.Gather = _gather;

            _splash.Update(_cam);
            _list.Update(dt);
            if (!_busy && !_room && !_returnToRoom)
            {
                Locomotion(dt);
                KeepInBounds();
            }
            TrailPanels(dt);
            // Let the room transition finish before ending an outdoor visit based on distance.
            if (!_busy && !_roomBusy && ElderTellingShouldStop(_elderSession, _screen == Screen.Play && _story.Live && !_story.Ended,
                VillageLayout.LeftElder(ViewerOnGround), _room))
                EndElderVisit();

            if (_hatGame != null) _turnHat.gameObject.SetActive(!Playing && _story.Live && _story.Ended && _story.StoryId == EshuStory && !_busy);
            if (_game != null) _faceDodo.gameObject.SetActive(!Playing && _story.Live && _story.Ended && _story.StoryId == DodoStory && !_busy);
            if (_drumGame != null) _playDrum.gameObject.SetActive(!Playing && _story.Live && _story.Ended && _story.StoryId == DrumStory && !_busy);
            if (_story.Live && !Playing)
            {
                var s = _story.Update(dt);
                _camp.SetStory(s);
                _audio.SetStorm(_room ? 0f : s.Rain); // no storm in your room (the fire, the story and the thunder carry on)
                _player.ShowLine(s.Index, s.T);
                _player.SetProgress(s.T, _story.Duration);
                _player.SetPauseLabel(_story.Ended ? "Replay" : _story.Paused ? "Resume" : "Pause");
                if (_story.Ended && !_endNoted)
                {
                    _endNoted = true;
                    _player.SetNote("The end. Try Hypa Translate, or replay.");
                }
                if (!_story.Ended) _endNoted = false;
                _player.SetTryOffer(_story.Ended && !_tryOpen && _ask.State == AskState.Idle);
                if (_tryOpen && !_story.Ended) HideTryNow();
            }
            _debug?.Update();
            _camp.Teller.Level = _voice.Level;
            _camp.Teller.Attend = _ask.State != AskState.Idle ? 1f : 0f;

            if (_tryOpen)
            {
                _try.Update(dt);
                _player.SetCorner(_tryFlow.Listening ? "Done" : "Speak", _tryFlow.Listening ? "check" : "mic");
            }
            UpdatePortal(dt);
            var raised = RaiseHand.Probe != null && RaiseHand.Probe();
            if (_raise.Update(raised, dt) && _screen == Screen.Play && !_busy && !_tryOpen && _ask.State == AskState.Idle && !Playing && !InHutTelling()) _ = _ask.BeginAsync();
            if (Application.isEditor) EditorKeys();
        }

        void LateUpdate()
        {
            if (_camp == null || _camp.Village == null || _voice == null) return;
            var speaking = _elderSession && !_away && _voice.IsSpeaking
                && _ask.State != AskState.Listening && !(_tryOpen && _tryFlow.Listening);
            _camp.Village.UpdateElderSpeech(_voice.Level, speaking, Time.unscaledDeltaTime);
        }

        void EditorKeys()
        {
            if (Input.GetKeyDown(KeyCode.R)) ToggleRoom(); // In my room / back to the forest
            if (Input.GetKeyDown(KeyCode.P)) { if (_room) UseDoor(null); else ToggleRoom(); } // editor: step through the door (into your room first)
            if (Input.GetKeyDown(KeyCode.G)) StepThroughPortal(); // editor: step through the door, or back out (you can't walk here)
            if (Input.GetKeyDown(KeyCode.V) && _screen == Screen.Play) _player.SetHidden(!_player.Hidden); // hide / show the story panel
            switch (_screen)
            {
                case Screen.Splash:
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                    {
                        var p = ViewerOnGround;
                        if (!_room && VillageLayout.AtLocal(p)) OpenStories(true);
                        else if (_room || p.magnitude <= 9f) OpenStories(false);
                    }
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
                    if (_tryOpen)
                    {
                        if (Input.GetKeyDown(KeyCode.Escape)) CloseTry();
                        for (var k = 0; k < 3; k++)
                            if (Input.GetKeyDown(KeyCode.Alpha1 + k))
                            {
                                var words = _tryFlow.Suggestions;
                                if (words != null && k < words.Length) _ = _tryFlow.UseWordAsync(words[k]);
                            }
                        break;
                    }
                    if (Playing)
                    {
                        if (Input.GetKeyDown(KeyCode.Escape)) { EndGames(); _voice.StopAll(); _player.Root.gameObject.SetActive(true); _player.Follow.Snap(); }
                        break; // Space is the shout; P, T and M belong to the fight
                    }
                    if (Input.GetKeyDown(KeyCode.Space)) _story.TogglePause();
                    if (Input.GetKeyDown(KeyCode.RightArrow)) _story.Jump(1);
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) _story.Jump(-1);
                    if (Input.GetKeyDown(KeyCode.H) && _ask.State == AskState.Idle && !InHutTelling()) _ = _ask.BeginAsync();
                    if (Input.GetKeyDown(KeyCode.T)) OpenTry();
                    if (Input.GetKeyDown(KeyCode.Q)) { _qDown = Time.time; AskTap(); }
                    if (Input.GetKeyUp(KeyCode.Q) && Time.time - _qDown > 0.5f && _ask.State == AskState.Listening) _ask.StopListening(); // hold-to-talk
                    for (var k = 0; k < 3; k++)
                        if (Input.GetKeyDown(KeyCode.Alpha1 + k))
                        {
                            var qs = _ask.Suggestions;
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

        /// <summary>The old woman in the hut is telling you about a thing, or you're out on the trails: a raised hand is not for the fire.</summary>
        bool InHutTelling() => (_hut != null && _hut.Telling) || !_ui.gameObject.activeSelf;

        // Headset off, system menu or app switch: pause the story. It never resumes on its own.
        void OnApplicationPause(bool paused) { if (paused) _story?.Pause(); }
        void OnApplicationFocus(bool focus) { if (!focus && !Application.isEditor) _story?.Pause(); }

        void OnDestroy()
        {
            Places.Leaving -= OnLeavingPlace;
            Places.Arrived -= OnArrivedPlace;
            if (Places.InCamp == (System.Func<string, bool>)HoldsPlace) { Places.InCamp = null; Places.TravelInCamp = null; }
            if (Places.NumberKeysTaken == (System.Func<bool>)NumberKeysTaken) Places.NumberKeysTaken = null;
            StoryRoom.Depth?.Invoke(false);
            DestroyDoors();
            _cts.Cancel();
            _cts.Dispose();
            _ask?.Cancel();
            _tryFlow?.Cancel();
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

        // ---------- in my room (3K) ----------

        /// <summary>
        /// The room button (or R in the editor): bring the current storyteller into the room, then return to the outdoor session.
        /// Elder sessions bring only the elder; fireside sessions keep the fire and its storyteller. Playback continues.
        /// </summary>
        void ToggleRoom()
        {
            if (_busy || _roomBusy || _away) return;
            StartCoroutine(_room ? LeaveRoom() : EnterRoom());
        }

        /// <summary>The floor under you: the rig's origin, or an eye height below the head when the tracking origin is at the eyes.</summary>
        float FloorHeight()
        {
            var head = _cam.position;
            var floor = _cam.parent != null ? _cam.root.position.y : head.y - seatedEyeHeight;
            return head.y - floor < 0.6f ? head.y - seatedEyeHeight : floor;
        }

        IEnumerator EnterRoom()
        {
            _roomBusy = true;
            if (StoryRoom.Passthrough != null && !StoryRoom.Passthrough())
            {
                ShowRoomNote("The headset's see-through view isn't running, so your room can't show.", 5f);
                _roomBusy = false;
                yield break;
            }

            // read the room (Meta's scan; with none, Meta's own set-up flow asks the player to scan it, and we wait for them to come back)
            Task<RoomScan> scan = null;
            if (!_elderSession && StoryRoom.Scan != null)
            {
                try { scan = StoryRoom.Scan(); } catch (Exception e) { Debug.LogWarning("Hypa Stories: room scan: " + e.Message); }
            }
            if (scan != null)
            {
                ShowRoomNote("Looking at your room…", 0f);
                var t0 = Time.realtimeSinceStartup;
                while (!scan.IsCompleted && Time.realtimeSinceStartup - t0 < 600f) yield return null;
            }
            if (_away || _busy) { HideRoomNote(); _roomBusy = false; yield break; } // gone elsewhere meanwhile
            var found = scan != null && scan.Status == TaskStatus.RanToCompletion && scan.Result != null ? scan.Result : null;
            _roomScan = found;

            string note = null;
            RoomSpotPlan plan = default;
            float floorY = 0f;
            if (!_elderSession)
            {
                // where the fire goes: an open spot in the scanned room, else in front of you
                var head = _cam.position;
                var user = new Vector2(head.x, head.z);
                var fwd3 = _cam.forward; fwd3.y = 0f;
                var fwd = new Vector2(fwd3.x, fwd3.z);
                floorY = FloorHeight();
                if (found != null && found.Result == RoomScanResult.Found && found.Clearance != null)
                {
                    floorY = found.FloorY;
                    var spot = RoomSpot.Find(user, fwd, found.Clearance, found.Floor);
                    Debug.Log("Hypa Stories room: " + (spot.HasValue ? $"fire at {spot.Value.Fire}, gap {spot.Value.Gap:0.0} m, clearance {found.Clearance(spot.Value.Fire):0.00} m" : "no open spot found") + $", you at {user}, floor y {found.FloorY:0.00}");
                    if (spot.HasValue) plan = spot.Value;
                    else
                    {
                        plan = RoomSpot.InFront(user, fwd, RoomSpot.MinGap);
                        note = "There's no open floor in your room, so the fire is just in front of you.";
                    }
                }
                else
                {
                    plan = RoomSpot.InFront(user, fwd);
                    var why = found != null ? found.Result : RoomScanResult.Unavailable;
                    if (why == RoomScanResult.NoScan) note = "No room scan yet, so the fire is just in front of you. Scan your room in your headset's settings, then try again.";
                    else if (why == RoomScanResult.NoPermission) note = "Allow Hypa to see your room and the fire will find its spot. For now it's just in front of you.";
                }
            }

            // through the veil: the forest goes, your room shows, the fire is on its floor
            Run(0f, 0.45f, u => SetVeil(u), In);
            yield return new WaitForSeconds(0.5f);
            if (_away) { SetVeil(0f); _roomBusy = false; yield break; }
            _room = true;
            _outdoorPosition = _world.position; _outdoorRotation = _world.rotation;
            _returnToRoom = false;
            _roomPlan = plan;
            _roomFloorY = floorY;
            _voice.SetOrigin(_elderSession ? _camp.Village.ElderMouth : _camp.Teller.Mouth);
            _camp.SetRoom(true, _elderSession);
            StoryRoom.Depth?.Invoke(true);
            PlaceInRoom();
            SetRoomButtons(true);
            RefitGames();
            // The elder's room has no doorway. A portal left over from the village trip is removed; the fireside room gets a fresh one.
            string doorNote = null;
            if (_elderSession) DestroyDoors();
            else doorNote = ArmPortal();
            SnapPanels();
            Run(0.05f, 0.7f, u => SetVeil(1f - u), Out);
            if (note != null) ShowRoomNote(note, 7f); else ShowRoomNote(doorNote, 8f);
            yield return new WaitForSeconds(0.8f);
            _roomBusy = false;
        }

        IEnumerator LeaveRoom()
        {
            _roomBusy = true;
            Run(0f, 0.45f, u => SetVeil(u), In);
            yield return new WaitForSeconds(0.5f);
            if (_room)
            {
                ExitRoomNow();
                if (!_elderSession) Recenter(_screen == Screen.Splash ? CampWorld.SplashSeat : CampWorld.StorySeat);
                RefitGames();
                SnapPanels();
            }
            Run(0.05f, 0.7f, u => SetVeil(1f - u), Out);
            yield return new WaitForSeconds(0.8f);
            _roomBusy = false;
        }

        /// <summary>Back to the forest at once (the world around the fire returns; where the camp stands is the caller's to set).</summary>
        void ExitRoomNow(bool keepPortal = false)
        {
            if (!_room) return;
            if (!keepPortal) DestroyDoors();
            _room = false;
            _camp.SetRoom(false);
            if (_elderSession) _world.SetPositionAndRotation(_outdoorPosition, _outdoorRotation);
            StoryRoom.Depth?.Invoke(false);
            SetRoomButtons(false);
            HideRoomNote();
        }

        /// <summary>Every In my room button: the story panel, and the panels of the games after a story.</summary>
        void SetRoomButtons(bool inRoom)
        {
            _player.SetRoom(inRoom);
            _game?.SetInRoom(inRoom);
            _hatGame?.SetInRoom(inRoom);
            _drumGame?.SetInRoom(inRoom);
        }

        /// <summary>Place the fire and its storyteller on the scanned room floor.</summary>
        void PlaceInRoom()
        {
            if (_elderSession) return; // the elder keeps his outdoor pose in passthrough
            var away = new Vector3(_roomPlan.Away.x, 0f, _roomPlan.Away.y);
            _world.SetPositionAndRotation(new Vector3(_roomPlan.Fire.x, _roomFloorY, _roomPlan.Fire.y), Quaternion.LookRotation(away));
            _camp.SetTellerGap(_roomPlan.Gap);
            _camp.FaceTeller(_cam.position);
        }

        // ---------- portal: hut, village or camp (3L) ----------

        /// <summary>
        /// A doorway on every real door the scan found, each open and showing where it leads (live). With no door in the scan, one on a clear
        /// stretch of wall; with no scan, one in front of you. Three chips beside each switch the place. A step through goes in; a step back
        /// brings you home. Returns a line to show.
        /// </summary>
        string ArmPortal()
        {
            DestroyDoors();
            var head = _cam.position;
            var user = new Vector2(head.x, head.z);
            var fwd3 = _cam.forward; fwd3.y = 0f;
            var fwd = new Vector2(fwd3.x, fwd3.z);
            var fire = _roomPlan.Fire;
            var plans = new System.Collections.Generic.List<PortalPlan>();
            var note = "Step through the door to go where it shows.";
            var scanned = _roomScan != null && _roomScan.Result == RoomScanResult.Found && _roomScan.Surfaces != null && _roomScan.Surfaces.Count > 0 && _roomScan.Clearance != null;
            if (scanned)
            {
                plans = PortalSpot.Doors(user, _roomScan.Surfaces, _roomFloorY);
                if (plans.Count == 0)
                {
                    var found = PortalSpot.Find(user, fwd, _roomScan.Surfaces, _roomFloorY, _roomScan.Clearance, fire);
                    if (found.HasValue) { plans.Add(found.Value); note = "Your scan has no door, so there's a doorway on your wall. Step through it to go where it shows."; }
                    else { plans.Add(PortalSpot.InFront(user, fwd, _roomFloorY, fire)); note = "Your scan has no door and no clear wall, so the doorway is just in front of you. Step through it."; }
                }
            }
            else plans.Add(PortalSpot.InFront(user, fwd, _roomFloorY, fire));
            foreach (var plan in plans)
            {
                Debug.Log($"Hypa Stories portal: {plan.Kind}{(plan.Scanned ? "" : " (in front of you)")} at {plan.Base}, facing {plan.Normal}, {plan.Width:0.00} x {plan.Height:0.00} m");
                AddDoor(plan);
            }
            PaintChips();
            _portalQuietUntil = Time.time + 1.5f;
            _audio.PortalOpen();
            return note;
        }

        void AddDoor(PortalPlan plan)
        {
            var d = new DoorPortal { Plan = plan };
            d.Portal = StoryPortal.Create(assets.portal != null ? assets.portal : Shader.Find("HypaStories/Portal"),
                assets.portalHole != null ? assets.portalHole : Shader.Find("HypaStories/PortalHole"), plan, () => UseDoor(d), CycleDestination);
            d.Portal.Link = () => PortalLink(d);
            d.Portal.Fog = () => _camp.ForestFog;
            d.Portal.Show();
            BuildChips(d);
            _doors.Add(d);
        }

        /// <summary>The real doorway linked to the chosen place's doorway in the camp's world (null when there is nothing to show).</summary>
        Matrix4x4? PortalLink(DoorPortal d)
        {
            if (!_room || d.Portal == null || !d.Portal.Opening) return null;
            var real = PortalDestinations.Frame(d.Plan.Base, d.Plan.Normal);
            return PortalDestinations.Link(real, PortalDestinations.DoorWorld(_world.localToWorldMatrix, _dest));
        }

        // the sign over a door: Hut, Village, Campfire (solid glass, like the other menus), mounted just above the door. It turns to face you.
        void BuildChips(DoorPortal d)
        {
            var p = d.Plan;
            const float W = 0.64f, H = 0.17f;
            d.Chips = new GameObject("Portal Bar").transform;
            d.Chips.SetParent(d.Portal.transform, false);
            d.Chips.localPosition = new Vector3(0f, p.Height + 0.1f + H * 0.5f, 0.04f); // a sign over the door (FaceBars keeps it just above the top)
            Kit.Panel(d.Chips, "Bar", Vector3.zero, new Vector2(W, H), 0.05f, KitTheme.GlassTop, KitTheme.GlassBottom, KitTheme.Hairline, 10, 0.5f);
            Kit.Text(d.Chips, "Where to?", 0.02f, new Vector3(0f, 0.056f, -0.004f), new Vector2(0.5f, 0.035f), KitTheme.Dim);
            d.Buttons = new KitButton[PortalDestinations.All.Length];
            for (var i = 0; i < d.Buttons.Length; i++)
            {
                var dest = PortalDestinations.All[i];
                d.Buttons[i] = Kit.Button(d.Chips, new Vector3((i - 1) * 0.205f, -0.012f, -0.004f), new Vector2(0.195f, 0.075f), PortalDestinations.Label(dest),
                    PortalDestinations.Icon(dest), KitStyle.Ghost, () => ChooseDestination(dest), null, 0.022f);
            }
        }

        void ChooseDestination(PortalDestination dest)
        {
            if (_busy || _roomBusy || !_room || _dest == dest) return;
            _dest = dest;
            _audio.Tap();
            foreach (var d in _doors) if (d.Portal != null) d.Portal.Dip();
            PaintChips();
        }

        /// <summary>Turns each bar toward you (level) and makes it bigger the further you are, so it reads from across the room.</summary>
        void FaceBars()
        {
            var head = _cam.position;
            foreach (var d in _doors)
            {
                if (d.Chips == null || !d.Chips.gameObject.activeInHierarchy) continue;
                var to = d.Chips.position - head;
                var dist = to.magnitude;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-4f) d.Chips.rotation = Quaternion.LookRotation(to.normalized);
                var k = Mathf.Clamp(dist / 2.5f, 0.85f, 1.4f);                     // about the door's width at a few metres
                d.Chips.localScale = Vector3.one * k;
                var lp = d.Chips.localPosition;
                d.Chips.localPosition = new Vector3(0f, d.Plan.Height + 0.1f + 0.085f * k, lp.z);   // its bottom edge stays just over the door
            }
        }

        /// <summary>The door itself was pinched: the next place (hut, village, campfire, round again).</summary>
        void CycleDestination()
        {
            if (_busy || _roomBusy || !_room) return;
            var all = PortalDestinations.All;
            _dest = all[(System.Array.IndexOf(all, _dest) + 1) % all.Length];
            _audio.Tap();
            foreach (var d in _doors) if (d.Portal != null) d.Portal.Dip();
            PaintChips();
        }

        void PaintChips()
        {
            foreach (var d in _doors)
            {
                if (d.Buttons == null) continue;
                for (var i = 0; i < d.Buttons.Length; i++)
                {
                    if (d.Buttons[i] == null) continue;
                    var on = PortalDestinations.All[i] == _dest;
                    d.Buttons[i].Style = on ? KitStyle.Primary : KitStyle.Ghost;
                    d.Buttons[i].Selected = on;
                }
            }
        }

        void DestroyDoors()
        {
            _returnToRoom = false;
            _prevDepth = float.NaN;
            _through = null;
            foreach (var d in _doors) if (d.Portal != null) Destroy(d.Portal.gameObject);
            _doors.Clear();
            _camp?.SetViewer(null);
        }

        /// <summary>The door nearest your head.</summary>
        DoorPortal NearestDoor()
        {
            DoorPortal best = null; var bd = float.MaxValue;
            foreach (var d in _doors)
            {
                var dist = (d.Plan.Base - _cam.position).sqrMagnitude;
                if (dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        /// <summary>Per frame: the pull as you near a doorway, the camp's draw distance for the view through it, and the step in or back.</summary>
        void UpdatePortal(float dt)
        {
            var head = _cam.position;
            if (_room && _doors.Count > 0)
            {
                FaceBars();
                var near = NearestDoor();
                var link = near != null ? PortalLink(near) : null;
                _camp.SetViewer(link.HasValue ? _world.InverseTransformPoint(link.Value.MultiplyPoint(head)) : (Vector3?)null);
            }
            if (_busy || _roomBusy) return;
            if (_room)
            {
                foreach (var d in _doors)
                {
                    if (d.Portal == null || !d.Portal.Opening) continue;
                    var l = PortalSpot.Local(d.Plan, head);
                    d.Portal.SetPull(PortalSpot.Pull(l.z, l.x, d.Plan.Width));
                    if (Time.time < _portalQuietUntil || Playing) { d.Prev = l.z; continue; }
                    if (!float.IsNaN(d.Prev) && PortalSpot.SteppedIn(d.Prev, l.z, l.x, l.y, d.Plan.Width, d.Plan.Height, -PortalSpot.Through)) { d.Prev = float.NaN; UseDoor(d, steppedThrough: true); return; }
                    d.Prev = l.z;
                }
            }
            else if (_returnToRoom && _through != null)
            {
                var here = _world.InverseTransformPoint(head);
                var v = new Vector2(here.x, here.z) - PortalDestinations.Door(_dest);
                var dir = PortalDestinations.DoorDir(_dest);
                var depth = Vector2.Dot(v, dir);                          // + = outside, the room's side
                var across = Vector2.Dot(v, new Vector2(dir.y, -dir.x));
                if (Time.time >= _portalQuietUntil && !float.IsNaN(_prevDepth) && PortalSpot.SteppedOut(_prevDepth, depth, across, _through.Plan.Width)) { _prevDepth = float.NaN; UseDoor(null); return; }
                _prevDepth = depth;
            }
        }

        /// <summary>
        /// A step through a door (or pinching its glass, or P in the editor): into the place it shows. From inside, a step back (or G) brings you
        /// home. <paramref name="door"/> null = the nearest.
        /// </summary>
        void UseDoor(DoorPortal door, bool steppedThrough = false)
        {
            if (_busy || _roomBusy || _away || Playing) return;
            if (_room)
            {
                door = door ?? NearestDoor();
                if (door != null && door.Portal != null && door.Portal.Opening) StartCoroutine(EnterDestination(door, steppedThrough));
            }
            else if (_returnToRoom) StartCoroutine(BackToTheRoom());
        }

        void StepThroughPortal() => UseDoor(null);

        /// <summary>
        /// A soft flash and you are in the place, just inside its doorway, which is laid on the real door: you see your room through it, and
        /// stepping back over it takes you home. (Pinched from afar or by key, the doorway comes to you, a step in front of your head.)
        /// </summary>
        IEnumerator EnterDestination(DoorPortal door, bool steppedThrough)
        {
            _roomBusy = true;
            _audio.PortalPass();
            Run(0f, 0.22f, u => SetVeil(u), In);
            yield return new WaitForSeconds(0.26f);
            if (_away) { SetVeil(0f); _roomBusy = false; yield break; }
            ExitRoomNow(keepPortal: true);
            var at = door.Plan;
            if (!steppedThrough)
            {
                var depth = PortalSpot.Local(at, _cam.position).z;
                at.Base += at.Normal * (depth + PortalSpot.Through); // bring the doorway to a seated player
            }
            var pose = PortalSpot.WorldPose(at, PortalDestinations.Door(_dest), PortalDestinations.DoorDir(_dest));
            _world.SetPositionAndRotation(pose.position, pose.rotation);
            _through = door;
            foreach (var d in _doors)
            {
                if (d != door) { d.Portal.gameObject.SetActive(false); continue; }
                d.Portal.transform.position = at.Base;
                d.Portal.SetHole(true);
                if (d.Chips != null) d.Chips.gameObject.SetActive(false); // the plate
            }
            _returnToRoom = true; _prevDepth = float.NaN; _portalQuietUntil = Time.time + 0.8f;
            SnapPanels();
            Run(0.05f, 0.5f, u => SetVeil(1f - u), Out);
            yield return new WaitForSeconds(0.6f);
            _roomBusy = false;
        }

        /// <summary>Stop the elder's visit and return to the camp splash. He stays in the village.</summary>
        void EndElderVisit()
        {
            if (!_elderSession) return;
            _elderSession = false;
            if (_tryOpen) HideTryNow();
            _ask.Cancel();
            _voice.StopAll();
            if (_story.Live) _story.Stop();
            _audio.SetStorm(0f);
            _audio.SetScene(StoryAudio.Splash, 0.6f);
            _camp.SetStory(null);
            _camp.Dim = 0f;
            _boost = 1f;
            _gatherTarget = 0f;
            _player.Root.gameObject.SetActive(false);
            _list.Root.gameObject.SetActive(false);
            _prep.Set(0f);
            _splash.Brand.gameObject.SetActive(true);
            _splash.SetAlpha(1f);
            _splash.Follow.Snap();
            // In the room the fire and its ring were already put away with the village. Outside, the splash shows the ring, not the storyteller.
            if (!_room)
            {
                _camp.Teller.gameObject.SetActive(false);
                _camp.PortalRing.gameObject.SetActive(true);
            }
            _camp.RingBurst = 0f;
            _screen = Screen.Splash;
            _busy = false;
            UpdateNarrationAudio();
        }

        IEnumerator BackToTheRoom()
        {
            if (_elderSession) EndElderVisit(); // his story does not follow you home
            _roomBusy = true;
            _audio.PortalPass();
            Run(0f, 0.22f, u => SetVeil(u), In);
            yield return new WaitForSeconds(0.26f);
            if (_away) { SetVeil(0f); _roomBusy = false; yield break; }
            _returnToRoom = false;
            _room = true;
            _camp.SetRoom(true);
            StoryRoom.Depth?.Invoke(true);
            PlaceInRoom();
            SetRoomButtons(true);
            RefitGames();
            foreach (var d in _doors)
            {
                d.Portal.gameObject.SetActive(true);
                d.Portal.transform.position = d.Plan.Base;
                d.Portal.SetHole(false);
                d.Portal.Show();
                d.Prev = float.NaN;
                if (d.Chips != null) d.Chips.gameObject.SetActive(true);
            }
            _through = null;
            _prevDepth = float.NaN; _portalQuietUntil = Time.time + 1.2f;
            SnapPanels();
            Run(0.05f, 0.5f, u => SetVeil(1f - u), Out);
            yield return new WaitForSeconds(0.6f);
            _roomBusy = false;
        }

        /// <summary>
        /// How a game should lay itself out now: as built in the forest, or pulled in to your room's real distances. If you've walked
        /// well round the fire since it was placed, the camp turns (round the fire, which stays put) so the game is in front of you.
        /// </summary>
        GameFit CurrentFit()
        {
            if (!_room) return GameFit.Forest;
            var head = new Vector2(_cam.position.x, _cam.position.z);
            var toFire = _roomPlan.Fire - head;
            if (toFire.sqrMagnitude > 0.04f && Vector2.Angle(toFire, _roomPlan.Away) > 30f)
            {
                _roomPlan = new RoomSpotPlan(_roomPlan.Fire, toFire.normalized, _roomPlan.Gap, _roomPlan.Scanned);
                PlaceInRoom();
            }
            return GameFit.InRoom(toFire.magnitude, _roomPlan.Gap);
        }

        /// <summary>A game going on when you switch worlds carries on, laid out for the new one.</summary>
        void RefitGames()
        {
            if (_game != null && _game.Active) _game.Fit(CurrentFit());
            if (_hatGame != null && _hatGame.Active) _hatGame.Fit(CurrentFit());
            if (_drumGame != null && _drumGame.Active) _drumGame.Fit(CurrentFit());
        }

        /// <summary>A line in front of you for <paramref name="seconds"/> (0 = until hidden).</summary>
        void ShowRoomNote(string text, float seconds)
        {
            _roomNoteText.text = text;
            _roomNote.Refresh();
            _roomNote.Set(1f);
            var id = ++_roomNoteId;
            if (seconds > 0f) At(seconds, () => { if (id == _roomNoteId) HideRoomNote(); });
        }

        void HideRoomNote()
        {
            _roomNoteId++;
            _roomNote.Set(0f);
        }

        // ---------- walking about ----------

        /// <summary>
        /// Left thumbstick walks (where you look), right thumbstick snap-turns; WASD in the editor. The world moves, not the rig, so
        /// it works under the shell too. You stay within <see cref="walkRadius"/> of the fire and can't walk into it.
        /// </summary>
        void Locomotion(float dt)
        {
            if (Playing) return; // the fight is staged at the seat
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
            // the clearing (out of the fire), the trail to the hut, its doorway and the inside; never through the hut's walls
            var p = new Vector2(local.x, local.z);
            var q = Grounds.Clamp(p, walkRadius);
            if ((q - p).sqrMagnitude < 1e-8f) return;
            _world.position += _world.TransformVector(new Vector3(p.x - q.x, 0f, p.y - q.y));
        }

        // ---------- the trail to the hut (3G) ----------

        /// <summary>
        /// Out on a trail the campfire's panels step aside (they belong to the fire). At the hut's door and the village gate the
        /// place row shows, the way Gather round greets you at the fire: Back, the other places, and the one you're at in the middle.
        /// </summary>
        void BuildDoorRows()
        {
            System.Action back = HypaShell.Instance != null ? (System.Action)(() => HypaShell.Instance.ExitToHome()) : null;
            AddDoorRow("Hut Door Buttons", "The hut", HutTrailLayout.AtDoor, row =>
                PlaceButtons.Add(row, PlaceIds.Hut, back, BackToTheFire, StepInside, () => Places.GoTo(PlaceIds.Gathering)));
            AddDoorRow("Village Gate Buttons", "The village", VillageLayout.AtGate, row =>
                PlaceButtons.Add(row, PlaceIds.Gathering, back, BackToTheFire, () => Places.GoTo(PlaceIds.Hut), MeetTheLocals));
            AddDoorRow("Elder Buttons", "Stories with the elder", VillageLayout.AtLocal, row =>
                PlaceButtons.Add(row, PlaceIds.Gathering, back, BackToTheFire, () => Places.GoTo(PlaceIds.Hut), () => OpenStories(true), "Narrate story"), true);
        }

        void AddDoorRow(string name, string title, System.Func<Vector2, bool> at, System.Action<Transform> buttons, bool elderRow = false)
        {
            var row = new GameObject(name).transform;
            row.SetParent(transform, false);
            Kit.Text(row, title, 0.02f, new Vector3(0f, 0.058f, 0f), new Vector2(0.5f, 0.03f), KitTheme.Dim);
            buttons(row);
            var f = row.gameObject.AddComponent<FollowView>();
            f.Distance = 0.95f; f.Height = -0.34f;
            var g = row.gameObject.AddComponent<KitGroup>();
            g.Refresh();
            g.Set(0f);
            row.gameObject.SetActive(false);
            _doorRows.Add(new DoorRow { Row = row, Group = g, At = at, ElderRow = elderRow });
        }

        /// <summary>The places the camp holds in its own world: the fire, the hut and the village, at the ends of their trails.</summary>
        static bool HoldsPlace(string id) => id == PlaceIds.Camp || id == PlaceIds.Hut || id == PlaceIds.Gathering;

        /// <summary>A place button pressed while you're in the camp's world: through the veil to the fire, the hut's door or the village gate.</summary>
        void TravelInCamp(string id)
        {
            if (id == PlaceIds.Hut) TravelTo(HutTrailLayout.FrontOfDoor, HutTrailLayout.Hut);
            else if (id == PlaceIds.Gathering) TravelTo(VillageLayout.FrontOfGate, VillageLayout.Plaza);
            else BackToTheFire();
        }

        /// <summary>Step inside, at the door: a step into the room, facing the old woman and her household things (<see cref="HutVisit"/>).</summary>
        void StepInside() => TravelTo(HutTrailLayout.Hut + HutTrailLayout.DoorDir * 1.15f, HutTrailLayout.Hut - HutTrailLayout.DoorDir);

        /// <summary>Meet the locals, at the gate: in front of the elder by the fence, where the story prompt appears.</summary>
        void MeetTheLocals() => TravelTo(VillageLayout.MeetSpot, VillageLayout.Local);

        /// <summary>Through the veil, stand at <paramref name="at"/> looking toward <paramref name="look"/> (camp ground, x and z).</summary>
        void TravelTo(Vector2 at, Vector2 look)
        {
            if (_busy) return;
            _busy = true;
            _audio.Tap();
            Run(0f, 0.45f, u => SetVeil(u), In);
            At(0.5f, () => { StandAt(at, look); SnapPanels(); });
            Run(0.55f, 0.6f, u => SetVeil(1f - u), Out);
            At(1.2f, () => _busy = false);
        }

        void SnapPanels()
        {
            if (_screen == Screen.Splash) _splash.Follow.Snap();
            else if (_screen == Screen.List) _list.Follow.Snap();
            else _player.Follow.Snap();
            foreach (var d in _doorRows) if (d.Row.gameObject.activeSelf) d.Row.GetComponent<FollowView>().Snap();
        }

        /// <summary>
        /// A button in another place asked for the hut or the village: stand there. Returns true when a spot was waiting.
        /// The camp's own arrival (the seat by the fire) leaves it false.
        /// </summary>
        bool ApplyCampSpot()
        {
            var spot = Places.TakeCampSpot();
            if (spot == PlaceIds.Hut) { StandAt(HutTrailLayout.FrontOfDoor, HutTrailLayout.Hut); return true; }
            if (spot == PlaceIds.Gathering) { StandAt(VillageLayout.FrontOfGate, VillageLayout.Plaza); return true; }
            return false;
        }

        /// <summary>Put the head at <paramref name="at"/> on the camp ground, looking toward <paramref name="look"/>. The world moves, not the rig.</summary>
        void StandAt(Vector2 at, Vector2 look)
        {
            if (_room) ExitRoomNow(); // out on the trails you're in the forest
            else if (_returnToRoom) DestroyDoors();
            var head = _cam.position;
            var floor = _cam.parent != null ? _cam.root.position.y : head.y - seatedEyeHeight;
            if (head.y - floor < 0.6f) floor = head.y - seatedEyeHeight;
            var headFwd = _cam.forward; headFwd.y = 0f;
            if (headFwd.sqrMagnitude < 1e-4f) headFwd = Vector3.forward;
            var want = new Vector3(look.x - at.x, 0f, look.y - at.y);
            if (want.sqrMagnitude < 1e-6f) want = Vector3.forward;
            var worldRot = Quaternion.LookRotation(headFwd.normalized) * Quaternion.Inverse(Quaternion.LookRotation(want.normalized));
            _world.SetPositionAndRotation(new Vector3(head.x, floor, head.z) - worldRot * new Vector3(at.x, 0f, at.y), worldRot);
        }

        /// <summary>Campfire, from the hut's door or the village gate: through the veil, back to your spot facing the fire.</summary>
        void BackToTheFire()
        {
            if (_busy) return;
            _busy = true;
            _audio.Tap();
            Run(0f, 0.45f, u => SetVeil(u), In);
            At(0.5f, () =>
            {
                if (_returnToRoom) DestroyDoors();
                Recenter(_screen == Screen.Splash ? CampWorld.SplashSeat : CampWorld.StorySeat);
                SnapPanels();
            });
            Run(0.55f, 0.6f, u => SetVeil(1f - u), Out);
            At(1.2f, () => _busy = false);
        }

        void TrailPanels(float dt)
        {
            var v = ViewerOnGround;
            // the fire's panels: shown in the clearing, put away out on the trails
            var far = !_room && v.magnitude > 9f;
            var showUi = !far || _elderSession;
            if (_ui.gameObject.activeSelf != showUi)
            {
                _ui.gameObject.SetActive(showUi);
                if (showUi) SnapPanels();
            }
            // The list and player may follow an elder session; Gather round still belongs to the clearing.
            _splash.Prompt.gameObject.SetActive(!far && _splash.Brand.gameObject.activeSelf);
            // a place's row, at its door
            var elderUiOpen = _elderSession && (_screen != Screen.Splash || _busy);
            foreach (var d in _doorRows)
            {
                d.Shown = Approach(d.Shown, PlaceRowShown(_room, d.At(v), d.ElderRow, elderUiOpen) ? 1f : 0f, 6f, dt);
                var on = d.Shown > 0.01f;
                if (d.Row.gameObject.activeSelf != on)
                {
                    d.Row.gameObject.SetActive(on);
                    if (on) d.Row.GetComponent<FollowView>().Snap();
                }
                if (on) d.Group.Set(d.Shown);
            }
        }
    }
}
