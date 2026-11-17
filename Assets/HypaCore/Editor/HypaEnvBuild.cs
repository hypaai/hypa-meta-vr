using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace HypaCore.EditorTools
{
    /// <summary>
    /// Gets your .env onto the headset with every Android build, so the app never runs without its keys (it then fell back to
    /// offline: silent locals, no speech, no wake word). The keys are NOT put in the APK (see <see cref="HypaConfig"/>): the file is
    /// copied over adb into the app's private folder, where HypaConfig reads it on the Quest.
    ///  - Before the build: checks the project's .env and says which keys it has (never their values); warns if one is missing.
    ///  - After the build: pushes .env to the connected headset now if the app is installed, and again as soon as Build And Run
    ///    (re)installs it, so a first install gets it too.
    ///  - Any time: Hypa > Push .env to Headset. From a terminal: tools/push-env.sh.
    /// </summary>
    public sealed class HypaEnvBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        static readonly string[] Keys = { "HYPA_API_KEY", "HYPA_BASE_URL", "WIT_CLIENT_TOKEN" };
        static string _installedBefore;

        static string ProjectRoot => Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        static string EnvPath => Path.Combine(ProjectRoot, HypaConfig.EnvFileName);
        static string Package => PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        static string DeviceEnv => $"/sdcard/Android/data/{Package}/files/{HypaConfig.EnvFileName}";

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            if (!File.Exists(EnvPath))
            {
                Debug.LogWarning($"Hypa env: no {HypaConfig.EnvFileName} in the project root. The headset build will run without keys " +
                                 "(offline: no speech, chat or wake word). Copy .env.example to .env and fill it in.");
                return;
            }
            var env = HypaConfig.ParseEnv(File.ReadAllText(EnvPath));
            var have = Keys.Where(k => env.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)).ToArray();
            var missing = Keys.Except(have).Where(k => k != "HYPA_BASE_URL").ToArray();
            Debug.Log($"Hypa env: .env has {string.Join(", ", have)}; it will be pushed to the headset after the build (not built into the APK).");
            if (missing.Length > 0) Debug.LogWarning("Hypa env: .env is missing " + string.Join(", ", missing) + ".");
            _installedBefore = InstalledStamp(Adb());
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android || !File.Exists(EnvPath)) return;
            var adb = Adb();
            if (adb == null) { Debug.LogWarning("Hypa env: adb not found; push .env yourself: tools/push-env.sh"); return; }
            if (!Connected(adb)) { Debug.LogWarning("Hypa env: no headset connected over USB; run Hypa > Push .env to Headset once it is."); return; }
            var before = _installedBefore;
            if (InstalledStamp(adb) != null) Push(adb, "after the build");
            // Build And Run installs the APK after this callback: wait for it (up to 5 min), then push again.
            new Thread(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    Thread.Sleep(3000);
                    var now = InstalledStamp(adb);
                    if (now != null && now != before) { Push(adb, "after the install"); return; }
                }
            }) { IsBackground = true, Name = "Hypa env push" }.Start();
        }

        [MenuItem("Hypa/Push .env to Headset")]
        static void PushMenu()
        {
            var adb = Adb();
            if (adb == null) { Debug.LogError("Hypa env: adb not found (Unity's Android SDK, ~/Library/Android/sdk, or PATH)."); return; }
            if (!File.Exists(EnvPath)) { Debug.LogError($"Hypa env: no {HypaConfig.EnvFileName} in {ProjectRoot}."); return; }
            if (!Connected(adb)) { Debug.LogError("Hypa env: no headset connected (adb devices). Plug it in and allow USB debugging."); return; }
            Push(adb, "from the menu");
        }

        static void Push(string adb, string when)
        {
            Run(adb, $"shell mkdir -p /sdcard/Android/data/{Package}/files");
            var (ok, output) = Run(adb, $"push \"{EnvPath}\" {DeviceEnv}");
            if (ok) Debug.Log($"Hypa env: pushed .env to the headset {when} ({DeviceEnv}). Restart the app if it was already running.");
            else Debug.LogWarning($"Hypa env: couldn't push .env {when}: {output.Trim()}");
        }

        /// <summary>The installed app's last update time, or null when it isn't installed (or there's no device).</summary>
        static string InstalledStamp(string adb)
        {
            if (adb == null) return null;
            var (ok, output) = Run(adb, $"shell dumpsys package {Package}");
            if (!ok) return null;
            var line = output.Split('\n').FirstOrDefault(l => l.Trim().StartsWith("lastUpdateTime="));
            return line?.Trim();
        }

        static bool Connected(string adb)
        {
            var (ok, output) = Run(adb, "devices");
            return ok && output.Split('\n').Skip(1).Any(l => l.Trim().EndsWith("device"));
        }

        /// <summary>Unity's own Android SDK first, then the usual SDK folder, then PATH.</summary>
        static string Adb()
        {
            var candidates = new[]
            {
                SafeSdkRoot(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Android/sdk"),
                Environment.GetEnvironmentVariable("ANDROID_HOME"),
            };
            foreach (var root in candidates.Where(r => !string.IsNullOrEmpty(r)))
            {
                var p = Path.Combine(root, "platform-tools", "adb");
                if (File.Exists(p)) return p;
            }
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Concat(new[] { "/opt/homebrew/bin", "/usr/local/bin" }))
            {
                var p = Path.Combine(dir, "adb");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static string SafeSdkRoot()
        {
            try { return UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath; } catch { return null; }
        }

        /// <summary>
        /// Runs adb with a timeout. Output is read asynchronously: the first adb call can start adb's background server, which
        /// inherits the output pipes, so reading them to the end would never return (it hung the editor).
        /// </summary>
        static (bool ok, string output) Run(string exe, string args, int timeoutMs = 20000)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                using (var p = new Process { StartInfo = new ProcessStartInfo(exe, args)
                       { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } })
                {
                    p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (false, "timed out: adb " + args); }
                    lock (sb) return (p.ExitCode == 0, sb.ToString());
                }
            }
            catch (Exception e) { return (false, e.Message); }
        }
    }
}
