using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class Puah
    {
        static readonly string[] ForkJobDefs = { "HaulToInventory", "Dreadarm_PickUpAndHaul_Batch" };

        static void RequireFork(ScenarioContext s) {
            if (!ForkJobDefs.Any(d => DefDatabase<JobDef>.GetNamedSilentFail(d) != null))
                throw new ScenarioFailure("no Pick Up And Haul fork is loaded; launch with --mod puah-continued or --mod puah-reworked");
        }

        static string Sequence(ScenarioContext s, Pawn pawn) => string.Join(", ", s.JobsOf(pawn).Select(j => j.ToString()));

        [Scenario("puah-grabs-extra-items-only-when-unloading-them-is-also-on-the-way", MapSize = 100, Mods = "puah-fork")]
        static void GrabsExtrasOnlyWhenUnloadingIsOnTheWay(ScenarioContext s) {
            RequireFork(s);
            var pawn      = Opportunity.SpawnWorker(s, s.Cell(-15, 0));
            var steel     = s.SpawnItem("Steel", 10, s.Cell(-8, 3));
            var plasteel  = s.SpawnItem("Plasteel", 10, s.Cell(-8, -3));
            var gold      = s.SpawnItem("Gold", 10, s.Cell(-8, 0));
            var goldCell  = s.CellOf(gold);
            var steelZone = s.Stockpile(CellRect.CenteredOn(s.Cell(10, 0), 1), "Steel");
            var plasZone  = s.Stockpile(CellRect.CenteredOn(s.Cell(20, 0), 1), "Plasteel");
            s.Stockpile(CellRect.CenteredOn(s.Cell(-10, -40), 1), "Gold");
            var stool = Opportunity.DesignateDeconstruct(s, s.Cell(32, 0));

            var done = s.RunUntil(() => stool.Destroyed, 5000);
            s.Check(done, "the pawn should still do the job (deconstruct the stool); jobs were: " + Sequence(s, pawn));
            s.Check(steel.Destroyed || s.StorageOf(steel) == steelZone.slotGroup, "the steel should be unloaded into the stockpile on the route; it is at " + s.CellOf(steel));
            s.Check(plasteel.Destroyed || s.StorageOf(plasteel) == plasZone.slotGroup, "the plasteel should be unloaded into the stockpile on the route; it is at " + s.CellOf(plasteel));
            s.Check(!gold.Destroyed && s.CellOf(gold) == goldCell && !s.IsCarriedBy(gold, pawn),
                "the gold should be left where it was because its storage is off the route; it is at " + s.CellOf(gold) + ", carried: " + s.IsCarriedBy(gold, pawn));
            var haul = Opportunity.IndexOfHaul(s, pawn);
            var job  = Opportunity.IndexOfJob(s, pawn, "Deconstruct");
            s.Check(haul >= 0 && job >= 0 && haul < job, "the pawn should haul before it deconstructs; jobs were: " + Sequence(s, pawn));
        }

        [Scenario("puah-unloads-once-per-storage-destination-closest-first", MapSize = 100, Mods = "puah-fork")]
        static void UnloadsOncePerDestinationClosestFirst(ScenarioContext s) {
            RequireFork(s);
            var pawn  = s.SpawnColonist(s.Cell(-20, 0));
            var zones = new Dictionary<string, Zone_Stockpile> {
                ["A"] = s.Stockpile(CellRect.CenteredOn(s.Cell(-8, 0), 1), "Steel", "Plasteel"),
                ["B"] = s.Stockpile(CellRect.CenteredOn(s.Cell(12, 0), 1), "Silver"),
                ["C"] = s.Stockpile(CellRect.CenteredOn(s.Cell(32, 0), 1), "Gold"),
            };
            var destination = new Dictionary<string, string> { ["Steel"] = "A", ["Plasteel"] = "A", ["Silver"] = "B", ["Gold"] = "C" };
            s.SpawnItem("Gold", 10, s.Cell(-18, 2));
            s.SpawnItem("Silver", 20, s.Cell(-17, -2));
            s.SpawnItem("Plasteel", 10, s.Cell(-18, -1));
            s.SpawnItem("Steel", 10, s.Cell(-17, 1));

            var arrivals = new List<(string def, int tick)>();
            bool AllArrived() {
                foreach (var kv in destination) {
                    if (arrivals.Any(a => a.def == kv.Key)) continue;
                    if (zones[kv.Value].slotGroup.HeldThings.Any(t => t.def.defName == kv.Key))
                        arrivals.Add((kv.Key, s.TicksRun));
                }
                return arrivals.Count == destination.Count;
            }
            var done = s.RunUntil(AllArrived, 8000);
            s.Check(done, "every item should reach its stockpile; arrived: " + string.Join(", ", arrivals.Select(a => a.def)) + "; jobs were: " + Sequence(s, pawn));
            if (!done) return;

            var visits = new List<string>();
            foreach (var a in arrivals.OrderBy(a => a.tick)) {
                var zone = destination[a.def];
                if (visits.Count == 0 || visits[visits.Count - 1] != zone) visits.Add(zone);
            }
            s.AreEqual("A,B,C", string.Join(",", visits), "storage destinations in the order they were unloaded at (once each, closest first); arrivals " + string.Join(", ", arrivals.OrderBy(a => a.tick).Select(a => a.def + "@" + a.tick)));
        }

        [Scenario("puah-closer-to-moves-supplies-between-equal-priority-storage-and-leaves-the-haul-list-alone", MapSize = 60, Mods = "puah-fork")]
        static void CloserToLeavesHaulListAlone(ScenarioContext s) {
            RequireFork(s);
            var pawn     = s.SpawnColonist(s.Cell(0, 0));
            var farZone  = s.Stockpile(CellRect.CenteredOn(s.Cell(-20, 0), 1), "Steel");
            var nearZone = s.Stockpile(CellRect.CenteredOn(s.Cell(9, 0), 1), "Steel");
            s.SpawnItem("Steel", 75, s.Cell(-20, 0));
            s.PlaceBlueprint("Wall", s.Cell(16, 0), "Steel");
            var steel = ScenarioContext.Def("Steel");

            var movedNearer = s.RunUntil(() => s.Map.listerThings.ThingsOfDef(steel).Any(t => s.StorageOf(t) == nearZone.slotGroup), 4000);
            s.Check(movedNearer, "steel should be hauled into the stockpile nearer the blueprint; jobs were: " + Sequence(s, pawn));

            var built = s.RunUntil(() => s.Map.listerThings.ThingsOfDef(ScenarioContext.Def("Wall")).Any(), 6000);
            s.Check(built, "the wall should be built; jobs were: " + Sequence(s, pawn));

            var stored = s.Map.listerHaulables.ThingsPotentiallyNeedingHauling().Where(t => s.IsStored(t)).Select(t => t.LabelCap + " at " + s.CellOf(t)).ToList();
            s.Check(stored.Count == 0, "things already in storage should not be on the haul list: " + string.Join(", ", stored));
        }

        const string StubPackageId = "rimhunter.test.puahstub";

        [Scenario("puah-fork-missing-a-member-logs-a-red-error-and-the-rest-still-works", MapSize = 80, Mods = "TestRunner/Fixtures/puah-stub")]
        static void ForkMissingAMember(ScenarioContext s) {
            if (!LoadedModManager.RunningModsListForReading.Any(m => m.PackageIdPlayerFacing == StubPackageId))
                throw new ScenarioFailure("the test stub is not loaded; launch with --mod TestRunner/Fixtures/puah-stub");

            var errors = s.PlayerLogErrorLines().Where(l => l.IndexOf("Pick Up And Haul", System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            s.AreEqual(1, errors.Count, "number of Pick Up And Haul errors in Player.log: " + string.Join(" || ", errors));
            var error = errors.FirstOrDefault() ?? "";
            s.Check(error.Contains(StubPackageId), "the error should name the fork's packageId " + StubPackageId + ": " + error);
            s.Check(error.Contains("WorkGiver_HaulToInventory.skipCells"), "the error should name the missing member skipCells: " + error);

            var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            Opportunity.RunToJobTarget(s, l);
            Opportunity.CheckHauledThenDidJob(s, l);
            s.Check(Opportunity.IndexOfJob(s, l.Pawn, "HaulToCell") >= 0, "without the Pick Up And Haul patches the pawn should use a plain HaulToCell; jobs were: " + Sequence(s, l.Pawn));
        }
    }
}
