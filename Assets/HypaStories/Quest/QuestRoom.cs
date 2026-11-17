using HypaCore.Quest;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.Android;

namespace HypaStories.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).

    /// <summary>
    /// Ticket 3K, the headset's half of "Campfire in your room" (the scan is in Mruk/QuestRoomScan): whether the real room can show
    /// through (<see cref="PassthroughKeep"/> keeps it running under the app), and the Depth API, so a real table, sofa or body in
    /// front of the fire or the storyteller hides them. Quest 3 and later only; elsewhere <see cref="StoryRoom.Depth"/> does nothing.
    /// </summary>
    public static class QuestRoom
    {
        static GameObject _depth;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            StoryRoom.Passthrough = () => PassthroughKeep.Active;
            StoryRoom.Depth = SetDepth;
        }

        static void SetDepth(bool on)
        {
            if (!on)
            {
                if (_depth != null) Object.Destroy(_depth);
                _depth = null;
                return;
            }
            if (_depth != null || !EnvironmentDepthManager.IsSupported) return; // a Quest 2 or Pro, the editor, the simulator: no depth, the fire just shows
            // the depth texture needs the same "see your room" permission as the room scan; the manager waits for it
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
                Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission);
            _depth = new GameObject("Hypa Depth");
            var manager = _depth.AddComponent<EnvironmentDepthManager>();
            manager.OcclusionShadersMode = OcclusionShadersMode.SoftOcclusion; // the flame, glow, embers and lit shaders carry the keyword
        }
    }
}
