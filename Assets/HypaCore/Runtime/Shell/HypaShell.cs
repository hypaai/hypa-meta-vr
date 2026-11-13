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
        // The shell UI lives in the room, not on the camera: head-locked UI at hand depth slides away from a hand ray
        // the moment the user turns to look at it. It sits 1 m out (beyond the hands), scaled so it reads as it would
        // at 0.6 m, and is re-placed in front of the user whenever it opens or they have walked away from it.
        const float Distance = 1.0f, Scale = Distance / 0.6f;
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
            // Walked away from it, or it ended up behind the user: bring it back in front.
            if (_anchor != null && _cam != null)
            {
                var to = _anchor.position - _cam.position;
                if (to.magnitude > 2.5f || Vector3.Dot(_cam.forward, to) < 0f) PlaceInFront();
            }
        }

        /// <summary>Puts the shell UI 1 m in front of the eyes, square to the view (yaw only), facing the user.</summary>
        void PlaceInFront()
        {
            if (_anchor == null || _cam == null) return;
            var fwd = _cam.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            _anchor.position = _cam.position + fwd * Distance;
            _anchor.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            _anchor.localScale = Vector3.one * Scale;
        }

        void BuildMenuButton()
        {
            _anchor = new GameObject("Hypa Shell UI").transform;
            DontDestroyOnLoad(_anchor.gameObject);
            PlaceInFront();
            // The Home button: low-left, as in a narrow field of view. A mode with its own X hides it.
            _menuButton = Ui.IconButton("house", _anchor, new Vector3(-0.30f, -0.22f, 0f), 0.03f, () => ShowMenu(true)).transform.parent.gameObject;

            // Home: the first thing the user sees. Title row, the modes, how it works, and an X.
            _menu = new GameObject("Home Card").transform;
            _menu.SetParent(_anchor, false);
            _menu.localPosition = new Vector3(0f, 0.05f, 0f); // eye level, clear of a mode's own HUD below
            const float w = 0.32f, h = 0.40f;
            Ui.Quad("bg", _menu, new Vector3(0, 0, 0.003f), new Vector2(w, h), Ui.Panel, true, rounded: true).AddComponent<PinchTarget>();
            var logo = Resources.Load<Texture2D>(LogoResource);
            const float titleY = 0.15f;
            if (logo != null)
            {
                var icon = Ui.Quad("logo", _menu, new Vector3(-0.115f, titleY, -0.001f), Vector2.one * 0.036f, Color.white, false, rounded: false);
                icon.GetComponent<Renderer>().material.mainTexture = logo;
            }
            Ui.Text(_menu, "Hypa", 0.024f, new Vector3(-0.085f, titleY, 0), Color.white, TextAnchor.MiddleLeft);
            Ui.IconButton("x", _menu, new Vector3(0.115f, titleY, -0.001f), 0.024f, () => ShowMenu(false));
            Ui.Button("Hypa Lens", _menu, new Vector3(0, 0.065f, 0), new Vector2(0.26f, 0.06f), Ui.Accent, () => OpenOrResume(HypaScenes.Lens));
            Ui.Button("Stories", _menu, new Vector3(0, -0.005f, 0), new Vector2(0.26f, 0.06f), Ui.Muted, () => OpenOrResume(HypaScenes.Story));
            Ui.Button("How it works", _menu, new Vector3(0, -0.075f, 0), new Vector2(0.26f, 0.06f), Ui.Muted, () => ShowHelp(true));
            _status = Ui.Text(_menu, "", 0.015f, new Vector3(0, -0.145f, 0), Color.white);
            _menu.gameObject.SetActive(false);

            // How it works: a card with the two experiences explained, and its own X.
            _help = new GameObject("How It Works").transform;
            _help.SetParent(_anchor, false);
            _help.localPosition = new Vector3(0f, 0.05f, -0.01f);
            Ui.Quad("bg", _help, new Vector3(0, 0, 0.003f), new Vector2(0.50f, 0.34f), Ui.Panel, true, rounded: true).AddComponent<PinchTarget>();
            Ui.Text(_help, "How it works", 0.024f, new Vector3(-0.22f, 0.135f, 0), Color.white, TextAnchor.MiddleLeft);
            Ui.IconButton("x", _help, new Vector3(0.21f, 0.135f, -0.001f), 0.024f, () => ShowHelp(false));
            Ui.Text(_help,
                "Hypa Lens\nLook at a sign, menu or book and pinch Capture. Each word becomes a label on the real\nsurface: pinch one to translate, hear and define it; hold the pinch for the whole sentence.\nCaption reads the whole view at once. Save words to practise later.\n\nStories\nSit by the campfire and listen to a storyteller in your language. Raise a hand to ask a question.\n\nEverything works with your hands. Pinch X at any time to come back here.",
                0.0155f, new Vector3(-0.22f, -0.02f, 0), new Color(1f, 1f, 1f, 0.92f), TextAnchor.MiddleLeft);
            _help.gameObject.SetActive(false);
        }

        /// <summary>Opens/closes Home. Modes call this from their X.</summary>
        public void ToggleMenu() { if (_menu != null) ShowMenu(!_menu.gameObject.activeSelf); }

        public bool IsMenuOpen => _menu != null && (_menu.gameObject.activeSelf || _help.gameObject.activeSelf);

        /// <summary>Home always comes up in front of the user, over whatever the mode is showing.</summary>
        public void ShowMenu(bool open)
        {
            if (_menu == null) return;
            if (open) PlaceInFront();
            _menu.gameObject.SetActive(open);
            if (!open) _help.gameObject.SetActive(false);
        }

        void ShowHelp(bool open)
        {
            _help.gameObject.SetActive(open);
            _menu.gameObject.SetActive(!open);
        }

        /// <summary>A mode with its own X hides the shell's Home button (and shows it again when it closes).</summary>
        public void SetMenuButtonVisible(bool visible) { if (_menuButton != null) _menuButton.SetActive(visible); }

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
