using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Everything the story scene loads, referenced from one asset so it ends up in the build. Filled by
    /// Hypa > Stories > Create Story Scene (StorySceneBuilder). Any field may be empty: the scene falls back to procedural
    /// stand-ins (no models: primitive rocks and logs; no recorded beds: synthesised wind and fire; no narration: TTS).
    /// The recorded beds and the storyteller avatar live in the git-ignored Assets/HypaStories/Local folder (licences).
    /// The witch song after the drum game is under Art/Audio.
    /// </summary>
    public sealed class StoryAssets : ScriptableObject
    {
        [Header("Shaders (Assets/HypaStories/Shaders)")]
        public Shader sky;
        public Shader flame;
        public Shader particles;
        public Shader rain;
        public Shader mist;
        public Shader additive;
        public Shader card;
        public Shader cutout;
        public Shader veil;
        public Shader pine;
        public Shader uiPanel;
        public Shader uiIcon;
        [Tooltip("Meta's depth-occlusion surface shader (Core SDK): the storyteller and the fire's stones use it in the room, so a real table or body in front hides them.")]
        public Shader depthLit;

        [Header("Materials")]
        [Tooltip("Standard material used as the template for runtime lit materials (keeps the Standard shader in the build).")]
        public Material lit;
        public Material ground;
        [Tooltip("Bark for the bonfire's logs.")]
        public Material log;

        [Header("Models (glTF, imported by glTFast)")]
        public GameObject campFire;
        public GameObject storyteller;
        [Tooltip("Dodo, the creature in the Seven Hot Stones game (Quaternius' Demon, CC0), with its animations (legacy clips).")]
        public GameObject dodo;
        public GameObject deadTrunk;
        public GameObject stump;
        public GameObject fern;
        public GameObject grass;
        public GameObject[] rocks = new GameObject[0];

        [Header("Audio")]
        [Tooltip("The witch song played after the drum game, to drum along with (witch-song.mp3).")]
        public AudioClip witchSong;
        public AudioClip ambienceSplash;
        public AudioClip ambienceStory;
        public AudioClip fireFar;
        public AudioClip fireNear;
        public AudioClip wolf;

        [Header("Brand and UI")]
        public Texture2D logo;
        [Tooltip("Outfit (UI) and Cormorant Garamond (story text), as TextMeshPro font assets.")]
        public TMPro.TMP_FontAsset uiFont;
        public TMPro.TMP_FontAsset uiFontBold;
        public TMPro.TMP_FontAsset serifFont;
        public TMPro.TMP_FontAsset serifItalic;
        [Tooltip("Lucide icons (Art/Icons), white on transparent. Looked up by file name.")]
        public Texture2D[] icons = new Texture2D[0];

        public Texture2D Icon(string name)
        {
            foreach (var t in icons) if (t != null && t.name == name) return t;
            return null;
        }
    }
}
