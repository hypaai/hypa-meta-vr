using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HypaCore
{
    /// <summary>
    /// Where the Hypa API key and base URL come from. Nothing is baked into the repo or the APK.
    /// Order: environment variables (HYPA_API_KEY, HYPA_BASE_URL), then a git-ignored <c>hypa.local.json</c>:
    ///   Editor: project root (next to Assets/).
    ///   Quest:  Application.persistentDataPath, pushed with adb (see README). Never StreamingAssets/Resources.
    /// File shape: { "apiKey": "...", "baseUrl": "https://api.hypaintelligence.com" }
    /// The judges' build points baseUrl at the private proxy and needs no key in the app.
    /// </summary>
    public sealed class HypaConfig
    {
        public const string DefaultBaseUrl = "https://api.hypaintelligence.com";
        public const string FileName = "hypa.local.json";

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

        public static string LocalFilePath =>
            Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName)
                : Path.Combine(Application.persistentDataPath, FileName);

        public static HypaConfig Load()
        {
            string key = Environment.GetEnvironmentVariable("HYPA_API_KEY");
            string url = Environment.GetEnvironmentVariable("HYPA_BASE_URL");
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
            return new HypaConfig(key, url);
        }

        /// <summary>For tests and for switching to the proxy at runtime.</summary>
        public static void Override(HypaConfig config) => _current = config;
    }
}
