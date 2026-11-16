using System;

namespace ProtoHarness.ChainRush.Bots
{
    // Code-only presets: the tuning loop changes this file and RaceBot, never serialized track or physics values.
    public readonly struct BotTuning
    {
        public double GrappleGapJumpDistance { get; } // Grapple-gap takeoff distance before the edge, metres.
        public double JumpGapJumpDistance { get; } // Jump-only takeoff distance before the edge, metres.
        public float GrappleHeight { get; } // Minimum height to press grapple, metres above the road.
        public float OffsetGain { get; } // Lateral error converted to wanted heading, degrees/metre.
        public float HeadingClamp { get; } // Maximum lateral heading correction, degrees.
        public float HeadingDivisor { get; } // Heading error for one full steering input, degrees.
        public bool ShortcutSwing { get; } // Enables the two swings of each defined shortcut, boolean.
        public double HookWindow { get; } // Distance before a shortcut hook to press swing, metres.
        public float ShortcutTurn { get; } // Heading change before releasing a shortcut swing, degrees.
        public float SwingSteer { get; } // Steering toward the pole during a swing, normalized input.
        public bool CornerSwing { get; } // Enables hooking reachable corner poles, boolean.
        public int SwingHoldTicks { get; } // Duration of a corner swing, fixed ticks.
        public int SwingCooldownTicks { get; } // Delay before another corner hook, fixed ticks.
        public bool InsideLine { get; } // Enables an offset toward the inside of corners, boolean.
        public float InsideOffset { get; } // Wanted inside distance from the centerline, metres.
        public double CornerLookAhead { get; } // Distance to inspect for a coming curve, metres.
        public bool Booster { get; } // Enables a gauge-funded booster while swinging or on a straight, boolean.
        public double BoosterStraight { get; } // Required clear straight before firing at running speed, metres.
        public float BoosterMinSpeedRatio { get; } // Minimum current speed divided by run speed, ratio.
        public bool Slingshot { get; } // Enables low-speed straight launches and grapple empowerment, boolean.
        public double SlingshotStraight { get; } // Required clear straight for a low-speed launch, metres.
        public float SlingshotMaxSpeedRatio { get; } // Maximum launch speed divided by run speed, ratio.
        public bool ContactResponse { get; } // Enables nearby-runner avoidance and interrupted-swing retry, boolean.
        public float ContactDistance { get; } // Current-position separation below which a hook is delayed, metres.
        public float EdgeMargin { get; } // Road-edge clearance to preserve when another runner is close, metres.
        public float AvoidOffset { get; } // Lateral target shift away from a close runner, metres.
        public int ContactRetryTicks { get; } // Delay before retrying a contact-broken swing, fixed ticks.
        public bool HoldThroughCorner { get; } // Keeps a corner swing past SwingHoldTicks while the corner lasts, boolean.
        public float CornerReleaseHeading { get; } // Largest heading error from the road that still keeps holding, degrees.
        public int CornerHoldLimitTicks { get; } // Longest corner swing when holding through, fixed ticks.
        public int ContactRehookTicks { get; } // No new hook for this long after a contact breaks a swing, fixed ticks.

        // CircuitBot's values with every technique off: RaceBotTests checks RaceBot against CircuitBot with this. Fixed —
        // the tuning loop changes Normal and Hard only, never this or the constructor's defaults.
        public static BotTuning CircuitEquivalent => new BotTuning(4.5d);
        // The tuning loop's current values (docs/BOT_TUNING_LOG.tsv: Normal round 18, Hard round 22). Both started at round zero
        // equal to CircuitEquivalent.
        public static BotTuning Normal => new BotTuning(4.5d, shortcutSwing: true, cornerSwing: true, swingHoldTicks: 38, insideLine: true, booster: true);
        public static BotTuning Hard => new BotTuning(4.5d, shortcutSwing: true, cornerSwing: true, swingHoldTicks: 45, swingCooldownTicks: 75, insideLine: true, booster: true, boosterStraight: 55d, holdThroughCorner: true);

        public BotTuning(double grappleGapJumpDistance, double jumpGapJumpDistance = 0.8d, float grappleHeight = 2.6f,
            float offsetGain = 4f, float headingClamp = 20f, float headingDivisor = 20f,
            bool shortcutSwing = false, double hookWindow = 0.6d, float shortcutTurn = 88f, float swingSteer = 0f,
            bool cornerSwing = false, int swingHoldTicks = 30, int swingCooldownTicks = 100,
            bool insideLine = false, float insideOffset = 4f, double cornerLookAhead = 30d,
            bool booster = false, double boosterStraight = 110d, float boosterMinSpeedRatio = 0.95f,
            bool slingshot = false, double slingshotStraight = 50d, float slingshotMaxSpeedRatio = 0.8f,
            bool contactResponse = false, float contactDistance = 2f, float edgeMargin = 1f, float avoidOffset = 1f,
            int contactRetryTicks = 5, bool holdThroughCorner = false, float cornerReleaseHeading = 15f, int cornerHoldLimitTicks = 150,
            int contactRehookTicks = 25)
        {
            GrappleGapJumpDistance = grappleGapJumpDistance;
            JumpGapJumpDistance = jumpGapJumpDistance;
            GrappleHeight = grappleHeight;
            OffsetGain = offsetGain;
            HeadingClamp = headingClamp;
            HeadingDivisor = headingDivisor;
            ShortcutSwing = shortcutSwing;
            HookWindow = hookWindow;
            ShortcutTurn = shortcutTurn;
            SwingSteer = swingSteer;
            CornerSwing = cornerSwing;
            SwingHoldTicks = swingHoldTicks;
            SwingCooldownTicks = swingCooldownTicks;
            InsideLine = insideLine;
            InsideOffset = insideOffset;
            CornerLookAhead = cornerLookAhead;
            Booster = booster;
            BoosterStraight = boosterStraight;
            BoosterMinSpeedRatio = boosterMinSpeedRatio;
            Slingshot = slingshot;
            SlingshotStraight = slingshotStraight;
            SlingshotMaxSpeedRatio = slingshotMaxSpeedRatio;
            ContactResponse = contactResponse;
            ContactDistance = contactDistance;
            EdgeMargin = edgeMargin;
            AvoidOffset = avoidOffset;
            ContactRetryTicks = contactRetryTicks;
            HoldThroughCorner = holdThroughCorner;
            CornerReleaseHeading = cornerReleaseHeading;
            CornerHoldLimitTicks = cornerHoldLimitTicks;
            ContactRehookTicks = contactRehookTicks;
            Validate();
        }

        public void Validate()
        {
            Range(GrappleGapJumpDistance, 0.01d, 20d, nameof(GrappleGapJumpDistance));
            Range(JumpGapJumpDistance, 0.01d, 10d, nameof(JumpGapJumpDistance));
            Range(GrappleHeight, 0d, 20d, nameof(GrappleHeight));
            Range(OffsetGain, 0d, 30d, nameof(OffsetGain));
            Range(HeadingClamp, 0.01d, 90d, nameof(HeadingClamp));
            Range(HeadingDivisor, 0.01d, 180d, nameof(HeadingDivisor));
            Range(HookWindow, 0.01d, 10d, nameof(HookWindow));
            Range(ShortcutTurn, 1d, 180d, nameof(ShortcutTurn));
            Range(SwingSteer, -1d, 1d, nameof(SwingSteer));
            Range(SwingHoldTicks, 1d, 500d, nameof(SwingHoldTicks));
            Range(SwingCooldownTicks, 0d, 1000d, nameof(SwingCooldownTicks));
            Range(InsideOffset, 0d, 10d, nameof(InsideOffset));
            Range(CornerLookAhead, 0d, 200d, nameof(CornerLookAhead));
            Range(BoosterStraight, 1d, 500d, nameof(BoosterStraight));
            Range(BoosterMinSpeedRatio, 0d, 3d, nameof(BoosterMinSpeedRatio));
            Range(SlingshotStraight, 1d, 500d, nameof(SlingshotStraight));
            Range(SlingshotMaxSpeedRatio, 0d, 3d, nameof(SlingshotMaxSpeedRatio));
            Range(ContactDistance, 0.01d, 20d, nameof(ContactDistance));
            Range(EdgeMargin, 0d, 10d, nameof(EdgeMargin));
            Range(AvoidOffset, 0d, 10d, nameof(AvoidOffset));
            Range(ContactRetryTicks, 0d, 500d, nameof(ContactRetryTicks));
            Range(CornerReleaseHeading, 0d, 90d, nameof(CornerReleaseHeading));
            Range(CornerHoldLimitTicks, 1d, 1000d, nameof(CornerHoldLimitTicks));
            Range(ContactRehookTicks, 0d, 500d, nameof(ContactRehookTicks));
        }

        private static void Range(double value, double min, double max, string name)
        {
            if (!(value >= min && value <= max)) throw new ArgumentException($"BotTuning: {name} must be within [{min}, {max}].", name);
        }
    }
}
