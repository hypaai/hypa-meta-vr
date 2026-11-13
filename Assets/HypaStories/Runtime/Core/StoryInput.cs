using System;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Moving around the camp. HypaStories.Quest sets these on device: <see cref="Move"/> = left thumbstick (walk, relative to where
    /// you look), <see cref="Turn"/> = right thumbstick x (snap turn). In the editor, WASD walks.
    /// </summary>
    public static class StoryInput
    {
        public static Func<Vector2> Move;
        public static Func<float> Turn;
    }
}
