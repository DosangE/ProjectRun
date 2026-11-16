using System;
using ProtoHarness.ChainRush.Control;
using ProtoHarness.ChainRush.Motion;
using ProtoHarness.ChainRush.Race;
using ProtoHarness.ChainRush.Track;
using UnityEngine;

namespace ProtoHarness.ChainRush.Bots
{
    // Decisions use only the track, this runner, and the field's common pre-input position/velocity snapshot.
    // No simulation mutation: even chain actions go through TickInput and the motor's real gauge spending rule.
    public sealed class RaceBot : IInputSource
    {
        private enum Stage { OnLoop, FirstSwing, Straight, SecondSwing }
        private readonly ChainRushGame game;
        private readonly RunnerMotor runner;
        private readonly GrappleController grapple;
        private readonly CircuitRace circuit;
        private readonly double[] leaveS, straightEndS;
        private RaceField field;
        private int racerIndex;
        private Stage stage;
        private int active = -1;
        private float turnFrom;
        private bool pressed;
        private int swingLeft, cooldown, retryLeft, rehookLeft, observedBreaks;
        private bool retryShortcut;

        public BotTuning Tuning { get; }

        public RaceBot(ChainRushGame game, RunnerMotor runner, GrappleController grapple, CircuitRace circuit, BotTuning tuning)
        {
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.grapple = grapple ?? throw new ArgumentNullException(nameof(grapple));
            this.circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            tuning.Validate(); // Reject a zero-initialized struct too.
            Tuning = tuning;
            int count = circuit.Definition.Shortcuts.Count;
            leaveS = new double[count];
            straightEndS = new double[count];
            for (int i = 0; i < count; i++)
            {
                TrackDefinition.Shortcut shortcut = circuit.Definition.Shortcuts[i];
                leaveS[i] = shortcut.FromS;
                double along = Mathf.PI * shortcut.Radius * 0.5d + shortcut.Straight;
                straightEndS[i] = shortcut.FromS + along / shortcut.Length * (shortcut.ToS - shortcut.FromS);
            }
        }

        // Bind after the field has its full lineup; a solo bot never binds and sees no other runners.
        public void Observe(RaceField raceField, int index)
        {
            if (raceField == null) throw new ArgumentNullException(nameof(raceField));
            if (index < 0 || index >= raceField.RacerCount) throw new ArgumentException("RaceBot: invalid field index.", nameof(index));
            field = raceField;
            racerIndex = index;
            observedBreaks = field.ContactSwingBreaks(index);
        }

        public void Poll() { }
        public void Clear()
        {
            stage = Stage.OnLoop;
            active = -1;
            turnFrom = 0f;
            pressed = false;
            swingLeft = cooldown = retryLeft = rehookLeft = 0;
            retryShortcut = false;
            observedBreaks = 0;
        }

        public TickInput Consume()
        {
            // Keep TrackFollower's operation order exactly, including Frame then Project, for all-off bit equality.
            Vector3 position = runner.transform.position;
            TrackFrame frame = game.Track.Frame(position);
            float offset = game.Track.Project(position).D;
            float target = 0f;
            if (Tuning.InsideLine)
            {
                float curve = frame.Curvature;
                if (curve == 0f) curve = game.Track.FrameAt(game.Track.Project(position).S + Tuning.CornerLookAhead).Curvature;
                if (curve != 0f) target = Mathf.Sign(curve) * Tuning.InsideOffset;
            }
            bool crowded = false;
            if (Tuning.ContactResponse && field != null) target = AvoidNearby(position, frame, target, out crowded);
            float trackYaw = Mathf.Atan2(frame.Forward.x, frame.Forward.z) * Mathf.Rad2Deg;
            float wanted = trackYaw - Mathf.Clamp((offset - target) * Tuning.OffsetGain, -Tuning.HeadingClamp, Tuning.HeadingClamp);
            float turnLimit = runner.TurnLimit;
            float feedForward = turnLimit > 0f ? runner.ForwardSpeed * frame.Curvature * Mathf.Rad2Deg / turnLimit : 0f;
            float steer = Mathf.Clamp(feedForward + Mathf.DeltaAngle(runner.Heading, wanted) / Tuning.HeadingDivisor, -1f, 1f);
            bool primary = false;
            TrackCoord coord = game.Track.Project(position);
            if (circuit.TryGetNextGap(coord.S, out double startS, out _, out bool hasAnchor))
            {
                if (runner.IsGrounded)
                {
                    double toEdge = startS - coord.S;
                    primary = toEdge > 0d && toEdge <= (hasAnchor ? Tuning.GrappleGapJumpDistance : Tuning.JumpGapJumpDistance);
                }
                else if (hasAnchor && !grapple.IsAttached && coord.H > Tuning.GrappleHeight && coord.S > startS - 10d && coord.S < startS + 20d)
                    primary = true;
            }

            bool press = false, held = false;
            if (field != null)
            {
                int breaks = field.ContactSwingBreaks(racerIndex);
                if (breaks > observedBreaks)
                {
                    // A contact-broken bot hooks nothing new for a human's reaction time (user 2026-11-16, 0.5 s): a broken
                    // shortcut swing used to fall back to the corner rule and hook again on the next tick.
                    rehookLeft = Tuning.ContactRehookTicks;
                    if (Tuning.ContactResponse)
                    {
                        retryShortcut = stage == Stage.FirstSwing || stage == Stage.SecondSwing;
                        retryLeft = Tuning.ContactRetryTicks;
                        swingLeft = 0;
                        cooldown = 0;
                    }
                }
                observedBreaks = breaks;
            }
            bool waitingRetry = retryLeft > 0;
            if (waitingRetry) retryLeft--;
            if (rehookLeft > 0) { rehookLeft--; waitingRetry = true; }
            if (Tuning.ShortcutSwing)
                ShortcutInput(coord.S, crowded || primary || waitingRetry, ref steer, out held, out press);
            if (Tuning.CornerSwing && stage == Stage.OnLoop && !held)
            {
                if (swingLeft > 0)
                {
                    swingLeft--;
                    // Past the minimum hold, a corner swing that still runs along the road keeps going until the corner
                    // ends (DECISIONS 2026-11-15 "MP2 ④", round 22: fixed-tick swings let go mid-corner). The road's yaw is
                    // recomputed here, not read from trackYaw: keeping trackYaw alive this long changed the steering's float
                    // bits and broke the all-off bit equality with CircuitBot (RaceBotTests, 2026-11-16).
                    if (swingLeft == 0 && Tuning.HoldThroughCorner && game.Racer.SwingTicks > 0 &&
                        game.Racer.SwingTicks < Tuning.CornerHoldLimitTicks && frame.Curvature != 0f &&
                        Mathf.Abs(Mathf.DeltaAngle(runner.Heading, Mathf.Atan2(frame.Forward.x, frame.Forward.z) * Mathf.Rad2Deg)) <= Tuning.CornerReleaseHeading)
                        swingLeft = 1;
                    held = swingLeft > 0 && game.Racer.SwingTicks > 0;
                    if (!held) { swingLeft = 0; cooldown = Tuning.SwingCooldownTicks; }
                }
                else if (cooldown > 0) cooldown--;
                else if (!crowded && !primary && !waitingRetry && runner.IsGrounded && !grapple.IsAttached &&
                    (!Tuning.InsideLine || frame.Curvature != 0f) && runner.TryFindPole(out _))
                {
                    press = held = true;
                    swingLeft = Tuning.SwingHoldTicks;
                }
                if (held) steer = SwingSteering();
            }
            bool chain = false;
            if (runner.Gauge >= 1f && game.Racer.SlingTicks == 0)
            {
                // The motor calls both a straight launch and the swing booster Slingshot. The flags select contexts.
                if (Tuning.Booster && !grapple.IsAttached &&
                    (game.Racer.SwingTicks > 0 || (runner.IsGrounded && runner.HorizontalSpeed >= runner.RunSpeed * Tuning.BoosterMinSpeedRatio && ClearStraight(coord.S, Tuning.BoosterStraight))))
                    chain = true;
                if (Tuning.Slingshot && ((grapple.IsAttached && !game.Racer.GrappleEmpowered) ||
                    (runner.IsGrounded && !grapple.IsAttached && runner.HorizontalSpeed <= runner.RunSpeed * Tuning.SlingshotMaxSpeedRatio && ClearStraight(coord.S, Tuning.SlingshotStraight))))
                    chain = true;
            }
            return new TickInput(steer, primary, false, false, held, chain, press);
        }

        private float AvoidNearby(Vector3 position, TrackFrame frame, float target, out bool crowded)
        {
            crowded = false;
            float shift = 0f;
            for (int i = 0; i < field.ObservedRacerCount; i++)
            {
                if (i == racerIndex) continue;
                Vector3 delta = field.ObservedPosition(i) - position;
                // Use current relative closing speed only; no future state or another runner's controls are visible.
                Vector3 relativeVelocity = field.ObservedVelocity(i) - runner.Velocity;
                float distance = delta.magnitude;
                if (distance > Tuning.ContactDistance) continue;
                crowded = true;
                if (Vector3.Dot(delta, relativeVelocity) > 0f) continue;
                float side = Vector3.Dot(delta, frame.Right);
                // An exact tie follows stable field order, never the input visitation order.
                shift += side == 0f ? (i < racerIndex ? Tuning.AvoidOffset : -Tuning.AvoidOffset) : -Mathf.Sign(side) * Tuning.AvoidOffset;
            }
            if (!crowded) return target;
            float half = circuit.Definition.HalfWidthAt(game.Track.Project(position).S);
            float limit = Mathf.Max(0f, half - runner.ContactRadius - Tuning.EdgeMargin);
            return Mathf.Clamp(target + shift, -limit, limit);
        }

        private bool ClearStraight(double s, double ahead)
        {
            if (!game.Track.IsLoop && s + ahead >= game.Track.EndS) return false;
            if (game.Track.TryBoostBetween(s, s + ahead)) return false;
            if (circuit.TryGetNextGap(s, out double gap, out _, out _) && gap - s <= ahead) return false;
            for (double d = 0d; d <= ahead; d += 5d)
                if (game.Track.FrameAt(s + d).Curvature != 0f) return false;
            return true;
        }

        private float SwingSteering()
        {
            if (game.Racer.SwingTicks == 0) return 0f;
            Vector3 toPole = game.Racer.SwingCenter - runner.transform.position;
            float side = Vector3.Dot(toPole, RunDrive.RightOf(runner.Heading)) >= 0f ? 1f : -1f;
            return side * Tuning.SwingSteer;
        }

        private void ShortcutInput(double s, bool delay, ref float steer, out bool held, out bool press)
        {
            held = press = false;
            if (retryShortcut)
            {
                if (delay) return;
                if (runner.IsGrounded && runner.TryFindPole(out _))
                {
                    press = true;
                    turnFrom = runner.Heading;
                    retryShortcut = false;
                }
                else { retryShortcut = false; stage = Stage.OnLoop; }
            }
            else if (stage == Stage.OnLoop && !delay)
            {
                for (int i = 0; i < leaveS.Length; i++)
                    if (s >= leaveS[i] - Tuning.HookWindow && s < leaveS[i])
                    { active = i; stage = Stage.FirstSwing; turnFrom = runner.Heading; press = true; break; }
            }
            else if (stage == Stage.Straight && !delay && s >= straightEndS[active] - Tuning.HookWindow)
            { stage = Stage.SecondSwing; turnFrom = runner.Heading; press = true; }
            else if (stage == Stage.FirstSwing || stage == Stage.SecondSwing)
            {
                if (pressed && game.Racer.SwingTicks == 0) stage = Stage.OnLoop;
                else if (game.Racer.SwingTicks == 0 || Mathf.DeltaAngle(turnFrom, runner.Heading) >= Tuning.ShortcutTurn)
                    stage = stage == Stage.FirstSwing ? Stage.Straight : Stage.OnLoop;
            }
            pressed = press;
            held = stage == Stage.FirstSwing || stage == Stage.SecondSwing;
            if (held) steer = SwingSteering();
        }
    }
}
