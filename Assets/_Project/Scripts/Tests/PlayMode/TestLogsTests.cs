using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProtoHarness.Tests.PlayMode
{
    public sealed class TestLogsTests
    {
        private const string OtherWarning = "TestLogsTests: some other warning";

        [Test]
        public void AssertNoUnexpected_AfterTheAccountApiWarning_Passes()
        {
            Debug.LogWarning(TestLogs.AccountApiWarning);
            TestLogs.AssertNoUnexpected();
        }

        // The package logs from an editor update callback, not from inside the test; a player-loop callback
        // is the nearest runtime stand-in for that call stack.
        [UnityTest]
        public IEnumerator AssertNoUnexpected_AfterTheAccountApiWarningFromAPlayerLoopCallback_Passes()
        {
            SynchronizationContext context = SynchronizationContext.Current;
            Assert.That(context, Is.Not.Null, "The player loop needs a SynchronizationContext to post the warning.");
            bool logged = false;
            context.Post(_ => { Debug.LogWarning(TestLogs.AccountApiWarning); logged = true; }, null);
            for (int frame = 0; frame < 10 && !logged; frame++) yield return null;
            Assert.That(logged, Is.True, "The posted warning never ran.");
            TestLogs.AssertNoUnexpected();
        }

        [Test]
        public void AssertNoUnexpected_AfterAnotherWarning_StillFails()
        {
            Debug.LogWarning(OtherWarning);
            Assert.That(TestLogs.AssertNoUnexpected, Throws.InstanceOf<ResultStateException>().With.Message.Contains(OtherWarning));
        }

        [Test]
        public void AssertNoUnexpected_AfterTheAccountApiWarningAndAnother_StillFailsOnTheOther()
        {
            Debug.LogWarning(TestLogs.AccountApiWarning);
            Debug.LogWarning(OtherWarning);
            Assert.That(TestLogs.AssertNoUnexpected, Throws.InstanceOf<ResultStateException>().With.Message.Contains(OtherWarning));
        }
    }
}
