using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The font for what you say and what Hypa answers in Try Hypa. The story fonts (Outfit, Cormorant Garamond) are static atlases
    /// of Latin-1, which has no Yoruba tone marks, Igbo dots, Hausa hooks, Arabic or Chinese. This one is built from the device's own
    /// fonts (Roboto and the Noto family on a Quest, the system fonts in the editor), filled in glyph by glyph as text arrives, with
    /// one fallback per script. Fonts a device doesn't have are skipped; with none at all <see cref="Content"/> is null and the
    /// UI font is used (Latin-1 text still works).
    /// </summary>
    public static class TryFonts
    {
        static readonly string[] Primary = { "Roboto", "Noto Sans", "Helvetica Neue", "Arial", "Segoe UI" };
        static readonly string[] Scripts =
        {
            "Noto Sans Arabic", "Geeza Pro", "Noto Sans CJK SC", "PingFang SC", "Noto Sans Devanagari", "Kohinoor Devanagari",
            "Noto Sans Ethiopic", "Kefa", "Noto Sans Hebrew", "Arial Hebrew", "Noto Sans Thai", "Thonburi", "Arial Unicode MS",
        };

        static TMP_FontAsset _content;
        static bool _built;

        /// <summary>The device-font text face, or null if none could be made.</summary>
        public static TMP_FontAsset Content
        {
            get
            {
                if (_built) return _content;
                _built = true;
                try { _content = Build(); }
                catch (System.Exception e) { Debug.LogWarning("Hypa Stories: device fonts unavailable, using the story font: " + e.Message); }
                return _content;
            }
        }

        static TMP_FontAsset Build()
        {
            TMP_FontAsset main = null;
            foreach (var n in Primary) { main = Make(n); if (main != null) break; }
            var fallbacks = new List<TMP_FontAsset>();
            var scripts = 0;
            foreach (var n in Scripts)
            {
                if (scripts >= 6) break; // each is an atlas texture; a device has few of these anyway
                var f = Make(n);
                if (f != null) { fallbacks.Add(f); scripts++; }
            }
            if (main == null)
            {
                if (fallbacks.Count == 0) return null;
                main = fallbacks[0];
                fallbacks.RemoveAt(0);
            }
            if (main.fallbackFontAssetTable == null) return main;
            foreach (var f in fallbacks) if (f != main && !main.fallbackFontAssetTable.Contains(f)) main.fallbackFontAssetTable.Add(f);
            return main;
        }

        static TMP_FontAsset Make(string family)
        {
            try
            {
                var fa = TMP_FontAsset.CreateFontAsset(family, "Regular", 56);
                if (fa == null) return null;
                var mobile = Shader.Find("TextMeshPro/Mobile/Distance Field");
                if (mobile != null && fa.material != null) fa.material.shader = mobile; // same cheap shader as the story fonts
                fa.name = "Try Hypa " + family;
                return fa;
            }
            catch (System.Exception) { return null; } // not installed
        }
    }
}
