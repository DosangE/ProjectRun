using System;

namespace ProtoHarness.ChainRush.Motion
{
    // Catching up (user 2026-11-27, "어느정도는 따라잡을 수 있도록 … 드래프트와 견인으로 추월기회를 주는것"): in a race, a runner
    // behind the leader heads for a faster speed the further behind it is, so of two runners of the same skill the one behind
    // is pulled into the draft's range (Slipstream.Range), where the draft gives it the chance to pass. The pull weakens as
    // the gap closes ("가까워질수록 그 견인 수준은 떨어져야") and is gone right behind, where the draft takes over. Its share is
    // of the running speed; RaceField works it out from the standings at the start of each tick, the same for the player
    // and the bots, and nothing of it is kept in RacerState. A runner racing alone is never pulled.
    public static class Catchup
    {
        // No pull this near the leader, in metres along the race: well inside the draft's range.
        public const float NoneWithin = 8f;
        // The share at the draft's range, so a runner just outside it still closes in (a flat estimate, 2026-11-27: from
        // 60 m behind an equal runner it reaches the range in about 36 s; with nothing there it would stop at the range).
        public const float ShareAtDraftRange = 0.05f;
        // The most there is, from this far behind on speed class 1. First 150 m and 15% (DECISIONS 2026-11-26 "봇 드래프트
        // 추월"); user 2026-11-27 raised the limits and kept the rise per metre ("한도를 늘리자. 현재 있는 비율에 대해선
        // 괜찮은듯"): (25% - 5%) / (288 m - 12 m) is (15% - 5%) / (150 m - 12 m). A faster class covers more road in the
        // same time, so the far end stretches with it; the near end is the draft's.
        public const float FullGap = 288f;
        public const float MaxShare = 0.25f;

        // The share of its running speed a runner gap metres behind the leader is pulled along by: 0 within NoneWithin,
        // rising to ShareAtDraftRange at the draft's range, then in a straight line to MaxShare at FullGap x speedClassScale.
        // It never grows as the gap closes.
        public static float Share(double gap, float speedClassScale)
        {
            if (double.IsNaN(gap) || double.IsInfinity(gap)) throw new ArgumentOutOfRangeException(nameof(gap), gap, "Catchup: the gap must be finite.");
            if (!(speedClassScale > 0f) || float.IsInfinity(speedClassScale))
                throw new ArgumentOutOfRangeException(nameof(speedClassScale), speedClassScale, "Catchup: the speed class scale must be above zero and finite.");
            double full = (double)FullGap * speedClassScale;
            if (full <= Slipstream.Range)
                throw new ArgumentOutOfRangeException(nameof(speedClassScale), speedClassScale, $"Catchup: the full pull ({full} m) must come beyond the draft's range ({Slipstream.Range} m).");
            if (gap <= NoneWithin) return 0f;
            if (gap <= Slipstream.Range) return (float)(ShareAtDraftRange * (gap - NoneWithin) / (Slipstream.Range - NoneWithin));
            if (gap >= full) return MaxShare;
            return (float)(ShareAtDraftRange + (MaxShare - ShareAtDraftRange) * (gap - Slipstream.Range) / (full - Slipstream.Range));
        }
    }
}
