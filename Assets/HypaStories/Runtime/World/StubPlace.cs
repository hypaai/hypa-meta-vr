using System;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// A stand-in place (ticket 3F) until its owner builds it (3H the hut, 3M the gathering): a title in front of the entry
    /// and a pinchable door to each other place, so moving between places can be tried in the editor and on a Quest from
    /// day 1. Built at runtime with HypaCore's UI; the scene's builder adds the floor and the one light.
    /// </summary>
    public sealed class StubPlace : MonoBehaviour
    {
        [Tooltip("Shown in front of the entry.")]
        public string Title = "A place";
        [Tooltip("Shown under the title.")]
        public string Note = "A stand-in until this place is built.";
        [Tooltip("Place ids to offer doors to.")]
        public string[] Doors = { PlaceIds.Camp };

        // The UI was designed in metres at 0.6 m; the sign stands about 1.8 m from the entry, so it is scaled up to match.
        const float SignDistance = 1.8f, Scale = SignDistance / 0.6f;

        void Start()
        {
            Places.EnsureRunner();
            PinchPointer.EnsureExists();
            var sign = new GameObject("Stub Sign").transform;
            sign.SetParent(transform, false);
            sign.localPosition = new Vector3(0f, 1.45f, SignDistance); // ahead of the entry (this object sits on it), at eye level
            sign.localScale = Vector3.one * Scale;

            // Title, note, then one full-width door per row (labels never squeeze).
            const float pitch = 0.062f;
            var h = 0.13f + Doors.Length * pitch;
            Ui.Quad("bg", sign, new Vector3(0f, 0.08f - h / 2f, 0.003f), new Vector2(0.36f, h), Ui.Panel, true).AddComponent<PinchTarget>();
            Ui.Text(sign, Title, 0.026f, new Vector3(0f, 0.045f, 0f), Color.white);
            Ui.Text(sign, Note, 0.013f, new Vector3(0f, 0.008f, 0f), new Color(1f, 1f, 1f, 0.7f));
            for (var i = 0; i < Doors.Length; i++)
            {
                var id = Doors[i];
                Ui.Button(DoorLabel(id), sign, new Vector3(0f, -0.045f - i * pitch, -0.001f), new Vector2(0.28f, 0.05f), i == 0 ? Ui.Accent : Ui.Muted, () => Places.GoTo(id));
            }
        }

        /// <summary>"Back to the camp", "To the hut"... Pure.</summary>
        public static string DoorLabel(string placeId) => placeId == PlaceIds.Camp ? "Back to the camp" : "To the " + placeId;
    }
}
