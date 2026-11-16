using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace HypaStories
{
    /// <summary>What the room scan gave back.</summary>
    public enum RoomScanResult
    {
        /// <summary>A scanned room: <see cref="RoomScan.FloorY"/> and <see cref="RoomScan.Clearance"/> are set.</summary>
        Found,
        /// <summary>No room scanned, and the scan flow (Meta's own) was not completed.</summary>
        NoScan,
        /// <summary>The player did not allow the app to see the room.</summary>
        NoPermission,
        /// <summary>No headset scene API here (the editor, the simulator, or a build without it).</summary>
        Unavailable,
    }

    public sealed class RoomScan
    {
        public RoomScanResult Result;
        /// <summary>The real floor's height, world y.</summary>
        public float FloorY;
        /// <summary>Metres from a floor point (world x, z) to the nearest wall or furniture; zero or less outside the room or inside something.</summary>
        public Func<Vector2, float> Clearance;
        /// <summary>The room's floor outline's bounding box (world x, z), for a search of the whole floor.</summary>
        public Rect Floor;
        /// <summary>Ticket 3L: the doors, windows and wall faces of the room, for the portal. Empty if the scan has none.</summary>
        public List<PortalSurface> Surfaces = new List<PortalSurface>();
    }

    /// <summary>
    /// Ticket 3K: the headset side of "Campfire in your room", set by HypaStories.Quest when the Meta XR SDK is installed (the
    /// same seam as <see cref="StoryInput"/>). Without it the camp still goes into the room, with the fire put in front of you and
    /// no hiding behind real things.
    /// </summary>
    public static class StoryRoom
    {
        /// <summary>
        /// Reads the scanned room (Meta's Scene API, MRUK). With no scan yet it starts Meta's room set-up flow and waits for it.
        /// Null = not available. Never throws.
        /// </summary>
        public static Func<Task<RoomScan>> Scan;

        /// <summary>Turns the headset's depth occlusion on or off, so a real table, sofa or body hides the fire and the storyteller in front of them. Null = none.</summary>
        public static Action<bool> Depth;

        /// <summary>True when the headset can show the real room (the passthrough is running).</summary>
        public static Func<bool> Passthrough;
    }
}
