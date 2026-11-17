using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HypaCore
{
    /// <summary>
    /// Development builds only: test on the headset from adb, without the UI or wearing it.
    ///   adb shell am start -n ai.hypa.lens/com.unity3d.player.UnityPlayerGameActivity -e hypa_open HypaStory
    ///   ... -e hypa_hide "Pine,Mist"   hide objects whose name contains any of these (find what costs the frames)
    /// and a perf line every 2 s in logcat ("Hypa perf"): CPU and GPU frame times, draw calls, triangles.
    /// Release builds ignore all of it.
    /// </summary>
    public sealed class DevTools : MonoBehaviour
    {
        static DevTools _instance;
        string[] _hide = new string[0];
        float _hideUntil, _logAt;
        readonly FrameTiming[] _timings = new FrameTiming[1];
        double _cpuMain, _cpuRender, _gpu, _frame;
        int _n;
        ProfilerRecorder _draws, _setPass, _tris, _verts, _gcAlloc;

        /// <summary>A string extra from the launching intent (Android), else null.</summary>
        public static string Extra(string key)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                    return intent.Call<string>("getStringExtra", key);
            }
            catch { return null; }
#else
            return null;
#endif
        }

        /// <summary>Starts the tools in a development build; returns the mode to open from adb, or null.</summary>
        public static string Begin(GameObject host)
        {
            if (!Debug.isDebugBuild || _instance != null) return null;
            _instance = host.AddComponent<DevTools>();
            var hide = Extra("hypa_hide");
            if (!string.IsNullOrWhiteSpace(hide))
                _instance._hide = hide.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            var open = Extra("hypa_open");
            Debug.Log($"Hypa dev: open={open ?? "-"} hide={(hide ?? "-")}");
            return string.IsNullOrWhiteSpace(open) ? null : open.Trim();
        }

        void OnEnable()
        {
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            SceneManager.sceneLoaded += OnLoaded;
        }

        void OnDisable()
        {
            _draws.Dispose(); _setPass.Dispose(); _tris.Dispose(); _verts.Dispose(); _gcAlloc.Dispose();
            SceneManager.sceneLoaded -= OnLoaded;
        }

        // Worlds are built in Start and over the first seconds: keep hiding for a while after each load.
        void OnLoaded(Scene s, LoadSceneMode m) => _hideUntil = Time.unscaledTime + 15f;

        void Hide()
        {
            if (_hide.Length == 0) return;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(false))
                        if (t.gameObject.activeSelf && _hide.Any(h => t.name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0))
                            t.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (Time.unscaledTime < _hideUntil && Time.frameCount % 15 == 0) Hide();

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                _cpuMain += _timings[0].cpuMainThreadFrameTime; _cpuRender += _timings[0].cpuRenderThreadFrameTime;
                _gpu += _timings[0].gpuFrameTime; _frame += _timings[0].cpuFrameTime; _n++;
            }
            if (Time.unscaledTime < _logAt) return;
            _logAt = Time.unscaledTime + 2f;
            var sb = new StringBuilder("Hypa perf: ");
            if (_n > 0) sb.Append($"cpu {_frame / _n:0.0} ms (main {_cpuMain / _n:0.0}, render {_cpuRender / _n:0.0}) gpu {_gpu / _n:0.0} ms · ");
            sb.Append($"fps {1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):0} · draws {_draws.LastValue} setpass {_setPass.LastValue} tris {_tris.LastValue / 1000}k verts {_verts.LastValue / 1000}k · gc {_gcAlloc.LastValue / 1024}KB/frame · ");
            var scenes = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++) scenes.Add(SceneManager.GetSceneAt(i).name);
            sb.Append(string.Join("+", scenes));
            Debug.Log(sb.ToString());
            _cpuMain = _cpuRender = _gpu = _frame = 0; _n = 0;
        }
    }
}
