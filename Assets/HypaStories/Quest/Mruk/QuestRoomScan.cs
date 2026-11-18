using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Android;

namespace HypaStories.Quest.Mruk
{
    // Compiled only when the Meta XR Core SDK and the MR Utility Kit are installed (asmdef defineConstraints).

    /// <summary>
    /// Ticket 3K: reads the player's room through Meta's Scene API (MR Utility Kit) for <see cref="StoryRoom.Scan"/>: the floor's
    /// height, and how far any floor point is from the nearest wall or piece of furniture. With no room scanned yet it starts
    /// Meta's own room set-up flow (the player scans, then comes back). Never throws.
    /// </summary>
    public static class QuestRoomScan
    {
        // what the fire and the storyteller must keep away from; the floor, the ceiling and the room's mesh are what we stand on
        const MRUKAnchor.SceneLabels NotObstacles = MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.GLOBAL_MESH;
        // a low, a table-top and a head-high check, so a table or a shelf counts even though the floor beneath is open
        static readonly float[] Heights = { 0.1f, 0.8f, 1.7f };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => StoryRoom.Scan = ScanAsync;

        static async Task<RoomScan> ScanAsync()
        {
            try
            {
                if (!OVRPlugin.initialized) return new RoomScan { Result = RoomScanResult.Unavailable }; // the editor, with no headset
                if (!await AskScenePermission()) return new RoomScan { Result = RoomScanResult.NoPermission };

                var mruk = EnsureMruk();
                var loaded = await mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: true, removeMissingRooms: false);
                if (loaded == MRUK.LoadDeviceResult.NoScenePermission) return new RoomScan { Result = RoomScanResult.NoPermission };
                if (loaded != MRUK.LoadDeviceResult.Success) return new RoomScan { Result = RoomScanResult.NoScan };

                var room = mruk.GetCurrentRoom();
                if (room == null && mruk.Rooms.Count > 0) room = mruk.Rooms[0];
                if (room == null) return new RoomScan { Result = RoomScanResult.NoScan };
                return Describe(room);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Hypa Stories: reading the room failed: " + e.Message);
                return new RoomScan { Result = RoomScanResult.Unavailable };
            }
        }

        static RoomScan Describe(MRUKRoom room)
        {
            var floorY = room.FloorAnchors.Count > 0 ? room.FloorAnchors[0].transform.position.y : room.GetRoomBounds().min.y;
            var filter = new LabelFilter(~NotObstacles); // everything but the floor, the ceiling and the room's mesh
            float Clearance(Vector2 p)
            {
                var nearest = float.PositiveInfinity;
                foreach (var h in Heights)
                {
                    var at = new Vector3(p.x, floorY + h, p.y);
                    if (!room.IsPositionInRoom(at, false)) return 0f;       // outside the walls
                    if (room.IsPositionInSceneVolume(at, 0f)) return 0f;   // inside a table, a sofa, a cupboard
                    var d = room.TryGetClosestSurfacePosition(at, out _, out _, filter);
                    if (d < nearest) nearest = d;
                }
                return float.IsPositiveInfinity(nearest) ? 0f : nearest;
            }
            var b = room.GetRoomBounds();
            Debug.Log($"Hypa Stories room: floor y={floorY:0.00}, bounds x {b.min.x:0.0}..{b.max.x:0.0}, z {b.min.z:0.0}..{b.max.z:0.0}, {room.Anchors.Count} anchors");
            var scan = new RoomScan { Result = RoomScanResult.Found, FloorY = floorY, Clearance = Clearance, Floor = new Rect(b.min.x, b.min.z, b.size.x, b.size.z) };
            scan.Surfaces = Surfaces(room, b.center);
            Debug.Log($"Hypa Stories room: {scan.Surfaces.FindAll(x => x.Kind == PortalKind.Door).Count} door(s), {scan.Surfaces.FindAll(x => x.Kind == PortalKind.Window).Count} window(s), {scan.Surfaces.FindAll(x => x.Kind == PortalKind.Wall).Count} wall(s)");
            return scan;
        }

        /// <summary>Ticket 3L: the doors, windows and walls as flat faces, each turned to face into the room (toward its middle).</summary>
        static List<PortalSurface> Surfaces(MRUKRoom room, Vector3 middle)
        {
            var list = new List<PortalSurface>();
            void Add(MRUKAnchor a, PortalKind kind)
            {
                if (a == null || !a.PlaneRect.HasValue) return;
                var size = a.PlaneRect.Value.size;
                var c = a.GetAnchorCenter();
                var n = a.transform.forward; n.y = 0f;
                if (n.sqrMagnitude < 1e-4f) return;
                n.Normalize();
                if (Vector3.Dot(middle - c, n) < 0f) n = -n; // always toward the inside of the room
                list.Add(new PortalSurface { Center = c, Normal = n, Width = size.x, Height = size.y, Kind = kind });
            }
            foreach (var a in room.Anchors)
            {
                if (a.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME)) Add(a, PortalKind.Door);
                else if (a.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME)) Add(a, PortalKind.Window);
            }
            foreach (var w in room.WallAnchors) Add(w, PortalKind.Wall);
            return list;
        }

        // MRUK reads the room only once the player has allowed it; ask here, and wait for the answer
        static Task<bool> AskScenePermission()
        {
            if (Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission)) return Task.FromResult(true);
            var answer = new TaskCompletionSource<bool>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => answer.TrySetResult(true);
            callbacks.PermissionDenied += _ => answer.TrySetResult(false);
            Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission, callbacks);
            return answer.Task;
        }

        // one MRUK for the app, made when first needed. Off while it wakes, so it doesn't load the room on its own.
        static MRUK EnsureMruk()
        {
            if (MRUK.Instance != null) return MRUK.Instance;
            var go = new GameObject("Hypa MRUK");
            go.SetActive(false);
            var mruk = go.AddComponent<MRUK>();
            mruk.SceneSettings = new MRUK.MRUKSettings { LoadSceneOnStartup = false };
            go.SetActive(true);
            return mruk;
        }
    }
}
