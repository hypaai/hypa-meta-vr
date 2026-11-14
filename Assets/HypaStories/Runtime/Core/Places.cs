using System;
using System.Collections;
using System.Collections.Generic;
using HypaCore;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HypaStories
{
    /// <summary>The places you can visit (ticket 3F). Each is its own scene, built from code by a menu item.</summary>
    public static class PlaceIds
    {
        public const string Camp = "camp", Hut = "hut", Gathering = "gathering";
    }

    /// <summary>
    /// Goes from place to place (ticket 3F): <c>Places.GoTo("hut")</c> fades through the warm veil, loads the hut's scene,
    /// puts you on its <see cref="PlaceEntry"/> (moving the world, never the camera) and fades back in.
    ///
    /// The camp is home: it stays loaded but hidden while you're away (it hears <see cref="Leaving"/> / <see cref="Arrived"/>),
    /// so the campfire story pauses when you leave and picks up when you come back. Other places load additively and
    /// unload when you leave them. Works under HypaShell and when a place scene is played on its own: there is only ever
    /// one camera rig and one audio listener (see <c>StandaloneOnly</c>), and if the shell unloads the camp, any place
    /// scenes go with it. Editor: 1 = camp, 2 = hut, 3 = gathering (unless the current place uses those keys).
    /// </summary>
    public static class Places
    {
        static readonly Dictionary<string, string> SceneById = new Dictionary<string, string>
        {
            { PlaceIds.Camp, HypaScenes.Story },
            { PlaceIds.Hut, HypaScenes.StoryHut },
            { PlaceIds.Gathering, HypaScenes.StoryGathering },
        };

        /// <summary>All place ids, in menu order.</summary>
        public static readonly string[] All = { PlaceIds.Camp, PlaceIds.Hut, PlaceIds.Gathering };

        /// <summary>The scene a place lives in, or null for an unknown id. Pure, unit tested.</summary>
        public static string SceneFor(string placeId) => placeId != null && SceneById.TryGetValue(placeId, out var s) ? s : null;

        /// <summary>The place a scene is, or null if it isn't one. Pure, unit tested.</summary>
        public static string IdForScene(string sceneName)
        {
            foreach (var kv in SceneById) if (kv.Value == sceneName) return kv.Key;
            return null;
        }

        /// <summary>Where you are: the last place moved to, else the place the active scene is (the camp if neither).</summary>
        public static string Current => _current ?? IdForScene(SceneManager.GetActiveScene().name) ?? PlaceIds.Camp;
        internal static string _current;

        /// <summary>True while a move is under way (the veil is up). A second GoTo is ignored until it finishes.</summary>
        public static bool IsMoving { get; internal set; }

        /// <summary>Fired with the place being left, once the veil is fully dark (pause, hide, stop listening).</summary>
        public static event Action<string> Leaving;
        /// <summary>Fired with the place arrived at, before the veil lifts (show, recentre, resume).</summary>
        public static event Action<string> Arrived;
        internal static void RaiseLeaving(string id) => Leaving?.Invoke(id);
        internal static void RaiseArrived(string id) => Arrived?.Invoke(id);

        /// <summary>A place that uses the 1-3 keys itself (the campfire's suggested questions) says so here.</summary>
        public static Func<bool> NumberKeysTaken;

        /// <summary>Starts the place runner (the veil and the editor keys). Places call it on start; GoTo does too.</summary>
        public static void EnsureRunner() { var _ = PlaceRunner.Instance; }

        /// <summary>Goes to a place with the fade. Unknown ids, the current place, or a move already under way: ignored.</summary>
        public static void GoTo(string placeId)
        {
            if (IsMoving || SceneFor(placeId) == null || placeId == Current) return;
            PlaceRunner.Instance.Begin(placeId);
        }

        /// <summary>
        /// Moves a root of a place scene so the place's entry (position <paramref name="entryPos"/>, facing <paramref name="entryYaw"/>)
        /// lands under the viewer's head, on the floor, facing the way the head faces. Returns the root's new pose. Pure, unit tested.
        /// </summary>
        public static Pose Align(Vector3 entryPos, float entryYaw, Vector3 head, float headYaw, float floorY, Pose root)
        {
            var turn = Quaternion.Euler(0f, headYaw - entryYaw, 0f);
            var target = new Vector3(head.x, floorY, head.z);
            return new Pose(turn * (root.position - entryPos) + target, turn * root.rotation);
        }

        /// <summary>Yaw of a direction, degrees (0 = +Z). Pure.</summary>
        public static float YawOf(Vector3 forward) => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    /// <summary>Runs the moves: the veil, scene loading and unloading, and placing the viewer. One per app, created on demand.</summary>
    sealed class PlaceRunner : MonoBehaviour
    {
        const float FadeOut = 0.45f, FadeIn = 0.6f, SeatedEyeHeight = 1.2f;
        static readonly Color VeilColor = new Color(0.024f, 0.016f, 0.012f); // the campfire's warm dark veil

        static PlaceRunner _instance;
        public static PlaceRunner Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("Hypa Places");
                DontDestroyOnLoad(go);
                return _instance = go.AddComponent<PlaceRunner>();
            }
        }

        Renderer _veil;
        Material _veilMat;
        Color? _campBackground; // the camp's clear colour, restored when you come back

        void Awake()
        {
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (_veil != null) Destroy(_veil.gameObject);
        }

        public void Begin(string target) => StartCoroutine(Move(target));

        IEnumerator Move(string target)
        {
            Places.IsMoving = true;
            var from = Places.Current;
            for (var t = 0f; t < FadeOut; t += Time.unscaledDeltaTime) { SetVeil(t / FadeOut); yield return null; }
            SetVeil(1f);
            Places.RaiseLeaving(from);

            var targetScene = Places.SceneFor(target);
            if (!Application.CanStreamedLevelBeLoaded(targetScene))
            {
                Debug.LogError($"Hypa Places: scene '{targetScene}' isn't in Build Settings. Run Hypa > Stories > Create Place Scenes, then Hypa > Sync Build Settings.");
                Places.RaiseArrived(from); // stay where you were
                yield return Lift();
                yield break;
            }

            // Arrive first: Unity can't unload the last loaded scene (a place played on its own is the only one).
            var scene = SceneManager.GetSceneByName(targetScene);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                yield return SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Additive);
                scene = SceneManager.GetSceneByName(targetScene);
            }
            if (scene.IsValid()) SceneManager.SetActiveScene(scene); // its lighting and fog

            // Then leave: the camp stays loaded (hidden by StoryApp); any other place is unloaded.
            if (from != PlaceIds.Camp && from != target)
            {
                var old = SceneManager.GetSceneByName(Places.SceneFor(from));
                if (old.IsValid() && old.isLoaded)
                {
                    KeepRig(old); // played on its own, this place's scene holds the only camera rig
                    var unload = SceneManager.UnloadSceneAsync(old);
                    if (unload != null) yield return unload;
                    else Debug.LogWarning($"Hypa Places: couldn't unload {old.name}.");
                }
            }

            var cam = Camera.main;
            if (from == PlaceIds.Camp && cam != null && _campBackground == null) _campBackground = cam.backgroundColor;
            if (target != PlaceIds.Camp)
            {
                var entry = PlaceViewer(scene); // the camp places itself (StoryApp.Recenter)
                if (entry != null && cam != null) cam.backgroundColor = entry.Background;
            }
            else if (cam != null)
            {
                // Back at the camp. Started at another place (its scene played on its own), the camp's colour was never seen:
                // use the one the campfire scene is built with.
                cam.backgroundColor = _campBackground ?? CampWorld.FogColor;
                _campBackground = null;
            }
            Places._current = target;
            Places.RaiseArrived(target);
            yield return null; // let the place settle (recentre, panels snap) under the veil
            yield return Lift();
        }

        IEnumerator Lift()
        {
            for (var t = 0f; t < FadeIn; t += Time.unscaledDeltaTime) { SetVeil(1f - t / FadeIn); yield return null; }
            SetVeil(0f);
            Places.IsMoving = false;
        }

        /// <summary>Puts the viewer on the scene's PlaceEntry by moving the scene's roots (not the camera, not a rig).</summary>
        static PlaceEntry PlaceViewer(Scene scene)
        {
            var cam = Camera.main;
            if (cam == null || !scene.IsValid()) return null;
            PlaceEntry entry = null;
            foreach (var root in scene.GetRootGameObjects())
                if (entry == null) entry = root.GetComponentInChildren<PlaceEntry>(true);
            if (entry == null) { Debug.LogWarning($"Hypa Places: {scene.name} has no PlaceEntry; you arrive at its origin."); return null; }

            var head = cam.transform.position;
            var fwd = cam.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            var floor = FloorY(cam.transform);
            var entryPos = entry.transform.position;
            var entryYaw = Places.YawOf(entry.Forward);
            var headYaw = Places.YawOf(fwd);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<Camera>(true) != null) continue; // a rig, if any: the camera never moves
                var p = Places.Align(entryPos, entryYaw, head, headYaw, floor, new Pose(root.transform.position, root.transform.rotation));
                root.transform.SetPositionAndRotation(p.position, p.rotation);
            }
            return entry;
        }

        /// <summary>The floor under the viewer: the rig's origin with floor-level tracking, else an assumed seated eye height.</summary>
        static float FloorY(Transform cam)
        {
            var floor = cam.parent != null ? cam.root.position.y : cam.position.y - SeatedEyeHeight;
            if (cam.position.y - floor < 0.6f) floor = cam.position.y - SeatedEyeHeight; // eye-level tracking origin
            return floor;
        }

        /// <summary>If the camera's rig lives in a scene about to be unloaded, keep it (and the veil on it) alive.</summary>
        static void KeepRig(Scene scene)
        {
            var cam = Camera.main;
            if (cam != null && cam.gameObject.scene == scene) DontDestroyOnLoad(cam.transform.root.gameObject);
        }

        /// <summary>The shell unloaded the camp (Home, or another mode): take any place scenes with it.</summary>
        void OnSceneUnloaded(Scene scene)
        {
            if (scene.name != HypaScenes.Story || Places.IsMoving) return;
            foreach (var id in Places.All)
            {
                if (id == PlaceIds.Camp) continue;
                var s = SceneManager.GetSceneByName(Places.SceneFor(id));
                if (s.IsValid() && s.isLoaded) SceneManager.UnloadSceneAsync(s);
            }
            Places._current = null;
        }

        // ---------- the veil: a dark quad just in front of the eyes ----------

        void SetVeil(float a)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (_veil == null || _veil.transform.parent != cam.transform) BuildVeil(cam.transform);
            _veilMat.color = new Color(VeilColor.r, VeilColor.g, VeilColor.b, Mathf.Clamp01(a));
            _veil.enabled = a > 0.001f;
        }

        void BuildVeil(Transform cam)
        {
            if (_veil != null) Destroy(_veil.gameObject);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Places Veil";
            Destroy(go.GetComponent<Collider>()); // never blocks a pinch ray
            go.transform.SetParent(cam, false);
            go.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(0.06f, cam.GetComponent<Camera>().nearClipPlane + 0.02f));
            go.transform.localScale = Vector3.one * 1.2f;
            _veil = go.GetComponent<Renderer>();
            _veilMat = Ui.NewMaterial(VeilColor);
            _veilMat.renderQueue = 4000; // over everything, UI included
            _veil.sharedMaterial = _veilMat;
            _veil.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _veil.receiveShadows = false;
        }

        // ---------- editor keys ----------

        void Update()
        {
            if (!Application.isEditor || Places.IsMoving) return;
            if (Places.NumberKeysTaken != null && Places.NumberKeysTaken()) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Places.GoTo(PlaceIds.Camp);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Places.GoTo(PlaceIds.Hut);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Places.GoTo(PlaceIds.Gathering);
        }

        /// <summary>Makes sure the runner exists in any scene that has places, so the editor keys work from the start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Places.IdForScene(SceneManager.GetActiveScene().name) != null) { var _ = Instance; }
        }
    }
}
