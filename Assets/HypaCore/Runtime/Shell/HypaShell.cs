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
    /// across modes, and shows a hand menu: Lens, Practice (Saved words, Hypa Story) and the HypaCore API test.
    /// The menu is opened with the Menu button low in the view (or M in the editor); it sits within arm's reach.
    /// </summary>
    public sealed class HypaShell : MonoBehaviour
    {
        public static HypaShell Instance { get; private set; }

        [Tooltip("Root of the camera rig to keep across scenes. Empty = the main camera's root.")]
        [SerializeField] Transform cameraRig;
        [SerializeField] bool openLastModeOnStart = true;

        Transform _cam, _anchor, _menu, _practice;
        GameObject _menuButton;
        TextMesh _status;
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
            // Wait one frame so the camera rig can turn passthrough on before Lens replaces the scene.
            if (openLastModeOnStart) StartCoroutine(OpenNextFrame());
        }

        System.Collections.IEnumerator OpenNextFrame()
        {
            yield return null;
            Open(HypaSettings.LastMode);
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
            // The Menu button: low-left, as in a narrow field of view. Modes with their own launcher hide it.
            _menuButton = Ui.Button("Menu", _anchor, new Vector3(-0.30f, -0.22f, 0f), new Vector2(0.10f, 0.05f), Ui.Muted, ToggleMenu).transform.parent.gameObject;

            // The app menu: a card at eye level with a title row, the modes, and its own Close.
            _menu = new GameObject("Menu Panel").transform;
            _menu.SetParent(_anchor, false);
            _menu.localPosition = new Vector3(0f, 0.05f, 0f); // eye level, clear of a mode's own HUD below
            const float w = 0.30f, h = 0.42f;
            Ui.Quad("bg", _menu, new Vector3(0, 0, 0.003f), new Vector2(w, h), Ui.Panel, true, rounded: true).AddComponent<PinchTarget>();
            var logo = Resources.Load<Texture2D>(LogoResource);
            const float titleY = 0.165f;
            if (logo != null)
            {
                var icon = Ui.Quad("logo", _menu, new Vector3(-0.095f, titleY, -0.001f), Vector2.one * 0.036f, Color.white, false, rounded: false);
                icon.GetComponent<Renderer>().material.mainTexture = logo;
            }
            Ui.Text(_menu, "Hypa", 0.024f, new Vector3(-0.065f, titleY, 0), Color.white, TextAnchor.MiddleLeft);
            Ui.Button("Hypa Lens", _menu, new Vector3(0, 0.09f, 0), new Vector2(0.24f, 0.06f), Ui.Accent, () => Open(HypaScenes.Lens));
            Ui.Button("Practice", _menu, new Vector3(0, 0.02f, 0), new Vector2(0.24f, 0.06f), Ui.Muted, TogglePractice);
            Ui.Button("API test", _menu, new Vector3(0, -0.05f, 0), new Vector2(0.24f, 0.05f), Ui.Muted, () => Open(HypaScenes.CoreTest));
            Ui.Button("Close", _menu, new Vector3(0, -0.12f, 0), new Vector2(0.14f, 0.046f), Ui.Muted, () => ShowMenu(false));
            _status = Ui.Text(_menu, "", 0.015f, new Vector3(0, -0.175f, 0), Color.white);

            _practice = new GameObject("Practice Panel").transform;
            _practice.SetParent(_menu, false);
            _practice.localPosition = new Vector3(0.29f, 0.02f, 0f);
            Ui.Quad("bg", _practice, new Vector3(0, 0, 0.003f), new Vector2(0.24f, 0.17f), Ui.Panel, true, rounded: true).AddComponent<PinchTarget>();
            Ui.Button("Saved words", _practice, new Vector3(0, 0.035f, 0), new Vector2(0.21f, 0.055f), Ui.Muted, () =>
            {
                AppRouter.Request(AppRouter.LensSavedWords);
                Open(HypaScenes.Lens);
            });
            Ui.Button("Hypa Story", _practice, new Vector3(0, -0.035f, 0), new Vector2(0.21f, 0.055f), Ui.Muted, () => Open(HypaScenes.Story));

            _practice.gameObject.SetActive(false);
            _menu.gameObject.SetActive(false);
        }

        /// <summary>Opens/closes the app menu (modes, practice, API test). Modes with their own launcher call this.</summary>
        public void ToggleMenu() { if (_menu != null) ShowMenu(!_menu.gameObject.activeSelf); }

        public bool IsMenuOpen => _menu != null && _menu.gameObject.activeSelf;

        /// <summary>Opening always brings the card in front of the user, over whatever the mode is showing.</summary>
        public void ShowMenu(bool open)
        {
            if (_menu == null) return;
            if (open) PlaceInFront();
            _menu.gameObject.SetActive(open);
            if (!open && _practice != null) _practice.gameObject.SetActive(false);
        }

        /// <summary>A mode with its own launcher hides the shell's Menu button (and shows it again when it closes).</summary>
        public void SetMenuButtonVisible(bool visible) { if (_menuButton != null) _menuButton.SetActive(visible); }
        void TogglePractice() => _practice.gameObject.SetActive(!_practice.gameObject.activeSelf);

        public void Open(string scene)
        {
            if (string.IsNullOrEmpty(scene) || scene == HypaScenes.Shell) scene = HypaScenes.Lens;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                if (_status != null) _status.text = scene + " isn't in Build Settings.\nRun Hypa > Sync Build Settings.";
                return;
            }
            HypaSettings.LastMode = scene;
            ShowMenu(false);
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }
    }
}
