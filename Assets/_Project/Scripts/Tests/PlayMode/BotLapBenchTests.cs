using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using ProtoHarness.ChainRush;
using ProtoHarness.ChainRush.Audio;
using ProtoHarness.ChainRush.Bots;
using ProtoHarness.ChainRush.Control;
using ProtoHarness.ChainRush.Flow;
using ProtoHarness.ChainRush.Race;
using ProtoHarness.ChainRush.Track;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace ProtoHarness.Tests.PlayMode
{
    // Explicit on both suite and leaves: only a name selection may run these measurements.
    [Explicit]
    public sealed class BotLapBenchTests
    {
        private const int TickLimit = 60000;
        // 0.5 s (user 2026-11-16); BotTuning.ContactRehookTicks in both presets.
        private const int RehookTicks = 25;
        private static readonly string[] Tracks = { "Stadium", "Skyway", "FingerLanes", "PalmCoast", "SummitDrop" };
        private float scale, volume;
        private string commit;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scale = Time.timeScale;
            volume = AudioListener.volume;
            Time.timeScale = 0f; // Synchronous fixed ticks, as in the existing SummitDrop measurements.
            AudioListener.volume = 0f;
            RaceLaunch.Clear();
            var info = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            using (Process process = Process.Start(info))
            {
                commit = process.StandardOutput.ReadToEnd().Trim();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || commit.Length != 40) throw new InvalidOperationException("BotBench git rev-parse HEAD failed: " + error);
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RaceLaunch.Clear();
            Time.timeScale = scale;
            AudioListener.volume = volume;
            yield return null;
            TestLogs.AssertNoUnexpected();
        }

        // Select the generated case name (e.g. Solo_FiveTracks(Normal)) to measure just a changed difficulty.
        [UnityTest, Explicit, Timeout(1800000)]
        public IEnumerator Solo_FiveTracks([Values(BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard)] BotDifficulty difficulty)
        {
            var suiteClock = Stopwatch.StartNew();
            foreach (string track in Tracks)
            {
                var clock = Stopwatch.StartNew();
                RaceWorld world = null;
                yield return RaceBotTests.LoadRace(track, difficulty, false, 0, (w, _) => world = w);
                int falls = 0;
                Action<RaceCue> cue = value => { if (value == RaceCue.Respawn) falls++; };
                world.Game.CuePlayed += cue;
                IInputSource source = RaceBotTests.InputFor(world, difficulty);
                world.Game.SetInputSource(source);
                world.Game.StartRun();
                for (int tick = 0; tick < TickLimit && world.Game.IsRunning; tick++)
                {
                    world.Game.StepTick(source);
                    if (tick % 200 == 0) yield return null;
                }
                world.Game.CuePlayed -= cue;
                int unfinished = world.Game.HasFinished ? 0 : 1;
                double seconds = unfinished == 0 ? world.Game.Tick * (double)Ticks.Seconds : double.NaN;
                Append("Solo", track, difficulty, 0, seconds, 0, falls, unfinished);
                Debug.Log($"BotBench Solo {track} {difficulty}: ticks={world.Game.Tick}, seconds={Number(seconds)}, falls={falls}, unfinished={unfinished}, wallMinutes={clock.Elapsed.TotalMinutes:F6}.");
            }
            Debug.Log($"BotBench Solo suite {difficulty}: 5 tracks, wallMinutes={suiteClock.Elapsed.TotalMinutes:F6}.");
        }

        [UnityTest, Explicit, Timeout(3600000)]
        public IEnumerator Race_FourSeedsTwoTracks([Values(BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard)] BotDifficulty difficulty)
            => RaceFourSeedsTwoTracks(difficulty, true, true);

        // The same races with drafting and catch-up off (RaceField.DraftEnabled, CatchupEnabled), the baseline drafting is
        // compared with.
        [UnityTest, Explicit, Timeout(3600000)]
        public IEnumerator Race_FourSeedsTwoTracks_WithoutDrafts([Values(BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard)] BotDifficulty difficulty)
            => RaceFourSeedsTwoTracks(difficulty, false, false);

        // Drafting on and catch-up off (RaceField.CatchupEnabled), the baseline catch-up is compared with.
        [UnityTest, Explicit, Timeout(3600000)]
        public IEnumerator Race_FourSeedsTwoTracks_WithoutCatchup([Values(BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard)] BotDifficulty difficulty)
            => RaceFourSeedsTwoTracks(difficulty, true, false);

        private IEnumerator RaceFourSeedsTwoTracks(BotDifficulty difficulty, bool drafts, bool catchups)
        {
            // The labels the earlier runs were logged with stay theirs: "RaceDraft" is drafting without catch-up.
            string label = !drafts ? "Race" : catchups ? "RaceCatchup" : "RaceDraft";
            var suiteClock = Stopwatch.StartNew();
            int earlyRehooks = 0;
            foreach (string track in new[] { "SummitDrop", "Stadium" })
            {
                var times = new double[4];
                int allBreaks = 0, allFalls = 0, allUnfinished = 0;
                for (int seed = 1; seed <= 4; seed++)
                {
                    var clock = Stopwatch.StartNew();
                    RaceWorld player = null;
                    RaceField field = null;
                    yield return RaceBotTests.LoadRace(track, difficulty, true, seed, (w, f) => { player = w; field = f; });
                    Assert.That(field.ContactEnabled, Is.True);
                    field.DraftEnabled = drafts;
                    field.CatchupEnabled = catchups;
                    int draftsStarted = 0, leadChanges = 0, leader = -1, longestCharge = 0, chargesLost = 0;
                    var charged = new int[4];
                    Action<RaceCue> draftCue = value => { if (value == RaceCue.Draft) draftsStarted++; };
                    var worlds = new[] { player, field.Opponents[0], field.Opponents[1], field.Opponents[2] };
                    int falls = 0;
                    Action<RaceCue> cue = value => { if (value == RaceCue.Respawn) falls++; };
                    for (int i = 0; i < 4; i++)
                    {
                        IInputSource source = RaceBotTests.InputFor(worlds[i], difficulty);
                        worlds[i].Game.SetInputSource(source);
                        if (source is RaceBot bot) bot.Observe(field, i);
                        worlds[i].Game.CuePlayed += cue;
                        worlds[i].Game.CuePlayed += draftCue;
                    }
                    player.Game.StartRun();
                    // A contact-broken bot waits before hooking again (user 2026-11-16, "다시 스윙을 거는 속도는 0.5초로"):
                    // the ticks from each break to that runner's next new hook.
                    var seenBreaks = new int[4];
                    var lastBreak = new[] { -1, -1, -1, -1 };
                    var wasSwinging = new bool[4];
                    int minGap = int.MaxValue;
                    for (int tick = 0; tick < TickLimit && player.Game.IsRunning; tick++)
                    {
                        RaceBotTests.StepField(field);
                        Assert.That(player.Game.Tick, Is.EqualTo(tick + 1), "The field stopped before this benchmark tick.");
                        for (int i = 0; i < 4; i++)
                        {
                            int charge = worlds[i].Game.Racer.DraftChargeTicks;
                            longestCharge = Math.Max(longestCharge, charge);
                            // Half a second or more behind, then out of it without a boost.
                            if (charge == 0 && charged[i] >= 25 && !worlds[i].Player.IsDrafting) chargesLost++;
                            charged[i] = charge;
                            if (field.Place(i) == 1 && leader != i && (leader < 0 || field.Place(leader) > 1)) { if (leader >= 0) leadChanges++; leader = i; }
                            int now = field.ContactSwingBreaks(i);
                            if (now > seenBreaks[i]) { seenBreaks[i] = now; lastBreak[i] = tick + 1; }
                            bool swinging = worlds[i].Game.Racer.SwingTicks > 0;
                            if (swinging && !wasSwinging[i] && lastBreak[i] >= 0)
                            {
                                int gap = tick + 1 - lastBreak[i];
                                minGap = Math.Min(minGap, gap);
                                if (gap <= RehookTicks) earlyRehooks++;
                                Debug.Log($"BotBench Race {track} {difficulty} seed={seed}: racer {i} hooked {gap} ticks after a contact break.");
                                lastBreak[i] = -1;
                            }
                            wasSwinging[i] = swinging;
                        }
                        if (tick % 200 == 0) yield return null;
                    }
                    Debug.Log($"BotBench Race {track} {difficulty} seed={seed}: shortest break-to-hook {(minGap == int.MaxValue ? "none" : minGap + " ticks")}.");
                    int breaks = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        breaks += field.ContactSwingBreaks(i);
                        worlds[i].Game.CuePlayed -= cue;
                        worlds[i].Game.CuePlayed -= draftCue;
                    }
                    double front = double.MinValue, back = double.MaxValue;
                    for (int i = 0; i < 4; i++) { front = Math.Max(front, field.Progress(i)); back = Math.Min(back, field.Progress(i)); }
                    Debug.Log($"BotBench Draft {track} {difficulty} seed={seed} on={drafts} catchup={catchups}: drafts={draftsStarted}, leadChanges={leadChanges}, longestCharge={longestCharge}, lost={chargesLost}, spread={Number(front - back)} m.");
                    // MP2's real race ends on the player's finish (D7 a), so the other three are still out by rule and
                    // are not counted. Unfinished is the measured runner (the player's slot) not finishing by TickLimit.
                    int unfinished = player.Game.HasFinished ? 0 : 1;
                    double seconds = player.Game.HasFinished ? player.Game.Tick * (double)Ticks.Seconds : double.NaN;
                    times[seed - 1] = seconds;
                    allBreaks += breaks; allFalls += falls; allUnfinished += unfinished;
                    Append(label, track, difficulty, seed, seconds, breaks, falls, unfinished);
                    Debug.Log($"BotBench Race {track} {difficulty} seed={seed}: ticks={player.Game.Tick}, seconds={Number(seconds)}, breaks={breaks}, falls={falls}, unfinished={unfinished}, wallMinutes={clock.Elapsed.TotalMinutes:F6}.");
                }
                // A missing player finish makes the aggregate explicitly NA, rather than dropping the slow run.
                bool complete = true;
                foreach (double time in times) if (double.IsNaN(time)) complete = false;
                Array.Sort(times);
                double median = complete ? (times[1] + times[2]) * 0.5d : double.NaN;
                double worst = complete ? times[3] : double.NaN;
                Debug.Log($"BotBench Race summary {track} {difficulty}: median={Number(median)}, worst={Number(worst)}, breaks={allBreaks}, falls={allFalls}, unfinished={allUnfinished}.");
            }
            Debug.Log($"BotBench Race suite {difficulty}: 8 races, wallMinutes={suiteClock.Elapsed.TotalMinutes:F6}.");
            Assert.That(earlyRehooks, Is.EqualTo(0), $"A bot hooked again within {RehookTicks} ticks of a contact break.");
        }

        // Diagnosis, not a gate (user 2026-11-27: "달릴때 속도는 견인에따라 잘 되는데 스윙속도에대해서 추가가 잘 안되었다"): in
        // a four-bot race with catch-up on, how much of the pull a swing without a booster or a draft gets. Per swing: the
        // share on the tick before the hook and the mean share during it (the gap is by S, which runs faster than the ground
        // round a corner's inside), and how far the hook's speed rose toward the throttle swing's speed plus the pull.
        [UnityTest, Explicit, Timeout(1800000)]
        public IEnumerator Trace_CatchupOnSwings([Values("Stadium", "SummitDrop")] string track)
        {
            const BotDifficulty difficulty = BotDifficulty.Normal;
            RaceWorld player = null;
            RaceField field = null;
            yield return RaceBotTests.LoadRace(track, difficulty, true, 1, (w, f) => { player = w; field = f; });
            var worlds = new[] { player, field.Opponents[0], field.Opponents[1], field.Opponents[2] };
            for (int i = 0; i < 4; i++)
            {
                IInputSource source = RaceBotTests.InputFor(worlds[i], difficulty);
                worlds[i].Game.SetInputSource(source);
                if (source is RaceBot bot) bot.Observe(field, i);
            }
            player.Game.StartRun();
            var runShare = new float[4];
            var inSwing = new bool[4];
            var swingTicks = new int[4];
            var swingShareSum = new double[4];
            var swingEntry = new float[4];
            int swings = 0, pulledSwings = 0, swingTickCount = 0, reached = 0, runTicks = 0;
            double entryShareSum = 0d, meanShareSum = 0d, lengthSum = 0d, risePulled = 0d, wantedPulled = 0d, runShareSum = 0d, runGain = 0d;
            for (int tick = 0; tick < TickLimit && player.Game.IsRunning; tick++)
            {
                RaceBotTests.StepField(field);
                for (int i = 0; i < 4; i++)
                {
                    RacerState racer = worlds[i].Game.Racer;
                    RunnerMotor motor = worlds[i].Player;
                    float share = field.CatchupShare(i);
                    bool plain = racer.SlingTicks == 0 && racer.DraftBoostTicks == 0;
                    bool swinging = racer.SwingTicks > 0;
                    if (swinging && plain)
                    {
                        if (!inSwing[i]) { inSwing[i] = true; swingTicks[i] = 0; swingShareSum[i] = 0d; swingEntry[i] = runShare[i]; }
                        swingTicks[i]++;
                        swingShareSum[i] += share;
                        swingTickCount++;
                        float throttle = motor.RunSpeed * 1.2f;
                        float pull = share * motor.RunSpeed;
                        if (pull > 0.5f)
                        {
                            risePulled += racer.SwingHookSpeed - throttle;
                            wantedPulled += pull;
                            if (racer.SwingHookSpeed >= throttle + pull - 0.1f) reached++;
                        }
                    }
                    else
                    {
                        if (inSwing[i])
                        {
                            inSwing[i] = false;
                            swings++;
                            lengthSum += swingTicks[i];
                            if (swingEntry[i] > 0.02f)
                            {
                                pulledSwings++;
                                entryShareSum += swingEntry[i];
                                meanShareSum += swingShareSum[i] / swingTicks[i];
                            }
                        }
                        if (!swinging && plain && racer.Grounded && racer.SwingCarrySpeed <= 0f && racer.BoostZoneCarrySpeed <= 0f && share > 0.02f)
                        {
                            runTicks++;
                            runShareSum += share;
                            runGain += motor.ForwardSpeed - motor.RunSpeed;
                        }
                        runShare[i] = share;
                    }
                }
                if (tick % 200 == 0) yield return null;
            }
            Debug.Log($"BotBench CatchupSwing {track} {difficulty}: swings={swings}, meanLength={Number(swings > 0 ? lengthSum / swings : double.NaN)} ticks, "
                + $"pulledSwings={pulledSwings}, entryShare={Number(pulledSwings > 0 ? entryShareSum / pulledSwings : double.NaN)}, "
                + $"duringShare={Number(pulledSwings > 0 ? meanShareSum / pulledSwings : double.NaN)}, swingTicks={swingTickCount}, "
                + $"pulledSwingRise={Number(wantedPulled > 0d ? risePulled / wantedPulled : double.NaN)} of the pull, reachedTicks={reached}, "
                + $"runTicks={runTicks}, runShare={Number(runTicks > 0 ? runShareSum / runTicks : double.NaN)}, runGain={Number(runTicks > 0 ? runGain / runTicks : double.NaN)} m/s.");
        }

        // Diagnosis, not a gate: one SummitDrop Fast solo bot run beside this PC's player ghost. Line the two up by 3D
        // position, not S: before StartRun the centerline's focus sits at S 0 and far ghost points project onto the wrong
        // piece (Centerline.PieceAt, CircuitRace.PrepareTick). DECISIONS 2026-11-15 "MP2 ④".
        [UnityTest, Explicit, Timeout(1800000)]
        public IEnumerator Trace_SummitDropVsGhost([Values(BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard)] BotDifficulty difficulty)
        {
            string ghostPath = Path.Combine(Application.persistentDataPath, "Ghosts", "Circuit_SummitDrop.Fast.ghost");
            Assert.That(File.Exists(ghostPath), Is.True, "BotBench trace needs the player's SummitDrop Fast ghost at " + ghostPath);
            GhostTrace ghost = GhostTrace.Decode(File.ReadAllBytes(ghostPath));
            yield return Trace("SummitDrop", difficulty, ghost, Path.Combine(Path.GetTempPath(), "ChainRush-GhostCompare.tsv"));
        }

        // The same per-tick trace on any track, without a ghost, to see what a preset's swings do there.
        [UnityTest, Explicit, Timeout(1800000)]
        public IEnumerator Trace_Solo([Values("Stadium", "Skyway", "FingerLanes", "PalmCoast", "SummitDrop")] string track,
            [Values(BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard)] BotDifficulty difficulty)
        {
            yield return Trace(track, difficulty, null, Path.Combine(Path.GetTempPath(), $"ChainRush-Trace-{track}-{difficulty}.tsv"));
        }

        private static IEnumerator Trace(string track, BotDifficulty difficulty, GhostTrace ghost, string path)
        {
            RaceWorld world = null;
            yield return RaceBotTests.LoadRace(track, difficulty, false, 0, (w, _) => world = w);
            var source = new LastInputSpy(RaceBotTests.InputFor(world, difficulty));
            world.Game.SetInputSource(source);
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine("kind	tick	x	y	z	s	d	h	speed	heading	grounded	attached	swingTicks	slingTicks	gauge	steer	primary	swingPress	swingHeld	chain	curvature	pole");
                for (int i = 0; ghost != null && i < ghost.Count; i++)
                {
                    GhostTrace.Sample sample = ghost[i];
                    TrackCoord coord = world.Game.Track.Project(sample.Position);
                    float speed = i == 0 ? 0f : Vector3.Distance(new Vector3(sample.Position.x, 0f, sample.Position.z),
                        new Vector3(ghost[i - 1].Position.x, 0f, ghost[i - 1].Position.z)) / Ticks.Seconds;
                    writer.WriteLine(string.Join("	", "ghost", (i + 1).ToString(CultureInfo.InvariantCulture),
                        Number(sample.Position.x), Number(sample.Position.y), Number(sample.Position.z), Number(coord.S),
                        Number(coord.D), Number(coord.H), Number(speed), Number(sample.Heading), "", "", "", "", "", "", "", "", "", "", "", ""));
                }
                world.Game.StartRun();
                for (int tick = 0; tick < TickLimit && world.Game.IsRunning; tick++)
                {
                    world.Game.StepTick(source);
                    TickInput input = source.Last;
                    Vector3 position = world.Player.transform.position;
                    TrackCoord coord = world.Game.Track.Project(position);
                    // Read-only queries; the bot makes the same ones (RaceBot.Consume), and the race sets the focus each tick.
                    float curvature = world.Game.Track.Frame(position).Curvature;
                    bool pole = world.Player.TryFindPole(out _);
                    writer.WriteLine(string.Join("	", "bot", world.Game.Tick.ToString(CultureInfo.InvariantCulture),
                        Number(position.x), Number(position.y), Number(position.z), Number(coord.S),
                        Number(coord.D), Number(coord.H), Number(world.Player.HorizontalSpeed), Number(world.Player.Heading),
                        Flag(world.Player.IsGrounded), Flag(world.Grapple.IsAttached),
                        world.Game.Racer.SwingTicks.ToString(CultureInfo.InvariantCulture), world.Game.Racer.SlingTicks.ToString(CultureInfo.InvariantCulture),
                        Number(world.Player.Gauge), Number(input.Steer), Flag(input.PrimaryPressed), Flag(input.SwingPressed),
                        Flag(input.SwingHeld), Flag(input.ChainActionPressed), Number(curvature), Flag(pole)));
                    if (tick % 200 == 0) yield return null;
                }
            }
            Assert.That(world.Game.HasFinished, Is.True, $"The traced bot did not finish {track}.");
            Debug.Log($"BotBench Trace {track} {difficulty}: botTicks={world.Game.Tick}, ghostTicks={(ghost == null ? 0 : ghost.Count)}, file={path}.");
        }

        private sealed class LastInputSpy : IInputSource
        {
            private readonly IInputSource inner;
            public TickInput Last { get; private set; }
            public LastInputSpy(IInputSource inner) => this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            public void Poll() => inner.Poll();
            public void Clear() => inner.Clear();
            public TickInput Consume() => Last = inner.Consume();
        }

        private static string Flag(bool value) => value ? "1" : "0";

        private static string Number(double value) => double.IsNaN(value) ? "NA" : value.ToString("F4", CultureInfo.InvariantCulture);

        private void Append(string kind, string track, BotDifficulty difficulty, int seed, double seconds, int breaks, int falls, int unfinished)
        {
            string path = Path.Combine(Path.GetTempPath(), "ChainRush-BotBench.tsv");
            bool header = !File.Exists(path) || new FileInfo(path).Length == 0;
            using (var writer = new StreamWriter(path, true))
            {
                if (header) writer.WriteLine("timestamp\tcommit\tkind\ttrack\tdifficulty\tseed\tseconds\tbreaks\tfalls\tunfinished");
                writer.WriteLine(string.Join("\t", DateTime.UtcNow.ToString("O"), commit, kind, track, difficulty.ToString(),
                    seed.ToString(CultureInfo.InvariantCulture), Number(seconds), breaks.ToString(CultureInfo.InvariantCulture),
                    falls.ToString(CultureInfo.InvariantCulture), unfinished.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }
}
