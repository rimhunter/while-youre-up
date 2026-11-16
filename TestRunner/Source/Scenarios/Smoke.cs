namespace WyuTestRunner.Scenarios
{
    static class Smoke
    {
        [Scenario("starts-with-no-errors", MapSize = 30)]
        static void StartsWithNoErrors(ScenarioContext s) {
            s.Tick(60);
            s.RequireNoPlayerLogErrors();
        }
    }
}
