using ProtoHarness.ChainRush.Track;
using UnityEngine;

namespace ProtoHarness.ChainRush.Motion
{
    // Drafting (user 2026-11-26, KartRider style): a runner that stays right behind another for ChargeTicks is sped up for
    // BoostTicks, on its own. Behind is judged along the road, so the space behind a runner bends with the corner; one tick
    // out of it and the time already spent there is gone. The boost adds to whatever speed the runner is heading for (a run,
    // a swing, a booster, a pad's carry) instead of competing with it ("속도 상한에 안걸리도록"). Its state is RacerState's
    // Draft*Ticks; RaceField says who is behind whom, so a runner racing alone never drafts.
    public static class Slipstream
    {
        // How far behind the other runner, in metres as the crow flies (user 2026-11-26: 12; 8 first, then 16), and how far to
        // the side of it across the road.
        public const float Range = 12f;
        public const float HalfWidth = 1.5f;
        // The height above the road may differ by a jump's top (11.5 m/s against 22 m/s a second: 3.0 m), no more.
        public const float HeightGap = 3f;
        // Nearer along the road than this the runners are side by side, not behind. Two bodies touching nose to tail are
        // 0.76 m apart.
        public const float MinRoadGap = 0.5f;
        // The road's S runs faster than the ground inside a corner and on a shortcut (its S covers the main line's length), so
        // S only says who is ahead and Range measures the distance. Past this S the other is on another part of the road.
        public const double MaxRoadGap = 36d;
        // Both must be going along the road at least this fast, so nobody drafts a runner held against a guard.
        public const float MinRoadSpeed = 8f;
        // The speed added while boosting, as a share of the running speed, and how fast it comes, in run accelerations.
        public const float SpeedShare = 0.3f;
        public const float AccelerationScale = 2f;
        // The turn radius grows with the running speed the boost adds to, so it turns no faster than the run does (the rule
        // a booster's turn follows, user 2026-11-04).
        public const float TurnRadiusScale = 1f + SpeedShare;
        // User 2026-11-26: 2 s (tried 3 s and 2.5 s, then back to 2 s).
        public static readonly int ChargeTicks = Ticks.FromSeconds(2f);
        public static readonly int BoostTicks = Ticks.FromSeconds(1.5f);
        // After a boost ends, however it ends, no charge for this long (user 2026-11-26 "가속 끝나고 나서 부터 5초").
        public static readonly int CooldownTicks = Ticks.FromSeconds(5f);

        // Where a runner is on the road. Road tells the main line from each shortcut (Centerline.BranchAt): a shortcut's S
        // runs beside the main line's, so S and D alone would put runners on different roads behind one another.
        public readonly struct Spot
        {
            public readonly Vector3 Position;
            public readonly TrackCoord Coord;
            public readonly int Road;
            // The speed along the road, negative against it.
            public readonly float RoadSpeed;

            public Spot(Vector3 position, TrackCoord coord, int road, float roadSpeed)
            {
                Position = position;
                Coord = coord;
                Road = road;
                RoadSpeed = roadSpeed;
            }
        }

        // Whether runner is right behind ahead. lapLength is the closed track's, 0 for an open course.
        public static bool IsBehind(in Spot runner, in Spot ahead, double lapLength)
        {
            if (runner.Road != ahead.Road) return false;
            if (runner.RoadSpeed < MinRoadSpeed || ahead.RoadSpeed < MinRoadSpeed) return false;
            double gap = ahead.Coord.S - runner.Coord.S;
            if (lapLength > 0d) gap -= System.Math.Floor(gap / lapLength + 0.5d) * lapLength;
            if (gap < MinRoadGap || gap > MaxRoadGap) return false;
            if (Mathf.Abs(ahead.Coord.D - runner.Coord.D) > HalfWidth) return false;
            if (Mathf.Abs(ahead.Coord.H - runner.Coord.H) > HeightGap) return false;
            float x = ahead.Position.x - runner.Position.x;
            float z = ahead.Position.z - runner.Position.z;
            return x * x + z * z <= Range * Range;
        }

        // One tick of drafting; true on the tick the boost starts. A boost running, and the cooldown after it, charge nothing.
        // Without the throttle a boost is cut (its cooldown starts) and a full charge waits, as a booster does not fire
        // either (Slingshot.TryFire), as long as the runner stays behind.
        public static bool Advance(RacerState racer, bool behind, bool coasting)
        {
            if (coasting && racer.DraftBoostTicks > 0) End(racer);
            if (!behind || racer.DraftBoostTicks > 0 || racer.DraftCooldownTicks > 0)
            {
                racer.DraftChargeTicks = 0;
                return false;
            }
            if (racer.DraftChargeTicks < ChargeTicks) racer.DraftChargeTicks++;
            if (racer.DraftChargeTicks < ChargeTicks || coasting) return false;
            racer.DraftChargeTicks = 0;
            racer.DraftBoostTicks = BoostTicks;
            return true;
        }

        // At the end of every tick: the boost counts down and, on its last tick, starts the cooldown; the cooldown counts
        // down after it.
        public static void CountDown(RacerState racer)
        {
            if (racer.DraftBoostTicks > 0)
            {
                if (--racer.DraftBoostTicks == 0) racer.DraftCooldownTicks = CooldownTicks;
            }
            else if (racer.DraftCooldownTicks > 0) racer.DraftCooldownTicks--;
        }

        private static void End(RacerState racer)
        {
            racer.DraftBoostTicks = 0;
            racer.DraftCooldownTicks = CooldownTicks;
        }

        // The speed a running boost adds to the one the runner heads for; zero without one.
        public static float SpeedBonus(RacerState racer, float runSpeed) => racer.DraftBoostTicks > 0 ? runSpeed * SpeedShare : 0f;
    }
}
