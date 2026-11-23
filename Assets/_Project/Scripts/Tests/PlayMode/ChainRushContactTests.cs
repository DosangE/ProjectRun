using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ProtoHarness.ChainRush;
using ProtoHarness.ChainRush.Bots;
using ProtoHarness.ChainRush.Control;
using ProtoHarness.ChainRush.Flow;
using ProtoHarness.ChainRush.Motion;
using ProtoHarness.ChainRush.Race;
using ProtoHarness.ChainRush.Track;
using ProtoHarness.ChainRush.Visuals;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace ProtoHarness.Tests.PlayMode
{
    public sealed class ChainRushContactTests
    {
        private const string RacePath = "Assets/_Project/Scenes/ChainRushCircuit.unity";
        private static readonly MethodInfo FieldTick = typeof(RaceField).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ContactPass = typeof(RaceField).GetMethod("ResolveContact", BindingFlags.Instance | BindingFlags.NonPublic);
        private RaceWorld player;
        private RaceAssembler assembler;
        private RaceWorld[] worlds;
        private RaceField Field => assembler.Field;
        private float timeScale;
        private float volume;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            timeScale = Time.timeScale;
            volume = AudioListener.volume;
            AudioListener.volume = 0f;
            Time.timeScale = 0f;
            RaceLaunch.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (worlds != null)
                foreach (RaceWorld world in worlds) if (world.Game != null) world.Game.SetInputSource(Idle());
            RaceLaunch.Clear();
            Time.timeScale = timeScale;
            AudioListener.volume = volume;
            yield return null;
            TestLogs.AssertNoUnexpected();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_OverlappingRunners_SeparateOnTheNextTickAndHeavierRunnerChangesLess()
        {
            yield return Load();
            Begin();
            ParkUnused();
            Place(worlds[1], new Vector3(-0.3f, 1.05f, 10f), Vector3.right * 8f); // OMEGA 1.2
            Place(worlds[2], new Vector3(0.3f, 1.05f, 10f), Vector3.right * 2f);  // BETA 0.9
            Vector3 a = worlds[1].Player.transform.position, b = worlds[2].Player.transform.position;
            Contact();
            float heavyMove = Mathf.Abs(worlds[1].Player.transform.position.x - a.x);
            float lightMove = Mathf.Abs(worlds[2].Player.transform.position.x - b.x);
            Assert.That(heavyMove, Is.GreaterThan(0f).And.LessThan(lightMove));
            Assert.That(8f - worlds[1].Player.Velocity.x, Is.LessThan(worlds[2].Player.Velocity.x - 2f));
            AssertSeparated(worlds[1], worlds[2], 0.001f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_RearImpact_SlowsRearAcceleratesFrontAndConservesMomentum()
        {
            yield return Load();
            Begin();
            ParkUnused();
            Place(worlds[1], new Vector3(0f, 1.05f, 10f), Vector3.forward * 8f);
            Place(worlds[2], new Vector3(0f, 1.05f, 10.6f), Vector3.forward * 2f);
            float before = 1.2f * 8f + 0.9f * 2f;
            Contact();
            float rear = worlds[1].Player.Velocity.z, front = worlds[2].Player.Velocity.z;
            Assert.That(rear, Is.LessThan(8f));
            Assert.That(front, Is.GreaterThan(2f));
            Assert.That(1.2f * rear + 0.9f * front, Is.EqualTo(before).Within(0.00001f));
            Assert.That(front - rear, Is.EqualTo(6f * RunnerContact.Restitution).Within(0.00001f));
            AssertSeparated(worlds[1], worlds[2], 0.001f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_SideImpact_DividesSidewaysAndLeavesForwardVelocityAlone()
        {
            yield return Load();
            Begin();
            ParkUnused();
            Place(worlds[1], new Vector3(-0.3f, 1.05f, 10f), new Vector3(4f, 0f, 7f));
            Place(worlds[2], new Vector3(0.3f, 1.05f, 10f), new Vector3(-4f, 0f, 7f));
            Contact();
            Assert.That(worlds[1].Player.transform.position.x, Is.LessThan(-0.3f));
            Assert.That(worlds[2].Player.transform.position.x, Is.GreaterThan(0.3f));
            Assert.That(worlds[1].Player.Velocity.z, Is.EqualTo(7f));
            Assert.That(worlds[2].Player.Velocity.z, Is.EqualTo(7f));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_WallPressureWithTwoAndThreeRunners_DoesNotCrossTheWallAndLeavesAtMostTwoCentimetres()
        {
            yield return Load();
            Begin();
            for (int count = 2; count <= 3; count++)
            {
                ParkUnused();
                float width = player.Circuit.Definition.HalfWidthAt(10d);
                for (int i = 0; i < count; i++)
                {
                    float radius = worlds[i].Player.ContactRadius;
                    Place(worlds[i], new Vector3(width - radius - i * (radius * 2f - 0.04f), 1.05f, 10f), Vector3.zero);
                }
                Contact();
                for (int i = 0; i < count; i++)
                {
                    CharacterController capsule = worlds[i].Player.GetComponent<CharacterController>();
                    // CharacterController's documented collision envelope includes its skin; no capsule crosses it.
                    Assert.That(worlds[i].Player.transform.position.x + capsule.radius - capsule.skinWidth,
                        Is.LessThanOrEqualTo(width + 0.001f), $"{count} runners: wall, racer {i}");
                    for (int j = i + 1; j < count; j++) AssertSeparated(worlds[i], worlds[j], 0.02f);
                }
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_PushedPastGapEdge_LosesGroundAndFalls()
        {
            yield return Load();
            Begin();
            double edge = player.Circuit.Definition.Gaps()[0].Start;
            TrackFrame frame = player.Game.Track.FrameAt(edge - 0.45d);
            Place(player, frame.TransformPoint(new Vector3(0f, 1.05f, 0f)), Vector3.zero);
            player.Player.ApplyContact(frame.Forward * 1.3f, Vector3.zero);
            Assert.That(player.Player.IsGrounded, Is.False);
            float height = player.Player.transform.position.y;
            for (int i = 0; i < 12; i++) Step();
            Assert.That(player.Player.transform.position.y, Is.LessThan(height - 0.2f));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_SwingBreaks_GrappleStays_AndNoTimerBoosterOrGaugeAdvances()
        {
            yield return Load();
            Begin();
            ParkUnused();
            Place(worlds[1], new Vector3(-0.3f, 4f, 10f), Vector3.zero);
            Place(worlds[2], new Vector3(0.3f, 4f, 10f), Vector3.zero);
            RacerState swing = worlds[1].Game.Racer, grapple = worlds[2].Game.Racer;
            PoleSwing.Hook(swing, worlds[1].Player.transform.position, new Vector3(-5f, 0f, 10f));
            grapple.Attach(0, 9f);
            swing.SlingTicks = 17;
            swing.BoostZoneCarrySpeed = 35f;
            swing.AddGauge(1.25f);
            int tick = worlds[1].Game.Tick;
            float gauge = swing.Gauge;
            int anchor = grapple.AnchorIndex;
            ContactPass.Invoke(Field, null); // Same three real-position passes used by FixedUpdate, isolated from the tick.
            Assert.That(worlds[1].Player.IsSwinging, Is.False);
            Assert.That(worlds[2].Grapple.IsAttached, Is.True);
            Assert.That(grapple.AnchorIndex, Is.EqualTo(anchor));
            Assert.That(grapple.RopeLength, Is.EqualTo(9f));
            Assert.That(worlds[1].Game.Tick, Is.EqualTo(tick));
            Assert.That(swing.SlingTicks, Is.EqualTo(17));
            Assert.That(swing.RespawnHoldTicks, Is.EqualTo(0), "Contact starts no respawn hold.");
            Assert.That(swing.BoostZoneCarrySpeed, Is.EqualTo(35f));
            Assert.That(swing.Gauge, Is.EqualTo(gauge));
        }

        // A bot whose swing a contact broke hooks nothing new for BotTuning.ContactRehookTicks (user 2026-11-16, "다시 스윙을
        // 거는 속도는 0.5초로"), even with a corner pole in reach the whole time, then hooks on the next tick. Without the wait
        // the press comes on the first tick. The spot is where a Hard bot presses a STADIUM corner hook (S 140, tick 415 of
        // its solo trace).
        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_BrokenBotSwing_WaitsTheRehookTicksWithAPoleInReach_ThenHooks()
        {
            yield return Load();
            Begin();
            ParkUnused();
            RaceWorld bot = worlds[1], other = worlds[2];
            var spot = new Vector3(3.854f, 1.05f, 140.09f);
            Place(bot, spot, Vector3.zero);
            Assert.That(bot.Player.IsGrounded, Is.True, "precondition: on the road.");
            Assert.That(bot.Game.Track.Frame(bot.Player.transform.position).Curvature, Is.Not.EqualTo(0f), "precondition: in the corner.");
            Assert.That(bot.Player.TryFindPole(out Vector3 pole), Is.True, "precondition: the corner pole is in reach.");
            PoleSwing.Hook(bot.Game.Racer, bot.Player.transform.position, pole);
            var input = new RaceBot(bot.Game, bot.Player, bot.Grapple, bot.Circuit, BotTuning.Hard);
            input.Observe(Field, 1);
            Place(other, bot.Player.transform.position + Vector3.right * 0.3f, Vector3.zero);
            ContactPass.Invoke(Field, null);
            Assert.That(Field.ContactSwingBreaks(1), Is.EqualTo(1), "precondition: the contact broke the bot's swing.");
            Assert.That(bot.Player.IsSwinging, Is.False);
            ParkUnused();
            Place(bot, spot, Vector3.zero);
            Assert.That(bot.Player.TryFindPole(out _), Is.True, "precondition: back beside the pole.");
            int wait = BotTuning.Hard.ContactRehookTicks;
            Assert.That(wait, Is.EqualTo(25), "0.5 s at 50 ticks a second.");
            for (int tick = 1; tick <= wait; tick++)
                Assert.That(input.Consume().SwingPressed, Is.False, $"No new hook {tick} ticks after the break.");
            Assert.That(input.Consume().SwingPressed, Is.True, "Once the wait ends the same pole is hooked.");
        }

        // A runner whose swing a contact broke hooks no corner pole for 0.5 s, whoever presses: the wait is the simulation's
        // (user 2026-11-21, swing cooldown S1-S3 "추천대로 진행"), so the player holding the swing key every tick waits as a bot
        // does, and hooks again on the 25th tick after the break. A contact that breaks no swing starts no wait. Same STADIUM
        // corner spot as the bot test above.
        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_BrokenSwing_HooksNoPoleForHalfASecond_ThenHooks_AndAContactWithoutASwingStartsNoWait()
        {
            yield return Load();
            Begin();
            ParkUnused();
            RaceWorld runner = player, other = worlds[1];
            var spot = new Vector3(3.854f, 1.05f, 140.09f);
            var press = new TickInput(0f, false, false, false, swingHeld: true, swingPressed: true);
            const int wait = 25; // 0.5 s at 50 ticks a second.
            Assert.That(RunnerMotor.ContactRehookTicks, Is.EqualTo(wait));

            Place(runner, spot, Vector3.zero);
            Assert.That(runner.Player.IsGrounded, Is.True, "precondition: on the road.");
            Assert.That(runner.Player.TryFindPole(out Vector3 pole), Is.True, "precondition: the corner pole is in reach.");
            Place(other, runner.Player.transform.position + Vector3.right * 0.3f, Vector3.zero);
            Contact();
            Assert.That(Field.ContactSwingBreaks(0), Is.EqualTo(0), "precondition: no swing to break.");
            ParkUnused();
            Place(runner, spot, Vector3.zero);
            runner.Player.Step(press);
            Assert.That(runner.Player.IsSwinging, Is.True, "A contact that broke no swing leaves the next press free to hook.");

            ParkUnused();
            Place(runner, spot, Vector3.zero);
            PoleSwing.End(runner.Game.Racer, Vector3.zero, runner.Game.Racer.Heading);
            PoleSwing.Hook(runner.Game.Racer, runner.Player.transform.position, pole);
            Place(other, runner.Player.transform.position + Vector3.right * 0.3f, Vector3.zero);
            Contact();
            Assert.That(Field.ContactSwingBreaks(0), Is.EqualTo(1), "precondition: the contact broke the swing.");
            Assert.That(runner.Player.IsSwinging, Is.False);
            ParkUnused();
            for (int tick = 1; tick < wait; tick++)
            {
                Place(runner, spot, Vector3.zero);
                runner.Player.Step(press);
                Assert.That(runner.Player.IsSwinging, Is.False, $"No hook {tick} ticks after the break, the pole in reach and the key pressed.");
            }
            Place(runner, spot, Vector3.zero);
            runner.Player.Step(press);
            Assert.That(runner.Player.IsSwinging, Is.True, "The 25th tick after the break hooks the same pole.");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_NoOverlap_DoesNotMoveAgainOrChangeSwingTimersAndGroundedPushUpdatesReturnS()
        {
            yield return Load();
            Begin();
            ParkUnused();
            RacerState state = player.Game.Racer;
            state.SwingTicks = 12;
            state.SlingTicks = 18;
            state.LastGroundedS = -123d;
            Vector3 position = player.Player.transform.position;
            ContactPass.Invoke(Field, null);
            Assert.That(player.Player.transform.position, Is.EqualTo(position));
            Assert.That(state.LastGroundedS, Is.EqualTo(-123d), "No additional Move/ground update without contact.");
            Assert.That(state.SwingTicks, Is.EqualTo(12));
            Assert.That(state.SlingTicks, Is.EqualTo(18));
            Place(player, new Vector3(0f, 1.05f, 10f), Vector3.zero);
            player.Player.ApplyContact(Vector3.forward * 0.1f, Vector3.zero);
            Assert.That(player.Player.IsGrounded, Is.True);
            Assert.That(state.LastGroundedS, Is.EqualTo(player.Game.Track.Project(player.Player.transform.position).S).Within(0.00001d));
        }

        // Runner art 3·4 (docs/PROPOSAL_RUNNER_UNITY.md "러너 간 Bump"): the field counts each contact that begins, once,
        // and every runner's model in the bot race flinches at its own.
        [UnityTest, Timeout(120000)]
        public IEnumerator Contact_BumpSequence_CountsEachContactOnceAsItBegins_ClearsOnRestart_AndBumpsThatRunnersModel()
        {
            yield return Load();
            Begin();
            ParkUnused();
            var animations = new RunnerAnimation[worlds.Length];
            for (int i = 0; i < worlds.Length; i++)
            {
                animations[i] = worlds[i].Player.GetComponent<RunnerAnimation>();
                Assert.That(animations[i], Is.Not.Null, "racer " + i);
            }
            Assert.Throws<ArgumentNullException>(() => animations[0].BindField(null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => animations[0].BindField(Field, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => animations[0].BindField(Field, worlds.Length));
            Assert.Throws<ArgumentOutOfRangeException>(() => Field.ContactSequence(worlds.Length));
            Step();
            AssertSequences(0, 0, 0, 0, "Runners apart do not count.");

            // Every runner's model: the player's (racer 0) and each opponent's (i + 1) flinch at their own contact only.
            // Checked first: with time stopped (SetUp) a flinch never runs out, so no other contact may come before.
            Overlap(worlds[0], worlds[3], 14f);
            Contact();
            AssertSequences(1, 0, 0, 1, "The player and the third opponent.");
            foreach (RaceWorld world in worlds) world.Game.Racer.RespawnHoldTicks = 0;
            yield return null;
            Assert.That(animations[0].Current, Is.EqualTo(RunnerAnimation.Pose.Bump), "The player's model flinches.");
            Assert.That(animations[3].Current, Is.EqualTo(RunnerAnimation.Pose.Bump), "The third opponent's model flinches.");
            Assert.That(animations[1].Current, Is.Not.EqualTo(RunnerAnimation.Pose.Bump), "No contact began for racer 1.");
            Assert.That(animations[2].Current, Is.Not.EqualTo(RunnerAnimation.Pose.Bump), "No contact began for racer 2.");

            ParkUnused();
            Step();
            Overlap(worlds[1], worlds[2], 10f);
            Contact();
            AssertSequences(1, 1, 1, 1, "One contact, three solve passes: one count each, for those two only.");
            Overlap(worlds[1], worlds[2], 10f);
            Contact();
            AssertSequences(1, 1, 1, 1, "Still pressed together on the next tick: the same contact.");
            ParkUnused();
            Step();
            AssertSequences(1, 1, 1, 1, "Apart again.");
            Overlap(worlds[1], worlds[2], 10f);
            Contact();
            AssertSequences(1, 2, 2, 1, "A new contact counts again.");

            player.Game.StartRun();
            Step();
            AssertSequences(0, 0, 0, 0, "A restart starts the count over.");
            yield return null;
            foreach (RunnerAnimation animation in animations)
                Assert.That(animation.Current, Is.Not.EqualTo(RunnerAnimation.Pose.Bump), "The count starting over is no bump.");
        }

        // Two runners, the first moving into the second (the rear impact above), for the contact pass alone (Contact).
        private static void Overlap(RaceWorld rear, RaceWorld front, float z)
        {
            Place(rear, new Vector3(0f, 1.05f, z), Vector3.forward * 8f);
            Place(front, new Vector3(0f, 1.05f, z + 0.6f), Vector3.forward * 2f);
        }

        private void AssertSequences(uint a, uint b, uint c, uint d, string because)
        {
            uint[] expected = { a, b, c, d };
            for (int i = 0; i < expected.Length; i++)
                Assert.That(Field.ContactSequence(i), Is.EqualTo(expected[i]), because + " racer " + i);
        }

        // D8 (user 2026-11-17 "그 깜빡이는 동안은 유령이다"): a runner blinking out its respawn hold (RunnerMotor.IsRespawning,
        // RunnerTilt) is a ghost — the field's contact pass leaves it out. Two runners who fall at one place come back on
        // one point of the centreline (RunnerMotor.RespawnOnTrack), stay there through each other while held, and the
        // contact pass parts them on the tick their hold runs out. Coincident centres part along world +X
        // (RunnerContact.cs:54), so once where the road runs along X and once across it.
        [UnityTest, Timeout(120000)]
        public IEnumerator Respawn_TwoRunnersFallingAtOnePlace_AreGhostsWhileHeld_AndPartOnTheRoadAsTheHoldEnds([Values(true, false)] bool roadAlongX)
        {
            yield return Load();
            Begin();
            ParkUnused();
            HoldStill(worlds[0], worlds[3]);
            RaceWorld heavy = worlds[1], light = worlds[2]; // OMEGA 1.2, BETA 0.9
            float backoff = Serialized(heavy.Player, "respawnBackoff");
            double spot = RespawnSpot(roadAlongX, Serialized(heavy.Player, "respawnPadReach"));
            TrackFrame frame = player.Game.Track.FrameAt(spot);
            float forwardX = Mathf.Abs(frame.Forward.x);
            Assert.That(roadAlongX ? forwardX > 0.95f : forwardX < 0.05f, Is.True, $"precondition: the road at S {spot:F1} runs {(roadAlongX ? "along" : "across")} world X (|forward.x| {forwardX:F3}).");
            Fall(heavy, spot, backoff);
            Fall(light, spot, backoff);
            Step();
            foreach (RaceWorld world in new[] { heavy, light })
            {
                Assert.That(world.Player.IsRespawning, Is.True, "precondition: both fell and came back on this tick.");
                Assert.That(world.Game.Track.Project(world.Player.transform.position).S, Is.EqualTo(spot).Within(1d), "precondition: both came back at the spot.");
            }
            Vector3 heavyAt = heavy.Player.transform.position, lightAt = light.Player.transform.position;
            Assert.That(Horizontal(heavyAt - lightAt), Is.LessThan(0.001f), "Ghosts: the contact pass leaves both on the point they came back on.");
            uint heavyContacts = Field.ContactSequence(1), lightContacts = Field.ContactSequence(2);
            int hold = heavy.Game.Racer.RespawnHoldTicks;
            Assert.That(light.Game.Racer.RespawnHoldTicks, Is.EqualTo(hold));
            for (int t = 1; t < hold; t++)
            {
                Step();
                Assert.That(heavy.Player.transform.position, Is.EqualTo(heavyAt), "OMEGA held in place, hold tick " + t);
                Assert.That(light.Player.transform.position, Is.EqualTo(lightAt), "BETA held in place, hold tick " + t);
            }
            Assert.That(Field.ContactSequence(1), Is.EqualTo(heavyContacts), "No contact while both are ghosts.");
            Assert.That(Field.ContactSequence(2), Is.EqualTo(lightContacts), "No contact while both are ghosts.");
            Step();
            Assert.That(heavy.Player.IsRespawning || light.Player.IsRespawning, Is.False, "The hold ran out on this tick.");
            AssertSeparated(heavy, light, 0.02f);
            AssertOnTheRoad(heavy, "OMEGA as the hold ends");
            AssertOnTheRoad(light, "BETA as the hold ends");
            Assert.That(Field.ContactSequence(1), Is.EqualTo(heavyContacts + 1), "Their contact begins as the hold ends.");
            Vector3 apart = light.Player.transform.position - heavy.Player.transform.position;
            apart.y = 0f;
            float alongRoad = Vector3.Dot(apart, frame.Forward), acrossRoad = Vector3.Dot(apart, frame.Right);
            float heavyLeft = Horizontal(heavy.Player.Velocity), lightLeft = Horizontal(light.Player.Velocity);
            Step();
            float heavyNext = Horizontal(heavy.Player.Velocity), lightNext = Horizontal(light.Player.Velocity);
            Debug.Log($"ChainRush D8 coincident respawn: road {(roadAlongX ? "along" : "across")} world +X at S {spot:F1} (|forward.x| {forwardX:F3}); " +
                      $"ghosts on one point for the {hold}-tick hold, parted as it ended {apart.magnitude:F3} m (along the road {alongRoad:F3}, across {acrossRoad:F3}); " +
                      $"speed then OMEGA {heavyLeft:F3} / BETA {lightLeft:F3} m/s, on the next tick {heavyNext:F3} / {lightNext:F3} m/s.");
        }

        // D8 ⓒ: a runner that fell into a gap blinks out its hold respawnBackoff before the gap while a heavier runner with
        // the throttle on comes from behind on the same line. A ghost, it is passed through: not moved, and left with no
        // speed for the end of its hold. Before ghosts (2026-11-17, DECISIONS "MP2 남은 것") this push moved it 3.671 m, to
        // 0.329 m before the gap, and left it 9.572 m/s to set off with.
        [UnityTest, Timeout(120000)]
        public IEnumerator Respawn_RunnerHoldingBeforeAGap_IsAGhost_TheRunnerBehindPassesThroughWithoutMovingIt()
        {
            yield return Load();
            Begin();
            ParkUnused();
            HoldStill(worlds[0], worlds[3]);
            RaceWorld runner = worlds[1], held = worlds[2]; // OMEGA 1.2 behind BETA 0.9
            const int HeldRacer = 2;
            float backoff = Serialized(held.Player, "respawnBackoff");
            double gapStart = player.Circuit.Definition.Gaps()[0].Start;
            Fall(held, gapStart - backoff, backoff);
            Step();
            Assert.That(held.Player.IsRespawning, Is.True, "precondition: it fell and came back.");
            double start = held.Game.Track.Project(held.Player.transform.position).S;
            Assert.That(start, Is.EqualTo(gapStart - backoff).Within(0.5d), "precondition: it came back respawnBackoff before the gap (no pad behind).");
            TrackFrame frame = held.Game.Track.FrameAt(start);
            Place(runner, frame.TransformPoint(new Vector3(0f, 1.05f, -2.5f)), frame.Forward * 10f);
            runner.Game.Racer.RespawnHoldTicks = 0;
            runner.Game.Racer.Heading = Mathf.Atan2(frame.Forward.x, frame.Forward.z) * Mathf.Rad2Deg;
            Vector3 at = held.Player.transform.position;
            uint contacts = Field.ContactSequence(HeldRacer);
            int hold = held.Game.Racer.RespawnHoldTicks;
            float closest = float.PositiveInfinity;
            for (int t = 1; t <= hold; t++)
            {
                Step();
                closest = Mathf.Min(closest, Horizontal(runner.Player.ContactCenter - held.Player.ContactCenter));
                Assert.That(held.Player.transform.position, Is.EqualTo(at), "Not moved, hold tick " + t);
                Assert.That(held.Player.Velocity, Is.EqualTo(Vector3.zero), "No speed taken, hold tick " + t);
            }
            Assert.That(closest, Is.LessThan(runner.Player.ContactRadius + held.Player.ContactRadius), "precondition: the runner behind went through it during the hold.");
            Assert.That(held.Player.IsRespawning, Is.False, "The hold ran out.");
            Assert.That(Field.ContactSequence(HeldRacer), Is.EqualTo(contacts), "No contact began with the ghost.");
            AssertOnTheRoad(held, "as the hold ends");
            Debug.Log($"ChainRush D8 ghost before a gap: came back at S {start:F2}, {gapStart - start:F2} m before the gap at {gapStart:F2}; " +
                      $"OMEGA passed through it ({closest:F3} m between centres at the closest), not moved, 0 m/s as the hold ran out.");
        }

        // The S nearest the start (away from the parked runners, gaps and the pads a fall comes back before) where the road
        // runs most nearly along world X, or most nearly across it.
        private double RespawnSpot(bool roadAlongX, float padReach)
        {
            TrackDefinition track = player.Circuit.Definition;
            List<(double Start, double End)> gaps = track.Gaps();
            double best = double.NaN;
            float bestScore = -1f;
            for (double s = 0d; s < track.LapLength; s += 0.5d)
            {
                if (Math.Abs(s - 25d) < 12d) continue; // ParkUnused
                bool nearGap = false;
                foreach ((double Start, double End) gap in gaps) if (s > gap.Start - 8d && s < gap.End + 8d) nearGap = true;
                if (nearGap || player.Game.Track.TryBoostZoneBehind(s, padReach, out _)) continue;
                float along = Mathf.Abs(player.Game.Track.FrameAt(s).Forward.x);
                float score = roadAlongX ? along : 1f - along;
                if (score <= bestScore) continue;
                bestScore = score;
                best = s;
            }
            Assert.That(double.IsNaN(best), Is.False, "No spot on the track to come back on.");
            return best;
        }

        // Below the road at the spot with its last ground respawnBackoff past it: the next tick it falls and comes back there.
        private static void Fall(RaceWorld world, double respawnAt, float backoff)
        {
            world.Game.Racer.RespawnHoldTicks = 0;
            world.Game.Racer.LastGroundedS = respawnAt + backoff;
            Place(world, world.Game.Track.FrameAt(respawnAt).TransformPoint(new Vector3(0f, -20f, 0f)), Vector3.zero);
        }

        private static void HoldStill(params RaceWorld[] held)
        {
            foreach (RaceWorld world in held) world.Game.Racer.RespawnHoldTicks = 10000;
        }

        private static void AssertOnTheRoad(RaceWorld world, string when)
        {
            TrackCoord at = world.Game.Track.Project(world.Player.transform.position);
            Assert.That(world.Player.IsGrounded, Is.True, when + ": on the ground.");
            Assert.That(Mathf.Abs(at.D), Is.LessThanOrEqualTo(world.Circuit.Definition.HalfWidthAt(at.S)), when + ": inside the road.");
        }

        private static float Horizontal(Vector3 vector) => new Vector2(vector.x, vector.z).magnitude;

        private static float Serialized(RunnerMotor motor, string field)
        {
#if UNITY_EDITOR
            SerializedProperty property = new SerializedObject(motor).FindProperty(field);
            Assert.That(property, Is.Not.Null, "RunnerMotor has no serialized " + field);
            return property.floatValue;
#else
            Assert.Fail("Contact tests require the Unity Editor.");
            return 0f;
#endif
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Grid_UniqueSeededSlotsAgreeWithLapSAndSurviveRestart()
        {
            yield return Load();
            int[] slots = StartGrid.Assign(4, assembler.Setup.Seed);
            var starts = new Vector3[4];
            Assert.That(Field.ContactEnabled, Is.True);
            for (int i = 0; i < worlds.Length; i++)
            {
                worlds[i].Game.ReturnToStart();
                starts[i] = worlds[i].Player.transform.position;
                Vector2 slot = StartGrid.Slot(assembler.Setup.Track, 4, slots[i]);
                TrackCoord actual = worlds[i].Game.Track.Project(starts[i]);
                Assert.That(actual.S, Is.EqualTo(-slot.x).Within(0.001d));
                Assert.That(worlds[i].Circuit.LastS, Is.EqualTo(-slot.x));
                Assert.That(worlds[i].Circuit.Laps.Progress, Is.EqualTo(-slot.x));
                Assert.That(actual.D, Is.EqualTo(slot.y).Within(0.001f));
                for (int j = 0; j < i; j++) Assert.That(starts[i], Is.Not.EqualTo(starts[j]));
            }
            Begin();
            for (int i = 0; i < 50; i++) Step();
            foreach (RaceWorld world in worlds) world.Game.StartRun();
            for (int i = 0; i < worlds.Length; i++) Assert.That(worlds[i].Player.transform.position, Is.EqualTo(starts[i]));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Field_SeededBotRaceTwice_RepeatsEveryRunnersPositionHash()
        {
            yield return Load();
            ulong first = RunBots(300);
            yield return Load();
            ulong second = RunBots(300);
            Assert.That(second, Is.EqualTo(first));
            Debug.Log($"ChainRush contact: 4 runners x 300 ticks repeated; hash=0x{first:X16}");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Tick_StageOrderIsEnforced_AndCompletionWaitsForJudgment()
        {
            yield return Load();
            Begin();
            ChainRushGame game = player.Game;
            int completions = 0;
            Action<int> completed = _ => completions++;
            game.TickCompleted += completed;
            try
            {
                Assert.Throws<InvalidOperationException>(() => game.MoveTick());
                Assert.Throws<InvalidOperationException>(() => game.JudgeTick());
                Assert.That(game.InputTick(), Is.True);
                Assert.Throws<InvalidOperationException>(() => game.InputTick());
                Assert.Throws<InvalidOperationException>(() => game.JudgeTick());
                game.MoveTick();
                Assert.Throws<InvalidOperationException>(() => game.MoveTick());
                Assert.That(completions, Is.EqualTo(0));
                game.JudgeTick();
                Assert.That(completions, Is.EqualTo(1));
                Assert.Throws<InvalidOperationException>(() => game.JudgeTick());
                var refused = Assert.Throws<InvalidOperationException>(() => game.StepTick(new RefusedInput()));
                Assert.That(refused.Message, Is.EqualTo("The input source refuses this tick."));
                Assert.That(completions, Is.EqualTo(1), "A refused tick does not complete.");
                game.StepTick(Idle());
                Assert.That(completions, Is.EqualTo(2), "The next complete tick must not inherit the refused tick's stage.");
            }
            finally { game.TickCompleted -= completed; }
        }

        // Review 2026-11-14 (Codex medium, unity-reviewer 보통 2): the field runs the stages itself, past StepTick's
        // finally. One runner's input that throws stops the race once and loudly; it never leaves the others mid-stage
        // to throw the stage-order error on every later tick.
        [UnityTest, Timeout(120000)]
        public IEnumerator Field_AnOpponentsInputThrows_StopsTheRaceOnceAndLoudly()
        {
            yield return Load();
            Begin();
            worlds[2].Game.SetInputSource(new RefusedInput());
            LogAssert.Expect(LogType.Exception, new Regex("The input source refuses this tick"));
            LogAssert.Expect(LogType.Error, new Regex("^RaceField: .*the race stops"));
            Step();
            var ticks = new int[worlds.Length];
            for (int i = 0; i < worlds.Length; i++) ticks[i] = worlds[i].Game.Tick;
            worlds[2].Game.SetInputSource(Idle());
            Step();
            for (int i = 0; i < worlds.Length; i++)
                Assert.That(worlds[i].Game.Tick, Is.EqualTo(ticks[i]), "A stopped field steps nobody, racer " + i);
        }

        // Review 2026-11-14 (Codex high, unity-reviewer 보통 1): while the opponents' worlds load, Enter must not start
        // the player's run — a run that started before the field gathered had no grid slot and no opponents.
        [UnityTest, Timeout(120000)]
        public IEnumerator Bots_WhileTheFieldGathers_TheKeyboardCannotStartTheRace_AndCanOnceItCountsIn()
        {
            yield return Launch();
            // Read while loading, assert once gathered: a failure here must not leave a half-loaded field to the next test.
            bool assembling = assembler.IsAssembling, manualWhileGathering = player.Game.ManualStart;
            yield return Gathered();
            Assert.That(assembling, Is.True, "The opponents' worlds were still loading.");
            Assert.That(manualWhileGathering, Is.False, "Enter must not start the run while the field gathers.");
            Assert.That(player.Game.ManualStart, Is.True, "The player starts the race again once it counts in.");
            Assert.That(player.Game.IsCountingDown, Is.True);
        }

        private ulong RunBots(int ticks)
        {
            foreach (RaceWorld world in worlds) world.Game.SetInputSource(new CircuitBot(world.Game, world.Player, world.Grapple, world.Circuit));
            Begin();
            ulong hash = 14695981039346656037UL;
            for (int t = 0; t < ticks; t++)
            {
                Step();
                foreach (RaceWorld world in worlds)
                {
                    Vector3 p = world.Player.transform.position;
                    foreach (float value in new[] { p.x, p.y, p.z })
                    {
                        hash ^= BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
                        hash *= 1099511628211UL;
                    }
                    Assert.That(world.Game.IsRunning, Is.True);
                }
            }
            return hash;
        }

        private void Begin()
        {
            player.Game.StartRun();
            Step();
        }

        private void Step() => FieldTick.Invoke(Field, null);

        // The field's contact pass alone, on where the runners stand: no input or movement first. A runner standing out
        // its respawn hold is a ghost to it (D8), so a hold cannot keep runners still for a contact.
        private void Contact() => ContactPass.Invoke(Field, null);

        private void ParkUnused()
        {
            for (int i = 0; i < worlds.Length; i++) Place(worlds[i], new Vector3(-3f + i * 2f, 1.05f, 25f), Vector3.zero);
        }

        private static void Place(RaceWorld world, Vector3 position, Vector3 velocity)
        {
            world.Player.SetPosition(position);
            CharacterController capsule = world.Player.GetComponent<CharacterController>();
            capsule.Move(Vector3.down * 0.3f);
            world.Game.Racer.Grounded = capsule.isGrounded;
            Physics.SyncTransforms();
            world.Game.Racer.Velocity = velocity;
        }

        private static void AssertSeparated(RaceWorld a, RaceWorld b, float tolerance)
        {
            Vector3 delta = a.Player.ContactCenter - b.Player.ContactCenter;
            delta.y = 0f;
            Assert.That(delta.magnitude, Is.GreaterThanOrEqualTo(a.Player.ContactRadius + b.Player.ContactRadius - tolerance), "Residual overlap must stay within the allowed metres.");
        }

        private static IInputSource Idle() => new InputReplay(new InputLog());

        private sealed class RefusedInput : IInputSource
        {
            public void Poll() { }
            public void Clear() { }
            public TickInput Consume() => throw new InvalidOperationException("The input source refuses this tick.");
        }

        private IEnumerator Load()
        {
            yield return Launch();
            yield return Gathered();
        }

        // Requests the bot race and loads its scene; the opponents' worlds may still be loading when it returns.
        private IEnumerator Launch()
        {
#if UNITY_EDITOR
            RunnerProfile Profile(string name) => AssetDatabase.LoadAssetAtPath<RunnerProfile>("Assets/_Project/Data/RunnerProfile_" + name + ".asset");
            var definition = AssetDatabase.LoadAssetAtPath<TrackDefinition>("Assets/_Project/Data/Circuit_Stadium.asset");
            RaceLaunch.Request(new RaceSetup(definition, "STADIUM", "", RaceMode.Bots, SpeedClass.Normal, Profile("Alpha"), seed: 13579)
                .WithOpponents(Profile("Omega"), Profile("Beta"), Profile("Theta")));
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(RacePath, new LoadSceneParameters(LoadSceneMode.Single));
            Scene scene = SceneManager.GetActiveScene();
            player = RaceWorld.FromScene(scene);
            assembler = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out RaceAssembler found)) assembler = found;
            Assert.That(assembler, Is.Not.Null);
#else
            Assert.Fail("Contact tests require the Unity Editor.");
            yield break;
#endif
        }

        // Waits for the field to gather and takes it over for synchronous ticks.
        private IEnumerator Gathered()
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (assembler.IsAssembling && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(assembler.enabled, Is.True);
            Assert.That(assembler.IsAssembling, Is.False);
            Assert.That(Field, Is.Not.Null);
            Field.enabled = false; // Invoke the real field tick synchronously; the frame cannot add extra ticks.
            worlds = new[] { player, Field.Opponents[0], Field.Opponents[1], Field.Opponents[2] };
            foreach (RaceWorld world in worlds) world.Game.SetInputSource(Idle());
        }
    }
}
