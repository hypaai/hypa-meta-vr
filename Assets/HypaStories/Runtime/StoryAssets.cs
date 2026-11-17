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
        [Tooltip("The doorway of light that opens in your room (3L).")]
        public Shader portal;
        [Tooltip("The same doorway while you are in a place: an opening onto your real room (3L).")]
        public Shader portalHole;
        [Tooltip("Meta's depth-occlusion surface shader (Core SDK): the storyteller and the fire's stones use it in the room, so a real table or body in front hides them.")]
        public Shader depthLit;
        [Tooltip("Dust kicked up by the hut's broom (a ParticleSystem): soft, alpha blended, faded by vertex colour.")]
        public Shader dust;

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

        [Header("Meta XR Interaction SDK (the hut's household items can be picked up)")]
        [Tooltip("OVRInteraction (Interaction SDK): the camera rig reference the grab interactors read from.")]
        public GameObject grabInteraction;
        [Tooltip("OVRHandsDataSource: tracked hands for the Interaction SDK.")]
        public GameObject grabHands;
        [Tooltip("OVRControllersDataSource: controllers for the Interaction SDK.")]
        public GameObject grabControllers;
        [Tooltip("HandGrabInteractor: one per hand (pinch or palm grab).")]
        public GameObject grabHandInteractor;
        [Tooltip("ControllerGrabInteractor: one per controller (grip button).")]
        public GameObject grabControllerInteractor;
        [Tooltip("DistanceHandGrabInteractor: point a hand at a thing and pinch, and it comes to your hand.")]
        public GameObject grabDistanceHandInteractor;
        [Tooltip("ControllerDistanceGrabInteractor: point a controller at a thing and press the grip.")]
        public GameObject grabDistanceControllerInteractor;

        [Header("Audio")]
        [Tooltip("The witch song played after the drum game, to drum along with (Art/Audio/witch-song.mp3). The camp beds stay in the git-ignored Local folder.")]
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
