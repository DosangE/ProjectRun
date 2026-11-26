using NUnit.Framework;
using ProtoHarness.ChainRush;
using ProtoHarness.ChainRush.Motion;
using ProtoHarness.ChainRush.Track;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProtoHarness.Tests.EditMode
{
    public sealed class SlipstreamTests
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

        private static Slipstream.Spot At(double s, float d = 0f, float h = 0f, int road = -1, float roadSpeed = 20f) =>
            new Slipstream.Spot(new Vector3(d, h, (float)s), new TrackCoord(s, d, h), road, roadSpeed);

        // ---- Behind ----------------------------------------------------------------------------------

        [TestCase(0.4f, false)]
        [TestCase(0.5f, true)]
        [TestCase(12f, true)]
        [TestCase(12.1f, false)]
        [TestCase(-4f, false)]
        public void IsBehind_DistanceAlongTheRoad_OnlyFromRightBehindToTheRange(float gap, bool expected)
        {
            Assert.That(Slipstream.IsBehind(At(100d), At(100d + gap), 400d), Is.EqualTo(expected));
        }

        [TestCase(1.5f, true)]
        [TestCase(-1.5f, true)]
        [TestCase(1.6f, false)]
        [TestCase(-1.6f, false)]
        public void IsBehind_AcrossTheRoad_OnlyInLine(float side, bool expected)
        {
            Assert.That(Slipstream.IsBehind(At(100d), At(104d, side), 400d), Is.EqualTo(expected));
        }

        [TestCase(3f, true)]
        [TestCase(3.1f, false)]
        [TestCase(-3.1f, false)]
        public void IsBehind_HeightAboveTheRoad_WithinAJump(float height, bool expected)
        {
            Assert.That(Slipstream.IsBehind(At(100d), At(104d, 0f, height), 400d), Is.EqualTo(expected));
        }

        [Test]
        public void IsBehind_AnotherRoadOrTooSlowOrAgainstTheRoad_IsNot()
        {
            Assert.That(Slipstream.IsBehind(At(100d), At(104d), 400d), Is.True, "precondition");
            Assert.That(Slipstream.IsBehind(At(100d), At(104d, road: 0), 400d), Is.False, "The other is on a shortcut.");
            Assert.That(Slipstream.IsBehind(At(100d, road: 0), At(104d, road: 0), 400d), Is.True, "Both on the same shortcut.");
            Assert.That(Slipstream.IsBehind(At(100d, roadSpeed: 7.9f), At(104d), 400d), Is.False);
            Assert.That(Slipstream.IsBehind(At(100d), At(104d, roadSpeed: 7.9f), 400d), Is.False);
            Assert.That(Slipstream.IsBehind(At(100d), At(104d, roadSpeed: -20f), 400d), Is.False);
        }

        [Test]
        public void IsBehind_AcrossTheStartLine_OnlyOnALoop()
        {
            // 398 m to 2 m on a 400 m lap, 4 m apart on the ground.
            var runner = new Slipstream.Spot(new Vector3(0f, 0f, -2f), new TrackCoord(398d, 0f, 0f), -1, 20f);
            var ahead = new Slipstream.Spot(new Vector3(0f, 0f, 2f), new TrackCoord(2d, 0f, 0f), -1, 20f);
            Assert.That(Slipstream.IsBehind(runner, ahead, 400d), Is.True);
            Assert.That(Slipstream.IsBehind(ahead, runner, 400d), Is.False);
            Assert.That(Slipstream.IsBehind(runner, ahead, 0d), Is.False, "An open course has no such line.");
        }

        // The space behind a runner bends with the road (user 2026-11-26 "코너 각만큼 그 판정 공간이 꺾여있어야"): on a corner
        // the point behind along the curve is, and the point straight behind the runner's back, off the curve, is not.
        [TestCase(90f)]
        [TestCase(-90f)]
        public void IsBehind_InACorner_FollowsTheCurve(float degrees)
        {
            var line = new Centerline(Vector3.zero, 0f);
            line.AppendStraight(40f);
            line.AppendArc(20f, degrees);
            line.AppendStraight(40f);
            TrackFrame ahead = line.FrameAt(60d);
            Vector3 alongTheCurve = line.FrameAt(49d).Position;
            Vector3 straightBack = ahead.Position - ahead.Forward * 11f;
            Assert.That(Slipstream.IsBehind(OnRoad(line, alongTheCurve), OnRoad(line, ahead.Position), 0d), Is.True, "Along the curve.");
            Assert.That(Mathf.Abs(line.Project(straightBack).D), Is.GreaterThan(Slipstream.HalfWidth), "precondition: off the curve.");
            Assert.That(Slipstream.IsBehind(OnRoad(line, straightBack), OnRoad(line, ahead.Position), 0d), Is.False, "Straight back, off the curve.");
            // Into the corner from the straight before it.
            Assert.That(Slipstream.IsBehind(OnRoad(line, line.FrameAt(37d).Position), OnRoad(line, line.FrameAt(48d).Position), 0d), Is.True);
        }

        private static Slipstream.Spot OnRoad(Centerline line, Vector3 at) => new Slipstream.Spot(at, line.Project(at), line.BranchAt(at), 20f);

        // ---- Charge ----------------------------------------------------------------------------------

        [Test]
        public void Advance_TwoSecondsBehind_BoostsOnItsOwnForOneAndAHalf()
        {
            Assert.That(Slipstream.ChargeTicks, Is.EqualTo(100));
            Assert.That(Slipstream.BoostTicks, Is.EqualTo(75));
            for (int i = 1; i < 100; i++) Assert.That(Slipstream.Advance(racer, true, false), Is.False, "tick " + i);
            Assert.That(racer.DraftChargeTicks, Is.EqualTo(99));
            Assert.That(Slipstream.Advance(racer, true, false), Is.True);
            Assert.That(racer.DraftChargeTicks, Is.Zero);
            Assert.That(racer.DraftBoostTicks, Is.EqualTo(75));
            Assert.That(Slipstream.Advance(racer, true, false), Is.False);
            Assert.That(racer.DraftChargeTicks, Is.Zero, "A boost running charges nothing.");
        }

        // User 2026-11-26: "판정 범위에서 벗어나면 바로 그 시간 스택이 날라간다".
        [Test]
        public void Advance_OneTickOutOfIt_LosesAllTheTime()
        {
            for (int i = 0; i < 99; i++) Slipstream.Advance(racer, true, false);
            Assert.That(Slipstream.Advance(racer, false, false), Is.False);
            Assert.That(racer.DraftChargeTicks, Is.Zero);
            for (int i = 0; i < 99; i++) Assert.That(Slipstream.Advance(racer, true, false), Is.False);
            Assert.That(Slipstream.Advance(racer, true, false), Is.True);
        }

        // User 2026-11-26: "한번쓰고나면 쿨타임 5초(가속 끝나고 나서 부터 5초)".
        [Test]
        public void CountDown_ABoostThatRunsOut_StartsFiveSecondsWithoutACharge()
        {
            Assert.That(Slipstream.CooldownTicks, Is.EqualTo(250));
            racer.DraftBoostTicks = 2;
            Slipstream.CountDown(racer);
            Assert.That(racer.DraftCooldownTicks, Is.Zero, "Still boosting.");
            Slipstream.CountDown(racer);
            Assert.That(racer.DraftBoostTicks, Is.Zero);
            Assert.That(racer.DraftCooldownTicks, Is.EqualTo(250));
            for (int tick = 1; tick <= 250; tick++)
            {
                Assert.That(Slipstream.Advance(racer, true, false), Is.False);
                Assert.That(racer.DraftChargeTicks, Is.Zero, "cooldown tick " + tick);
                Slipstream.CountDown(racer);
            }
            Assert.That(Slipstream.Advance(racer, true, false), Is.False);
            Assert.That(racer.DraftChargeTicks, Is.EqualTo(1), "Charging again on the 251st tick.");
        }

        [Test]
        public void Advance_WithoutTheThrottle_CutsABoostIntoItsCooldownAndHoldsAFullCharge()
        {
            racer.DraftBoostTicks = 40;
            Slipstream.Advance(racer, true, true);
            Assert.That(racer.DraftBoostTicks, Is.Zero);
            Assert.That(racer.DraftCooldownTicks, Is.EqualTo(Slipstream.CooldownTicks), "A cut boost has ended too.");
            racer.DraftCooldownTicks = 0;
            for (int i = 0; i < 200; i++) Assert.That(Slipstream.Advance(racer, true, true), Is.False);
            Assert.That(Slipstream.Advance(racer, true, false), Is.True, "The throttle fires it.");
        }

        // ---- Speed -----------------------------------------------------------------------------------

        // "드래프트는 속도 상한에 안걸리도록" (user 2026-11-26): the boost goes past the run's, the booster's and the swing's
        // speeds by the same 6 m/s, not up to the largest of them.
        [TestCase(false, 20f, 26f)]
        [TestCase(true, 32f, 38f)]
        public void Drive_Drafting_AddsToTheRunAndToABooster(bool pulling, float without, float with)
        {
            Assert.That(Settle(pulling, false), Is.EqualTo(without).Within(0.6f));
            Assert.That(Settle(pulling, true), Is.EqualTo(with).Within(0.6f));
        }

        private float Settle(bool pulling, bool drafting)
        {
            racer.Reset();
            racer.DraftBoostTicks = drafting ? 1 : 0;
            racer.Velocity = Vector3.forward * 20f;
            var now = new RunDrive.Conditions { Grounded = true, Pulling = pulling, Drafting = drafting };
            for (int i = 0; i < 400; i++) RunDrive.Drive(racer, 0f, now, 60f, Dt, Drive(), Sling());
            return racer.Velocity.z;
        }

        [TestCase(false, 24f, 30f)]
        [TestCase(true, 32f, 38f)]
        public void SpeedUp_DraftingOnASwing_AddsToTheSwingsSpeed(bool pulling, float without, float with)
        {
            Assert.That(Swing(pulling, false), Is.EqualTo(without).Within(0.6f));
            Assert.That(Swing(pulling, true), Is.EqualTo(with).Within(0.6f));
        }

        private float Swing(bool pulling, bool drafting)
        {
            racer.Reset();
            racer.DraftBoostTicks = drafting ? 1 : 0;
            racer.SwingHookSpeed = 20f;
            var swing = new PoleSwing.Settings { RunSpeed = 20f, RunAcceleration = 8f, ThrottleSpeedScale = 1.2f, CarryDecay = 0.8f };
            for (int i = 0; i < 400; i++) PoleSwing.SpeedUp(racer, true, false, pulling, false, Dt, swing, Sling());
            return racer.SwingHookSpeed;
        }

        [Test]
        public void TurnLimit_Drafting_TurnsNoFasterThanTheRunDoes()
        {
            RunDrive.Settings drive = Drive();
            float run = RunDrive.TurnLimit(Vector3.forward * 20f, RunDrive.TurnRadiusScale(racer, drive), drive);
            racer.DraftBoostTicks = 1;
            Assert.That(RunDrive.TurnLimit(Vector3.forward * 26f, RunDrive.TurnRadiusScale(racer, drive), drive), Is.EqualTo(run).Within(0.001f));
        }

        // The circuit scene's values.
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
