using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Placeholder for Hypa Story (tickets 3A-3E). Lives in the HypaStory scene so the shell's Practice > Hypa Story
    /// entry works on day 1. The Stories team replaces this. Use HypaCore for API calls, audio (HypaAudioPlayer.Volume
    /// for lip sync), languages (HypaLanguages with HypaFeature.Tts) and settings. Never reference HypaLens.
    /// </summary>
    public sealed class StoryStub : MonoBehaviour
    {
        void Start()
        {
            PinchPointer.EnsureExists();
            var cam = Camera.main.transform;
            var root = new GameObject("Story Stub UI").transform;
            root.position = cam.position + cam.forward * 0.7f;
            root.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
            Ui.Quad("bg", root, Vector3.zero, new Vector2(0.5f, 0.22f), Ui.Panel, false);
            Ui.Text(root, "Hypa Story\n\nComing soon (tickets 3A-3E).\nTarget language: " + HypaSettings.TargetLanguage,
                0.024f, Vector3.zero, Color.white);
        }
    }
}
