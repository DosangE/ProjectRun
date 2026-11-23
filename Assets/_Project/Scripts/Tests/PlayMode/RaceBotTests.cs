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
        private string savedLastRunner;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ChainRushFlowTests.RememberAndClearPlayerLastRunner(out savedLastRunner);
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
                ChainRushFlowTests.RestorePlayerLastRunner(savedLastRunner);
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
