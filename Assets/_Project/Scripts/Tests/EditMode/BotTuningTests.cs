using System;
using System.Reflection;
using NUnit.Framework;
using ProtoHarness.ChainRush;
using ProtoHarness.ChainRush.Bots;
using ProtoHarness.ChainRush.Flow;
using ProtoHarness.ChainRush.Track;
using UnityEngine;

namespace ProtoHarness.Tests.EditMode
{
    public sealed class BotTuningTests
    {
        [Test]
        public void Constructor_EveryNumericParameterRejectsNonfiniteAndOutOfRangeValues()
        {
            ConstructorInfo constructor = typeof(BotTuning).GetConstructors()[0];
            ParameterInfo[] parameters = constructor.GetParameters();
            var arguments = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : 4.5d;
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -10000d, 10000d })
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type type = parameters[i].ParameterType;
                    if (type == typeof(bool) || (type == typeof(int) && double.IsNaN(bad))) continue;
                    object saved = arguments[i];
                    if (type == typeof(int)) arguments[i] = bad < 0 ? int.MinValue : int.MaxValue;
                    else if (type == typeof(float)) arguments[i] = (float)bad;
                    else arguments[i] = bad;
                    TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() => constructor.Invoke(arguments), parameters[i].Name);
                    Assert.That(failure.InnerException, Is.TypeOf<ArgumentException>(), parameters[i].Name);
                    arguments[i] = saved;
                }
            }
            Assert.DoesNotThrow(() => BotTuning.Normal.Validate());
            Assert.DoesNotThrow(() => BotTuning.Hard.Validate());
            Assert.Throws<ArgumentException>(() => default(BotTuning).Validate());
        }

        // The fixed all-off baseline the PlayMode input equality runs on (RaceBotTests.AllTechniquesOff_*). Normal and Hard
        // started here at round zero and the tuning loop has since turned techniques on (DECISIONS 2026-11-15 "MP2 ④"), so
        // the round-zero contract is CircuitEquivalent's (user 2026-11-16 "남은것 진행해라" on the proposal to move it).
        [Test]
        public void CircuitEquivalent_MatchesCircuitBotValuesAndDisablesEveryTechnique()
        {
            BotTuning tuning = BotTuning.CircuitEquivalent;
            Assert.That(tuning.GrappleGapJumpDistance, Is.EqualTo(4.5d));
            Assert.That(tuning.JumpGapJumpDistance, Is.EqualTo(0.8d));
            Assert.That(tuning.GrappleHeight, Is.EqualTo(2.6f));
            Assert.That(tuning.OffsetGain, Is.EqualTo(4f));
            Assert.That(tuning.HeadingClamp, Is.EqualTo(20f));
            Assert.That(tuning.HeadingDivisor, Is.EqualTo(20f));
            Assert.That(tuning.ShortcutSwing || tuning.CornerSwing || tuning.InsideLine || tuning.Booster || tuning.Slingshot
                || tuning.ContactResponse || tuning.HoldThroughCorner, Is.False);
        }

        [Test]
        public void Setup_RejectsDifficultyOutsideBotsAndPreservesItThroughCopies()
        {
            var track = ScriptableObject.CreateInstance<TrackDefinition>();
            var runner = ScriptableObject.CreateInstance<RunnerProfile>();
            try
            {
                Assert.Throws<ArgumentException>(() => new RaceSetup(track, "Test", "", RaceMode.Single, SpeedClass.Fast, runner, difficulty: BotDifficulty.Normal));
                Assert.Throws<ArgumentException>(() => new RaceSetup(track, "Test", "", RaceMode.Bots, SpeedClass.Fast, runner, difficulty: (BotDifficulty)99));
                RaceSetup setup = new RaceSetup(track, "Test", "", RaceMode.Bots, SpeedClass.Fast, runner, difficulty: BotDifficulty.Hard).WithOpponents(runner);
                Assert.That(setup.Difficulty, Is.EqualTo(BotDifficulty.Hard));
                Assert.That(setup.ForOpponent(0).Difficulty, Is.EqualTo(BotDifficulty.Hard));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(track);
                UnityEngine.Object.DestroyImmediate(runner);
            }
        }
    }
}
