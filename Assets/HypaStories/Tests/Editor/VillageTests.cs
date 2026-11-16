using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    /// <summary>The trail to the village and the village (3M first pass): where it runs, where you can walk, how the buttons get you there.</summary>
    public class VillageTests
    {
        const float Clearing = 18f;

        [Test] public void Trail_LeavesOnTheRight_AndEndsAtTheGate()
        {
            Assert.Greater(VillageLayout.Path[0].x, 0f, "the trail starts on the right of the fire (+X), where the Village button is");
            Assert.Greater(VillageLayout.Gate.magnitude, Clearing + 5f, "the village stands well beyond the clearing");
            Assert.Less(VillageLayout.DistanceToPath(VillageLayout.Gate), 1e-3f, "the trail reaches the gate");
            Assert.Less(Vector2.Distance(VillageLayout.Sign, VillageLayout.Path[0]), 1.5f, "the signpost stands at the trail's mouth");
            Assert.Greater(Vector2.Distance(VillageLayout.Plaza, HutTrailLayout.Hut), 60f, "the village and the hut are on opposite sides");
        }

        [Test] public void Trail_IsAboutAsLongAsTheHuts()
        {
            float Length(Vector2[] pts, Vector2 end)
            {
                var l = 0f;
                for (var i = 0; i + 1 < pts.Length; i++) l += Vector2.Distance(pts[i], pts[i + 1]);
                return l + Vector2.Distance(pts[pts.Length - 1], end);
            }
            var hut = Length(HutTrailLayout.Path, HutTrailLayout.Door);
            var village = Length(VillageLayout.Path, VillageLayout.Gate);
            Assert.Less(Mathf.Abs(hut - village) / hut, 0.15f, $"hut trail {hut:0.0} m, village trail {village:0.0} m");
        }

        [Test] public void Walk_TheTrailThePlaza_NotPastTheFence()
        {
            Assert.IsTrue(Grounds.Allowed(VillageLayout.Along(0.5f).point, Clearing), "on the trail");
            Assert.IsTrue(Grounds.Allowed(VillageLayout.FrontOfGate, Clearing), "outside the gate");
            Assert.IsTrue(Grounds.Allowed(VillageLayout.Plaza, Clearing), "in the plaza");
            Assert.IsTrue(Grounds.Allowed(VillageLayout.MeetSpot, Clearing), "in front of the local");
            Assert.IsFalse(Grounds.Allowed(VillageLayout.YamPile, Clearing), "the crowd is past the fence");
            Assert.IsFalse(Grounds.Allowed(VillageLayout.Fire, Clearing), "the village fire is past the fence");
            for (var i = 0; i < VillageLayout.HutAngles.Length; i++)
                Assert.IsFalse(Grounds.Allowed(VillageLayout.HutAt(i), Clearing), $"hut {i}");
            Assert.IsFalse(Grounds.Allowed(new Vector2(22f, 12f), Clearing), "off in the woods");
            Assert.IsTrue(Grounds.Allowed(HutTrailLayout.Hut, Clearing), "the hut is still walkable");
        }

        [Test] public void Clamp_PutsYouSomewhereYouCanStand()
        {
            var r = new System.Random(9);
            for (var i = 0; i < 600; i++)
            {
                var p = new Vector2(-45f + (float)r.NextDouble() * 100f, -25f + (float)r.NextDouble() * 50f);
                var q = Grounds.Clamp(p, Clearing);
                Assert.IsTrue(Grounds.Allowed(q, Clearing), $"{p} -> {q}");
                if (Grounds.Allowed(p, Clearing)) Assert.AreEqual(p, q, "an allowed spot stays put");
            }
        }

        [Test] public void Clamp_AtTheFence_StopsYouThere()
        {
            var past = VillageLayout.At(VillageLayout.FenceAt + 0.6f, 0.5f);
            var q = Grounds.Clamp(past, Clearing);
            Assert.AreEqual(VillageLayout.FenceAt, Vector2.Dot(q - VillageLayout.Plaza, VillageLayout.Into), 1e-3f);
            Assert.Less(Vector2.Distance(q, past), 0.61f);
        }

        [Test] public void GateButtons_ShowAtTheGate()
        {
            Assert.IsTrue(VillageLayout.AtGate(VillageLayout.FrontOfGate));
            Assert.IsFalse(VillageLayout.AtGate(VillageLayout.Local), "not by the local");
            Assert.IsFalse(VillageLayout.AtGate(VillageLayout.Path[0]), "not back at the clearing");
            Assert.IsFalse(HutTrailLayout.AtDoor(VillageLayout.FrontOfGate), "the hut's row stays at the hut");
        }

        [Test] public void StandInVillage_HutOpensTheCampHut()
        {
            Assert.IsTrue(Places.CampWorldPlace(PlaceIds.Hut), "the hut is the one on the trail");
            Assert.IsTrue(Places.CampWorldPlace(PlaceIds.Gathering));
            Assert.IsFalse(Places.CampWorldPlace(PlaceIds.Camp), "the camp is its own scene");
            Assert.AreEqual(Places.Route.CampThenSpot, Places.RouteFor(PlaceIds.Hut, PlaceIds.Gathering, true), "village stand-in: load the camp, stand at the hut");
            Assert.AreEqual(Places.Route.CampThenSpot, Places.RouteFor(PlaceIds.Gathering, PlaceIds.Hut, true), "hut stand-in: load the camp, stand at the gate");
            Assert.AreEqual(Places.Route.LoadScene, Places.RouteFor(PlaceIds.Camp, PlaceIds.Gathering, false), "Campfire from the stand-in is the camp scene");
        }

        [Test] public void Places_TheCampsOwnPlaces_AreWalkedTo()
        {
            Assert.AreEqual(Places.Route.InCampWorld, Places.RouteFor(PlaceIds.Hut, PlaceIds.Camp, true), "at the camp: to the hut's door");
            Assert.AreEqual(Places.Route.InCampWorld, Places.RouteFor(PlaceIds.Gathering, PlaceIds.Camp, true), "at the camp: to the gate");
            Assert.AreEqual(Places.Route.CampThenSpot, Places.RouteFor(PlaceIds.Hut, PlaceIds.Gathering, true), "from a stand-in scene: the camp, then the hut");
            Assert.AreEqual(Places.Route.LoadScene, Places.RouteFor(PlaceIds.Hut, PlaceIds.Camp, false), "no camp holding it: its scene");
            Assert.AreEqual(Places.Route.Ignore, Places.RouteFor(PlaceIds.Hut, PlaceIds.Hut, false), "already there");
            Assert.AreEqual(Places.Route.Ignore, Places.RouteFor("moon", PlaceIds.Camp, true), "unknown place");
        }

        [Test] public void DrumPattern_FitsTheLoop_AndEveryDrumPlays()
        {
            var hits = ProceduralClips.DrumPattern();
            var seen = new bool[3];
            var last = -1f;
            foreach (var h in hits)
            {
                Assert.GreaterOrEqual(h.T, 0f); Assert.Less(h.T, ProceduralClips.DrumLoopLength);
                Assert.GreaterOrEqual(h.T, last, "in time order");
                last = h.T;
                seen[h.Drum] = true;
            }
            Assert.IsTrue(seen[0] && seen[1] && seen[2]);
        }

        [Test] public void WaxPrints_TilesStayInsideTheirOwnSquare_AwayFromThePlainOne()
        {
            var plain = new Rect(0f, 0f, 0.25f, 1f / 3f);
            for (var k = 1; k <= WaxPrints.Count; k++)
            {
                var t = WaxPrints.Tile(k);
                Assert.IsFalse(t.Overlaps(plain), $"print {k} keeps off the plain tile (skin samples it)");
                Assert.GreaterOrEqual(t.xMin, 0f); Assert.LessOrEqual(t.xMax, 1f);
                Assert.GreaterOrEqual(t.yMin, 0f); Assert.LessOrEqual(t.yMax, 1f);
                for (var j = k + 1; j <= WaxPrints.Count; j++) Assert.IsFalse(t.Overlaps(WaxPrints.Tile(j)), $"prints {k} and {j}");
            }
            Assert.AreEqual(WaxPrints.Tile(1), WaxPrints.Tile(1 + WaxPrints.Count), "numbers past the last wrap round");
            Assert.IsTrue(plain.Contains(TrailBuilder.MeshKit.Plain), "untextured shapes sample the plain tile");
        }

        [Test] public void FarThings_AreNotDrawn_AndComeBackAsYouWalkUp()
        {
            var hut = new Bounds(new Vector3(HutTrailLayout.Hut.x, 2f, HutTrailLayout.Hut.y), new Vector3(6f, 4.5f, 6f));
            var seat = new Vector3(0f, 1.2f, -3.1f);
            Assert.IsFalse(DistanceCull.Visible(hut, seat, DistanceCull.Range), "from the seat the hut is past the fog: not drawn");
            var trail = new Vector3(HutTrailLayout.Along(0.6f).point.x, 1.6f, HutTrailLayout.Along(0.6f).point.y);
            Assert.IsTrue(DistanceCull.Visible(hut, trail, DistanceCull.Range), "partway down the trail it is");
            Assert.IsTrue(DistanceCull.Visible(new Bounds(new Vector3(-30f, 1f, 0f), Vector3.one), seat, DistanceCull.Range, 16f), "moving creatures get extra room");
            Assert.Greater(DistanceCull.Range, 36f, "never closer than the fog's end: you never see anything vanish");
        }
    }
}
