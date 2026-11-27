using System;
using NUnit.Framework;
using ProtoHarness.ChainRush;
using ProtoHarness.ChainRush.Motion;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProtoHarness.Tests.EditMode
{
    // Catching up (Motion.Catchup): the pull by the gap to the leader, and how the running rules add it.
    public sealed class CatchupTests
    {
        private const float Dt = Ticks.Seconds;
        private RunRules rules;
        private RacerState racer;

        [SetUp]
        public void SetUp()
        {
            rules = ScriptableObject.CreateInstance<RunRules>();
            racer = new RacerState(rules);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(rules);

        // ---- Share -----------------------------------------------------------------------------------

        [TestCase(0d, 0f)]
        [TestCase(8d, 0f)]
        [TestCase(10d, 0.025f)]
        [TestCase(12d, 0.05f)]
        [TestCase(81d, 0.10f)]
        [TestCase(150d, 0.15f)]
        [TestCase(288d, 0.25f)]
        [TestCase(400d, 0.25f)]
        public void Share_ByTheGapToTheLeader_RisesFromRightBehindToTheFullGap(double gap, float expected)
        {
            Assert.That(Catchup.Share(gap, 1f), Is.EqualTo(expected).Within(1e-5f));
        }

        // "가까워질수록 그 견인 수준은 떨어져야" (user 2026-11-27).
        [TestCase(1f)]
        [TestCase(1.2f)]
        public void Share_AsTheGapCloses_NeverGrows(float speedClassScale)
        {
            float last = Catchup.Share(400d, speedClassScale);
            for (double gap = 399.5d; gap >= 0d; gap -= 0.5d)
            {
                float share = Catchup.Share(gap, speedClassScale);
                Assert.That(share, Is.LessThanOrEqualTo(last), "gap " + gap);
                last = share;
            }
        }

        // A runner just outside the draft's range is still pulled toward it: an equal runner behind would stop short of it.
        [Test]
        public void Share_JustOutsideTheDraftsRange_StillPulls()
        {
            Assert.That(Catchup.Share(Slipstream.Range + 0.1d, 1f), Is.GreaterThan(0f));
            Assert.That(Catchup.Share(Catchup.NoneWithin, 1f), Is.Zero);
        }

        [Test]
        public void Share_OnAFasterClass_StretchesOnlyTheFarEnd()
        {
            Assert.That(Catchup.Share(288d, 1.2f), Is.LessThan(Catchup.MaxShare));
            Assert.That(Catchup.Share(346d, 1.2f), Is.EqualTo(Catchup.MaxShare));
            Assert.That(Catchup.Share(Slipstream.Range, 1.2f), Is.EqualTo(Catchup.ShareAtDraftRange).Within(1e-6f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Share_ABadSpeedClassScale_Throws(float speedClassScale)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Catchup.Share(100d, speedClassScale));
        }

        // A class so slow that the full pull would come inside the draft's range has no line to rise along; just above that
        // it has (the boundary itself is not exact in float: 12 / 288 rounds up).
        [Test]
        public void Supports_AClassWhoseFullPullIsInsideTheDraftsRange_IsRefused()
        {
            float edge = Slipstream.Range / Catchup.FullGap;
            Assert.That(Catchup.Supports(edge * 0.999f), Is.False);
            Assert.That(Catchup.Supports(edge * 1.001f), Is.True);
            Assert.That(Catchup.Supports(0.8f) && Catchup.Supports(1f) && Catchup.Supports(1.2f), Is.True, "Every lobby speed class.");
            Assert.Throws<ArgumentOutOfRangeException>(() => Catchup.Share(0d, edge * 0.999f), "Refused at any gap, right behind too.");
        }

        // The leader is the furthest, so a runner is never ahead of it: a gap below zero is the caller's mistake (§5).
        [TestCase(double.NaN)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-0.001d)]
        public void Share_AGapThatIsNotANumberOrBelowZero_Throws(double gap)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Catchup.Share(gap, 1f));
        }

        // ---- Speed -----------------------------------------------------------------------------------

        // On top of the run and of a booster alike, as a draft's boost is.
        [TestCase(false, 20f, 23f)]
        [TestCase(true, 32f, 35f)]
        public void Drive_PulledAlong_AddsToTheRunAndToABooster(bool pulling, float without, float with)
        {
            Assert.That(Settle(pulling, 0f), Is.EqualTo(without).Within(0.6f));
            Assert.That(Settle(pulling, 3f), Is.EqualTo(with).Within(0.6f));
        }

        private float Settle(bool pulling, float catchup)
        {
            racer.Reset();
            racer.Velocity = Vector3.forward * 20f;
            var now = new RunDrive.Conditions { Grounded = true, Pulling = pulling, Catchup = catchup };
            for (int i = 0; i < 400; i++) RunDrive.Drive(racer, 0f, now, 60f, Dt, Drive(), Sling());
            return racer.Velocity.z;
        }

        // Without the throttle the runner rolls to a stop, pulled or not.
        [Test]
        public void Drive_PulledAlongWithoutTheThrottle_RollsToAStop()
        {
            racer.Velocity = Vector3.forward * 20f;
            RunDrive.Settings drive = Drive();
            drive.CoastDeceleration = 10f;
            var now = new RunDrive.Conditions { Grounded = true, Coasting = true, Catchup = 3f };
            for (int i = 0; i < 200; i++) RunDrive.Drive(racer, 0f, now, 60f, Dt, drive, Sling());
            Assert.That(racer.Velocity.z, Is.EqualTo(0f).Within(0.001f));
        }

        // A pad's carry is not raised; the larger of the two holds, so a pull above what is left of the carry wins.
        [TestCase(55f, 55f)]
        [TestCase(22f, 23f)]
        public void Drive_PulledAlongOnAPadsCarry_GoesByTheLargerOfTheTwo(float carry, float expected)
        {
            racer.BoostZoneCarrySpeed = carry;
            racer.Velocity = Vector3.forward * carry;
            var now = new RunDrive.Conditions { Grounded = true, Catchup = 3f };
            float top = 0f;
            for (int i = 0; i < 100; i++)
            {
                RunDrive.Drive(racer, 0f, now, 60f, Dt, Drive(), Sling());
                top = Mathf.Max(top, racer.Velocity.z);
            }
            Assert.That(top, Is.LessThanOrEqualTo(expected + 0.001f));
            Assert.That(racer.Velocity.z, Is.EqualTo(expected).Within(0.05f));
        }

        // "기존 스윙의 속도 증가분에서 견인만큼의 속도만 증가하게 (비율X +되는 정도만)" (user 2026-11-27): the swing keeps its own
        // speed by its own rules and goes the pull faster.
        [TestCase(24f, 0f, 24f)]
        [TestCase(24f, 5f, 29f)]
        [TestCase(32f, 5f, 37f)]
        public void Step_PulledAlong_SwingsThePullFasterThanItsOwnSpeed(float own, float pull, float expected)
        {
            Assert.That(SwingStep(own, pull), Is.EqualTo(expected).Within(1e-4f));
        }

        // Hooked, the swing starts at the speed the runner came in with: its own speed leaves the pull out.
        [Test]
        public void Hook_PulledAlong_StartsAtTheSpeedItCameInWith()
        {
            racer.Velocity = Vector3.forward * 25f;
            PoleSwing.Hook(racer, new Vector3(10f, 0f, 0f), Vector3.zero, 5f);
            Assert.That(racer.SwingHookSpeed, Is.EqualTo(20f).Within(1e-5f));
            Assert.That(SwingStep(racer.SwingHookSpeed, 5f), Is.EqualTo(25f).Within(1e-4f));
        }

        // The swing's own speed rises by its own push, the same pulled or not (no ratio, no push of its own).
        [Test]
        public void SpeedUp_PulledOrNot_RisesTheSame()
        {
            var swing = new PoleSwing.Settings { RunSpeed = 20f, RunAcceleration = 8f, ThrottleSpeedScale = 1.2f, CarryDecay = 0.8f };
            racer.SwingHookSpeed = 20f;
            for (int i = 0; i < 25; i++) PoleSwing.SpeedUp(racer, true, false, false, false, Dt, swing, Sling());
            Assert.That(racer.SwingHookSpeed, Is.EqualTo(24f).Within(1e-4f), "Half a second at 8 m/s^2.");
            Assert.That(SwingStep(racer.SwingHookSpeed, 5f), Is.EqualTo(29f).Within(1e-4f));
        }

        // The swing's speed after one step round a pole 10 m off, with its own speed own and the pull on top.
        private float SwingStep(float own, float pull)
        {
            racer.SwingCenter = Vector3.zero;
            racer.SwingRadius = 10f;
            racer.SwingHookSpeed = own;
            racer.Steer = 0f;
            Vector3 velocity = Vector3.forward * own;
            float heading = 0f;
            Vector3 displacement = Vector3.zero;
            var swing = new PoleSwing.Settings { RopeRate = 6f, MinRopeLength = 2f, MetersPerSlot = 1000f, RunSpeed = 20f, RunAcceleration = 8f, ThrottleSpeedScale = 1.2f, CarryDecay = 0.8f };
            PoleSwing.Step(racer, new Vector3(10f, 0f, 0f), ref velocity, ref heading, ref displacement, Dt, swing, out Vector3 _, pull);
            return new Vector2(velocity.x, velocity.z).magnitude;
        }

        // A pulled swing let go heads for the speed it swung with: the carry leaves the pull out and the run adds it again,
        // neither twice nor lost.
        [Test]
        public void End_APulledSwingLetGo_KeepsTheSpeedItSwungWith()
        {
            const float swung = 27f, catchup = 3f;
            racer.SwingTicks = 10;
            PoleSwing.End(racer, Vector3.forward * swung, 0f, catchup);
            racer.Velocity = Vector3.forward * swung;
            var now = new RunDrive.Conditions { Grounded = true, Catchup = catchup };
            float top = 0f;
            for (int i = 0; i < 30; i++)
            {
                RunDrive.Drive(racer, 0f, now, 60f, Dt, Drive(), Sling());
                top = Mathf.Max(top, racer.Velocity.z);
            }
            Assert.That(top, Is.LessThanOrEqualTo(swung + 0.001f));
            Assert.That(racer.Velocity.z, Is.EqualTo(swung).Within(0.05f));
        }

        // The circuit scene's values (SlipstreamTests).
        private static RunDrive.Settings Drive() => new RunDrive.Settings
        {
            RunSpeed = 20f, RunAcceleration = 8f, MaxTurnRate = 120f, MinTurnRadius = 25f, BoostTurnRadiusScale = 2f, TurnFalloffScale = 1f,
            SideGrip = 60f, SlopeSpeedFactor = 1.5f, MinSlopeSpeedScale = 0.7f, MaxSlopeSpeedScale = 1.3f, OverspeedFalloff = 8f, Gravity = 22f,
        };

        private static Slingshot.Settings Sling() => new Slingshot.Settings
        {
            PullAcceleration = 45f, TopSpeed = 32f, CarrySpeed = 32f, OverspeedFalloff = 8f,
        };
    }
}
