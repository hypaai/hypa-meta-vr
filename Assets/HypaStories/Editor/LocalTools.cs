using System.IO;
using UnityEditor;
using UnityEngine;

namespace HypaStories.EditorTools
{
    /// <summary>
    /// Locals (ticket 3I) in the editor: the test persona (built from code, like the scenes) and the Local Brain setting, one
    /// menu item per brain. The setting is the same one the app reads (<see cref="LocalBrains.Model"/>), so switching it here
    /// changes which model the locals use, with no code change.
    /// </summary>
    public static class LocalTools
    {
        public const string TestPersonaPath = "Assets/HypaStories/Locals/Ama.asset";

        [MenuItem("Hypa/Stories/Create Test Local")]
        public static LocalPersona CreateTestPersona()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TestPersonaPath));
            var p = AssetDatabase.LoadAssetAtPath<LocalPersona>(TestPersonaPath);
            if (p == null) { p = ScriptableObject.CreateInstance<LocalPersona>(); AssetDatabase.CreateAsset(p, TestPersonaPath); }
            p.DisplayName = "Ama";
            p.Role = "weaver";
            p.Home = "a round hut of mud and thatch at the edge of Kiko's village, where the forest begins";
            p.Knows = "You weave cloth and baskets on a wooden loom your grandmother built, with cotton you spin and dye with indigo and " +
                      "bark. You have two sons who herd goats, and you trade cloth at the market by the old baobab tree. Everyone knows Kiko, the " +
                      "tortoise who asks too many questions; he often sits by your loom. The elders tell of the Remembering Fire, deep in the " +
                      "forest, which never goes out and keeps the memories of everyone who ever lived. You have seen its glow but never gone near.";
            p.DoesNotKnow = "Cities, machines, phones, money from far away, anything outside the village and the forest; what happens at " +
                            "the end of Kiko's story.";
            p.NativeLanguage = "Yoruba";
            p.Voice = "coral"; // a warm voice of openai/gpt-4o-mini-tts (the Local's speech model)
            p.Greeting = "Welcome, traveller! Come and sit by my loom. What would you like to know?";
            p.Suggested = new[] { "What are you weaving?", "Do you know Kiko?", "What is the Remembering Fire?" };
            p.Canned = new[]
            {
                new CannedAnswer { Keywords = "weave, weaving, loom, cloth, basket, making, make", Answer = "I am weaving a blanket for the cold season. My grandmother built this loom, and her hands taught mine." },
                new CannedAnswer { Keywords = "kiko, tortoise", Answer = "Kiko? That curious tortoise sits by my loom every morning. He asks so many questions that the goats run away!" },
                new CannedAnswer { Keywords = "fire, remembering, memory, memories, forest", Answer = "The Remembering Fire burns deep in the forest and never goes out. The elders say it keeps the memories of everyone who ever lived." },
                new CannedAnswer { Keywords = "family, sons, children, husband", Answer = "I have two sons. They herd our goats on the hill and come home hungry every evening." },
                new CannedAnswer { Keywords = "market, baobab, sell, trade", Answer = "On market days I carry my cloth to the old baobab tree. People come from three villages to trade there." },
                new CannedAnswer { Keywords = "name, who are you", Answer = "I am Ama, the weaver. Everyone in the village knows the sound of my loom." },
            };
            p.Fallback = "Hmm, that I would not know. Ask me about my weaving, my village or the forest.";
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return p;
        }

        // ---------- Local Brain setting ----------

        const string Menu = "Hypa/Stories/Local Brain/";

        [MenuItem(Menu + "Muse (Meta, default)")] static void Muse() => Pick(LocalBrains.Muse);
        [MenuItem(Menu + "Muse (Meta, default)", true)] static bool MuseOn() => Check(Menu + "Muse (Meta, default)", LocalBrains.Muse);
        [MenuItem(Menu + "OpenAI")] static void OpenAi() => Pick(LocalBrains.OpenAi);
        [MenuItem(Menu + "OpenAI", true)] static bool OpenAiOn() => Check(Menu + "OpenAI", LocalBrains.OpenAi);
        [MenuItem(Menu + "Gemini (Google)")] static void Gemini() => Pick(LocalBrains.Gemini);
        [MenuItem(Menu + "Gemini (Google)", true)] static bool GeminiOn() => Check(Menu + "Gemini (Google)", LocalBrains.Gemini);
        [MenuItem(Menu + "Hypa")] static void Hypa() => Pick(LocalBrains.Hypa);
        [MenuItem(Menu + "Hypa", true)] static bool HypaOn() => Check(Menu + "Hypa", LocalBrains.Hypa);
        [MenuItem(Menu + "Offline answers")] static void Stub() => Pick(LocalBrains.Stub);
        [MenuItem(Menu + "Offline answers", true)] static bool StubOn() => Check(Menu + "Offline answers", LocalBrains.Stub);

        static void Pick(string id)
        {
            LocalBrains.Model = id;
            Debug.Log($"Hypa Stories: locals now use {id}. New locals pick it up (enter Play mode again for ones already talking).");
        }

        static bool Check(string item, string id)
        {
            UnityEditor.Menu.SetChecked(item, LocalBrains.Model == id);
            return true;
        }
    }
}
