using System;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Picking things up. HypaStories.Quest sets <see cref="Make"/> on device (Meta XR Interaction SDK: hands pinch or close
    /// round a thing, controllers use the grip), so this assembly never references the SDK. Elsewhere it stays null and
    /// things just sit where they are.
    /// </summary>
    public static class StoryGrab
    {
        /// <summary>
        /// Makes a rigidbody (with its colliders) something you can pick up, look at and put down. Returns a probe giving how many
        /// hands (or controllers) hold it now (0 = none), or null when it couldn't be set up. <see cref="StoryAssets"/> carries the
        /// SDK's rig prefabs. A <paramref name="heavy"/> thing needs both hands: one hand only rocks it, two lift and carry it upright,
        /// it can't be pulled from a distance, and it drops straight when let go (no throw).
        /// </summary>
        public static Func<Rigidbody, StoryAssets, bool, Func<int>> Make;
    }
}
