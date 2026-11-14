using UnityEngine;
using UnityEngine.SceneManagement;

namespace HypaCore
{
    /// <summary>Scene names. Each team owns its own scene; the shell loads them by name only (no code references).</summary>
    public static class HypaScenes
    {
        public const string Shell = "HypaShell";
        public const string Lens = "HypaLens";
        public const string Story = "HypaStory";
        public const string CoreTest = "HypaCoreTest";
    }

    /// <summary>
    /// Lets the shell ask a mode to open a sub-view without either side referencing the other's code.
    /// e.g. the shell sets "lens.saved" and loads HypaLens; Lens consumes it and opens the saved-words practice view.
    /// </summary>
    public static class AppRouter
    {
        public const string LensSavedWords = "lens.saved";
        static string _pending;
        public static void Request(string view) => _pending = view;
        public static bool Consume(string view)
        {
            if (_pending != view) return false;
            _pending = null;
            return true;
        }
    }

    /// <summary>
    /// App shell, in the HypaShell scene (first in Build Settings). Keeps the camera rig, pinch pointer and audio alive
    /// across modes, and shows Home: Hypa Lens, Stories and How it works. Home is the first thing the user sees; a
    /// mode's X (or the Home button low in the view, or M in the editor) brings it back at any time.
    /// </summary>
    public sealed class HypaShell : MonoBehaviour
    {
        public static HypaShell Instance { get; private set; }

        [Tooltip("Root of the camera rig to keep across scenes. Empty = the main camera's root.")]
        [SerializeField] Transform cameraRig;
        [Tooltip("Skip the Home card on launch and open the last mode straight away (dev convenience).")]
        [SerializeField] bool openLastModeOnStart = false;

        Transform _cam, _anchor, _menu, _help;
        GameObject _menuButton;
        TextMesh _status;
        /// <summary>The scene a mode lives in, or null while only the shell is loaded.</summary>
        string _currentMode;
        // The shell UI follows where the head points (see HeadFollow): 1 m out (beyond the hands), scaled so it reads
        // as it would at 0.6 m. A small dead zone keeps it still while a hand ray aims at it.
        const float Distance = 1.0f, Scale = Distance / 0.6f;
        readonly HeadFollow _follow = new HeadFollow { Distance = Distance, Delay = 0.4f }; // a glance never moves it
        bool _xrWasActive;
        /// <summary>The shared Hypa mark: Assets/HypaCore/Resources/HypaLogo.png (square, transparent, white on dark).</summary>
        const string LogoResource = "HypaLogo";

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            var cam = Camera.main;
            if (cameraRig == null && cam != null) cameraRig = cam.transform.root;
            if (cameraRig != null && cameraRig != transform) DontDestroyOnLoad(cameraRig.gameObject);
            // Passthrough is its own object, not inside the camera rig. Loading Lens would otherwise
            // destroy it and leave a black view.
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root != gameObject && root.name.StartsWith("[BuildingBlock]"))
                    DontDestroyOnLoad(root);
            }
            _cam = cam != null ? cam.transform : null;
            PinchPointer.EnsureExists();
            _ = HypaAudioPlayer.Instance;
        }

        void Start()
        {
            if (_cam == null) { Debug.LogError("Hypa shell: no MainCamera."); return; }
            BuildMenuButton();
            // Wait one frame so the camera rig can turn passthrough on before a mode replaces the scene.
            StartCoroutine(StartNextFrame());
        }

        System.Collections.IEnumerator StartNextFrame()
        {
            yield return null;
            if (openLastModeOnStart) Open(HypaSettings.LastMode);
            else ShowMenu(true); // the Home card is the first thing the user sees
        }

        void Update()
        {
            if (Application.isEditor && Input.GetKeyDown(KeyCode.M)) ToggleMenu();
        }

        // After the camera rig has taken this frame's head pose, so the UI doesn't trail it by a frame.
        void LateUpdate() => FollowHead();

        void FollowHead()
        {
            if (_anchor == null || _cam == null) return;
            // The XR session starts a few frames in and lifts the camera to eye height: snap in front then.
            var xrActive = UnityEngine.XR.XRSettings.isDeviceActive;
            if (xrActive && !_xrWasActive) _follow.Reset();
            _xrWasActive = xrActive;
            var pose = _follow.Update(_cam.position, _cam.rotation, PointerOnShellUi, Time.unscaledDeltaTime);
            _anchor.SetPositionAndRotation(pose.position, pose.rotation);
        }

        bool PointerOnShellUi
        {
            get
            {
                var p = PinchPointer.Instance;
                if (p == null || _anchor == null) return false;
                return (p.Hover != null && p.Hover.transform.IsChildOf(_anchor)) || (p.Pressed != null && p.Pressed.transform.IsChildOf(_anchor));
            }
        }

        /// <summary>Puts the shell UI straight in front of the eyes now.</summary>
        void PlaceInFront()
        {
            if (_anchor == null) return;
            _anchor.localScale = Vector3.one * Scale;
            _follow.Reset();
            FollowHead();
        }

        void BuildMenuButton()
        {
            _anchor = new GameObject("Hypa Shell UI").transform;
            DontDestroyOnLoad(_anchor.gameObject);
            PlaceInFront();
            // The Home button: low and close to the middle, big enough to hit with a wobbly hand ray, with a caption.
            // In a mode it leaves the mode; on its own it opens the Home card. A mode with its own X hides it.
            _menuButton = Ui.IconButton("house", _anchor, new Vector3(-0.15f, -0.19f, 0f), 0.038f, HomeButton, "Home").transform.parent.gameObject;

            // Home: the first thing the user sees. Title row, the modes, how it works, and an X.
            _menu = new GameObject("Home Card").transform;
            _menu.SetParent(_anchor, false);
            _menu.localPosition = Vector3.zero; // dead centre of the view (a mode's HUD steps aside while Home is up)
            const float w = 0.32f, h = 0.46f;
            Ui.Quad("bg", _menu, new Vector3(0, 0, 0.003f), new Vector2(w, h), Ui.Panel, true, rounded: true).AddComponent<PinchTarget>();
            var logo = Resources.Load<Texture2D>(LogoResource);
            const float titleY = 0.18f;
            if (logo != null)
            {
                var icon = Ui.Quad("logo", _menu, new Vector3(-0.115f, titleY, -0.001f), Vector2.one * 0.036f, Color.white, false, rounded: false);
                icon.GetComponent<Renderer>().material.mainTexture = logo;
            }
            Ui.Text(_menu, "Hypa", 0.024f, new Vector3(-0.085f, titleY, 0), Color.white, TextAnchor.MiddleLeft);
            // The X closes the Hypa app.
            Ui.IconButton("x", _menu, new Vector3(0.11f, titleY, -0.001f), 0.024f, QuitApp);
            Ui.Button("Hypa Lens", _menu, new Vector3(0, 0.10f, 0), new Vector2(0.26f, 0.06f), Ui.Accent, () => OpenOrResume(HypaScenes.Lens));
            Ui.Button("Stories", _menu, new Vector3(0, 0.03f, 0), new Vector2(0.26f, 0.06f), Ui.Muted, () => OpenOrResume(HypaScenes.Story));
            Ui.Button("Saved words", _menu, new Vector3(0, -0.04f, 0), new Vector2(0.26f, 0.06f), Ui.Muted, OpenSavedWords);
            Ui.Button("How it works", _menu, new Vector3(0, -0.11f, 0), new Vector2(0.26f, 0.06f), Ui.Muted, () => ShowHelp(true));
            _status = Ui.Text(_menu, "", 0.015f, new Vector3(0, -0.18f, 0), Color.white);
            _menu.gameObject.SetActive(false);

            BuildHelp();
        }

        /// <summary>How it works: the two experiences, each a heading and a short wrapped paragraph, and a way back.</summary>
        void BuildHelp()
        {
            const float w = 0.50f, h = 0.44f, pad = 0.03f, left = -w / 2f + pad;
            const float bodyH = 0.0135f, headH = 0.018f, line = 1.08f;
            var chars = Mathf.FloorToInt((w - pad * 2f) / (bodyH * Ui.CharWidth)) - 7; // what fits across the card, with room for wide glyphs
            var body = new Color(1f, 1f, 1f, 0.78f);

            _help = new GameObject("How It Works").transform;
            _help.SetParent(_anchor, false);
            _help.localPosition = new Vector3(0f, 0f, -0.01f);
            Ui.Quad("bg", _help, new Vector3(0, 0, 0.003f), new Vector2(w, h), Ui.Panel, true, rounded: true).AddComponent<PinchTarget>();
            var top = h / 2f - pad - 0.012f;
            Ui.Text(_help, "How it works", 0.024f, new Vector3(left, top, 0), Color.white, TextAnchor.MiddleLeft);
            Ui.IconButton("x", _help, new Vector3(w / 2f - pad - 0.02f, top, -0.001f), 0.022f, () => ShowHelp(false)); // inside the inner edge

            var y = top - 0.045f;
            void Section(string icon, string title, string text)
            {
                var tex = Ui.Icon(icon);
                if (tex != null)
                {
                    var q = Ui.Quad("icon", _help, new Vector3(left + 0.011f, y - headH / 2f, -0.001f), Vector2.one * 0.022f, Color.white, false, rounded: false);
                    q.GetComponent<Renderer>().material.mainTexture = tex;
                }
                Ui.Text(_help, title, headH, new Vector3(left + 0.032f, y - headH / 2f, 0), Color.white, TextAnchor.MiddleLeft);
                y -= headH + 0.012f;
                var wrapped = Ui.Wrap(text, chars);
                Ui.Text(_help, wrapped, bodyH, new Vector3(left, y, 0), body, TextAnchor.UpperLeft);
                y -= wrapped.Split('\n').Length * bodyH * line + 0.03f;
            }
            Section("camera", "Hypa Lens",
                "Look at a sign, menu or book and pinch Capture. The text and its translation open in a popup: hear it, or save it to practise later. Caption reads the whole view at once.");
            Section("languages", "Stories",
                "Sit by the campfire and listen to a storyteller in your language. Raise a hand to ask a question.");
            Ui.Text(_help, Ui.Wrap("Everything works with your hands. Pinch Home at any time to come back here.", chars), bodyH,
                new Vector3(left, y, 0), body, TextAnchor.UpperLeft);
            // Ui.IconButton("chevron-left", _help, new Vector3(0f, -h / 2f + pad + 0.03f, -0.001f), 0.028f, () => ShowHelp(false)); // back to Home (Stories' chevron)
            _help.gameObject.SetActive(false);
        }

        /// <summary>Opens/closes Home. Modes call this from their X.</summary>
        public void ToggleMenu() { if (_menu != null) ShowMenu(!_menu.gameObject.activeSelf); }

        /// <summary>The shell's UI in the room (Home button and cards), e.g. to keep it out of a snapshot.</summary>
        public Transform UiRoot => _anchor;

        public bool IsMenuOpen => _menu != null && (_menu.gameObject.activeSelf || _help.gameObject.activeSelf);

        /// <summary>Home always comes up in front of the user, over whatever the mode is showing.</summary>
        public void ShowMenu(bool open)
        {
            if (_menu == null) return;
            if (open) PlaceInFront();
            _menu.gameObject.SetActive(open);
            if (!open) _help.gameObject.SetActive(false);
            RefreshMenuButton();
        }

        void ShowHelp(bool open)
        {
            _help.gameObject.SetActive(open);
            _menu.gameObject.SetActive(!open);
            RefreshMenuButton();
        }

        /// <summary>A mode with its own X hides the shell's Home button (and shows it again when it closes).</summary>
        public void SetMenuButtonVisible(bool visible) { _menuButtonWanted = visible; RefreshMenuButton(); }

        bool _menuButtonWanted = true;

        /// <summary>The Home button steps aside while a Home card is open (it would sit on the card and do nothing new).</summary>
        void RefreshMenuButton() { if (_menuButton != null) _menuButton.SetActive(_menuButtonWanted && !IsMenuOpen); }

        /// <summary>Saved words live in Lens: open Lens (or go back to it) straight onto the practice cards.</summary>
        void OpenSavedWords()
        {
            AppRouter.Request(AppRouter.LensSavedWords);
            OpenOrResume(HypaScenes.Lens);
        }

        /// <summary>Closes the Hypa app (stops Play Mode in the editor).</summary>
        public void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>The Home button: in a mode it leaves the mode; otherwise it opens the Home card.</summary>
        void HomeButton()
        {
            if (_currentMode != null) ExitToHome();
            else ShowMenu(true);
        }

        const string HomeScene = "HypaHome";

        /// <summary>
        /// Leaves the running mode and goes back to the entry screen: the mode's scene is unloaded (its world, HUD and
        /// audio go with it) and the Home card comes up in front of the user. The camera rig, passthrough and pointer
        /// live on (DontDestroyOnLoad), so an empty scene is all Home needs.
        /// </summary>
        public void ExitToHome()
        {
            var mode = _currentMode != null ? SceneManager.GetSceneByName(_currentMode) : default;
            _currentMode = null;
            if (mode.IsValid() && mode.isLoaded)
            {
                var home = SceneManager.GetSceneByName(HomeScene);
                if (!home.IsValid()) home = SceneManager.CreateScene(HomeScene);
                SceneManager.SetActiveScene(home);
                SceneManager.UnloadSceneAsync(mode);
            }
            SetMenuButtonVisible(true);
            ShowMenu(true);
        }

        /// <summary>Picking the mode that is already running just closes Home; anything else loads it.</summary>
        void OpenOrResume(string scene)
        {
            if (scene == _currentMode) { ShowMenu(false); return; }
            Open(scene);
        }

        public void Open(string scene)
        {
            if (string.IsNullOrEmpty(scene) || scene == HypaScenes.Shell) scene = HypaScenes.Lens;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                if (_status != null) _status.text = scene + " isn't in Build Settings.\nRun Hypa > Sync Build Settings.";
                return;
            }
            HypaSettings.LastMode = scene;
            _currentMode = scene;
            ShowMenu(false);
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }
    }
}
