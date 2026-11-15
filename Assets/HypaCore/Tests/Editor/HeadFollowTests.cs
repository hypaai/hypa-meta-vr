using NUnit.Framework;
using UnityEngine;

namespace HypaCore.Tests
{
    /// <summary>Panels follow like Stories' FollowView: in front at eye level, facing the eyes, gliding after a big turn.</summary>
    public class HeadFollowTests
    {
        static readonly Vector3 Head = new Vector3(0, 1.6f, 0);
        const float Dt = 1f / 72f;

        static Pose Run(HeadFollow f, Vector3 head, Quaternion rot, int frames, bool hold = false)
        {
            var p = default(Pose);
            for (var i = 0; i < frames; i++) p = f.Update(head, rot, hold, Dt);
            return p;
        }

        static float YawOff(Pose p, Vector3 head, Quaternion rot)
        {
            var to = p.position - head; to.y = 0f;
            var fwd = rot * Vector3.forward; fwd.y = 0f;
            return Vector3.Angle(fwd, to);
        }

        static float FacingError(Pose p, Vector3 head) => Vector3.Angle(p.rotation * Vector3.forward, p.position - head);

        [Test] public void FirstPlacement_InFront_AtEyeLevelPlusHeight_EvenLookingDown()
        {
            var f = new HeadFollow { Height = -0.1f };
            var p = f.Update(Head, Quaternion.Euler(50f, 30f, 0f), false, Dt);
            var expected = Head + Quaternion.Euler(0, 30f, 0) * Vector3.forward * f.Distance + Vector3.down * 0.1f;
            Assert.That(Vector3.Distance(p.position, expected), Is.LessThan(1e-4f), "not on the floor where the head points");
        }

        [Test] public void AlwaysFacesTheEyes_SoItReadsStraightOn()
        {
            var f = new HeadFollow { Height = -0.3f };
            var p = f.Update(Head, Quaternion.identity, false, Dt);
            Assert.That(FacingError(p, Head), Is.LessThan(0.5f));
            var crouched = Head + Vector3.down * 0.3f;
            p = Run(f, crouched, Quaternion.Euler(-20f, 0, 0), 10);
            Assert.That(FacingError(p, crouched), Is.LessThan(0.5f), "tilts to meet the eyes");
        }

        [Test] public void LookingAroundALittle_LeavesItStill()
        {
            var f = new HeadFollow();
            var p = f.Update(Head, Quaternion.identity, false, Dt);
            var after = Run(f, Head, Quaternion.Euler(30f, f.Threshold - 3f, 0), 120);
            Assert.That(Vector3.Distance(p.position, after.position), Is.LessThan(1e-4f));
        }

        [Test] public void BigTurn_GlidesBackInFront_RoundTheUser()
        {
            var f = new HeadFollow();
            var prev = f.Update(Head, Quaternion.identity, false, Dt);
            var rot = Quaternion.Euler(0, 90f, 0);
            var maxStep = 0f;
            for (var i = 0; i < 300; i++)
            {
                var p = f.Update(Head, rot, false, Dt);
                var step = Vector3.Distance(p.position, prev.position);
                if (i == 0) Assert.That(step, Is.GreaterThan(0f).And.LessThan(0.08f), "glides rather than jumps");
                maxStep = Mathf.Max(maxStep, step);
                var flat = p.position - Head; flat.y = 0f;
                Assert.That(flat.magnitude, Is.GreaterThan(f.Distance * 0.9f), "never cuts through the user");
                prev = p;
            }
            Assert.That(maxStep, Is.LessThan(0.08f));
            Assert.That(YawOff(prev, Head, rot), Is.LessThan(2.5f), "back in front");
            Assert.IsFalse(f.IsFollowing);
        }

        [Test] public void WhileTheHandIsOnIt_AGlideDoesNotStart_ButOneThatStartedFinishes()
        {
            var f = new HeadFollow();
            var p = f.Update(Head, Quaternion.identity, false, Dt);
            var rot = Quaternion.Euler(0, 60f, 0);
            var held = Run(f, Head, rot, 60, hold: true);
            Assert.That(Vector3.Distance(p.position, held.position), Is.LessThan(1e-4f), "aiming at a button doesn't chase it away");
            Run(f, Head, rot, 3);
            var done = Run(f, Head, rot, 300, hold: true);
            Assert.That(YawOff(done, Head, rot), Is.LessThan(2.5f), "a ray it sweeps past doesn't strand it");
        }

        [Test] public void HeadJump_SnapsInFront_AtOnce()
        {
            var f = new HeadFollow();
            f.Update(Vector3.zero, Quaternion.identity, false, Dt); // before tracking: camera on the floor
            var rot = Quaternion.Euler(0, 120f, 0);
            var p = f.Update(Head, rot, false, Dt);
            Assert.That(YawOff(p, Head, rot), Is.LessThan(0.01f));
            Assert.AreEqual(Head.y + f.Height, p.position.y, 1e-4f, "no glide up from the floor");
        }

        [Test] public void ALongFrame_DoesNotMakeItLeap()
        {
            var f = new HeadFollow();
            var p0 = f.Update(Head, Quaternion.identity, false, Dt);
            var p1 = f.Update(Head, Quaternion.Euler(0, 90f, 0), false, 0.8f);
            Assert.That(Vector3.Distance(p0.position, p1.position), Is.LessThan(0.15f));
        }

        [Test] public void Walking_BringsItAlong()
        {
            var f = new HeadFollow();
            f.Update(Head, Quaternion.identity, false, Dt);
            var walked = Head + new Vector3(0f, 0f, 0.45f);
            var p = Run(f, walked, Quaternion.identity, 300);
            Assert.That(Vector3.Distance(p.position, walked + Vector3.forward * f.Distance), Is.LessThan(0.01f));
        }

        [Test] public void FollowPitch_LookingUp_StaysCentred_AndSquareToTheView()
        {
            var f = new HeadFollow { FollowPitch = true, Threshold = 6f };
            f.Update(Head, Quaternion.identity, false, Dt);
            var up = Quaternion.Euler(-35f, 20f, 0f); // looking up and a little right
            var p = Run(f, Head, up, 300);
            Assert.That(Vector3.Angle(up * Vector3.forward, p.position - Head), Is.LessThan(0.6f), "centred where the head points");
            Assert.That(Vector3.Angle(up * Vector3.forward, p.rotation * Vector3.forward), Is.LessThan(0.6f), "reads as if looking straight at it");
            Assert.AreEqual(f.Distance, Vector3.Distance(p.position, Head), 1e-3f);
        }

        [Test] public void FollowPitch_GlideEasesIn_NoKick()
        {
            var f = new HeadFollow { FollowPitch = true, Threshold = 6f };
            var prev = f.Update(Head, Quaternion.identity, false, Dt);
            var rot = Quaternion.Euler(-40f, 0f, 0f);
            var steps = new float[12];
            for (var i = 0; i < steps.Length; i++) { var p = f.Update(Head, rot, false, Dt); steps[i] = Vector3.Distance(p.position, prev.position); prev = p; }
            Assert.That(steps[0], Is.LessThan(steps[5]), "speeds up gently instead of starting at full speed");
        }

        [Test] public void FollowPitch_IsClamped()
        {
            var f = new HeadFollow { FollowPitch = true, MaxPitch = 60f };
            var p = f.Update(Head, Quaternion.Euler(-89f, 0f, 0f), false, Dt);
            var dir = (p.position - Head).normalized;
            Assert.AreEqual(60f, Mathf.Asin(dir.y) * Mathf.Rad2Deg, 0.2f);
        }

        [Test] public void Delay_AGlancePastTheEdge_DoesNotMoveIt_StayingAwayDoes()
        {
            var f = new HeadFollow { FollowPitch = true, Threshold = 25f, Delay = 0.5f };
            var p0 = f.Update(Head, Quaternion.identity, false, Dt);
            var away = Quaternion.Euler(0, 40f, 0);
            var glance = Run(f, Head, away, 20);           // ~0.28 s
            Assert.That(Vector3.Distance(p0.position, glance.position), Is.LessThan(1e-4f), "a glance never moves it");
            var back = Run(f, Head, Quaternion.identity, 30);
            Assert.That(Vector3.Distance(p0.position, back.position), Is.LessThan(1e-4f));
            var stayed = Run(f, Head, away, 300);
            Assert.That(YawOff(stayed, Head, away), Is.LessThan(1f), "staying away brings it along");
        }
    }
}
