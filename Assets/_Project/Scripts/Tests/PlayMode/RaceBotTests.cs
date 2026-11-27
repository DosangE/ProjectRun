using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using ProtoHarness.ChainRush;
using ProtoHarness.ChainRush.Bots;
using ProtoHarness.ChainRush.Control;
using ProtoHarness.ChainRush.Flow;
using ProtoHarness.ChainRush.Flow.Lobby;
using ProtoHarness.ChainRush.Motion;
using ProtoHarness.ChainRush.Race;
using ProtoHarness.ChainRush.Track;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace ProtoHarness.Tests.PlayMode
{
    public sealed class RaceBotTests
    {
        internal const string RacePath = "Assets/_Project/Scenes/ChainRushCircuit.unity";
        private static readonly MethodInfo FieldTick = typeof(RaceField).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        private float scale, volume;
        private GameSettings lobbySettings;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            lobbySettings = new GameSettings("ChainRush.Test.RaceBotLobby.");
            lobbySettings.Clear();
            LobbyMenu.DefaultSettings = lobbySettings;
            scale = Time.timeScale;
            volume = AudioListener.volume;
            Time.timeScale = 0f;
            AudioListener.volume = 0f;
            RaceLaunch.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                RaceLaunch.Clear();
                Time.timeScale = scale;
                AudioListener.volume = volume;
                yield return null;
                TestLogs.AssertNoUnexpected();
            }
            finally
            {
                LobbyMenu.DefaultSettings = null;
                lobbySettings?.Clear();
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AllTechniquesOff_MatchesEveryCircuitBotInputBitForBit()
        {
            RaceWorld world = null;
            yield return LoadRace("Stadium", BotDifficulty.Easy, false, 1, (w, _) => world = w);
            IInputSource easy = InputFor(world, BotDifficulty.Easy);
            var bot = new RaceBot(world.Game, world.Player, world.Grapple, world.Circuit, BotTuning.CircuitEquivalent);
            world.Game.SetInputSource(easy);
            world.Game.StartRun();
            for (int tick = 0; tick < 1700; tick++)
            {
                TickInput expected = easy.Consume(), actual = bot.Consume();
                Assert.That(BitConverter.SingleToInt32Bits(actual.Steer), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Steer)), $"steer tick {tick}");
                Assert.That(actual.PrimaryPressed, Is.EqualTo(expected.PrimaryPressed));
                Assert.That(actual.ReleasePressed, Is.EqualTo(expected.ReleasePressed));
                Assert.That(actual.AttackPressed, Is.EqualTo(expected.AttackPressed));
                Assert.That(actual.SwingHeld, Is.EqualTo(expected.SwingHeld));
                Assert.That(actual.SwingPressed, Is.EqualTo(expected.SwingPressed));
                Assert.That(actual.ChainActionPressed, Is.EqualTo(expected.ChainActionPressed));
                Assert.That(actual.Coasting, Is.EqualTo(expected.Coasting));
                Assert.That(actual.Reversing, Is.EqualTo(expected.Reversing));
                world.Game.StepTick(easy);
                Assert.That(world.Game.IsRunning, Is.True);
                if (tick % 200 == 0) yield return null;
            }
            bot.Clear();
            Assert.That(bot.Consume(), Is.EqualTo(easy.Consume()));
            Debug.Log("RaceBot all-off: 1700 inputs identical bit for bit.");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Preset_TwoRuns_HaveIdenticalStateTrace([Values(BotDifficulty.Normal, BotDifficulty.Hard)] BotDifficulty difficulty)
        {
            yield return TwoPresetRuns("Stadium", difficulty, 1700, null);
        }

        // The shortcut logic (two scripted swings, ShortcutInput) twice on SummitDrop, whose one shortcut (S 1225 to 1505) both
        // presets pass before tick 3300 (HANDOFF 2026-11-15: "지름길 트랙에서의 프리셋 두 번 해시 확인").
        [UnityTest, Timeout(180000)]
        public IEnumerator Preset_TwoRunsThroughAShortcut_HaveIdenticalStateTrace([Values(BotDifficulty.Normal, BotDifficulty.Hard)] BotDifficulty difficulty)
        {
            yield return TwoPresetRuns("SummitDrop", difficulty, 3300, world =>
            {
                double end = world.Circuit.Definition.Shortcuts[0].ToS;
                Assert.That(world.Game.Track.Project(world.Player.transform.position).S, Is.GreaterThan(end),
                    "precondition: the run passed the shortcut within the compared ticks.");
            });
        }

        private static IEnumerator TwoPresetRuns(string track, BotDifficulty difficulty, int ticks, Action<RaceWorld> check)
        {
            StateTrace first = null;
            for (int run = 0; run < 2; run++)
            {
                RaceWorld world = null;
                yield return LoadRace(track, difficulty, false, 1, (w, _) => world = w);
                var trace = new StateTrace(world.Game, world.Player, InputFor(world, difficulty),
                    ("progress", () => world.Circuit.Laps.Progress), ("checkpoints", () => world.Circuit.Laps.CheckpointsPassed));
                world.Game.SetInputSource(trace);
                world.Game.StartRun();
                for (int tick = 0; tick < ticks; tick++)
                {
                    world.Game.StepTick(trace);
                    if (tick % 300 == 0) yield return null;
                }
                Assert.That(trace.Samples, Is.EqualTo(ticks));
                check?.Invoke(world);
                if (run == 0) first = trace;
                else
                {
                    Assert.That(StateTrace.Compare(first, trace, ticks), Is.Null);
                    Assert.That(trace.Hash(ticks), Is.EqualTo(first.Hash(ticks)));
                    Debug.Log($"RaceBot preset {difficulty} on {track}: {ticks} ticks identical, hash 0x{trace.Hash(ticks):X16}.");
                }
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Lobby_ChosenDifficulty_ReachesAllOpponentInputs([Values(BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard)] BotDifficulty difficulty)
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/ChainRushLobby.unity", new LoadSceneParameters(LoadSceneMode.Single));
            LobbyMenu menu = null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.TryGetComponent(out LobbyMenu found)) menu = found;
            Assert.That(menu, Is.Not.Null);
            menu.Highlight(MainPage.SingleRow); menu.Confirm();
            menu.Highlight(SingleModePage.BotsRow); menu.Confirm();
            Assert.That(menu.CurrentPage, Is.EqualTo(LobbyMenu.Page.Difficulty));
            Assert.That(menu.Cursor, Is.EqualTo(0));
            menu.Highlight((int)difficulty); menu.Confirm();
            menu.Back();
            Assert.That(menu.CurrentPage, Is.EqualTo(LobbyMenu.Page.Difficulty));
            Assert.That(menu.Cursor, Is.EqualTo((int)difficulty));
            menu.Confirm(); menu.Confirm(); menu.Confirm();
            RaceSetup setup = menu.BuildSetup();
            Assert.That(setup.Difficulty, Is.EqualTo(difficulty));
            Assert.That(setup.ForOpponent(0).Difficulty, Is.EqualTo(difficulty));
            RaceWorld world = null;
            RaceField field = null;
            yield return LoadSetup(setup, (w, f) => { world = w; field = f; });
            for (int i = 0; i < 3; i++)
            {
                IInputSource source = field.OpponentInput(i);
                Assert.That(field.Opponents[i].Game.InputSource, Is.SameAs(source));
                if (difficulty == BotDifficulty.Easy) Assert.That(source, Is.TypeOf<CircuitBot>());
                else
                {
                    Assert.That(source, Is.TypeOf<RaceBot>());
                    Assert.That(((RaceBot)source).Tuning, Is.EqualTo(difficulty == BotDifficulty.Normal ? BotTuning.Normal : BotTuning.Hard));
                }
            }
#else
            Assert.Fail("RaceBot tests require the Unity Editor.");
            yield break;
#endif
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ObservingFourBots_ThreeHundredTicks_RepeatAllPositionHashes()
        {
            ulong[] first = null;
            for (int run = 0; run < 2; run++)
            {
                RaceWorld player = null;
                RaceField field = null;
                yield return LoadRace("Stadium", BotDifficulty.Normal, true, 2, (w, f) => { player = w; field = f; });
                var worlds = new[] { player, field.Opponents[0], field.Opponents[1], field.Opponents[2] };
                var hashes = new ulong[4];
                for (int i = 0; i < worlds.Length; i++)
                {
                    RaceWorld w = worlds[i];
                    var bot = new RaceBot(w.Game, w.Player, w.Grapple, w.Circuit, new BotTuning(4.5d, contactResponse: true));
                    w.Game.SetInputSource(bot);
                    bot.Observe(field, i);
                }
                Assert.That(field.ObservedRacerCount, Is.EqualTo(0));
                player.Game.StartRun();
                for (int tick = 0; tick < 300; tick++)
                {
                    StepField(field);
                    Assert.That(field.ObservedRacerCount, Is.EqualTo(4));
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = worlds[i].Player.transform.position;
                        hashes[i] = SeedHash.SplitMix64(hashes[i] ^ (uint)BitConverter.SingleToInt32Bits(p.x));
                        hashes[i] = SeedHash.SplitMix64(hashes[i] ^ (uint)BitConverter.SingleToInt32Bits(p.y));
                        hashes[i] = SeedHash.SplitMix64(hashes[i] ^ (uint)BitConverter.SingleToInt32Bits(p.z));
                    }
                    if (tick % 100 == 0) yield return null;
                }
                if (run == 0) first = hashes;
                else
                {
                    Assert.That(hashes, Is.EqualTo(first));
                    Debug.Log($"RaceBot observed contact race: 4 x 300 ticks identical, hashes {hashes[0]:X16},{hashes[1]:X16},{hashes[2]:X16},{hashes[3]:X16}.");
                }
            }
        }

        // Passing while drafting (BotTuning.DraftPass) on STADIUM's opening straight: runner 0 ahead, nearly in line, runner 1's
        // bot behind it, the other two out of the way. The steering is compared with an all-off bot's on the same state, which
        // holds the centreline.
        [UnityTest, Timeout(120000)]
        public IEnumerator DraftPass_BoostingBehindARunner_StepsOutToTheSideWithMoreRoad([Values(0.5f, -0.5f)] float aheadSide)
        {
            RaceWorld[] worlds = null;
            RaceField field = null;
            yield return LoadStraight((w, f) => { worlds = w; field = f; });
            InLine(worlds, field, aheadSide, 5f, 1f);
            worlds[1].Game.Racer.DraftBoostTicks = 10;
            float pass = PassSteer(worlds[1], field, out float plain);
            Assert.That(Mathf.Abs(plain), Is.LessThan(0.05f), "precondition: an all-off bot holds the centreline here.");
            Assert.That(pass - plain, Is.EqualTo(-Mathf.Sign(aheadSide)).Within(0.9f), "It steers away from the runner ahead.");
            worlds[1].Game.Racer.DraftBoostTicks = 0;
            Assert.That(PassSteer(worlds[1], field, out plain), Is.EqualTo(plain), "Without a draft it keeps its line.");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator DraftPass_ChargingBehindARunner_StaysInLineUnlessClosingOnItRightInFront()
        {
            RaceWorld[] worlds = null;
            RaceField field = null;
            yield return LoadStraight((w, f) => { worlds = w; field = f; });
            InLine(worlds, field, 0.5f, 5f, 0.8f);
            worlds[1].Game.Racer.DraftChargeTicks = 10;
            Assert.That(PassSteer(worlds[1], field, out float plain), Is.EqualTo(plain), "Closing from 5 m it keeps the charge.");
            InLine(worlds, field, 0.5f, 2.5f, 1f);
            worlds[1].Game.Racer.DraftChargeTicks = 10;
            Assert.That(PassSteer(worlds[1], field, out plain), Is.EqualTo(plain), "2.5 m behind at the same speed it keeps the charge.");
            InLine(worlds, field, 0.5f, 2.5f, 0.8f);
            worlds[1].Game.Racer.DraftChargeTicks = 10;
            Assert.That(PassSteer(worlds[1], field, out plain) - plain, Is.EqualTo(-1f).Within(0.9f), "Closing from 2.5 m it steps out.");
            // A runner above coming down at the same road speed is not closed on (review 2026-11-26, Codex: a 3D dot counted
            // the fall).
            InLine(worlds, field, 0.5f, 2.5f, 1f, aheadHeight: 1f, aheadFall: 5f);
            worlds[1].Game.Racer.DraftChargeTicks = 10;
            Assert.That(PassSteer(worlds[1], field, out plain), Is.EqualTo(plain), "A runner falling in front is not closed on.");
        }

        // Off the centreline the side with more road and the side away from the runner differ (review 2026-11-26,
        // unity-reviewer): the bot 2 m right, the runner 0.5 m to its left, so it passes on the runner's left, across its nose.
        [UnityTest, Timeout(120000)]
        public IEnumerator DraftPass_BoostingOffTheCentreline_PassesOnTheSideWithMoreRoad()
        {
            RaceWorld[] worlds = null;
            RaceField field = null;
            yield return LoadStraight((w, f) => { worlds = w; field = f; });
            InLine(worlds, field, 1.5f, 5f, 1f, ownSide: 2f);
            worlds[1].Game.Racer.DraftBoostTicks = 10;
            float pass = PassSteer(worlds[1], field, out float plain);
            Assert.That(plain, Is.LessThan(-0.1f), "precondition: an all-off bot heads back to the centreline, left.");
            Assert.That(pass, Is.LessThan(plain - 0.1f), "It goes further left, to the runner's other side, not right.");
        }

        // In line bends with the corner, as a draft's does (review 2026-11-26, Codex): on STADIUM's first corner (radius 22 m)
        // a runner 7 m ahead along the road and 0.5 m to the inside is 1.6 m to the side of the bot's facing.
        [UnityTest, Timeout(120000)]
        public IEnumerator DraftPass_BoostingRoundACorner_PassesTheRunnerInLineAlongTheRoad()
        {
            RaceWorld[] worlds = null;
            RaceField field = null;
            yield return LoadStraight((w, f) => { worlds = w; field = f; });
            Centerline track = worlds[1].Game.Track;
            double s = 0d;
            while (s < 400d && track.FrameAt(s).Curvature == 0f) s += 1d;
            float curvature = track.FrameAt(s).Curvature;
            Assert.That(curvature, Is.Not.Zero, "precondition: a corner within 400 m of the start.");
            Assert.That(track.FrameAt(s + 10d).Curvature, Is.EqualTo(curvature), "precondition: the corner lasts 10 m.");
            TrackFrame here = track.FrameAt(s + 1d), there = track.FrameAt(s + 8d);
            float inside = Mathf.Sign(curvature) * 0.5f;
            float speed = worlds[1].Player.RunSpeed;
            // Facing the road, the corner's feed-forward alone holds both bots at full steering (2026-11-26: pass 1, plain 1), so
            // the bot faces 25 degrees further round, where the steering is not saturated.
            float heading = Mathf.Atan2(here.Forward.x, here.Forward.z) * Mathf.Rad2Deg + Mathf.Sign(curvature) * 25f;
            Arrange(worlds, field, here.Position + Vector3.up * 1.05f, here.Forward * speed, heading,
                there.Position + there.Right * inside + Vector3.up * 1.05f, there.Forward * speed);
            Vector3 own = worlds[1].Player.transform.position, ahead = worlds[0].Player.transform.position;
            Assert.That(Mathf.Abs(Vector3.Dot(ahead - own, here.Right)), Is.GreaterThan(Slipstream.HalfWidth), "precondition: off the facing's line.");
            TrackCoord ownAt = track.Project(own), aheadAt = track.Project(ahead);
            string at = $"own S {ownAt.S:F2} D {ownAt.D:F2} H {ownAt.H:F2}, ahead S {aheadAt.S:F2} D {aheadAt.D:F2} H {aheadAt.H:F2}, curvature {curvature}";
            Assert.That(aheadAt.S - ownAt.S, Is.InRange(6.5d, 7.5d), "precondition: 7 m ahead along the road. " + at);
            Assert.That(aheadAt.D - ownAt.D, Is.EqualTo(inside).Within(0.1f), "precondition: 0.5 m to the inside. " + at);
            worlds[1].Game.Racer.DraftBoostTicks = 10;
            float pass = PassSteer(worlds[1], field, out float plain);
            Assert.That(Mathf.Abs(plain), Is.LessThan(0.9f), $"precondition: the all-off bot's steering is not saturated, plain {plain}.");
            Assert.That(pass - plain, Is.EqualTo(-Mathf.Sign(inside)).Within(0.9f), $"It steps out to the outside of the corner: pass {pass}, plain {plain}. " + at);
        }

        private static IEnumerator LoadStraight(Action<RaceWorld[], RaceField> done)
        {
            RaceWorld player = null;
            RaceField field = null;
            yield return LoadRace("Stadium", BotDifficulty.Normal, true, 7, (w, f) => { player = w; field = f; });
            var worlds = new[] { player, field.Opponents[0], field.Opponents[1], field.Opponents[2] };
            foreach (RaceWorld world in worlds) world.Game.SetInputSource(new InputReplay(new InputLog()));
            player.Game.StartRun();
            StepField(field);
            done(worlds, field);
        }

        // On the opening straight: runner 0 at x = aheadSide, aheadHeight up and falling at aheadFall, gap metres ahead of
        // runner 1 at x = ownSide, going aheadScale of runner 1's run speed.
        private static void InLine(RaceWorld[] worlds, RaceField field, float aheadSide, float gap, float aheadScale,
            float ownSide = 0f, float aheadHeight = 0f, float aheadFall = 0f)
        {
            float speed = worlds[1].Player.RunSpeed;
            Arrange(worlds, field, new Vector3(ownSide, 1.05f, 8f - gap), Vector3.forward * speed, 0f,
                new Vector3(aheadSide, 1.05f + aheadHeight, 8f), Vector3.forward * speed * aheadScale + Vector3.down * aheadFall);
        }

        // Runner 1 at own facing heading, runner 0 at ahead, the other two out of the way at the start. One field tick takes
        // the snapshot the bots read, and all are put back where they were for the bot's own position.
        private static void Arrange(RaceWorld[] worlds, RaceField field, Vector3 own, Vector3 ownVelocity, float heading,
            Vector3 ahead, Vector3 aheadVelocity)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 2; i < worlds.Length; i++) Place(worlds[i], new Vector3(i == 2 ? -4.5f : 4.5f, 1.05f, -8f), Vector3.zero);
                Place(worlds[0], ahead, aheadVelocity);
                Place(worlds[1], own, ownVelocity);
                worlds[1].Game.Racer.Heading = heading;
                worlds[1].Game.Racer.DraftBoostTicks = worlds[1].Game.Racer.DraftChargeTicks = worlds[1].Game.Racer.DraftCooldownTicks = 0;
                if (pass == 0) StepField(field);
            }
        }

        // The steering of a passing bot and, in plain, an all-off bot's, both reading runner 1 and the same snapshot.
        private static float PassSteer(RaceWorld world, RaceField field, out float plain)
        {
            var passer = new RaceBot(world.Game, world.Player, world.Grapple, world.Circuit, new BotTuning(4.5d, draftPass: true));
            var allOff = new RaceBot(world.Game, world.Player, world.Grapple, world.Circuit, BotTuning.CircuitEquivalent);
            passer.Observe(field, 1);
            allOff.Observe(field, 1);
            plain = allOff.Consume().Steer;
            return passer.Consume().Steer;
        }

        // As ChainRushDraftTests.Place: on the road, grounded, at this velocity.
        private static void Place(RaceWorld world, Vector3 position, Vector3 velocity)
        {
            world.Player.SetPosition(position);
            CharacterController capsule = world.Player.GetComponent<CharacterController>();
            capsule.Move(Vector3.down * 0.3f);
            world.Game.Racer.Grounded = capsule.isGrounded;
            Physics.SyncTransforms();
            world.Game.Racer.Velocity = velocity;
        }

        internal static void StepField(RaceField field) => FieldTick.Invoke(field, null);

        internal static IInputSource InputFor(RaceWorld world, BotDifficulty difficulty) => difficulty == BotDifficulty.Easy
            ? (IInputSource)new CircuitBot(world.Game, world.Player, world.Grapple, world.Circuit)
            : new RaceBot(world.Game, world.Player, world.Grapple, world.Circuit, difficulty == BotDifficulty.Normal ? BotTuning.Normal : BotTuning.Hard);

        internal static IEnumerator LoadRace(string track, BotDifficulty difficulty, bool field, int seed, Action<RaceWorld, RaceField> done)
        {
#if UNITY_EDITOR
            TrackDefinition definition = AssetDatabase.LoadAssetAtPath<TrackDefinition>($"Assets/_Project/Data/Circuit_{track}.asset");
            RunnerProfile alpha = AssetDatabase.LoadAssetAtPath<RunnerProfile>("Assets/_Project/Data/RunnerProfile_Alpha.asset");
            Assert.That(definition, Is.Not.Null);
            Assert.That(alpha, Is.Not.Null);
            var setup = new RaceSetup(definition, track, "", field ? RaceMode.Bots : RaceMode.Single, SpeedClass.Fast, alpha,
                seed: seed, difficulty: field ? difficulty : BotDifficulty.Easy);
            if (field) setup = setup.WithOpponents(alpha, alpha, alpha);
            yield return LoadSetup(setup, done);
#else
            Assert.Fail("RaceBot tests require the Unity Editor.");
            yield break;
#endif
        }

        private static IEnumerator LoadSetup(RaceSetup setup, Action<RaceWorld, RaceField> done)
        {
#if UNITY_EDITOR
            RaceLaunch.Request(setup);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(RacePath, new LoadSceneParameters(LoadSceneMode.Single));
            Scene scene = SceneManager.GetActiveScene();
            RaceWorld world = RaceWorld.FromScene(scene);
            RaceAssembler assembler = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out RaceAssembler found)) assembler = found;
            Assert.That(assembler, Is.Not.Null);
            // A solo finish must write only test records/ghosts, never the player's best.
            assembler.Records = new BestRecords("ChainRush.Test.RaceBotBench.");
            assembler.Ghosts = new GhostFiles(Path.Combine(Path.GetTempPath(), "ProtoHarnessTests", "RaceBotBenchGhosts"));
            float deadline = Time.realtimeSinceStartup + 60f;
            while (assembler.IsAssembling && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(assembler.IsAssembling, Is.False);
            Assert.That(assembler.enabled, Is.True);
            Assert.That(world.Game.IsRunning, Is.False);
            world.Game.SelfStepping = false;
            if (setup.OpponentCount > 0)
            {
                Assert.That(assembler.Field, Is.Not.Null);
                assembler.Field.enabled = false;
            }
            done(world, assembler.Field);
#else
            Assert.Fail("RaceBot tests require the Unity Editor.");
            yield break;
#endif
        }
    }
}
