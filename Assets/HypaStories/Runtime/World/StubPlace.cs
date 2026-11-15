using System;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// A stand-in place until its owner builds it: the same button row as the campfire. The place you are in is the solid
    /// button (the start). The others are light and take you there. Hut and Village go to the camp's world (the real hut
    /// and the village at the ends of their trails), not these stand-in scenes.
    /// </summary>
    public sealed class StubPlace : MonoBehaviour
    {
        [Tooltip("Shown above the button row.")]
        public string Title = "A place";
        [Tooltip("Shown under the title.")]
        public string Note = "A stand-in until this place is built.";
        [Tooltip("Kept so older scenes still load. The row is the same in every place.")]
        public string[] Doors = { PlaceIds.Camp };

        void Start()
        {
            Places.EnsureRunner();
            PinchPointer.EnsureExists();
            if (!Kit.Ready) return; // the camp loads the UI kit; a place opened on its own has no row yet

            var here = Places.IdForScene(gameObject.scene.name);
            var row = new GameObject("Place Buttons").transform;
            row.SetParent(transform, false);
            Kit.Text(row, Title, 0.02f, new Vector3(0f, 0.058f, 0f), new Vector2(0.7f, 0.03f), KitTheme.Dim);
            PlaceButtons.Add(row, here, Back(), Go(PlaceIds.Camp), Go(PlaceIds.Hut), Go(PlaceIds.Gathering));
            var follow = row.gameObject.AddComponent<FollowView>();
            follow.Distance = 0.95f;
            follow.Height = -0.34f;
            row.gameObject.AddComponent<KitGroup>().Refresh();
        }

        /// <summary>The place you are in is the solid start button. It does not leave. The others go there.</summary>
        Action Go(string id) => Places.IdForScene(gameObject.scene.name) == id ? (Action)(() => { }) : () => Places.GoTo(id);

        static Action Back() => HypaShell.Instance != null ? () => HypaShell.Instance.ExitToHome() : null;

        /// <summary>"Back to the camp", "To the hut"... Pure.</summary>
        public static string DoorLabel(string placeId) => placeId == PlaceIds.Camp ? "Back to the camp" : "To the " + PlaceName(placeId);

        /// <summary>What a place is called on screen: the gathering is shown as "village". Pure.</summary>
        public static string PlaceName(string placeId) => placeId == PlaceIds.Gathering ? "village" : placeId;
    }
}
