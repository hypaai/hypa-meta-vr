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

        Transform _cam, _menu, _practice;
        GameObject _menuButton;
        TextMesh _status;
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
        }

        void BuildMenuButton()
        {
            var anchor = new GameObject("Hand Menu").transform;
            anchor.SetParent(_cam, false);
            anchor.localPosition = new Vector3(-0.30f, -0.22f, 0.55f); // low-left, inside reach and a narrow FOV
            _menuButton = Ui.Button("Menu", anchor, Vector3.zero, new Vector2(0.10f, 0.05f), Ui.Muted, ToggleMenu).transform.parent.gameObject;
            _menu = new GameObject("Menu Panel").transform;
            _menu.SetParent(anchor, false);
            _menu.localPosition = new Vector3(0.12f, 0.17f, 0f);
            Ui.Quad("bg", _menu, Vector3.zero, new Vector2(0.26f, 0.30f), Ui.Panel, false, rounded: true);
            var logo = Resources.Load<Texture2D>(LogoResource);
            if (logo != null)
            {
                var icon = Ui.Quad("logo", _menu, new Vector3(-0.072f, 0.125f, -0.001f), Vector2.one * 0.03f, Color.white, false);
                icon.GetComponent<Renderer>().material.mainTexture = logo;
            }
            Ui.Text(_menu, "Hypa", 0.02f, new Vector3(0.01f, 0.125f, 0), Color.white, TextAnchor.MiddleLeft);
            Ui.Button("Hypa Lens", _menu, new Vector3(0, 0.055f, 0), new Vector2(0.22f, 0.06f), Ui.Accent, () => Open(HypaScenes.Lens));
            Ui.Button("Practice", _menu, new Vector3(0, -0.015f, 0), new Vector2(0.22f, 0.06f), Ui.Muted, TogglePractice);
            Ui.Button("API test", _menu, new Vector3(0, -0.085f, 0), new Vector2(0.22f, 0.05f), Ui.Muted, () => Open(HypaScenes.CoreTest));
            _status = Ui.Text(_menu, "", 0.016f, new Vector3(0, -0.135f, 0), Color.white);

            _practice = new GameObject("Practice Panel").transform;
            _practice.SetParent(_menu, false);
            _practice.localPosition = new Vector3(0.25f, 0.01f, 0f);
            Ui.Quad("bg", _practice, Vector3.zero, new Vector2(0.24f, 0.17f), Ui.Panel, false, rounded: true);
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
        public void ToggleMenu() { if (_menu != null) _menu.gameObject.SetActive(!_menu.gameObject.activeSelf); }

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
            if (_menu != null) { _menu.gameObject.SetActive(false); _practice.gameObject.SetActive(false); }
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }
    }
}
