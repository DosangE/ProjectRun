using System;
using UnityEngine;

namespace ProtoHarness.ChainRush.Visuals
{
    // Presentation timing and sizes shared by the HUD and world effects, with no simulation state or clock of their own.
    public static class FeedbackLook
    {
        public const float PolePulsePeriod = 0.5f;
        public const float PolePulseMin = 1f;
        public const float PolePulseMax = 1.3f;
        public const float PoleEnterTime = 0.3f;
        public const float PoleEnterScale = 1.8f;
        // The pole's screen target, and the bracket it tightens into on the hook, drawn this much larger than DrawTarget's
        // own size (user 2026-11-22 "스윙 가능할 때의 사각형 조금 더 크게"); the tutorial's figures keep that size.
        public const float PoleTargetScale = 1.3f;
        // An off-screen pole's target is pinned this far inside the screen's edge, this much smaller, with an arrow this
        // far beyond it pointing at the pole (user 2026-10-29 "화면 밖에 있어도 그 방향에 있다고").
        public const float EdgeInset = 60f;
        public const float EdgeTargetScale = 0.8f;
        public const float ArrowGap = 8f;
        public const float BoostRingTime = 0.3f;
        // The screen's mint edges (user 2026-11-22 "보이는 시간을 좀 더 늘리고 화면상 더 잘보이게"): a burst over BoostFlashTime
        // when a booster fires, then held at BoostEdgeHold of it while the booster lasts, gone BoostEdgeFadeTime after it.
        public const float BoostFlashTime = 0.4f;
        public const float BoostEdgeHold = 0.55f;
        public const float BoostEdgeFadeTime = 0.3f;
        public const int BoostEdgeLayers = 7;
        public const float BoostEdgeLayerWidth = 16f;
        public const float BoostEdgeAlpha = 0.55f;
        public const int BoostEdgeStreaks = 12;
        public const float BoostEdgeStreakCycle = 0.45f;
        public const float GaugeBurstTime = 0.4f;
        // The HUD's booster slots (user 2026-11-22 "슬롯 미미하다"): a filled slot breathes every SlotBreathPeriod seconds, a
        // slot just filled comes in SlotGainScale times its size, white, settling over SlotGainTime, and a spent one bursts
        // SlotSpendSpread px out.
        public const float SlotBreathPeriod = 0.8f;
        public const float SlotGainTime = 0.3f;
        public const float SlotGainScale = 1.4f;
        public const float SlotSpendSpread = 30f;
        public const float HookRingTime = 0.3f;
        public const float BracketTightenTime = 0.25f;
        public const float BracketStartScale = 1.6f;
        public const float MissShakeTime = 0.3f;
        public const float MissShakeSize = 12f;
        // Boost pad wind (user 2026-10-30 "화면상 외곽에 바람을 가르는 듯한"): white streaks round the screen's rim, each
        // running out past the edge in PadWindCycle seconds; full for PadBurstTime after a pad, then as strong as the
        // pad's speed still is above running speed (PadShare), so the wind dies with the speed.
        public const float PadBurstTime = 0.35f;
        public const int PadWindStreaks = 28;
        public const float PadWindCycle = 0.22f;
        // The pole swing's effects (M6, SwingEffects). Sparks fly off the band and the chain by the hand: per second while
        // swinging, in a burst on the hook and on the snap. The chain whips aside by up to ChainSnapWhip metres as its
        // halves spring back.
        public const float SparkRate = 45f;
        public const float HandSparkRate = 30f;
        public const int SparkHookBurst = 24;
        public const int SparkSnapBurst = 10;
        public const float ChainSnapTime = 0.25f;
        public const float ChainSnapWhip = 0.7f;
        // The tails at speed (SpeedTrails). The swing's tail runs on for SwingTailAfter seconds after the swing; each tail
        // point lasts its Time (a booster's 0.5 s was too long, user 2026-11-06).
        public const float SwingTailAfter = 0.4f;
        public const float SwingTailTime = 0.25f;
        public const float SwingTailWidth = 0.12f;
        // Under a booster (user 2026-11-22 "이펙트가 너무 심심하다") the tail is layered: a white core down its middle and a thin
        // tail of its colour off each shoulder, CoreTailWidth and ShoulderTailWidth wide, ShoulderTailSide metres to either
        // side and ShoulderTailHeight up from the runner's root. Each keeps the booster tail's length.
        public const float BoostTailTime = 0.25f;
        public const float BoostTailWidth = 0.4f;
        public const float SwingBoostTailWidth = 0.5f;
        public const float CoreTailWidth = 0.1f;
        public const float CoreTailAlpha = 0.75f;
        public const float ShoulderTailWidth = 0.08f;
        public const float ShoulderTailSide = 0.35f;
        public const float ShoulderTailHeight = 0.3f;
        public const float TailGlow = 1.2f;
        // The white wind lines: every line at once as a booster starts, then one line about every WindLineEvery seconds,
        // each living WindLineTime, from WindLineAhead metres ahead of the runner, WindLineRadius round a centre
        // WindLineHeight up, reaching about WindLineReach metres back.
        public const float WindLineEvery = 0.045f;
        public const float WindLineTime = 0.28f;
        public const float WindLineAhead = 0.8f;
        public const float WindLineHeight = 0.4f;
        public const float WindLineRadius = 0.5f;
        public const float WindLineReach = 7f;
        // Thin (user 2026-11-23 "선이 굵고"): each line's width is 0.6 to 1.4 times this.
        public const float WindLineWidth = 0.07f;
        public const float WindLineAlpha = 0.85f;
        // Each wind line bends outward as it runs back (user 2026-11-22 "살짝의 디테일"), like air parted round the runner:
        // WindLineBend of its reach aside at its far end, growing with the square of the distance back.
        public const float WindLineBend = 0.05f;
        // Not mirrored (user 2026-11-23 "대칭이라"): each line starts within WindLineJitter degrees of its band round the
        // runner, fans WindLineFanMin to WindLineFanMax degrees off straight back and reaches WindLineReachMin to
        // WindLineReachMax of WindLineReach; the lines after the first burst start 0.5 to 1.5 WindLineEvery apart, and the
        // first burst's start up to WindLineBurstSpread seconds apart.
        public const float WindLineJitter = 12f;
        public const float WindLineFanMin = 12f;
        public const float WindLineFanMax = 32f;
        public const float WindLineReachMin = 0.6f;
        public const float WindLineReachMax = 1.25f;
        public const float WindLineBurstSpread = 0.08f;
        // The speed the HUD shows is km/h (user 2026-11-06); the simulation stays in m/s.
        public const float KmhPerMetrePerSecond = 3.6f;
        // The countdown's 3, 2, 1 and "GO!" (user 2026-11-06 "큰 글자가 축소되서"): each comes in CueStartScale times its
        // size and shrinks to it in CueShrinkTime, then goes by shrinking to nothing over its last CueExitTime. It stays
        // solid throughout: faded, its mint and the red track mixed to a muddy grey (user 2026-11-06).
        public const float CueStartScale = 2.6f;
        public const float CueShrinkTime = 0.3f;
        public const float CueExitTime = 0.1f;
        public static readonly Color SwingAmber = new Color(1f, 0.62f, 0.18f);
        public static readonly Color SwingBoostGold = new Color(1f, 0.86f, 0.32f);
        // Drafting (Motion.Slipstream) shows no gauge (user 2026-11-26): air parting over the screen grows out from the middle
        // as the charge does, stays full while the boost runs and closes after it. Its strength moves by DraftWindRise and
        // DraftWindFall a second, so a charge lost closes quickly rather than blinking out. Each streak runs from
        // DraftWindInner of the way to the screen's edge out to DraftWindReachMin (no charge) .. DraftWindReachMax (full) of
        // it in DraftWindCycle seconds, straight out from the middle (user 2026-11-26: no curves), as thick as DraftWindThickMin .. Max and as
        // strong as DraftWindAlphaMin .. Max.
        // The boost starts with a pulse along the screen's top and bottom, the booster's edges being the left and right.
        public const float DraftWindRise = 4f;
        public const float DraftWindFall = 5f;
        public const int DraftWindStreaks = 26;
        public const int DraftWindSegments = 7;
        public const float DraftWindCycle = 0.38f;
        public const float DraftWindInner = 0.2f;
        public const float DraftWindReachMin = 0.4f;
        public const float DraftWindReachMax = 1.15f;
        public const float DraftWindThickMin = 2.5f;
        public const float DraftWindThickMax = 7f;
        public const float DraftWindAlphaMin = 0.35f;
        public const float DraftWindAlphaMax = 0.8f;
        public const float DraftPulseTime = 0.35f;
        public const float DraftPulseAlpha = 0.35f;
        public const int DraftPulseLayers = 3;
        public const float DraftPulseLayerHeight = 28f / DraftPulseLayers;
        public static readonly Color DraftBlue = new Color(96f / 255f, 168f / 255f, 1f);
        public static readonly Color BoostZoneGreen = new Color(0.1f, 0.85f, 0.2f);

        // How open the parting air is at this charge (0 to 1) or under the boost, before it is smoothed: faint from the first
        // tick behind another, so the player knows at once.
        public static float DraftWindTarget(float charge, bool boosting) =>
            boosting ? 1f : charge > 0f ? Mathf.Lerp(0.12f, 0.85f, charge) : 0f;

        // One at the event, zero once its duration has passed or before it has ever happened.
        public static float Fade(float since, float duration)
        {
            RequireFinite(since, nameof(since));
            if (!(duration > 0f) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be finite and positive.");
            return since < 0f ? 0f : Mathf.Clamp01(1f - since / duration);
        }

        // A smooth 0..1 pulse starting at its smallest, repeating every half second.
        public static float Pulse(float time)
        {
            RequireFinite(time, nameof(time));
            return (1f - Mathf.Cos(time * (Mathf.PI * 2f / PolePulsePeriod))) * 0.5f;
        }

        // The entry flash settles into the regular pulse instead of snapping to it.
        public static float PoleScale(float since)
        {
            float pulse = Mathf.Lerp(PolePulseMin, PolePulseMax, Pulse(since));
            return Mathf.Lerp(pulse, PoleEnterScale, Fade(since, PoleEnterTime));
        }

        public static float BracketScale(float since)
            => Mathf.Lerp(1f, BracketStartScale, Fade(since, BracketTightenTime));

        // Three sideways shakes, fading to the bracket's original position.
        public static float ShakeOffset(float since)
            => Mathf.Sin(since * (Mathf.PI * 6f / MissShakeTime)) * MissShakeSize * Fade(since, MissShakeTime);

        public static float Kmh(float metresPerSecond) => metresPerSecond * KmhPerMetrePerSecond;

        // How strong the screen's mint edges are: 1 as a booster fires, easing to BoostEdgeHold while it lasts, and from
        // there to 0 over BoostEdgeFadeTime once it is over (sinceEnd, negative while it lasts or before any). A booster cut
        // inside its burst still lets the burst finish.
        public static float BoostEdge(float sinceBoost, bool boosting, float sinceEnd)
        {
            float burst = Fade(sinceBoost, BoostFlashTime);
            if (sinceBoost < 0f) return 0f;
            float hold = boosting ? BoostEdgeHold : BoostEdgeHold * Fade(sinceEnd, BoostEdgeFadeTime);
            return Mathf.Max(burst, hold);
        }

        // A cue's size against its resting size, life seconds after which it is gone: CueStartScale when it appears, slowing
        // into 1, and down to nothing as its time runs out.
        public static float CueScale(float since, float life)
        {
            RequireFinite(life, nameof(life));
            float left = Fade(since, CueShrinkTime);
            float size = 1f + (CueStartScale - 1f) * left * left * left;
            return size * Mathf.Clamp01((life - since) / CueExitTime);
        }

        // Whether a point in HUD coordinates (y down) lies on a screen this wide and high.
        public static bool IsOnScreen(Vector2 point, float width, float height)
            => point.x >= 0f && point.x <= width && point.y >= 0f && point.y <= height;

        // Where an off-screen target is pinned: where the line from the screen's center toward point leaves the screen
        // inset by EdgeInset, and that line's unit direction. A point at the center itself points right.
        public static Vector2 PinToEdge(Vector2 point, float width, float height, out Vector2 direction)
        {
            if (!(width > EdgeInset * 2f) || !(height > EdgeInset * 2f))
                throw new ArgumentOutOfRangeException(nameof(width), $"The screen ({width} x {height}) must be wider and higher than twice EdgeInset.");
            var center = new Vector2(width * 0.5f, height * 0.5f);
            Vector2 offset = point - center;
            direction = offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector2.right;
            float across = direction.x != 0f ? (center.x - EdgeInset) / Mathf.Abs(direction.x) : float.PositiveInfinity;
            float down = direction.y != 0f ? (center.y - EdgeInset) / Mathf.Abs(direction.y) : float.PositiveInfinity;
            return center + direction * Mathf.Min(across, down);
        }

        // How much of a boost pad's speed above running speed its carry still holds: 1 at the pad, 0 once it is gone.
        public static float PadShare(float carry, float runSpeed, float padSpeed)
        {
            RequireFinite(carry, nameof(carry));
            RequireFinite(runSpeed, nameof(runSpeed));
            RequireFinite(padSpeed, nameof(padSpeed));
            if (!(padSpeed > runSpeed)) throw new ArgumentOutOfRangeException(nameof(padSpeed), padSpeed, "The pad's speed must be above running speed.");
            return Mathf.Clamp01((carry - runSpeed) / (padSpeed - runSpeed));
        }

        private static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, value, "Time must be finite.");
        }
    }
}
