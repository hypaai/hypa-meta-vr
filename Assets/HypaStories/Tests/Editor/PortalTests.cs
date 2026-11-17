using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    /// <summary>Ticket 3L: where the portal to the hut goes in your room, and when you have stepped through it (pure maths).</summary>
    public class PortalTests
    {
        // a 5 x 4 m room (x 0..5, z 0..4); clearance = metres to the nearest wall
        static float Clear(Vector2 p) => Mathf.Min(Mathf.Min(p.x, 5f - p.x), Mathf.Min(p.y, 4f - p.y));

        // the four walls (2.5 m high), facing in
        static List<PortalSurface> Walls() => new List<PortalSurface>
        {
            new PortalSurface { Kind = PortalKind.Wall, Center = new Vector3(2.5f, 1.25f, 0f),  Normal = Vector3.forward, Width = 5f, Height = 2.5f },
            new PortalSurface { Kind = PortalKind.Wall, Center = new Vector3(2.5f, 1.25f, 4f),  Normal = Vector3.back,    Width = 5f, Height = 2.5f },
            new PortalSurface { Kind = PortalKind.Wall, Center = new Vector3(0f, 1.25f, 2f),    Normal = Vector3.right,   Width = 4f, Height = 2.5f },
            new PortalSurface { Kind = PortalKind.Wall, Center = new Vector3(5f, 1.25f, 2f),    Normal = Vector3.left,    Width = 4f, Height = 2.5f },
        };

        [Test] public void ARealDoor_IsPreferredOverWall()
        {
            var s = Walls();
            s.Add(new PortalSurface { Kind = PortalKind.Door, Center = new Vector3(5f, 1f, 3f), Normal = Vector3.left, Width = 0.9f, Height = 2f });
            var p = PortalSpot.Find(new Vector2(2.5f, 1f), Vector2.up, s, 0f, Clear).Value;
            Assert.AreEqual(PortalKind.Door, p.Kind);
            Assert.AreEqual(5f, p.Base.x, 1e-3f);
            Assert.AreEqual(3f, p.Base.z, 1e-3f);
            Assert.IsTrue(p.Scanned);
        }

        [Test] public void WithNoDoor_ItStandsOnClearWall_FacingIn_WithRoomToWalkUp()
        {
            var user = new Vector2(2.5f, 1f);
            var p = PortalSpot.Find(user, Vector2.up, Walls(), 0f, Clear).Value;
            Assert.AreEqual(PortalKind.Wall, p.Kind);
            var front = new Vector2(p.Base.x, p.Base.z) + new Vector2(p.Normal.x, p.Normal.z) * 0.9f;
            Assert.GreaterOrEqual(Clear(front), 0.35f, "you can walk up to it");
            Assert.AreEqual(1f, p.Normal.magnitude, 1e-4f);
            Assert.Greater(Vector3.Dot(p.Normal, new Vector3(2.5f - p.Base.x, 0f, 2f - p.Base.z)), 0f, "it faces into the room");
        }

        [Test] public void NeverOpensWhereYouCantWalk_BlockedFloorInFront()
        {
            // furniture fills the floor in front of the whole north wall: the portal must go elsewhere
            float Blocked(Vector2 p) => p.y > 2.6f ? 0f : Clear(p);
            var p = PortalSpot.Find(new Vector2(2.5f, 1.5f), Vector2.up, Walls(), 0f, Blocked).Value;
            Assert.Less(p.Base.z, 3.5f, "not on the wall behind the furniture");
        }

        [Test] public void NeverOverAWindow()
        {
            var s = Walls();
            s.Add(new PortalSurface { Kind = PortalKind.Window, Center = new Vector3(2.5f, 1.4f, 0f), Normal = Vector3.forward, Width = 1.2f, Height = 1f });
            var p = PortalSpot.Find(new Vector2(2.5f, 2f), Vector2.down, s, 0f, Clear).Value;
            Assert.GreaterOrEqual(Vector2.Distance(new Vector2(p.Base.x, p.Base.z), new Vector2(2.5f, 0f)), 1.3f - 1e-3f, "kept clear of the window");
        }

        [Test] public void AWallTooLowForADoorway_IsSkipped()
        {
            var s = new List<PortalSurface> { new PortalSurface { Kind = PortalKind.Wall, Center = new Vector3(2.5f, 0.6f, 0f), Normal = Vector3.forward, Width = 5f, Height = 1.2f } };
            Assert.IsNull(PortalSpot.Find(new Vector2(2.5f, 2f), Vector2.down, s, 0f, Clear));
        }

        [Test] public void NotNextToTheFire()
        {
            var fire = new Vector2(2.5f, 0.9f);
            var p = PortalSpot.Find(new Vector2(2.5f, 2.5f), Vector2.down, Walls(), 0f, Clear, fire).Value;
            var front = new Vector2(p.Base.x, p.Base.z) + new Vector2(p.Normal.x, p.Normal.z) * 0.9f;
            Assert.GreaterOrEqual(Vector2.Distance(front, fire), 1.4f - 1e-3f);
        }

        [Test] public void NoScan_TwoMetresInFront_FacingYou()
        {
            var p = PortalSpot.InFront(new Vector2(1f, 1f), Vector2.up, 0.1f);
            Assert.AreEqual(new Vector3(1f, 0.1f, 3f).x, p.Base.x, 1e-3f);
            Assert.AreEqual(3f, p.Base.z, 1e-3f);
            Assert.AreEqual(0.1f, p.Base.y, 1e-4f);
            Assert.Less(p.Normal.z, -0.99f, "turned back toward you");
            Assert.IsFalse(p.Scanned);
        }

        [Test] public void NoScan_StepsAsideForTheFire()
        {
            var user = new Vector2(1f, 1f);
            var p = PortalSpot.InFront(user, Vector2.up, 0f, fire: new Vector2(1f, 3f));
            Assert.GreaterOrEqual(Vector2.Distance(new Vector2(p.Base.x, p.Base.z), new Vector2(1f, 3f)), 1.4f - 1e-3f);
            Assert.AreEqual(2f, Vector2.Distance(new Vector2(p.Base.x, p.Base.z), user), 1e-3f, "still 2 m from you");
        }

        // ---------- stepping through ----------

        static PortalPlan Door() => new PortalPlan { Base = new Vector3(5f, 0f, 3f), Normal = Vector3.left, Width = 0.9f, Height = 2f };

        [Test] public void SteppingThroughTheOpening_Counts()
        {
            var p = Door();
            var before = PortalSpot.Local(p, new Vector3(4.6f, 1.6f, 3.1f));
            var after = PortalSpot.Local(p, new Vector3(5.2f, 1.6f, 3.1f));
            Assert.Greater(before.z, 0f);
            Assert.Less(after.z, 0f);
            Assert.IsTrue(PortalSpot.SteppedIn(before.z, after.z, after.x, after.y, p.Width, p.Height));
        }

        [Test] public void WalkingPastTheDoorway_DoesNot()
        {
            var p = Door();
            var before = PortalSpot.Local(p, new Vector3(4.6f, 1.6f, 1.5f));
            var after = PortalSpot.Local(p, new Vector3(5.2f, 1.6f, 1.5f)); // well to the side of it
            Assert.IsFalse(PortalSpot.SteppedIn(before.z, after.z, after.x, after.y, p.Width, p.Height));
        }

        [Test] public void AJumpAcrossTheRoom_IsNotAStep()
        {
            Assert.IsFalse(PortalSpot.SteppedIn(3f, -1f, 0f, 1.6f, 0.9f, 2f), "a recentre or a teleport must not drop you in the hut");
        }

        [Test] public void BackOutOfTheHutsDoor_IsASteppedOut_NotTheWayIn()
        {
            Assert.IsTrue(PortalSpot.SteppedOut(-0.2f, 0.1f, 0.2f, 1.2f));
            Assert.IsFalse(PortalSpot.SteppedOut(0.2f, -0.1f, 0.2f, 1.2f), "going in is not coming out");
            Assert.IsFalse(PortalSpot.SteppedOut(-0.2f, 0.1f, 2f, 1.2f), "round the side of the door");
        }

        [Test] public void Pull_GrowsAsYouNear_AndIsFullAtTheGlass()
        {
            Assert.AreEqual(0f, PortalSpot.Pull(3f, 0f, 0.9f), 1e-4f);
            Assert.Greater(PortalSpot.Pull(0.5f, 0f, 0.9f), PortalSpot.Pull(1.2f, 0f, 0.9f));
            Assert.AreEqual(1f, PortalSpot.Pull(0f, 0f, 0.9f), 1e-4f);
            Assert.Less(PortalSpot.Pull(0.3f, 2f, 0.9f), 0.2f, "far to the side it does not pull");
        }

        [Test] public void TheHutsDoorLiesOnThePortal_SoSteppingThroughNeedsNoJump()
        {
            var p = Door();
            var door = HutTrailLayout.Door; var dir = HutTrailLayout.DoorDir;
            var pose = PortalSpot.WorldPose(p, door, dir);
            var at = pose.position + pose.rotation * new Vector3(door.x, 0f, door.y);
            Assert.AreEqual(p.Base.x, at.x, 1e-3f); Assert.AreEqual(p.Base.z, at.z, 1e-3f);
            var out3 = pose.rotation * new Vector3(dir.x, 0f, dir.y);
            Assert.AreEqual(p.Normal.x, out3.x, 1e-3f); Assert.AreEqual(p.Normal.z, out3.z, 1e-3f); // the door's outside is your side
            Assert.AreEqual(0f, pose.rotation.eulerAngles.x, 1e-2f);
            Assert.AreEqual(0f, pose.rotation.eulerAngles.z, 1e-2f);
        }

        // ---- every real door, laid on the door itself

        static PortalSurface Door(float x, float z, float nx, float nz, float w = 0.9f, float h = 2f) =>
            new PortalSurface { Center = new Vector3(x, 1f, z), Normal = new Vector3(nx, 0f, nz), Width = w, Height = h, Kind = PortalKind.Door };

        [Test] public void Doors_AreEveryDoor_NearestFirst_OnTheFloor()
        {
            var list = PortalSpot.Doors(new Vector2(4.5f, 2f), new[] { Door(0f, 2f, 1f, 0f), Door(5f, 2f, -1f, 0f), Door(2.5f, 0f, 0f, 1f) }, 0.05f);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(5f, list[0].Base.x, 1e-4f, "nearest first");
            foreach (var p in list) { Assert.AreEqual(0.05f, p.Base.y, 1e-4f); Assert.AreEqual(PortalKind.Door, p.Kind); Assert.IsTrue(p.Scanned); }
        }

        [Test] public void Doors_IgnoreWindowsAndWalls_AndNeedNoWalkingRoom()
        {
            var surfaces = new List<PortalSurface>
            {
                new PortalSurface { Center = new Vector3(2f, 1.5f, 0f), Normal = Vector3.forward, Width = 1.2f, Height = 1.1f, Kind = PortalKind.Window },
                new PortalSurface { Center = new Vector3(2.5f, 1.2f, 4f), Normal = Vector3.back, Width = 5f, Height = 2.5f, Kind = PortalKind.Wall },
                Door(0.1f, 0.1f, 1f, 0f), // jammed in a corner, behind furniture: it is still where the door is
            };
            var list = PortalSpot.Doors(new Vector2(2.5f, 2f), surfaces, 0f);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(0.1f, list[0].Base.x, 1e-4f);
        }

        [Test] public void Doors_KeepTheirRealSize_WithinSaneLimits()
        {
            var list = PortalSpot.Doors(Vector2.zero, new[] { Door(1f, 1f, 1f, 0f, 1.4f, 2.3f), Door(2f, 1f, 1f, 0f, 0.1f, 5f) }, 0f);
            Assert.AreEqual(1.4f, list.Find(p => Mathf.Approximately(p.Base.x, 1f)).Width, 1e-4f);
            var odd = list.Find(p => Mathf.Approximately(p.Base.x, 2f));
            Assert.AreEqual(PortalSpot.DoorWidthMin, odd.Width, 1e-4f);
            Assert.AreEqual(PortalSpot.DoorHeightMax, odd.Height, 1e-4f);
        }

        [Test] public void SteppedIn_CanBeAStepPastTheDoorway_AndStepBackIsTheSameLine()
        {
            const float t = -PortalSpot.Through;
            Assert.IsFalse(PortalSpot.SteppedIn(0.2f, -0.1f, 0f, 1.6f, 0.9f, 2f, t), "only into the doorway: not through yet");
            Assert.IsTrue(PortalSpot.SteppedIn(-0.2f, -0.35f, 0f, 1.6f, 0.9f, 2f, t));
            Assert.IsTrue(PortalSpot.SteppedOut(-0.35f, 0.02f, 0f, 0.9f), "a step back over the doorway takes you home");
        }
    }
}
