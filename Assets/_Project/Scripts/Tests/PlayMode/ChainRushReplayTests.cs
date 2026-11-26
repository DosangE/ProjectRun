using ProtoHarness.ChainRush.Bots;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using ProtoHarness.ChainRush;
using ProtoHarness.ChainRush.Control;
using ProtoHarness.ChainRush.Flow;
using ProtoHarness.ChainRush.Race;
using ProtoHarness.ChainRush.Track;
using ProtoHarness.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace ProtoHarness.Tests.PlayMode
{
    // Determinism (DESIGN.md P2): the same seed and the same per-tick input give the same run, tick for tick and
    // bit for bit. A bot's run is recorded, then replayed from the log in the same session, again after the scene
    // is loaded afresh, and at other time scales; every sample of the state must match exactly. Same machine and
    // same editor build only: other platforms' floating point is not covered.
    public sealed class ChainRushReplayTests
    {
        private const string CircuitScene = "Assets/_Project/Scenes/ChainRushCircuit.unity";
        private const int CircuitTicks = 1700;

        private ChainRushGame game;
        private RunnerMotor player;
        private GrappleController grapple;
        private CircuitRace circuit;
        private float timeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            timeScale = Time.timeScale;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = timeScale;
            RaceLaunch.Clear();
            yield return null;
            TestLogs.AssertNoUnexpected();
        }

        // ---- circuit -------------------------------------------------------------------------------

        [UnityTest, Timeout(240000)]
        public IEnumerator Replay_CircuitBotRun_RepeatsEveryTickBitForBit()
        {
            yield return Load(CircuitScene);
            var log = new InputLog();
            StateTrace recorded = CircuitTrace(new InputRecorder(new CircuitBot(game, player, grapple, circuit), log));
            yield return Run(recorded, CircuitTicks, 3f);
            Assert.That(log.Count, Is.GreaterThanOrEqualTo(CircuitTicks));
            Assert.That(circuit.Laps.CompletedLaps, Is.EqualTo(1), "At 20 m/s (M1-T, 2026-10-18) 1700 ticks are one whole lap, with its checkpoints and grapple gap, and the start of the next.");

            // The same session, six times as fast: ticks per frame differ, ticks do not.
            StateTrace sameSession = CircuitTrace(new InputReplay(log));
            yield return Run(sameSession, CircuitTicks, 6f);
            AssertSame("circuit, same session at 6x", recorded, sameSession, CircuitTicks);

            // A freshly loaded scene, slower: nothing the first run left behind may matter.
            yield return Load(CircuitScene);
            StateTrace reloaded = CircuitTrace(new InputReplay(log));
            yield return Run(reloaded, CircuitTicks, 2f);
            AssertSame("circuit, reloaded scene at 2x", recorded, reloaded, CircuitTicks);
            Debug.Log($"ChainRush replay circuit: {CircuitTicks} ticks identical, hash 0x{recorded.Hash(CircuitTicks):X16}, " +
                      $"{log.Count} recorded ticks, grapples {game.Grapples}.");
        }

        // ---- the comparison can tell ----------------------------------------------------------------

        [UnityTest, Timeout(120000)]
        public IEnumerator Replay_OneTickChanged_DivergesFromThatTickOnAndNotBefore()
        {
            const int ticks = 700;
            const int changed = 300;
            yield return Load(CircuitScene);
            var log = new InputLog();
            StateTrace recorded = CircuitTrace(new InputRecorder(new CircuitBot(game, player, grapple, circuit), log));
            yield return Run(recorded, ticks, 3f);

            // Same log with the steering of one tick forced to full right.
            TickInput original = log[changed];
            InputLog tampered = log.CopyWith(changed, new TickInput(1f, original.PrimaryPressed, original.ReleasePressed, original.AttackPressed, original.SwingHeld, original.ChainActionPressed, original.SwingPressed));
            Assert.That(original.Steer, Is.LessThan(1f), "The bot must not already steer full right on that tick.");
            StateTrace diverged = CircuitTrace(new InputReplay(tampered));
            yield return Run(diverged, ticks, 6f);

            // Input index 300 is consumed as tick 301; its effect shows in the sample after that tick, sample 301.
            int first = StateTrace.FirstDifferentSample(recorded, diverged, ticks);
            Assert.That(first, Is.EqualTo(changed + 1), "The runs must be identical up to the changed tick and differ right after it. " + StateTrace.Compare(recorded, diverged, ticks));

            // And the untouched log still replays identically (the control for the control).
            StateTrace untouched = CircuitTrace(new InputReplay(log));
            yield return Run(untouched, ticks, 6f);
            AssertSame("circuit, untouched log", recorded, untouched, ticks);
        }

        // ---- bot race (MP3, docs/PROPOSAL_MP3_DETERMINISM.md 0절) ----------------------------------------

        private const int FieldSeed = 1;
        private const int FieldTickLimit = 6000;

        // The player and three opponents, each a Hard RaceBot in a world of its own, with contact on: every runner's input
        // is recorded to the player's finish, then the four logs are replayed in the same worlds, in freshly loaded worlds,
        // and with other numbers of ticks per frame. After every tick, every runner's whole state (SimSnapshotCodec bytes)
        // and the field's standings must be the same bit for bit. Same machine and editor build only.
        [UnityTest, Timeout(600000)]
        public IEnumerator Replay_BotRaceAllRunners_RepeatsEveryTickBitForBit([Values("Stadium", "SummitDrop")] string track)
        {
            Time.timeScale = 0f;
            RaceWorld[] worlds = null;
            RaceField field = null;
            yield return LoadField(track, (w, f) => { worlds = w; field = f; });
            InputLog[] logs = RecordBots(worlds, field);
            var recorded = new FieldTrace();
            yield return RunField(worlds, field, recorded, FieldTickLimit, 100);
            Assert.That(worlds[0].Game.HasFinished, Is.True, $"The player's bot must finish within {FieldTickLimit} ticks; it stopped at tick {worlds[0].Game.Tick} (failed {worlds[0].Game.HasFailed}).");
            Assert.That(field.HasResult, Is.True);
            int ticks = recorded.Count;
            uint contacts = 0;
            for (int r = 0; r < field.RacerCount; r++) contacts += field.ContactSequence(r);
            Assert.That(contacts, Is.GreaterThan(0u), "precondition: the runners must touch, or contact is not being replayed.");
            Assert.That(logs[0].Count, Is.EqualTo(ticks));
            for (int i = 1; i < logs.Length; i++) Assert.That(logs[i].Count, Is.GreaterThan(0));

            // The same worlds, another number of ticks per frame: nothing the first race left behind may matter.
            var sameSession = new FieldTrace();
            Replay(worlds, logs);
            yield return RunField(worlds, field, sameSession, ticks + 1, 37);
            AssertSameField($"{track}, same session", recorded, sameSession, ticks);

            // Freshly loaded worlds.
            yield return LoadField(track, (w, f) => { worlds = w; field = f; });
            var reloaded = new FieldTrace();
            Replay(worlds, logs);
            yield return RunField(worlds, field, reloaded, ticks + 1, 250);
            AssertSameField($"{track}, reloaded worlds", recorded, reloaded, ticks);

            var standings = new StringBuilder();
            for (int r = 0; r < field.RacerCount; r++)
                standings.Append($" racer {r}: place {field.Place(r)} finish {field.FinishTick(r)} breaks {field.ContactSwingBreaks(r)} contacts {field.ContactSequence(r)};");
            Debug.Log($"ChainRush replay bot race {track} Hard: {ticks} ticks identical for {field.RacerCount} runners, hash 0x{recorded.Hash(ticks):X16}," + standings);
        }

        // Changing one opponent's input on one tick must change that opponent from that tick on and nothing before it.
        [UnityTest, Timeout(300000)]
        public IEnumerator Replay_BotRaceOneInputChanged_DivergesFromThatTickOnAndNotBefore()
        {
            const int ticks = 700;
            const int changed = 300;
            const int racer = 2;
            Time.timeScale = 0f;
            RaceWorld[] worlds = null;
            RaceField field = null;
            yield return LoadField("Stadium", (w, f) => { worlds = w; field = f; });
            InputLog[] logs = RecordBots(worlds, field);
            var recorded = new FieldTrace();
            yield return RunField(worlds, field, recorded, ticks, 100);
            Assert.That(recorded.Count, Is.EqualTo(ticks), "The race must still be going after the compared ticks.");

            TickInput original = logs[racer][changed];
            Assert.That(original.Steer, Is.LessThan(1f), "The bot must not already steer full right on that tick.");
            InputLog[] tampered = (InputLog[])logs.Clone();
            tampered[racer] = logs[racer].CopyWith(changed, new TickInput(1f, original.PrimaryPressed, original.ReleasePressed,
                original.AttackPressed, original.SwingHeld, original.ChainActionPressed, original.SwingPressed, original.Coasting, original.Reversing));
            var diverged = new FieldTrace();
            Replay(worlds, tampered);
            yield return RunField(worlds, field, diverged, ticks, 100);

            // Input index 300 is consumed on tick 301, so the state after tick 300 is the last that must match.
            int first = FieldTrace.FirstDifferentTick(recorded, diverged, ticks);
            Assert.That(first, Is.EqualTo(changed + 1), "The races must be identical up to the changed tick and differ right after it. " + FieldTrace.Compare(recorded, diverged, ticks));
            Assert.That(FieldTrace.DifferentRacers(recorded, diverged, first), Does.Contain(racer), "The changed runner itself must differ first. " + FieldTrace.Compare(recorded, diverged, ticks));
            string withContact = string.Join(",", FieldTrace.DifferentRacers(recorded, diverged, first));

            // The control for the others: with contact and drafting off the worlds share nothing, so the change may reach no
            // other runner on any tick. With both on, the others above may differ only through them.
            field.ContactEnabled = false;
            field.DraftEnabled = false;
            logs = RecordBots(worlds, field);
            var apart = new FieldTrace();
            yield return RunField(worlds, field, apart, ticks, 100);
            original = logs[racer][changed];
            Assert.That(original.Steer, Is.LessThan(1f), "Without contact, the bot must not already steer full right on that tick.");
            tampered = (InputLog[])logs.Clone();
            tampered[racer] = logs[racer].CopyWith(changed, new TickInput(1f, original.PrimaryPressed, original.ReleasePressed,
                original.AttackPressed, original.SwingHeld, original.ChainActionPressed, original.SwingPressed, original.Coasting, original.Reversing));
            var apartDiverged = new FieldTrace();
            Replay(worlds, tampered);
            yield return RunField(worlds, field, apartDiverged, ticks, 100);
            Assert.That(FieldTrace.FirstDifferentTick(apart, apartDiverged, ticks), Is.EqualTo(changed + 1), "Without contact: " + FieldTrace.Compare(apart, apartDiverged, ticks));
            for (int tick = 1; tick <= ticks; tick++)
            {
                List<int> different = FieldTrace.DifferentRacers(apart, apartDiverged, tick);
                Assert.That(different.Count == 0 || (different.Count == 1 && different[0] == racer), Is.True,
                    $"Without contact, only runner {racer} may differ; after tick {tick} runners {string.Join(",", different)} do.");
            }
            Debug.Log($"ChainRush replay bot race one input changed: first difference after tick {first}, runners {withContact} with contact; " +
                      $"without contact only runner {racer} differs, from tick {changed + 1}.");
        }

        private static IEnumerator LoadField(string track, Action<RaceWorld[], RaceField> done)
        {
            RaceWorld player = null;
            RaceField field = null;
            yield return RaceBotTests.LoadRace(track, BotDifficulty.Hard, true, FieldSeed, (w, f) => { player = w; field = f; });
            Assert.That(field, Is.Not.Null);
            Assert.That(field.RacerCount, Is.EqualTo(4));
            Assert.That(field.ContactEnabled, Is.True);
            done(new[] { player, field.Opponents[0], field.Opponents[1], field.Opponents[2] }, field);
        }

        // A recorder around a Hard RaceBot for every runner. Wrapped, a bot is not one RaceField.Initialize binds, so it
        // is bound to the field's observation here (RaceBotTests.ObservingFourBots does the same).
        private static InputLog[] RecordBots(RaceWorld[] worlds, RaceField field)
        {
            var logs = new InputLog[worlds.Length];
            for (int i = 0; i < worlds.Length; i++)
            {
                RaceWorld w = worlds[i];
                var bot = new RaceBot(w.Game, w.Player, w.Grapple, w.Circuit, BotTuning.Hard);
                logs[i] = new InputLog();
                w.Game.SetInputSource(new InputRecorder(bot, logs[i]));
                bot.Observe(field, i);
            }
            return logs;
        }

        private static void Replay(RaceWorld[] worlds, InputLog[] logs)
        {
            for (int i = 0; i < worlds.Length; i++) worlds[i].Game.SetInputSource(new InputReplay(logs[i]));
        }

        // Starts the player's run (the field starts the others on its first tick) and steps the field until the player's
        // run ends or `limit` ticks have run, sampling after every tick and yielding a frame every `ticksPerFrame` ticks.
        private static IEnumerator RunField(RaceWorld[] worlds, RaceField field, FieldTrace trace, int limit, int ticksPerFrame)
        {
            ChainRushGame player = worlds[0].Game;
            player.StartRun();
            while (player.IsRunning && trace.Count < limit)
            {
                RaceBotTests.StepField(field);
                trace.Sample(field, worlds);
                Assert.That(player.Tick, Is.EqualTo(trace.Count), "The field must take exactly one tick per step.");
                if (trace.Count % ticksPerFrame == 0) yield return null;
            }
        }

        private static void AssertSameField(string what, FieldTrace expected, FieldTrace actual, int ticks)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), what + ": the replayed race took another number of ticks. " + FieldTrace.Compare(expected, actual, Math.Min(expected.Count, actual.Count)));
            string difference = FieldTrace.Compare(expected, actual, ticks);
            Assert.That(difference, Is.Null, what + ": " + difference);
            Assert.That(actual.Hash(ticks), Is.EqualTo(expected.Hash(ticks)), what);
        }

        // Every runner's state after each field tick, as SimSnapshotCodec bytes (a runner whose run has ended shows its
        // phase and the tick it ended on), and the field's standings for every runner. Sample k is the state after tick k + 1.
        private sealed class FieldTrace
        {
            private const int StandingValues = 5;
            private readonly List<byte[][]> states = new List<byte[][]>(4096);
            private readonly List<ulong[]> standings = new List<ulong[]>(4096);

            public int Count => states.Count;

            public void Sample(RaceField field, RaceWorld[] worlds)
            {
                var tickStates = new byte[worlds.Length][];
                var tickStandings = new ulong[worlds.Length * StandingValues + 1];
                for (int r = 0; r < worlds.Length; r++)
                {
                    ChainRushGame game = worlds[r].Game;
                    tickStates[r] = game.IsRunning ? SimSnapshotCodec.Encode(game.CaptureSnapshot()) : Ended(game);
                    int at = r * StandingValues;
                    tickStandings[at] = (ulong)field.Place(r);
                    tickStandings[at + 1] = (ulong)field.FinishTick(r);
                    tickStandings[at + 2] = (ulong)BitConverter.DoubleToInt64Bits(field.Progress(r));
                    tickStandings[at + 3] = (ulong)field.ContactSwingBreaks(r);
                    tickStandings[at + 4] = field.ContactSequence(r);
                }
                tickStandings[tickStandings.Length - 1] = field.HasResult ? 1UL : 0UL;
                states.Add(tickStates);
                standings.Add(tickStandings);
            }

            private static byte[] Ended(ChainRushGame game)
            {
                var bytes = new byte[7];
                bytes[0] = 0xFF;
                bytes[1] = game.HasFinished ? (byte)1 : (byte)0;
                bytes[2] = game.HasFailed ? (byte)1 : (byte)0;
                BitConverter.GetBytes(game.Tick).CopyTo(bytes, 3);
                return bytes;
            }

            public ulong Hash(int samples)
            {
                ulong hash = 0UL;
                for (int s = 0; s < Math.Min(samples, Count); s++)
                {
                    foreach (byte[] state in states[s])
                        foreach (byte b in state) hash = SeedHash.SplitMix64(hash ^ b);
                    foreach (ulong value in standings[s]) hash = SeedHash.SplitMix64(hash ^ value);
                }
                return hash;
            }

            // The tick (1-based) after which the two traces first differ within the first `samples`, or -1.
            public static int FirstDifferentTick(FieldTrace a, FieldTrace b, int samples)
            {
                int count = Math.Min(samples, Math.Min(a.Count, b.Count));
                for (int s = 0; s < count; s++)
                    if (DifferentRacers(a, b, s + 1).Count > 0 || !SameStandings(a.standings[s], b.standings[s])) return s + 1;
                return -1;
            }

            // The runners whose state differs after `tick`.
            public static List<int> DifferentRacers(FieldTrace a, FieldTrace b, int tick)
            {
                var different = new List<int>();
                if (tick < 1) return different;
                byte[][] left = a.states[tick - 1], right = b.states[tick - 1];
                for (int r = 0; r < left.Length; r++)
                    if (FirstDifferentByte(left[r], right[r]) >= 0) different.Add(r);
                return different;
            }

            private static bool SameStandings(ulong[] left, ulong[] right)
            {
                for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
                return true;
            }

            private static int FirstDifferentByte(byte[] left, byte[] right)
            {
                int count = Math.Min(left.Length, right.Length);
                for (int i = 0; i < count; i++) if (left[i] != right[i]) return i;
                return left.Length == right.Length ? -1 : count;
            }

            // null when the first `samples` samples are identical, else the first tick, runner and value that differ.
            public static string Compare(FieldTrace a, FieldTrace b, int samples)
            {
                if (a.Count < samples || b.Count < samples) return $"a trace is too short: {a.Count} and {b.Count} samples, {samples} wanted";
                int tick = FirstDifferentTick(a, b, samples);
                if (tick < 0) return null;
                var text = new StringBuilder($"first difference after tick {tick}:");
                foreach (int r in DifferentRacers(a, b, tick))
                {
                    byte[] left = a.states[tick - 1][r], right = b.states[tick - 1][r];
                    int at = FirstDifferentByte(left, right);
                    text.Append($" runner {r} state byte {at} of {left.Length}/{right.Length}");
                    if (at < left.Length && at < right.Length) text.Append($" (0x{left[at]:X2} vs 0x{right[at]:X2})");
                    text.Append(';');
                }
                ulong[] ls = a.standings[tick - 1], rs = b.standings[tick - 1];
                string[] names = { "place", "finishTick", "progress", "swingBreaks", "contactSequence" };
                for (int i = 0; i < ls.Length; i++)
                {
                    if (ls[i] == rs[i]) continue;
                    string name = i == ls.Length - 1 ? "hasResult" : $"runner {i / StandingValues} {names[i % StandingValues]}";
                    text.Append($" {name} 0x{ls[i]:X} vs 0x{rs[i]:X};");
                }
                if (tick > 1) text.Append(" The tick before it was identical.");
                return text.ToString();
            }
        }

        // ---- helpers -------------------------------------------------------------------------------

        private IEnumerator Load(string path)
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Fail("Replay tests require the Unity Editor.");
            yield break;
#endif
            game = null;
            player = null;
            circuit = null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.TryGetComponent(out ChainRushGame g)) game = g;
                if (root.TryGetComponent(out RunnerMotor p)) { player = p; grapple = p.GetComponent<GrappleController>(); }
                if (root.TryGetComponent(out CircuitRace c)) circuit = c;
            }
            Assert.That(game, Is.Not.Null);
            yield return null;
        }

        private StateTrace CircuitTrace(IInputSource inner) => new StateTrace(game, player, inner,
            ("lapProgress", () => circuit.Laps.Progress),
            ("checkpoints", () => circuit.Laps.CheckpointsPassed),
            ("laps", () => circuit.Laps.CompletedLaps));

        // Starts a run on `source` and lets it go on until the game has taken at least `ticks` ticks.
        private IEnumerator Run(IInputSource source, int ticks, float scale)
        {
            game.SetInputSource(source);
            Time.timeScale = scale;
            game.StartRun();
            float deadline = Time.realtimeSinceStartup + 200f;
            while (game.IsRunning && game.Tick < ticks && Time.realtimeSinceStartup < deadline) yield return null;
            Time.timeScale = timeScale;
            Assert.That(game.IsRunning, Is.True, $"The run ended at tick {game.Tick} (health {game.Health}, failed {game.HasFailed}).");
            Assert.That(game.Tick, Is.GreaterThanOrEqualTo(ticks), "Timed out before the wanted number of ticks.");
        }

        // The traces are compared over exactly `ticks` samples: a run goes a tick or two past it before the
        // frame ends, and those extra ticks are not part of the comparison.
        private static void AssertSame(string what, StateTrace expected, StateTrace actual, int ticks)
        {
            string difference = StateTrace.Compare(expected, actual, ticks);
            Assert.That(difference, Is.Null, what + ": " + difference);
            Assert.That(actual.Hash(ticks), Is.EqualTo(expected.Hash(ticks)), what);
        }
    }
}
