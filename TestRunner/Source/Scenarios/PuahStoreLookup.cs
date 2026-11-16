using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace WyuTestRunner.Scenarios
{
    static class PuahStoreLookup
    {
        static Job LookUp(ScenarioContext s, Pawn pawn, Thing thing) {
            var worker = (WorkGiver_Scanner) DefDatabase<WorkGiverDef>.GetNamed("HaulToInventory").Worker;
            if (!worker.HasJobOnThing(pawn, thing)) return null;
            return worker.JobOnThing(pawn, thing);
        }

        static string Describe(Job job) => job is null ? "no job" : $"{job.def.defName} B={job.targetB}";

        static void CheckStoredIn(ScenarioContext s, Job job, Zone_Stockpile zone, string what) {
            s.Check(job is not null && job.targetB.IsValid && zone.ContainsCell(job.targetB.Cell), what + "; the job was: " + Describe(job));
        }

        [Scenario("puah-store-lookup-in-the-same-tick-never-offers-removed-storage", MapSize = 80, Mods = "puah-continued")]
        static void NeverOffersRemovedStorage(ScenarioContext s) {
            var pawn   = s.SpawnColonist(s.Cell(-10, 0));
            var item   = s.SpawnItem("Steel", 15, s.Cell(-6, 2));
            var near   = s.Stockpile(CellRect.CenteredOn(s.Cell(6, 0), 1), "Steel");
            var far    = s.Stockpile(CellRect.CenteredOn(s.Cell(30, 0), 1), "Steel");
            var first  = LookUp(s, pawn, item);
            CheckStoredIn(s, first, near, "before anything changes the closer stockpile should be chosen");
            near.Delete();
            var second = LookUp(s, pawn, item);
            CheckStoredIn(s, second, far, "after the closer stockpile is removed in the same tick the other one should be chosen");
        }

        [Scenario("puah-store-lookup-in-the-same-tick-never-offers-storage-that-stopped-accepting", MapSize = 80, Mods = "puah-continued")]
        static void NeverOffersStorageThatStoppedAccepting(ScenarioContext s) {
            var pawn   = s.SpawnColonist(s.Cell(-10, 0));
            var item   = s.SpawnItem("Steel", 15, s.Cell(-6, 2));
            var near   = s.Stockpile(CellRect.CenteredOn(s.Cell(6, 0), 1), "Steel");
            var far    = s.Stockpile(CellRect.CenteredOn(s.Cell(30, 0), 1), "Steel");
            var first  = LookUp(s, pawn, item);
            CheckStoredIn(s, first, near, "before anything changes the closer stockpile should be chosen");
            near.settings.filter.SetAllow(ScenarioContext.Def("Steel"), false);
            var second = LookUp(s, pawn, item);
            CheckStoredIn(s, second, far, "after the closer stockpile stops accepting steel in the same tick the other one should be chosen");
        }
    }
}
