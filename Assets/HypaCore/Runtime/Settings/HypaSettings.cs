using UnityEngine;

namespace HypaCore
{
    /// <summary>Saved settings shared by every mode: target language and the last mode used.</summary>
    public static class HypaSettings
    {
        const string TargetKey = "hypa.targetLanguage", ModeKey = "hypa.lastMode";

        /// <summary>A Hypa language label, e.g. "Yoruba". Default English.</summary>
        public static string TargetLanguage
        {
            get => PlayerPrefs.GetString(TargetKey, "English");
            set { PlayerPrefs.SetString(TargetKey, value ?? "English"); PlayerPrefs.Save(); }
        }

        /// <summary>Scene name of the last mode opened from the hand menu (see <see cref="HypaScenes"/>).</summary>
        public static string LastMode
        {
            get => PlayerPrefs.GetString(ModeKey, HypaScenes.Lens);
            set { PlayerPrefs.SetString(ModeKey, value); PlayerPrefs.Save(); }
        }
    }
}
