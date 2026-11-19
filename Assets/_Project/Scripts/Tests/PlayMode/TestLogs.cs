using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProtoHarness.Tests.PlayMode
{
    // TearDown log check: LogAssert.NoUnexpectedReceived, except for one warning from the com.unity.ai.assistant
    // package (the MCP bridge). After a domain reload the package waits up to 30 s for its account API and, about
    // half the time, logs AccountApiWarning (ApiAccessibleState.WaitForCloudProjectSettings). The PlayMode run
    // reloads on entering Play, so whichever test is running 30 s later failed on it (docs/DECISIONS.md
    // 2026-11-18 "main v0.7", 1st gate run). Only that exact warning, logged during the current test, is
    // expected away; every other log is checked as before.
    internal static class TestLogs
    {
        internal const string AccountApiWarning =
            "Account API did not become accessible within 30 seconds. This may be due to network issues or editor focus.";

        private static string warnedTestId;
        private static int warnings;

        public static void AssertNoUnexpected()
        {
            Listen();
            if (warnings > 0 && warnedTestId == TestContext.CurrentContext.Test.ID)
                for (int i = 0; i < warnings; i++) LogAssert.Expect(LogType.Warning, AccountApiWarning);
            warnings = 0;
            warnedTestId = null;
            LogAssert.NoUnexpectedReceived();
        }

        // Play entry runs this after the domain reload and before the first test, so the warning is seen
        // even in a test that has not called AssertNoUnexpected yet.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Listen()
        {
            Application.logMessageReceived -= Record;
            Application.logMessageReceived += Record;
        }

        private static void Record(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Warning || message != AccountApiWarning) return;
            string id = TestContext.CurrentContext.Test.ID;
            if (id != warnedTestId) warnings = 0;
            warnedTestId = id;
            warnings++;
        }
    }
}
