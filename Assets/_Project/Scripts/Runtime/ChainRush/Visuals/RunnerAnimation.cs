using System;
using UnityEngine;
using ProtoHarness.ChainRush.Audio;
using ProtoHarness.ChainRush.Race;

namespace ProtoHarness.ChainRush.Visuals
{
    // Presentation only: picks the runner's pose from the sim's state each frame and hands it to the Animator
    // (Art/Animations/Runner.controller, whose clips come from the runner FBX). The sim never reads this.
    // The footstep and landing sounds are still timed here, from the gait phase. A pose is chosen, never blended by
    // hand: the controller's transitions blend.
    [DefaultExecutionOrder(120)]
    public sealed class RunnerAnimation : MonoBehaviour
    {
        // The controller's State values (Runner.controller: Any State -> the state of that number).
        public enum Pose { Idle = 0, Run = 1, Air = 2, Land = 3, GrappleHang = 4, PoleSwing = 5, Boost = 6, Bump = 7, PoleSwingEdge = 8 }

        // One run cycle of the Run clip (16 frames at 24 fps) and of the Boost clip (12 frames); a cycle is two steps.
        private const float RunCycleSeconds = 16f / 24f;
        private const float BoostCycleSeconds = 12f / 24f;
        // The one-shot clips' lengths: how long the runner stays in them (Runner_Land 5 frames, Runner_Bump 7).
        private const float LandSeconds = 5f / 24f;
        private const float BumpSeconds = 7f / 24f;
        // Gait phase runs this many radians per metre; a footstep is every pi (the old walk cycle, kept for the sounds).
        private const float RadiansPerMetre = 1.65f;
        // Forward speed under which a grounded runner stands rather than runs.
        private const float MinRunSpeed = 0.1f;
        private static readonly int StateParameter = Animator.StringToHash("State");
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");

        [SerializeField] private ChainRushGame game;
        [SerializeField] private RunnerMotor player;
        [SerializeField] private GrappleController grapple;
        [SerializeField] private ChainRushAudio sound;
        [Tooltip("The runner's model (RunnerTilt.Body); its RunnerVisual's Animator is the one driven.")]
        [SerializeField] private Transform model;
        private Animator animator;
        private Pose current = Pose.Idle;
        private float phase;
        private float clock;
        private float landUntil;
        private float bumpUntil;
        private bool bumpPending;
        private int lastStep;
        private bool wasGrounded = true;
        private float lastElapsed;
        private RaceField field;
        private int fieldRacer;
        private uint seenContact;

        public float GaitPhase => phase;
        // The pose last handed to the Animator.
        public Pose Current => current;
        public Animator Animator => animator;

        private void Awake()
        {
            if (game == null || player == null || grapple == null || sound == null || model == null)
            {
                Debug.LogError("RunnerAnimation: all runtime references and the model are required.", this);
                enabled = false;
                return;
            }
            RunnerVisual visual = model.GetComponentInChildren<RunnerVisual>(true);
            if (visual == null || visual.Animator == null)
            {
                Debug.LogError("RunnerAnimation: the model has no RunnerVisual with an Animator.", this);
                enabled = false;
                return;
            }
            animator = visual.Animator;
            animator.applyRootMotion = false;
        }

        private void OnEnable()
        {
            if (game != null) game.CuePlayed += OnCue;
        }

        private void OnDisable()
        {
            if (game != null) game.CuePlayed -= OnCue;
        }

        // A guard hit hard enough to hear is a bump (RunnerMotor.GuardHitCueSpeed); so is a contact with another runner
        // that has begun, once BindField has named this runner's place in a field.
        private void OnCue(RaceCue cue)
        {
            if (cue == RaceCue.GuardHit) Bump();
        }

        // A hit that shoves the runner: plays Runner_Bump once, then the pose that state calls for.
        public void Bump() => bumpPending = true;

        // This runner is racer `racer` of the field (RaceAssembler: the player 0, opponent i as i + 1): each contact that
        // begins for it (RaceField.ContactSequence) is a bump. A race on its own has no field and bumps on guards only.
        public void BindField(RaceField field, int racer)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (racer < 0 || racer >= field.RacerCount) throw new ArgumentOutOfRangeException(nameof(racer), racer, "RunnerAnimation: no such racer in the field.");
            this.field = field;
            fieldRacer = racer;
            seenContact = field.ContactSequence(racer);
        }

        // A contact begun since the last frame bumps. The field's count starts over at zero on a restart, which is no bump.
        private void ReadContact()
        {
            if (field == null) return;
            uint sequence = field.ContactSequence(fieldRacer);
            if (sequence > seenContact) bumpPending = true;
            seenContact = sequence;
        }

        private void LateUpdate()
        {
            if (game.IsPaused)
            {
                animator.speed = 0f;
                return;
            }
            if (game.Elapsed < lastElapsed) ResetPose();
            lastElapsed = game.Elapsed;
            if (!game.IsRunning)
            {
                // Before the run (ready, countdown) the runner stands; a finished or failed run holds its last pose.
                bool held = game.HasFinished || game.HasFailed;
                animator.speed = held ? 0f : 1f;
                if (!held) Set(Pose.Idle, 1f);
                return;
            }
            animator.speed = 1f;
            bool ground = player.IsGrounded;
            float dt = Time.deltaTime;
            clock += dt;
            if (ground && !wasGrounded && game.Elapsed > 0.2f) { landUntil = clock + LandSeconds; sound.PlayCue(RaceCue.Land); }
            wasGrounded = ground;
            float speed = Mathf.Max(0f, player.ForwardSpeed);
            if (ground) phase += dt * speed * RadiansPerMetre;
            int step = Mathf.FloorToInt(phase / Mathf.PI);
            if (ground && step != lastStep) { sound.PlayCue(RaceCue.Footstep); lastStep = step; }
            ReadContact();
            if (bumpPending) { bumpUntil = clock + BumpSeconds; bumpPending = false; }
            bool booster = game.Racer.SlingTicks > 0 || game.Racer.GrappleEmpowered;
            bool boosting = booster || FeedbackLook.PadShare(game.Racer.BoostZoneCarrySpeed, player.RunSpeed, player.BoostZoneSpeed) > 0f;
            // The first that applies. A swing stays PoleSwing under a booster (the art contract), and the pose of a hit
            // is held for its clip whatever else is going on.
            Pose next;
            if (player.IsRespawning) next = Pose.Idle;
            else if (clock < bumpUntil) next = Pose.Bump;
            else if (player.IsSwinging) next = Pose.PoleSwing;
            else if (grapple.IsAttached) next = Pose.GrappleHang;
            else if (!ground) next = Pose.Air;
            else if (clock < landUntil) next = Pose.Land;
            else if (boosting) next = Pose.Boost;
            else if (speed > MinRunSpeed) next = Pose.Run;
            else next = Pose.Idle;
            // A step lasts as long as the ground takes to pass under it, so the feet keep up with the road.
            float cycle = next == Pose.Boost ? BoostCycleSeconds : RunCycleSeconds;
            Set(next, next == Pose.Run || next == Pose.Boost ? speed * RadiansPerMetre / (2f * Mathf.PI) * cycle : 1f);
        }

        private void Set(Pose pose, float playbackSpeed)
        {
            if (pose != current)
            {
                current = pose;
                animator.SetInteger(StateParameter, (int)pose);
            }
            animator.SetFloat(SpeedParameter, playbackSpeed);
        }

        // A restart starts over: the runner stands, mid-clip or not.
        public void ResetPose()
        {
            phase = 0f; clock = 0f; landUntil = 0f; bumpUntil = 0f; bumpPending = false;
            lastStep = 0; wasGrounded = true; lastElapsed = 0f;
            // The field may start its count over before or after this; ReadContact takes the lower count without a bump.
            if (field != null) seenContact = field.ContactSequence(fieldRacer);
            current = Pose.Idle;
            animator.SetInteger(StateParameter, (int)Pose.Idle);
            animator.SetFloat(SpeedParameter, 1f);
            animator.Play(Pose.Idle.ToString(), 0, 0f);
            animator.Update(0f);
        }
    }
}
