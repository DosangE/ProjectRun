using System.Collections;
using System.IO;
using NUnit.Framework;
using ProtoHarness.ChainRush;
using ProtoHarness.ChainRush.Audio;
using ProtoHarness.ChainRush.Bots;
using ProtoHarness.ChainRush.Control;
using ProtoHarness.ChainRush.Motion;
using ProtoHarness.ChainRush.Race;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProtoHarness.Tests.PlayMode
{
    // Drafting in a real field (Motion.Slipstream, RaceField.FindDrafts), and the catch-up that brings a runner into the
    // draft's range (Motion.Catchup, RaceField.FindCatchups): STADIUM's opening straight, four ALPHA runners on the Fast
    // class holding the throttle, stepped by the field's own tick.
    public sealed class ChainRushDraftTests
    {
        private RaceWorld[] worlds;
        private RaceField field;
        private float timeScale;
        private float volume;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            timeScale = Time.timeScale;
            volume = AudioListener.volume;
            AudioListener.volume = 0f;
            Time.timeScale = 0f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (worlds != null)
                foreach (RaceWorld world in worlds) if (world.Game != null) world.Game.SetInputSource(Idle());
            Time.timeScale = timeScale;
            AudioListener.volume = volume;
            yield return null;
            TestLogs.AssertNoUnexpected();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Draft_AFullChargeBehind_BoostsPastTheRunSpeedForOneAndAHalfSeconds_ThenCoolsDown()
        {
            yield return Load();
            // Only the draft's own speed is measured below, while the runner ahead runs away: nothing pulls this one along.
            field.CatchupEnabled = false;
            RacerState racer = worlds[1].Game.Racer;
            int cues = 0;
            worlds[1].Game.CuePlayed += cue => { if (cue == RaceCue.Draft) cues++; };
            LineUp(5f);
            for (int tick = 1; tick < Slipstream.ChargeTicks; tick++)
            {
                Step();
                Assert.That(racer.DraftChargeTicks, Is.EqualTo(tick), "Charging on every tick behind.");
                Hold(5f);
            }
            Assert.That(worlds[0].Game.Racer.DraftChargeTicks, Is.Zero, "The runner ahead drafts nobody.");
            Assert.That(cues, Is.Zero);
            Step();
            Assert.That(worlds[1].Player.IsDrafting, Is.True);
            Assert.That(cues, Is.EqualTo(1));
            float top = 0f;
            int ticks = 1;
            while (worlds[1].Player.IsDrafting && ticks < 200)
            {
                // Held on the flat, alone: only the boost's own speed is measured.
                Place(worlds[1], new Vector3(0f, 1.05f, 2f), worlds[1].Player.Velocity);
                Step();
                top = Mathf.Max(top, worlds[1].Player.ForwardSpeed);
                ticks++;
            }
            // The tick that fired counts one of the boost's ticks down too, and the last is gone on the tick it ends.
            Assert.That(ticks, Is.EqualTo(Slipstream.BoostTicks));
            Assert.That(racer.DraftCooldownTicks, Is.EqualTo(Slipstream.CooldownTicks), "The cooldown starts as the boost ends.");
            Assert.That(top, Is.EqualTo(worlds[1].Player.RunSpeed * 1.3f).Within(0.3f));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Draft_OneTickToTheSide_LosesTheTime()
        {
            yield return Load();
            RacerState racer = worlds[1].Game.Racer;
            LineUp(5f);
            for (int tick = 0; tick < 60; tick++) { Step(); Hold(5f); }
            Assert.That(racer.DraftChargeTicks, Is.EqualTo(60));
            Place(worlds[1], worlds[0].Player.transform.position + new Vector3(2f, 0f, -5f), worlds[0].Player.Velocity);
            Step();
            Assert.That(racer.DraftChargeTicks, Is.Zero);
            Hold(5f);
            Step();
            Assert.That(racer.DraftChargeTicks, Is.EqualTo(1));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Draft_BehindARespawningRunner_OrSwitchedOff_ChargesNothing()
        {
            yield return Load();
            RacerState racer = worlds[1].Game.Racer;
            LineUp(5f);
            field.DraftEnabled = false;
            Step();
            Assert.That(racer.DraftChargeTicks, Is.Zero, "Switched off.");
            field.DraftEnabled = true;
            Hold(5f);
            Step();
            Assert.That(racer.DraftChargeTicks, Is.EqualTo(1));
            Hold(5f);
            worlds[0].Game.Racer.RespawnHoldTicks = 10;
            Step();
            Assert.That(racer.DraftChargeTicks, Is.Zero, "A runner blinking out its respawn hold is a ghost.");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Draft_AFallWhileDrafting_ComesBackWithoutIt()
        {
            yield return Load();
            RacerState racer = worlds[1].Game.Racer;
            LineUp(5f);
            racer.DraftBoostTicks = 50;
            racer.DraftChargeTicks = 30;
            worlds[1].Player.SetPosition(new Vector3(0f, -20f, 6f));
            Step();
            Assert.That(worlds[1].Player.IsRespawning, Is.True, "precondition: it fell and came back.");
            Assert.That(racer.DraftBoostTicks, Is.Zero);
            Assert.That(racer.DraftChargeTicks, Is.Zero);
        }

        // The air parting over the screen grows with the charge, is full under the boost and closes once the charge is lost
        // (user 2026-11-26, in place of a gauge). Screenshots of each go to the temp folder.
        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_DraftWind_GrowsWithTheChargeAndClosesWithoutIt()
        {
            yield return Load();
            // Past the "GO!".
            for (int i = 0; i < 60; i++) Step();
            LineUp(6f);
            RacerState racer = worlds[0].Game.Racer;
            // The player's own HUD: the player is the one behind here.
            Place(worlds[1], new Vector3(0f, 1.05f, 14f), Vector3.forward * worlds[1].Player.RunSpeed);
            ChainRushHud hud = null;
            foreach (GameObject root in worlds[0].Scene.GetRootGameObjects())
                if (hud == null) hud = root.GetComponentInChildren<ChainRushHud>();
            Assert.That(hud, Is.Not.Null);
            // The field stands still (no ticks); only the frames run.
            Time.timeScale = 1f;
            float last = 0f;
            foreach (float share in new[] { 0.1f, 0.5f, 0.95f })
            {
                racer.DraftChargeTicks = Mathf.RoundToInt(Slipstream.ChargeTicks * share);
                yield return new WaitForSecondsRealtime(0.5f);
                Assert.That(hud.DraftWind, Is.GreaterThan(last), "share " + share);
                last = hud.DraftWind;
                Capture("charge" + Mathf.RoundToInt(share * 100f));
                yield return null;
            }
            racer.DraftChargeTicks = 0;
            racer.DraftBoostTicks = Slipstream.BoostTicks;
            yield return new WaitForSecondsRealtime(0.12f);
            Capture("boost");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(hud.DraftWind, Is.EqualTo(1f));
            racer.DraftBoostTicks = 0;
            racer.DraftCooldownTicks = 0;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(hud.DraftWind, Is.Zero);
        }

        // Catching up (Motion.Catchup) in the same field: a runner left far behind the leader is pulled along by its share of
        // the running speed, the leader by nothing, and switched off nobody is.
        [UnityTest, Timeout(120000)]
        public IEnumerator Catchup_FarBehindTheLeader_RunsFasterByItsShare_AndTheLeaderIsNotPulled()
        {
            yield return Load();
            // The leader runs off on its bot while the others are held at the start.
            IInputSource source = RaceBotTests.InputFor(worlds[0], BotDifficulty.Normal);
            worlds[0].Game.SetInputSource(source);
            ((RaceBot)source).Observe(field, 0);
            int ticks = 0;
            while (field.Progress(0) - field.Progress(1) < 100d && ticks < 1500)
            {
                for (int i = 1; i < worlds.Length; i++) Place(worlds[i], new Vector3((i - 2) * 3f, 1.05f, 2f), Vector3.zero);
                Step();
                Assert.That(field.CatchupShare(0), Is.Zero, "The leader is pulled by nothing, tick " + ticks);
                ticks++;
                if (ticks % 200 == 0) yield return null;
            }
            Assert.That(field.Progress(0) - field.Progress(1), Is.GreaterThanOrEqualTo(100d), "precondition: the leader got away.");
            Assert.That(field.CatchupShare(1), Is.EqualTo(Catchup.Share(field.Progress(0) - field.Progress(1), worlds[1].Player.SpeedClassScale)).Within(0.002f));
            // Let go on the flat at running speed, alone at the back: only the pull's own speed is measured.
            Place(worlds[1], new Vector3(-3f, 1.05f, 2f), Vector3.forward * worlds[1].Player.RunSpeed);
            float top = 0f;
            for (int t = 0; t < 60; t++)
            {
                for (int i = 2; i < worlds.Length; i++) Place(worlds[i], new Vector3((i - 2) * 3f, 1.05f, 2f), Vector3.zero);
                Step();
                top = Mathf.Max(top, worlds[1].Player.ForwardSpeed);
            }
            float share = field.CatchupShare(1);
            Assert.That(share, Is.GreaterThan(0.1f), "Over 100 m behind.");
            Assert.That(worlds[1].Player.IsDrafting, Is.False);
            Assert.That(top, Is.EqualTo(worlds[1].Player.RunSpeed * (1f + share)).Within(0.3f));
            field.CatchupEnabled = false;
            Step();
            Assert.That(field.CatchupShare(1), Is.Zero, "Switched off.");
        }

        private static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetTempPath(), "ChainRush-draft-" + name + ".png"));

        // A race of four bots, seen through: somebody drafts, and the same race twice is the same race.
        [UnityTest, Timeout(300000)]
        public IEnumerator Draft_ABotRaceTwice_IsTheSameRace()
        {
            const int ticks = 1500;
            var boosts = new int[2];
            var ends = new Vector3[2][];
            for (int run = 0; run < 2; run++)
            {
                yield return Load(BotDifficulty.Hard);
                int count = 0;
                foreach (RaceWorld world in worlds) world.Game.CuePlayed += cue => { if (cue == RaceCue.Draft) count++; };
                for (int i = 0; i < worlds.Length; i++)
                {
                    IInputSource source = RaceBotTests.InputFor(worlds[i], BotDifficulty.Hard);
                    worlds[i].Game.SetInputSource(source);
                    ((RaceBot)source).Observe(field, i);
                }
                for (int t = 0; t < ticks; t++)
                {
                    Step();
                    if (t % 200 == 0) yield return null;
                }
                boosts[run] = count;
                ends[run] = new Vector3[worlds.Length];
                for (int i = 0; i < worlds.Length; i++) ends[run][i] = worlds[i].Player.transform.position;
            }
            Assert.That(ends[1], Is.EqualTo(ends[0]));
            Assert.That(boosts[1], Is.EqualTo(boosts[0]));
            Debug.Log($"ChainRush draft: four Hard bots on STADIUM, {ticks} ticks, {boosts[0]} drafts, the same twice.");
        }

        private IEnumerator Load(BotDifficulty difficulty = BotDifficulty.Normal)
        {
            RaceWorld player = null;
            yield return RaceBotTests.LoadRace("Stadium", difficulty, true, 7, (w, f) => { player = w; field = f; });
            worlds = new[] { player, field.Opponents[0], field.Opponents[1], field.Opponents[2] };
            foreach (RaceWorld world in worlds) world.Game.SetInputSource(Idle());
            player.Game.StartRun();
            Step();
        }

        // Runner 0 ahead, runner 1 gap metres behind it in line at running speed; the other two stand out of the way.
        private void LineUp(float gap)
        {
            for (int i = 2; i < worlds.Length; i++) Place(worlds[i], new Vector3(i == 2 ? -4.5f : 4.5f, 1.05f, -8f), Vector3.zero);
            Place(worlds[0], new Vector3(0f, 1.05f, 8f), Vector3.forward * worlds[0].Player.RunSpeed);
            Hold(gap);
        }

        // Back to the start of the straight each tick, so the charge is measured on the flat for as long as it takes.
        private void Hold(float gap)
        {
            Place(worlds[0], new Vector3(0f, 1.05f, 8f), Vector3.forward * worlds[0].Player.RunSpeed);
            Place(worlds[1], new Vector3(0f, 1.05f, 8f - gap), Vector3.forward * worlds[1].Player.RunSpeed);
            for (int i = 2; i < worlds.Length; i++) worlds[i].Game.Racer.Velocity = Vector3.zero;
        }

        private static void Place(RaceWorld world, Vector3 position, Vector3 velocity)
        {
            world.Player.SetPosition(position);
            CharacterController capsule = world.Player.GetComponent<CharacterController>();
            capsule.Move(Vector3.down * 0.3f);
            world.Game.Racer.Grounded = capsule.isGrounded;
            Physics.SyncTransforms();
            world.Game.Racer.Velocity = velocity;
        }

        private void Step() => RaceBotTests.StepField(field);

        private static IInputSource Idle() => new InputReplay(new InputLog());
    }
}
