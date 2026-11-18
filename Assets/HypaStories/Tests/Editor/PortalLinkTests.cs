using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    public class PortalLinkTests
    {
        static readonly PortalDestination[] All = PortalDestinations.All;

        [Test]
        public void EveryPlaceHasAUnitDoorDirection()
        {
            foreach (var d in All) Assert.AreEqual(1f, PortalDestinations.DoorDir(d).magnitude, 1e-3f, d.ToString());
        }

        [Test]
        public void TheHutAndVillageDoorsAreTheirRealDoors()
        {
            Assert.AreEqual(HutTrailLayout.Door, PortalDestinations.Door(PortalDestination.Hut));
            Assert.AreEqual(VillageLayout.Gate, PortalDestinations.Door(PortalDestination.Village));
            // the village door faces out of the village (toward the trail you come in on), away from the plaza
            Assert.Less(Vector2.Dot(PortalDestinations.DoorDir(PortalDestination.Village), VillageLayout.Plaza - VillageLayout.Gate), 0f);
        }

        [Test]
        public void TheCampDoorIsSouthOfTheFireFacingAwayFromIt()
        {
            var door = PortalDestinations.Door(PortalDestination.Camp);
            Assert.Less(door.y, -2f);
            Assert.Less(Vector2.Dot(PortalDestinations.DoorDir(PortalDestination.Camp), -door), 0f); // out of the camp, not toward the fire
        }

        [Test]
        public void LinkCarriesTheRealDoorwayOntoThePlacesDoorway()
        {
            var real = PortalDestinations.Frame(new Vector3(2f, 0.1f, 3f), new Vector3(0f, 0f, -1f));
            var world = Matrix4x4.TRS(new Vector3(-5f, 0.1f, 7f), Quaternion.Euler(0f, 37f, 0f), Vector3.one);
            foreach (var d in All)
            {
                var doorWorld = PortalDestinations.DoorWorld(world, d);
                var link = PortalDestinations.Link(real, doorWorld);
                Assert.AreEqual(0f, Vector3.Distance(doorWorld.GetColumn(3), link.MultiplyPoint(new Vector3(2f, 0.1f, 3f))), 1e-3f, "origin " + d);
                // what is in front of the real doorway (toward you) is in front of the place's doorway
                var front = link.MultiplyPoint(new Vector3(2f, 0.1f, 3f) + new Vector3(0f, 0f, -1f));
                var expected = (Vector3)doorWorld.GetColumn(3) + (Vector3)doorWorld.GetColumn(2);
                Assert.AreEqual(expected.x, front.x, 1e-3f); Assert.AreEqual(expected.z, front.z, 1e-3f);
            }
        }

        [Test]
        public void StandingAtTheDoorwayAndLookingThroughGivesTheViewFromThePlacesDoorway()
        {
            // an eye at the real doorway, looking straight through it
            var eyeToWorld = Matrix4x4.TRS(new Vector3(0f, 1.6f, 0f), Quaternion.LookRotation(Vector3.forward), Vector3.one); // looking through: against the doorway's facing (back, toward you)
            var real = PortalDestinations.Frame(new Vector3(0f, 0f, 0f), Vector3.back);
            var doorWorld = PortalDestinations.Frame(new Vector3(10f, 0f, 20f), Vector3.right);
            var link = PortalDestinations.Link(real, doorWorld);
            var view = eyeToWorld.inverse;
            var virtualToWorld = PortalDestinations.VirtualView(view, link).inverse;
            var eyePos = (Vector3)virtualToWorld.GetColumn(3);
            Assert.AreEqual(10f, eyePos.x, 1e-3f); Assert.AreEqual(1.6f, eyePos.y, 1e-3f); Assert.AreEqual(20f, eyePos.z, 1e-3f);
            // looking through the doorway means looking into the place: against the door's outward direction (here -X)
            var look = virtualToWorld.MultiplyVector(Vector3.forward);
            Assert.AreEqual(-1f, Vector3.Dot(look.normalized, Vector3.right), 1e-3f);
        }
    }
}
