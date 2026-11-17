using System;
using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    /// <summary>Ticket 3K: finding an open spot in the room for the fire and the storyteller (pure maths; the scan itself is the headset's).</summary>
    public class RoomTests
    {
        // a rectangular room from (0,0) to (w,d) with boxes of furniture: clearance = metres to the nearest wall or box, 0 or less outside or inside
        static Func<Vector2, float> Room(float w, float d, params Rect[] boxes) => p =>
        {
            var c = Mathf.Min(Mathf.Min(p.x, w - p.x), Mathf.Min(p.y, d - p.y));
            foreach (var b in boxes)
            {
                if (b.Contains(p)) return 0f;
                var dx = Mathf.Max(b.xMin - p.x, 0f, p.x - b.xMax);
                var dy = Mathf.Max(b.yMin - p.y, 0f, p.y - b.yMax);
                c = Mathf.Min(c, Mathf.Sqrt(dx * dx + dy * dy));
            }
            return c;
        };

        static void AssertFits(RoomSpotPlan s, Func<Vector2, float> room)
        {
            Assert.GreaterOrEqual(room(s.Fire), RoomSpot.FireClear, "the fire has room round it");
            Assert.GreaterOrEqual(room(s.Teller), RoomSpot.TellerClear, "the storyteller has room round it");
            Assert.GreaterOrEqual(s.Gap, RoomSpot.MinGap - 1e-3f);
            Assert.LessOrEqual(s.Gap, RoomSpot.WoodsGap + 1e-3f);
            Assert.AreEqual(1f, s.Away.magnitude, 1e-4f, "Away is a direction");
            Assert.IsTrue(s.Scanned);
        }

        [Test] public void OpenRoom_PutsTheFireAheadOfYou_WithTheWoodsGap()
        {
            var room = Room(6f, 5f);
            var s = RoomSpot.Find(new Vector2(3f, 0.8f), Vector2.up, room).Value;
            AssertFits(s, room);
            Assert.Greater(s.Away.y, 0.9f, "the fire is out in front of you, not off to a side");
            Assert.AreEqual(RoomSpot.WoodsGap, s.Gap, 0.01f, "plenty of room: the storyteller sits on the far stones");
            var between = Vector2.Distance(new Vector2(3f, 0.8f), s.Fire);
            Assert.That(between, Is.InRange(GameFit.StoneEdge, 1.3f), "you sit at the edge of the stones, not metres back");
        }

        [Test] public void SmallRoom_TheRoomFireStillFits_InsideTheWalls()
        {
            // 2.9 x 2.6: the room-size campfire, you at its stones and the storyteller across it, all inside the walls
            var room = Room(2.9f, 2.6f);
            var s = RoomSpot.Find(new Vector2(0.6f, 1.3f), Vector2.right, room).Value;
            AssertFits(s, room);
            Assert.Less(Vector2.Distance(new Vector2(0.6f, 1.3f), s.Fire), 1.3f, "still at the stones");
        }

        [Test] public void FurnitureIsKeptClearOf_AndYouAreNotSeatedOnTheWayThroughIt()
        {
            // a sofa and a table across the middle of the room
            var room = Room(7f, 5f, new Rect(2.5f, 1.8f, 2f, 0.9f), new Rect(0.4f, 3.8f, 1f, 1f));
            var user = new Vector2(3.5f, 0.7f);
            var s = RoomSpot.Find(user, Vector2.up, room).Value;
            AssertFits(s, room);
            for (var i = 0; i <= 10; i++) Assert.Greater(room(Vector2.Lerp(user, s.Fire, i / 10f)), 0f, "the way to the fire is open");
        }

        [Test] public void NoOpenSpot_ReturnsNull_AndTheFallbackIsInFront()
        {
            var tiny = Room(2.5f, 2.5f);
            Assert.IsNull(RoomSpot.Find(new Vector2(1.2f, 1.2f), Vector2.up, tiny));
            Assert.IsNull(RoomSpot.Find(Vector2.zero, Vector2.up, null), "no scan, no spot");

            var s = RoomSpot.InFront(new Vector2(1f, 2f), new Vector2(0f, 2f));
            Assert.AreEqual(new Vector2(1f, 2f + RoomSpot.InFrontDistance), s.Fire, "just in front, at the stones (forward need not be a unit vector)");
            Assert.AreEqual(Vector2.up, s.Away);
            Assert.AreEqual(RoomSpot.InFrontGap, s.Gap, 1e-4f);
            Assert.IsFalse(s.Scanned);
            Assert.AreEqual(RoomSpot.MinGap, RoomSpot.InFront(Vector2.zero, Vector2.up, 0.2f).Gap, 1e-4f, "never closer than the minimum gap");
            Assert.AreEqual(Vector2.up, RoomSpot.InFront(Vector2.zero, Vector2.zero).Away, "no facing: a default, never NaN");
        }

        [Test] public void CornerRoom_FindsTheSpotBehindYouWhenAheadIsAWall()
        {
            var room = Room(5f, 4f);
            // facing the near wall: the open floor is behind
            var s = RoomSpot.Find(new Vector2(2.5f, 0.5f), Vector2.down, room).Value;
            AssertFits(s, room);
            Assert.Greater(s.Away.y, 0.5f, "turned around to where the room is");
        }

        [Test] public void BedInFrontAndNoRoomNearby_StillFindsOpenFloorElsewhere_NeverOnTheBed()
        {
            // a 4 x 3.2 room with a big bed across the middle and a wardrobe by you: nothing open within the usual few metres
            var room = Room(4f, 3.2f, new Rect(1.4f, 0.2f, 2.4f, 1.9f), new Rect(0.1f, 2.2f, 1.4f, 0.9f));
            var user = new Vector2(0.5f, 1.2f);
            var near = RoomSpot.Find(user, Vector2.right, room);
            var s = RoomSpot.Find(user, Vector2.right, room, new Rect(0f, 0f, 4f, 3.2f));
            if (near.HasValue) Assert.GreaterOrEqual(room(near.Value.Fire), 0.4f);
            Assert.IsTrue(s.HasValue || !near.HasValue);
            if (s.HasValue) { Assert.Greater(room(s.Value.Fire), 0.3f, "never on the bed"); Assert.Greater(room(s.Value.Teller), 0.2f); }
        }

        [Test] public void AModestRoom_StillSeatsYouAtTheStones()
        {
            // about 3.4 m deep: too small for the old 2 m seat, big enough to sit at the stones with the storyteller across
            var room = Room(3.4f, 3f);
            var user = new Vector2(0.7f, 1.5f);
            var s = RoomSpot.Find(user, Vector2.right, room).Value;
            Assert.Less(Vector2.Distance(user, s.Fire), 1.3f, "the fire is at the stones, not back in the room");
            Assert.GreaterOrEqual(room(s.Fire), GameFit.StoneEdge, "the whole stone ring is on open floor");
            Assert.GreaterOrEqual(room(s.Teller), 0.25f);
        }

        [Test] public void SameRoom_SameSpot()
        {
            var room = Room(6f, 5f, new Rect(2f, 2f, 1f, 1f));
            var a = RoomSpot.Find(new Vector2(1f, 1f), new Vector2(1f, 1f), room).Value;
            var b = RoomSpot.Find(new Vector2(1f, 1f), new Vector2(1f, 1f), room).Value;
            Assert.AreEqual(a.Fire, b.Fire);
            Assert.AreEqual(a.Away, b.Away);
            Assert.AreEqual(a.Gap, b.Gap);
        }
    
        [Test] public void Games_InTheForest_AreAsBuilt()
        {
            var f = GameFit.Forest;
            Assert.IsFalse(f.Room);
            Assert.AreEqual(new Vector3(0.62f, 0f, -2.35f), f.At(new Vector3(0.62f, 0f, -2.35f)));
            Assert.AreEqual(1f, f.DodoScale);
        }

        [Test] public void Games_InASmallRoom_PullInToYou_AndStayInFrontOfYou()
        {
            // you at the room fire's stones, the storyteller the same distance beyond it
            var seat = RoomSpot.SeatDistance;
            var f = GameFit.InRoom(seat, RoomSpot.WoodsGap);
            var slab = f.At(new Vector3(0.62f, 0f, -2.35f));
            Assert.Greater(slab.z, -seat, "the slab is in front of you, not behind");
            Assert.LessOrEqual(slab.z, -GameFit.StoneEdge + 1e-4f, "the slab stays outside the stones");
            var post = f.At(new Vector3(0f, 0f, -1.45f));
            Assert.LessOrEqual(post.z, -GameFit.StoneEdge + 1e-4f, "clear of the stones");
            Assert.Greater(post.z, -seat, "in front of you");
            var eshu = f.At(new Vector3(1.15f, 0f, 1.45f));
            Assert.LessOrEqual(eshu.magnitude, RoomSpot.WoodsGap + 0.5f, "beyond the fire no farther than the storyteller's open floor");
            Assert.Less(f.DodoScale * 3.4f, 2.6f, "Dodo stands under a ceiling");
            // inside the ring is not a seat: you are never placed in the fire
            Assert.AreEqual(GameFit.InRoom(0.3f, 1.1f).Near, GameFit.InRoom(0.5f, 1.1f).Near, 1e-4);
        }
}
}
