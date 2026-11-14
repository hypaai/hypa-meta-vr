using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HypaLens
{
    /// <summary>
    /// Saved words/sentences as JSON in persistentDataPath, so they survive an app restart.
    /// Stores only the selected text and its translation, never images or audio links (they expire).
    /// </summary>
    public sealed class SavedListStore
    {
        [Serializable] sealed class Wrapper { public List<SavedItem> Items = new List<SavedItem>(); }

        readonly string _path;
        readonly List<SavedItem> _items = new List<SavedItem>();
        public IReadOnlyList<SavedItem> Items => _items;
        public event Action Changed;

        public SavedListStore(string path = null)
        {
            _path = path ?? Path.Combine(Application.persistentDataPath, "hypalens_saved.json");
            Load();
        }

        public bool Contains(string source, string targetLanguage)
            => _items.Exists(i => i.Source == source && i.TargetLanguage == targetLanguage);

        /// <summary>False if the same text and target language are already saved.</summary>
        public bool Add(LensResult r)
        {
            if (Contains(r.Selection.Text, r.TargetLanguage)) return false;
            _items.Add(new SavedItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Source = r.Selection.Text, Translation = r.Translation,
                SourceLanguage = r.SourceLanguage, TargetLanguage = r.TargetLanguage,
                Meaning = r.Meaning, SavedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            Save(); Changed?.Invoke();
            return true;
        }

        public void Remove(string id)
        {
            if (_items.RemoveAll(i => i.Id == id) > 0) { Save(); Changed?.Invoke(); }
        }

        void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var w = JsonUtility.FromJson<Wrapper>(File.ReadAllText(_path));
                if (w?.Items != null) _items.AddRange(w.Items);
            }
            catch (Exception e) { Debug.LogWarning("HypaLens: could not read saved list, starting empty. " + e.Message); }
        }

        void Save()
        {
            try
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(new Wrapper { Items = _items }));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path); // write then swap, so a crash never corrupts the list
            }
            catch (Exception e) { Debug.LogWarning("HypaLens: could not save list. " + e.Message); }
        }
    }

    /// <summary>Lens-only preferences. The target language lives in HypaCore.HypaSettings (shared by all modes).</summary>
    public static class LensPrefs
    {
        const string SourceKey = "hypalens.source", OnboardedKey = "hypalens.onboarded";
        /// <summary>Hypa language label, or empty = auto-detect.</summary>
        public static string Source { get => PlayerPrefs.GetString(SourceKey, ""); set { PlayerPrefs.SetString(SourceKey, value ?? ""); PlayerPrefs.Save(); } }
        public static bool Onboarded { get => PlayerPrefs.GetInt(OnboardedKey, 0) == 1; set { PlayerPrefs.SetInt(OnboardedKey, value ? 1 : 0); PlayerPrefs.Save(); } }
    }

    /// <summary>Flip-card practice state. Pure logic.</summary>
    public sealed class PracticeDeck
    {
        readonly IReadOnlyList<SavedItem> _items;
        public int Index { get; private set; }
        public bool Flipped { get; private set; }
        public PracticeDeck(IReadOnlyList<SavedItem> items, int startIndex = 0)
        {
            _items = items;
            Index = items.Count == 0 ? 0 : Mathf.Clamp(startIndex, 0, items.Count - 1);
        }
        /// <summary>Which side is showing: false = the original text, true = the translation.</summary>
        public void Show(bool translation) { Flipped = translation; }
        public bool IsEmpty => _items.Count == 0;
        public SavedItem Current => IsEmpty ? null : _items[Index % _items.Count];
        public string Face => IsEmpty ? "" : (Flipped ? Current.Translation : Current.Source);
        public string FaceLanguage => IsEmpty ? null : (Flipped ? Current.TargetLanguage : Current.SourceLanguage);
        public void Flip() { Flipped = !Flipped; }
        public int Count => _items.Count;
        // Moving keeps the side the user chose (the tab), so they can read through all the translations.
        public void Next() { if (IsEmpty) return; Index = (Index + 1) % _items.Count; }
        public void Prev() { if (IsEmpty) return; Index = (Index - 1 + _items.Count) % _items.Count; }
    }
}
