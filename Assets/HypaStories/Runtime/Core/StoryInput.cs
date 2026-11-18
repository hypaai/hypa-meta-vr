using System;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Moving around the camp. HypaStories.Quest sets these on device: <see cref="Move"/> = left thumbstick (walk, relative to where
    /// you look), <see cref="Turn"/> = right thumbstick x (snap turn). In the editor, WASD walks.
    /// <see cref="Hand"/>: where hand 0 or 1 is in the world (a tracked hand's palm, else a controller), null when not tracked:
    /// the drum game strikes the drum with it. Unset in the editor without a headset (the drum takes a pinch instead).
    /// </summary>
    public static class StoryInput
    {
        public static Func<Vector2> Move;
        public static Func<float> Turn;
        public static Func<int, Vector3?> Hand;
    }
}
