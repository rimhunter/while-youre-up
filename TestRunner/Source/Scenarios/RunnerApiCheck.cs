using System.Linq;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class RunnerApiCheck
    {
        [Scenario("runner-api-vanilla-haul", MapSize = 40, SelfTest = true)]
        static void VanillaHaul(ScenarioContext s) {
            var pawn  = s.SpawnColonist(s.Cell(-10, 0));
            var steel = s.SpawnItem("Steel", 40, s.Cell(-6, 4));
            var zone  = s.Stockpile(CellRect.CenteredOn(s.Cell(8, 0), 2), "Steel");

            var done = s.RunUntil(() => s.IsStored(steel) && s.StorageOf(steel) == zone.slotGroup, 2500);

            s.Check(done, "the pawn should haul the steel into the stockpile");
            s.Check(s.JobDefsOf(pawn).Contains("HaulToCell"), "job sequence should contain HaulToCell, was: " + string.Join(", ", s.JobDefsOf(pawn)));
            s.Check(s.JobsOf(pawn).Any(j => j.Reports.Any(r => r.Contains("Steel") || r.Contains("steel"))), "a job report should mention the steel, was: " + string.Join(" | ", s.JobsOf(pawn).SelectMany(j => j.Reports)));
        }
    }
}
