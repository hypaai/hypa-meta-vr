using HypaCore;
using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    /// <summary>Ticket 3F: place ids, their scenes, and putting the viewer on a place's entry by moving the world.</summary>
    public class PlacesTests
    {
        [Test] public void EachPlaceHasItsScene_AndBack()
        {
            Assert.AreEqual(HypaScenes.Story, Places.SceneFor(PlaceIds.Camp));
            Assert.AreEqual("HypaStoryHut", Places.SceneFor(PlaceIds.Hut));
            Assert.AreEqual("HypaStoryGathering", Places.SceneFor(PlaceIds.Gathering));
            foreach (var id in Places.All) Assert.AreEqual(id, Places.IdForScene(Places.SceneFor(id)));
            Assert.IsNull(Places.SceneFor("castle"), "unknown ids go nowhere");
            Assert.IsNull(Places.IdForScene(HypaScenes.Lens), "Lens is not a place");
        }

        [Test] public void Align_PutsTheEntryUnderTheHead_OnTheFloor_FacingTheWayTheHeadFaces()
        {
            // The place: its root at the origin; the entry 3 m in, 2 m to the right, facing +X (yaw 90).
            var entryLocal = new Vector3(2f, 0f, 3f);
            var head = new Vector3(5f, 1.6f, -1f);
            var root = Places.Align(entryLocal, 90f, head, 180f, 0.25f, new Pose(Vector3.zero, Quaternion.identity));

            var entryWorld = root.position + root.rotation * entryLocal;
            Assert.That(Vector3.Distance(entryWorld, new Vector3(5f, 0.25f, -1f)), Is.LessThan(1e-4f), "under the head, on the floor");
            var entryForward = root.rotation * Vector3.right; // the entry faced +X in the place
            Assert.That(Vector3.Angle(entryForward, Vector3.back), Is.LessThan(0.01f), "facing where the head faces (yaw 180)");
        }

        [Test] public void Align_KeepsThePlaceInOnePiece()
        {
            var a = Places.Align(new Vector3(1, 0, 1), 0f, new Vector3(0, 1.7f, 0), 45f, 0f, new Pose(new Vector3(3, 0, 0), Quaternion.identity));
            var b = Places.Align(new Vector3(1, 0, 1), 0f, new Vector3(0, 1.7f, 0), 45f, 0f, new Pose(new Vector3(-2, 1, 4), Quaternion.Euler(0, 30, 0)));
            Assert.AreEqual(Vector3.Distance(new Vector3(3, 0, 0), new Vector3(-2, 1, 4)), Vector3.Distance(a.position, b.position), 1e-4f,
                "every root moves the same way: distances between them don't change");
        }

        [Test] public void YawOf_ZeroIsForward() 
        {
            Assert.AreEqual(0f, Places.YawOf(Vector3.forward), 1e-4f);
            Assert.AreEqual(90f, Places.YawOf(Vector3.right), 1e-4f);
        }

        [Test] public void StubDoors_ReadNaturally()
        {
            Assert.AreEqual("Back to the camp", StubPlace.DoorLabel(PlaceIds.Camp));
            Assert.AreEqual("To the hut", StubPlace.DoorLabel(PlaceIds.Hut));
        }
    }
}
