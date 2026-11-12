using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HypaCore
{
    /// <summary>
    /// Where the Hypa API key and base URL come from. Nothing is baked into the repo or the APK.
    /// Order: environment variables (HYPA_API_KEY, HYPA_BASE_URL), then a git-ignored <c>.env</c>
    /// (copy <c>.env.example</c>), then a git-ignored <c>hypa.local.json</c>. Both files live in:
    ///   Editor: project root (next to Assets/).
    ///   Quest:  Application.persistentDataPath, pushed with adb (see README). Never StreamingAssets/Resources.
    /// .env shape: HYPA_API_KEY=... and HYPA_BASE_URL=...
    /// json shape: { "apiKey": "...", "baseUrl": "https://api.hypaintelligence.com" }
    /// The judges' build points baseUrl at the private proxy and needs no key in the app.
    /// </summary>
    public sealed class HypaConfig
    {
        public const string DefaultBaseUrl = "https://api.hypaintelligence.com";
        public const string FileName = "hypa.local.json";
        public const string EnvFileName = ".env";

        public string ApiKey { get; }
        public string BaseUrl { get; }
        public bool HasKey => !string.IsNullOrEmpty(ApiKey);

        public HypaConfig(string apiKey, string baseUrl = null)
        {
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
        }

        static HypaConfig _current;
        /// <summary>Loaded once on first use (main thread: touches Application paths).</summary>
        public static HypaConfig Current => _current ?? (_current = Load());

        static string ConfigDir =>
            Application.isEditor ? Directory.GetParent(Application.dataPath).FullName : Application.persistentDataPath;

        public static string LocalFilePath => Path.Combine(ConfigDir, FileName);
        public static string EnvFilePath => Path.Combine(ConfigDir, EnvFileName);

        public static HypaConfig Load()
        {
            string key = Environment.GetEnvironmentVariable("HYPA_API_KEY");
            string url = Environment.GetEnvironmentVariable("HYPA_BASE_URL");
            try
            {
                var path = EnvFilePath;
                if (File.Exists(path))
                {
                    var env = ParseEnv(File.ReadAllText(path));
                    if (string.IsNullOrEmpty(key)) env.TryGetValue("HYPA_API_KEY", out key);
                    if (string.IsNullOrEmpty(url)) env.TryGetValue("HYPA_BASE_URL", out url);
                }
            }
            catch (Exception e) { Debug.LogWarning("Hypa: could not read " + EnvFileName + ": " + e.Message); } // never log the key
            try
            {
                var path = LocalFilePath;
                if (File.Exists(path))
                {
                    var o = JObject.Parse(File.ReadAllText(path));
                    key = string.IsNullOrEmpty(key) ? (string)o["apiKey"] : key;
                    url = string.IsNullOrEmpty(url) ? (string)o["baseUrl"] : url;
                }
            }
            catch (Exception e) { Debug.LogWarning("Hypa: could not read " + FileName + ": " + e.Message); } // never log the key
            var cfg = new HypaConfig(key, url);
            // Says which server and whether a key was found, never the key itself.
            Debug.Log($"Hypa: API {cfg.BaseUrl} | key {(cfg.HasKey ? "found" : "missing (free tools only)")}");
            return cfg;
        }

        /// <summary>Minimal dotenv parser: KEY=VALUE lines, # comments, optional "export ", optional quotes.</summary>
        public static Dictionary<string, string> ParseEnv(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("export ")) line = line.Substring(7).TrimStart();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var k = line.Substring(0, eq).Trim();
                var v = line.Substring(eq + 1).Trim();
                if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[v.Length - 1] == v[0]) v = v.Substring(1, v.Length - 2);
                else { int hash = v.IndexOf(" #", StringComparison.Ordinal); if (hash >= 0) v = v.Substring(0, hash).TrimEnd(); }
                d[k] = v;
            }
            return d;
        }

        /// <summary>For tests and for switching to the proxy at runtime.</summary>
        public static void Override(HypaConfig config) => _current = config;
    }
}
