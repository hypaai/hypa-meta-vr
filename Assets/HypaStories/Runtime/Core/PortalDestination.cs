using UnityEngine;

namespace HypaStories
{
    /// <summary>Where the real door leads (ticket 3L): the doorway shows it, a step through takes you there.</summary>
    public enum PortalDestination { Hut, Village, Camp }

    /// <summary>
    /// The doorway of each destination in the camp's ground space, and the maths that links the real doorway to it: the doorway in your room
    /// and the destination's doorway are the same frame, so what you see through the glass is what you step into. Pure, unit tested.
    /// </summary>
    public static class PortalDestinations
    {
        /// <summary>The camp has no door, so it gets one: 4 m south of the fire (the way you sit at the story), the fire straight ahead of you.</summary>
        public static readonly Vector2 CampDoor = new Vector2(0f, -4.2f), CampDoorDir = new Vector2(0f, -1f);

        /// <summary>How far in you land (m): a step back across the doorway is then a real step.</summary>
        public const float LandInside = 0.45f;

        public static readonly PortalDestination[] All = { PortalDestination.Hut, PortalDestination.Village, PortalDestination.Camp };

        public static string Label(PortalDestination d) => d == PortalDestination.Hut ? "Hut" : d == PortalDestination.Village ? "Village" : "Campfire";
        public static string Icon(PortalDestination d) => d == PortalDestination.Hut ? "house" : d == PortalDestination.Village ? "users" : "flame";

        /// <summary>The doorway (camp ground x, z) of the place.</summary>
        public static Vector2 Door(PortalDestination d) =>
            d == PortalDestination.Hut ? HutTrailLayout.Door : d == PortalDestination.Village ? VillageLayout.Gate : CampDoor;

        /// <summary>The unit direction out of the place through its doorway (toward the side you are on before you step in).</summary>
        public static Vector2 DoorDir(PortalDestination d) =>
            d == PortalDestination.Hut ? HutTrailLayout.DoorDir : d == PortalDestination.Village ? -VillageLayout.Into : CampDoorDir;

        /// <summary>A frame at <paramref name="pos"/> whose +Z is <paramref name="forward"/> (level), unit scale.</summary>
        public static Matrix4x4 Frame(Vector3 pos, Vector3 forward) => Matrix4x4.TRS(pos, Quaternion.LookRotation(forward), Vector3.one);

        /// <summary>
        /// Takes anything seen from your side of the real doorway to the same place in front of the destination's doorway: destination frame
        /// (world) times the real doorway's frame inverse. <paramref name="doorWorld"/> is the destination's doorway in world space now.
        /// </summary>
        public static Matrix4x4 Link(Matrix4x4 portalFrame, Matrix4x4 doorWorld) => doorWorld * portalFrame.inverse;

        /// <summary>The view of a camera standing where your eye is, but looking from the other side of the link (worldToCamera form).</summary>
        public static Matrix4x4 VirtualView(Matrix4x4 eyeView, Matrix4x4 link) => eyeView * link.inverse;

        /// <summary>The doorway's frame in world space, from the camp's world transform and the place.</summary>
        public static Matrix4x4 DoorWorld(Matrix4x4 worldToWorld, PortalDestination d)
        {
            var door = Door(d); var dir = DoorDir(d);
            return worldToWorld * Frame(new Vector3(door.x, 0f, door.y), new Vector3(dir.x, 0f, dir.y));
        }
    }
}
