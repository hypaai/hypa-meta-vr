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
        /// Makes a rigidbody (with its colliders) something you can pick up, look at and put down. Returns a probe that is true
        /// while it is held, or null when it couldn't be set up. <see cref="StoryAssets"/> carries the SDK's rig prefabs.
        /// </summary>
        public static Func<Rigidbody, StoryAssets, Func<bool>> Make;
    }
}
