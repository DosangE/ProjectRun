using System;
using System.Collections.Generic;
using ProtoHarness.ChainRush.Bots;
using ProtoHarness.ChainRush.Control;
using ProtoHarness.ChainRush.Motion;
using ProtoHarness.ChainRush.Track;
using UnityEngine;

namespace ProtoHarness.ChainRush.Race
{
    // A race of several runners, each in a world of its own (RaceWorld; DECISIONS 2026-11-12 "MP1 구조"): the player's
    // game and one isolated copy of the race scene for every opponent. No game steps itself here (SelfStepping off);
    // every FixedUpdate runs everyone's input, everyone's movement, contact, then everyone's judgment on the same tick.
    // The opponents follow the player's game: they stand on the start line while it is ready or counting down, start
    // on the tick it starts, and are paused while it is paused. Once the player's run has ended nobody is stepped.
    // RaceAssembler adds the field when the opponents' worlds are loaded.
    public sealed class RaceField : MonoBehaviour
    {
        private ChainRushGame game;
        private RaceWorld[] opponents;
        private bool staged;
        private ChainRushGame[] games;
        private IInputSource[] inputs;
        private RaceOrder order;
        private int[] places;
        private int[] finishTicks;
        private double[] progress;
        private bool[] ended;
        private bool[] stepping;
        private RunnerMotor[] contactMotors;
        private Vector3[] contactPositions;
        private Vector3[] contactVelocities;
        private float[] contactRadii;
        private float[] contactHeights;
        private float[] contactWeights;
        private Vector3[] contactMoves;
        private Vector3[] contactChanges;
        private Vector3[] contactWallNormals;
        private Vector3[] observedPositions, observedVelocities;
        private int[] contactSwingBreaks;
        // Presentation only (RunnerAnimation's bump): never in RacerState, a snapshot or the contact solve.
        private int[] contactRacers;
        private uint[] contactSequences;
        private bool[] touchedThisTick, touchedLastTick;
        private bool observationReady;
        // Who is right behind another runner on this tick (FindDrafts), from the same snapshot the bots read.
        private Slipstream.Spot[] spots;
        private bool[] behind;
        public bool ContactEnabled { get; set; } = true;
        // Off, no runner drafts. With contact off too the worlds share nothing.
        public bool DraftEnabled { get; set; } = true;
        private bool stopped;
        // Match the existing running-order hysteresis (RaceServer); finishers are ranked separately below.
        private const double OrderMarginMetres = 1d;

        // The opponents' worlds, in the order they are stepped; empty until Initialize.
        public IReadOnlyList<RaceWorld> Opponents => opponents ?? Array.Empty<RaceWorld>();
        public int RacerCount => opponents == null ? 0 : opponents.Length + 1;
        public bool HasResult { get; private set; }
        // A common snapshot captured once immediately before input. No live motor references escape this API.
        public int ObservedRacerCount => observationReady ? RacerCount : 0;
        public Vector3 ObservedPosition(int racer) => observedPositions[CheckedObservation(racer)];
        public Vector3 ObservedVelocity(int racer) => observedVelocities[CheckedObservation(racer)];
        public int ContactSwingBreaks(int racer) => contactSwingBreaks[Checked(racer)];
        // Counts the contacts with other runners that began for this racer: one more on a tick on which the solve moved
        // it or changed its velocity after a tick on which it did not, so a shove counts once however long the runners
        // stay pressed together. At most one a tick. Zero again on a restart. Presentation only, as ContactSwingBreaks.
        public uint ContactSequence(int racer) => contactSequences[Checked(racer)];

        private int CheckedObservation(int racer)
        {
            if (!observationReady) throw new InvalidOperationException("RaceField: no input snapshot has been captured.");
            return Checked(racer);
        }
        // Raised only after every world has completed this tick and the standings are coherent.
        public event Action StandingsChanged;
        public int Place(int racer) => places[Checked(racer)];
        public int FinishTick(int racer) => finishTicks[Checked(racer)];
        public double Progress(int racer) => progress[Checked(racer)];
        public string RunnerName(int racer) => games[Checked(racer)].Player.Profile.DisplayName;
        public IInputSource OpponentInput(int opponent)
        {
            if (inputs == null || opponent < 0 || opponent >= inputs.Length) throw new ArgumentOutOfRangeException(nameof(opponent));
            return inputs[opponent];
        }

        private int Checked(int racer)
        {
            if (racer < 0 || racer >= RacerCount) throw new ArgumentOutOfRangeException(nameof(racer));
            return racer;
        }

        public void Initialize(ChainRushGame playerGame, IReadOnlyList<RaceWorld> opponentWorlds, IReadOnlyList<IInputSource> opponentInputs)
        {
            if (playerGame == null) throw new ArgumentNullException(nameof(playerGame));
            if (opponentWorlds == null) throw new ArgumentNullException(nameof(opponentWorlds));
            if (opponentWorlds.Count == 0) throw new ArgumentException("A field needs at least one opponent's world.", nameof(opponentWorlds));
            if (opponentInputs == null) throw new ArgumentNullException(nameof(opponentInputs));
            if (opponentInputs.Count != opponentWorlds.Count) throw new ArgumentException("Each opponent needs one input source.", nameof(opponentInputs));
            if (playerGame.Circuit == null) throw new ArgumentException("The player's world needs a circuit.", nameof(playerGame));
            // Contact weighs every runner by its profile (RunnerProfile.Weight), on every tick.
            if (playerGame.Player.Profile == null) throw new ArgumentException("The player's runner needs a profile (RunnerMotor.Configure).", nameof(playerGame));
            if (opponents != null) throw new InvalidOperationException("RaceField: the field is already set up.");
            PhysicsScene playerPhysics = playerGame.gameObject.scene.GetPhysicsScene();
            var worlds = new RaceWorld[opponentWorlds.Count];
            for (int i = 0; i < worlds.Length; i++)
            {
                RaceWorld world = opponentWorlds[i];
                if (world == null) throw new ArgumentException($"Opponent world {i} is missing.", nameof(opponentWorlds));
                if (opponentInputs[i] == null || opponentInputs[i] is KeyboardMouseInputSource)
                    throw new ArgumentException($"Opponent {i} needs an input source of its own, never the keyboard.", nameof(opponentInputs));
                if (world.Game == playerGame) throw new ArgumentException($"Opponent world {i} is the player's own world.", nameof(opponentWorlds));
                if (world.Game.Circuit == null) throw new ArgumentException($"Opponent world {i} needs a circuit.", nameof(opponentWorlds));
                if (world.Player.Profile == null) throw new ArgumentException($"Opponent world {i}'s runner needs a profile (RunnerMotor.Configure).", nameof(opponentWorlds));
                if (world.PhysicsWorld == playerPhysics)
                    throw new ArgumentException($"Opponent world {i} shares the player's physics scene; load it with RaceWorld.IsolatedLoad.", nameof(opponentWorlds));
                for (int k = 0; k < i; k++)
                    if (worlds[k].Game == world.Game) throw new ArgumentException($"Opponent worlds {k} and {i} are the same world.", nameof(opponentWorlds));
                worlds[i] = world;
            }
            game = playerGame;
            opponents = worlds;
            games = new ChainRushGame[RacerCount];
            games[0] = game;
            inputs = new IInputSource[opponents.Length];
            places = new int[RacerCount];
            finishTicks = new int[RacerCount];
            progress = new double[RacerCount];
            ended = new bool[RacerCount];
            stepping = new bool[RacerCount];
            contactMotors = new RunnerMotor[RacerCount];
            contactPositions = new Vector3[RacerCount];
            contactVelocities = new Vector3[RacerCount];
            contactRadii = new float[RacerCount];
            contactHeights = new float[RacerCount];
            contactWeights = new float[RacerCount];
            contactMoves = new Vector3[RacerCount];
            contactChanges = new Vector3[RacerCount];
            contactWallNormals = new Vector3[RacerCount];
            observedPositions = new Vector3[RacerCount];
            observedVelocities = new Vector3[RacerCount];
            spots = new Slipstream.Spot[RacerCount];
            behind = new bool[RacerCount];
            contactSwingBreaks = new int[RacerCount];
            contactRacers = new int[RacerCount];
            contactSequences = new uint[RacerCount];
            touchedThisTick = new bool[RacerCount];
            touchedLastTick = new bool[RacerCount];
            game.SelfStepping = false;
            for (int i = 0; i < opponents.Length; i++)
            {
                opponents[i].Game.SelfStepping = false;
                // The keyboard starts the player's run only; the field starts the others with it.
                opponents[i].Game.ManualStart = false;
                games[i + 1] = opponents[i].Game;
                inputs[i] = opponentInputs[i];
                games[i + 1].SetInputSource(inputs[i]);
                if (inputs[i] is RaceBot bot) bot.Observe(this, i + 1);
            }
            if (game.InputSource is RaceBot playerBot) playerBot.Observe(this, 0);
            Stage();
        }

        private void Start()
        {
            if (opponents != null) return;
            Debug.LogError("RaceField: Initialize was not called; the field has no worlds to run.", this);
            enabled = false;
        }

        private void Update()
        {
            if (opponents == null) return;
            MirrorPause();
            if ((game.IsReady || game.IsCountingDown) && !staged) Stage();
        }

        private void FixedUpdate()
        {
            if (opponents == null || stopped || !game.IsRunning) return;
            MirrorPause();
            if (game.Tick == 0)
            {
                // The player's run starts on this tick (a countdown ran out, or StartRun): every opponent's does too.
                for (int i = 0; i < opponents.Length; i++) opponents[i].Game.StartRun();
                ResetStandings();
                for (int i = 0; i < RacerCount; i++)
                {
                    order.Start(i);
                    order.Advance(i, 0, games[i].Circuit.Laps.Progress);
                }
                staged = false;
            }
            try
            {
                for (int i = 0; i < RacerCount; i++)
                {
                    observedPositions[i] = games[i].Player.transform.position;
                    observedVelocities[i] = games[i].Player.Velocity;
                }
                observationReady = true;
                FindDrafts();
                for (int i = 0; i < RacerCount; i++) stepping[i] = games[i].InputTick();
                for (int i = 0; i < RacerCount; i++) if (stepping[i]) games[i].MoveTick(behind[i]);
                if (ContactEnabled) ResolveContact();
                for (int i = 0; i < RacerCount; i++) if (stepping[i]) games[i].JudgeTick();
            }
            catch (Exception e)
            {
                // The field runs the stages itself, past StepTick's own clean-up: a stage that threw leaves the other runners
                // between stages, and no later tick of this race can be trusted. It stops once, loudly.
                Debug.LogException(e, this);
                Stop($"a runner's tick threw {e.GetType().Name}; the worlds are left mid-tick and the race stops.");
                return;
            }
            int tick = game.Tick;
            for (int i = 0; i < opponents.Length; i++)
            {
                ChainRushGame other = opponents[i].Game;
                // A run that has ended keeps the tick it ended on.
                if (!other.IsRunning || other.Tick == tick) continue;
                Stop($"opponent {i} is at tick {other.Tick} and the player at tick {tick}; the worlds are out of step and the race stops.");
                return;
            }
            CollectStandings();
        }

        // A stopped field steps nobody again, even when its tick is called directly.
        private void Stop(string why)
        {
            Debug.LogError("RaceField: " + why, this);
            stopped = true;
            enabled = false;
        }

        // Who is right behind whom as this tick begins, before anyone's input or movement, so the order the runners are
        // stepped in does not matter. Each runner is placed on the road by its own world's track (which looks for it near
        // where it was). A runner that has finished or is blinking out its respawn hold is neither behind nor drafted.
        private void FindDrafts()
        {
            Array.Clear(behind, 0, behind.Length);
            if (!DraftEnabled) return;
            for (int i = 0; i < RacerCount; i++)
            {
                if (!CanDraft(i)) continue;
                Centerline track = games[i].Track;
                Vector3 at = observedPositions[i];
                spots[i] = new Slipstream.Spot(at, track.Project(at), track.BranchAt(at), Vector3.Dot(observedVelocities[i], track.Frame(at).Forward));
            }
            double lap = game.Track.IsLoop ? game.Circuit.Definition.LapLength : 0d;
            for (int i = 0; i < RacerCount; i++)
            {
                if (!CanDraft(i)) continue;
                for (int ahead = 0; ahead < RacerCount && !behind[i]; ahead++)
                    behind[i] = ahead != i && CanDraft(ahead) && Slipstream.IsBehind(spots[i], spots[ahead], lap);
            }
        }

        private bool CanDraft(int racer) => games[racer].IsRunning && !games[racer].Player.IsRespawning;

        private void ResolveContact()
        {
            // Pack only running racers, preserving their field index order, into a prefix of the cached arrays. A runner
            // blinking out its respawn hold is a ghost (user 2026-11-17, "그 깜빡이는 동안은 유령이다"): left out, it is
            // neither pushed nor pushes, and keeps no velocity change for the end of its hold (DECISIONS "MP2 남은 것").
            Array.Clear(contactWallNormals, 0, contactWallNormals.Length);
            for (int pass = 0; pass < 3; pass++)
            {
                int count = 0;
                for (int i = 0; i < RacerCount; i++)
                {
                    if (!games[i].IsRunning) continue;
                    RunnerMotor motor = games[i].Player;
                    if (motor.IsRespawning) continue;
                    contactRacers[count] = i;
                    contactMotors[count] = motor;
                    contactPositions[count] = motor.ContactCenter;
                    contactVelocities[count] = motor.Velocity;
                    contactRadii[count] = motor.ContactRadius;
                    contactHeights[count] = motor.ContactHeight;
                    contactWeights[count++] = motor.Profile.Weight; // Initialize refused a runner without a profile.
                }
                RunnerContact.Solve(contactPositions, contactVelocities, contactRadii, contactHeights, contactWeights,
                    RunnerContact.Restitution, contactMoves, contactChanges, count, contactWallNormals);
                for (int i = 0; i < count; i++)
                {
                    int racer = contactRacers[i];
                    if (contactMoves[i] != Vector3.zero || contactChanges[i] != Vector3.zero) touchedThisTick[racer] = true;
                    bool swinging = contactMotors[i].IsSwinging;
                    Vector3 wall = contactMotors[i].ApplyContact(contactMoves[i], contactChanges[i]);
                    if (swinging && !contactMotors[i].IsSwinging) contactSwingBreaks[racer]++;
                    if (wall != Vector3.zero) contactWallNormals[i] = wall;
                }
            }
            for (int r = 0; r < RacerCount; r++)
            {
                if (touchedThisTick[r] && !touchedLastTick[r]) contactSequences[r]++;
                touchedLastTick[r] = touchedThisTick[r];
                touchedThisTick[r] = false;
            }
        }

        private void ResetStandings()
        {
            order = new RaceOrder(RacerCount, OrderMarginMetres);
            Array.Clear(places, 0, places.Length);
            Array.Clear(finishTicks, 0, finishTicks.Length);
            Array.Clear(progress, 0, progress.Length);
            Array.Clear(ended, 0, ended.Length);
            Array.Clear(contactSwingBreaks, 0, contactSwingBreaks.Length);
            Array.Clear(contactSequences, 0, contactSequences.Length);
            Array.Clear(touchedThisTick, 0, touchedThisTick.Length);
            Array.Clear(touchedLastTick, 0, touchedLastTick.Length);
            observationReady = false;
            HasResult = false;
            StandingsChanged?.Invoke();
        }

        private void CollectStandings()
        {
            // In particular, the player's finish does not freeze out an opponent finishing on this same tick.
            for (int r = 0; r < RacerCount; r++)
            {
                if (ended[r]) continue;
                ChainRushGame runner = games[r];
                progress[r] = runner.Circuit.Laps.Progress;
                // A failed runner stays at its frozen distance until the player's race ends.
                order.Advance(r, game.Tick, progress[r]);
                if (!runner.HasFinished) continue;
                finishTicks[r] = runner.Tick;
                ended[r] = true;
                order.End(r);
            }
            int finishers = 0;
            for (int r = 0; r < RacerCount; r++) if (finishTicks[r] > 0) finishers++;
            bool result = game.HasFinished || game.HasFailed;
            for (int r = 0; r < RacerCount; r++)
            {
                if (finishTicks[r] > 0)
                {
                    int place = 1;
                    for (int o = 0; o < RacerCount; o++)
                        if (finishTicks[o] > 0 && finishTicks[o] < finishTicks[r]) place++;
                    places[r] = place;
                }
                // While the race runs, the running order's margin keeps runners side by side from swapping places on every
                // tick. The result is final, so those still out go by the distance they reached (D7 (a)): the margin would
                // keep the order of a tick before.
                else places[r] = finishers + (result ? 1 + RunnersFurtherThan(r) : order[r]);
            }
            HasResult = result;
            StandingsChanged?.Invoke();
        }

        // How many runners who have not finished got further than this one; equal distance shares a place.
        private int RunnersFurtherThan(int racer)
        {
            int further = 0;
            for (int o = 0; o < RacerCount; o++)
                if (o != racer && finishTicks[o] == 0 && progress[o] > progress[racer]) further++;
            return further;
        }

        // Puts every opponent on the start line, at rest, as the player's game is before its run.
        private void Stage()
        {
            for (int i = 0; i < opponents.Length; i++) opponents[i].Game.ReturnToStart();
            ResetStandings();
            staged = true;
        }

        // An opponent is paused exactly while the player's game is.
        private void MirrorPause()
        {
            bool paused = game.IsPaused;
            for (int i = 0; i < opponents.Length; i++)
            {
                ChainRushGame other = opponents[i].Game;
                if (paused ? other.IsRunning : other.IsPaused) other.TogglePause();
            }
        }

        private void OnDestroy()
        {
            if (game != null) game.SelfStepping = true;
        }
    }
}
