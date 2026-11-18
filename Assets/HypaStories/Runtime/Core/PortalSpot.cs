using System;
using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>What a scanned surface is, for the portal. Windows are never walked through, so the portal keeps clear of them.</summary>
    public enum PortalKind { Door, Wall, Window }

    /// <summary>A flat piece of the real room the portal can use (or must avoid): world position of its centre, the way it faces into the room.</summary>
    public struct PortalSurface
    {
        public Vector3 Center;
        /// <summary>Unit, horizontal, pointing into the room.</summary>
        public Vector3 Normal;
        public float Width, Height;
        public PortalKind Kind;
    }

    /// <summary>Where the portal stands: its base on the floor, the way it faces (into the room), and its size.</summary>
    public struct PortalPlan
    {
        /// <summary>Centre of the doorway's foot, on the floor (world).</summary>
        public Vector3 Base;
        /// <summary>Unit, horizontal: toward you, the side you step through from.</summary>
        public Vector3 Normal;
        public float Width, Height;
        public PortalKind Kind;
        /// <summary>False when there was no scan and the portal was just put in front of you.</summary>
        public bool Scanned;

        public Vector3 Tangent => new Vector3(Normal.z, 0f, -Normal.x);
        public float Yaw => Mathf.Atan2(Normal.x, Normal.z) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Ticket 3L: where the portal to the hut goes in your room, and when you have stepped through it. A real door if the scan found one,
    /// else a clear stretch of wall, never a spot you can't walk to (open floor in front of it, away from the fire), never over a window.
    /// With no scan it stands 2 m in front of you. Pure, unit tested.
    /// </summary>
    public static class PortalSpot
    {
        public const float DoorWidthMin = 0.6f, DoorWidthMax = 1.7f, DoorHeightMin = 1.7f, DoorHeightMax = 2.5f;
        public const float WallWidth = 0.95f, WallHeight = 2.05f;
        public const float InFrontDistance = 2f;

        const float Step = 0.5f, EdgeMargin = 0.6f, FrontNear = 0.9f, FrontFar = 1.6f, FrontClear = 0.35f, FromFire = 1.4f, WindowKeepOut = 0.7f;
        const float MinFromUser = 0.9f, MaxFromUser = 6f;

        /// <summary>How far past the doorway (m) your head goes to count as stepping through it, and so the same line you step back over.</summary>
        public const float Through = 0.3f;

        /// <summary>
        /// Every real door the scan found, nearest to you first, each laid on the door itself at its real size (within sane limits). No
        /// walking-room tests: a door is where it is. Pure, unit tested.
        /// </summary>
        public static List<PortalPlan> Doors(Vector2 user, IList<PortalSurface> surfaces, float floorY)
        {
            var list = new List<PortalPlan>();
            if (surfaces == null) return list;
            foreach (var s in surfaces)
            {
                if (s.Kind != PortalKind.Door) continue;
                var n = new Vector3(s.Normal.x, 0f, s.Normal.z);
                if (n.sqrMagnitude < 1e-6f) continue;
                list.Add(new PortalPlan
                {
                    Base = new Vector3(s.Center.x, floorY, s.Center.z), Normal = n.normalized,
                    Width = Mathf.Clamp(s.Width, DoorWidthMin, DoorWidthMax), Height = Mathf.Clamp(s.Height, DoorHeightMin, DoorHeightMax),
                    Kind = PortalKind.Door, Scanned = true,
                });
            }
            list.Sort((a, b) => Vector2.Distance(user, new Vector2(a.Base.x, a.Base.z)).CompareTo(Vector2.Distance(user, new Vector2(b.Base.x, b.Base.z))));
            return list;
        }

        /// <summary>A door if there is a usable one, else open wall. Null when nothing in the room fits.</summary>
        public static PortalPlan? Find(Vector2 user, Vector2 forward, IList<PortalSurface> surfaces, float floorY,
            Func<Vector2, float> clearance, Vector2? avoid = null)
        {
            if (surfaces == null || clearance == null) return null;
            var look = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector2.up;
            PortalPlan? best = null;
            var bestScore = float.NegativeInfinity;

            void Try(Vector3 at, Vector3 normal, float width, float height, PortalKind kind)
            {
                var n2 = new Vector2(normal.x, normal.z);
                var b2 = new Vector2(at.x, at.z);
                var near = b2 + n2 * FrontNear;
                var far = b2 + n2 * FrontFar;
                if (clearance(near) < FrontClear || clearance(far) < FrontClear) return;     // can't walk up to it
                if (avoid.HasValue && Vector2.Distance(near, avoid.Value) < FromFire) return; // the fire is in the way
                var d = Vector2.Distance(near, user);
                if (d < MinFromUser || d > MaxFromUser) return;
                var toward = (b2 - user).normalized;
                var score = (kind == PortalKind.Door ? 100f : 0f) + Vector2.Dot(toward, look) * 8f - Mathf.Abs(d - 2.5f) * 2f
                            + Mathf.Min(clearance(far), 1.5f) * 2f;
                if (score <= bestScore) return;
                bestScore = score;
                best = new PortalPlan
                {
                    Base = new Vector3(at.x, floorY, at.z), Normal = new Vector3(normal.x, 0f, normal.z).normalized,
                    Width = width, Height = height, Kind = kind, Scanned = true,
                };
            }

            foreach (var s in surfaces)
            {
                if (s.Kind == PortalKind.Window) continue;
                if (s.Kind == PortalKind.Door)
                {
                    Try(s.Center, s.Normal, Mathf.Clamp(s.Width, DoorWidthMin, DoorWidthMax), Mathf.Clamp(s.Height, DoorHeightMin, DoorHeightMax), PortalKind.Door);
                    continue;
                }
                if (s.Height < WallHeight) continue; // too low to hold a doorway
                var tangent = new Vector3(s.Normal.z, 0f, -s.Normal.x);
                var half = s.Width * 0.5f - EdgeMargin;
                for (var along = -half; along <= half + 1e-4f; along += Step)
                {
                    var at = s.Center + tangent * along;
                    if (TooCloseToOpening(at, surfaces)) continue;
                    Try(at, s.Normal, WallWidth, WallHeight, PortalKind.Wall);
                }
            }
            return best;
        }

        /// <summary>Another door or a window sits here (within its width and a little more), so a wall doorway would overlap it.</summary>
        static bool TooCloseToOpening(Vector3 at, IList<PortalSurface> surfaces)
        {
            foreach (var o in surfaces)
            {
                if (o.Kind == PortalKind.Wall) continue;
                var d = new Vector2(at.x - o.Center.x, at.z - o.Center.z).magnitude;
                if (d < o.Width * 0.5f + WindowKeepOut) return true;
            }
            return false;
        }

        /// <summary>
        /// No scan: the doorway stands <see cref="InFrontDistance"/> m in front of you, facing you. If the fire is there, it moves a little to
        /// the side (the first of straight ahead, then 40 and 80 degrees either way that clears the fire).
        /// </summary>
        public static PortalPlan InFront(Vector2 user, Vector2 forward, float floorY, Vector2? fire = null)
        {
            var look = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector2.up;
            var angles = new[] { 0f, 40f, -40f, 80f, -80f };
            var pick = look;
            foreach (var a in angles)
            {
                var dir = (Vector2)(Quaternion.Euler(0f, 0f, -a) * (Vector3)look);
                pick = dir;
                if (!fire.HasValue || Vector2.Distance(user + dir * InFrontDistance, fire.Value) >= FromFire) break;
            }
            var at = user + pick * InFrontDistance;
            return new PortalPlan
            {
                Base = new Vector3(at.x, floorY, at.y), Normal = new Vector3(-pick.x, 0f, -pick.y),
                Width = WallWidth, Height = WallHeight, Kind = PortalKind.Wall, Scanned = false,
            };
        }

        // ---------- stepping through ----------

        /// <summary>Head relative to the doorway: depth (+ = your side, - = through), sideways across it, and height above the floor.</summary>
        public static Vector3 Local(PortalPlan p, Vector3 head)
        {
            var d = head - p.Base;
            return new Vector3(Vector3.Dot(d, p.Tangent), head.y - p.Base.y, Vector3.Dot(d, p.Normal)); // x across, y up, z depth
        }

        /// <summary>You went from your side to the far side of the doorway, through the opening (not round it). Pure.</summary>
        public static bool SteppedIn(float prevDepth, float depth, float across, float up, float width, float height, float through = 0f)
            => prevDepth > through && depth <= through && prevDepth - depth < 1f && Mathf.Abs(across) <= width * 0.5f + 0.15f && up >= 0f && up <= height + 0.6f;

        /// <summary>The same the other way: coming out through the hut's door (depth is measured on the door's outside, + = outside). Pure.</summary>
        public static bool SteppedOut(float prevDepth, float depth, float across, float width)
            => prevDepth < 0f && depth >= 0f && depth - prevDepth < 1f && Mathf.Abs(across) <= width * 0.5f + 0.15f;

        /// <summary>0 far away .. 1 at the glass: how strongly the portal pulls as you come near (ripples, streaks). Pure.</summary>
        public static float Pull(float depth, float across, float width)
        {
            if (depth <= 0f) return 1f;
            var lateral = Mathf.Max(0f, Mathf.Abs(across) - width * 0.5f);
            var d = Mathf.Sqrt(depth * depth + lateral * lateral);
            return Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01((d - 0.15f) / 1.6f));
        }

        /// <summary>
        /// Where the camp's world goes so the hut's door (camp ground x, z; facing <paramref name="doorDir"/> out) lies exactly on the portal:
        /// the hut is then right through the glass, and stepping through needs no jump. Pure, unit tested.
        /// </summary>
        public static Pose WorldPose(PortalPlan p, Vector2 door, Vector2 doorDir)
        {
            var rot = Quaternion.LookRotation(p.Normal) * Quaternion.Inverse(Quaternion.LookRotation(new Vector3(doorDir.x, 0f, doorDir.y)));
            return new Pose(p.Base - rot * new Vector3(door.x, 0f, door.y), rot);
        }
    }
}
