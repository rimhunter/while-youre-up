using System.Runtime.InteropServices;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class SelfTest
    {
        [Scenario("selftest-pass", SelfTest = true, MapSize = 30)]
        static void Pass(ScenarioContext s) {
            s.Check(s.Map.Size.x == 30, "map should be 30 wide");
        }

        [Scenario("selftest-fail", SelfTest = true, MapSize = 30)]
        static void Fail(ScenarioContext s) {
            s.Fail("deliberate failure to prove the runner exits non-zero");
        }

        [Scenario("selftest-log-error", SelfTest = true, MapSize = 30)]
        static void LogError(ScenarioContext s) {
            Log.Error("deliberate error from selftest-log-error");
        }

        [Scenario("selftest-throws", SelfTest = true, MapSize = 30)]
        static void Throws(ScenarioContext s) {
            throw new System.InvalidOperationException("deliberate exception");
        }

        [Scenario("selftest-hold", SelfTest = true, MapSize = 30)]
        static void Hold(ScenarioContext s) {
            System.Threading.Thread.Sleep(120000);
        }

        [DllImport("libc.so.6")]
        static extern void abort();

        [Scenario("selftest-native-crash", SelfTest = true, MapSize = 30)]
        static void NativeCrash(ScenarioContext s) {
            abort();
        }

        [Scenario("selftest-timeout", SelfTest = true, MapSize = 30, MaxSeconds = 2)]
        static void Timeout(ScenarioContext s) {
            while (true) s.Tick(1);
        }
    }
}
