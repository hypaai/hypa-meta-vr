using System;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The place row, the same in every place: Back (far left), then the three places with the one you are in solid in the
    /// middle (it starts that place: Gather round at the camp). The other two are light and take you there.
    /// Camp: Back, Hut, [Gather round], Village. Hut: Back, Campfire, [Hut], Village. Village: Back, Hut, [Village], Campfire.
    /// A null action leaves that button out (e.g. Back with no Hypa shell to go back to).
    /// </summary>
    public static class PlaceButtons
    {
        public const float Height = 0.064f;
        const float Gap = 0.016f, Em = 0.022f, BackW = 0.15f;

        /// <summary>Left to right, before the one you are in moves to the middle.</summary>
        static readonly string[] Order = { PlaceIds.Hut, PlaceIds.Camp, PlaceIds.Gathering };

        /// <summary>
        /// The row under <paramref name="parent"/>, centred on the place you are in. <paramref name="camp"/>, <paramref name="hut"/>
        /// and <paramref name="village"/>: for the place you are in, its start; for the others, going there.
        /// </summary>
        public static void Add(Transform parent, string here, Action back, Action camp, Action hut, Action village)
        {
            var (left, right) = Sides(here);
            var hereW = Width(here, true);
            Button(parent, here, true, 0f, hereW, For(here, camp, hut, village));

            var x = -hereW / 2f - Gap;
            if (For(left, camp, hut, village) != null)
            {
                var w = Width(left, false);
                Button(parent, left, false, x - w / 2f, w, For(left, camp, hut, village));
                x -= w + Gap;
            }
            if (back != null) Kit.Button(parent, new Vector3(x - BackW / 2f, 0f, 0f), new Vector2(BackW, Height), "Back", "arrow-left", KitStyle.Ghost, back, null, Em);

            if (For(right, camp, hut, village) != null)
            {
                var w = Width(right, false);
                Button(parent, right, false, hereW / 2f + Gap + w / 2f, w, For(right, camp, hut, village));
            }
        }

        /// <summary>The places either side of the one you are in, keeping the Hut, Camp, Village order. Pure, unit tested.</summary>
        public static (string left, string right) Sides(string here)
        {
            string a = null, b = null;
            foreach (var id in Order)
            {
                if (id == here) continue;
                if (a == null) a = id; else b = id;
            }
            return (a, b);
        }

        /// <summary>The button's label: the camp's start is "Gather round"; going back to it says "Campfire". Pure, unit tested.</summary>
        public static string Label(string id, bool here) =>
            id == PlaceIds.Camp ? (here ? "Gather round" : "Campfire") : id == PlaceIds.Hut ? "Hut" : "Village";

        static string Icon(string id) => id == PlaceIds.Camp ? "flame" : id == PlaceIds.Hut ? "house" : "users";

        static float Width(string id, bool here) => id == PlaceIds.Camp ? (here ? 0.24f : 0.19f) : id == PlaceIds.Hut ? 0.15f : 0.17f;

        static Action For(string id, Action camp, Action hut, Action village) =>
            id == PlaceIds.Camp ? camp : id == PlaceIds.Hut ? hut : id == PlaceIds.Gathering ? village : null;

        static void Button(Transform parent, string id, bool here, float x, float width, Action act)
        {
            if (act == null) return;
            Kit.Button(parent, new Vector3(x, 0f, 0f), new Vector2(width, Height), Label(id, here), Icon(id),
                here ? KitStyle.Primary : KitStyle.Ghost, act, null, Em);
        }
    }
}
