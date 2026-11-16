using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class CloserTo
    {
        static int IndexOfJob(IReadOnlyList<RecordedJob> jobs, string def) {
            for (var i = 0; i < jobs.Count; i++)
                if (jobs[i].Def == def) return i;
            return -1;
        }

        static string Sequence(ScenarioContext s, Pawn pawn) => string.Join(", ", s.JobsOf(pawn).Select(j => j.ToString()));

        static void CheckHaulCameFirst(ScenarioContext s, Pawn pawn, string deliveryDef, string what) {
            var jobs     = s.JobsOf(pawn).ToList();
            var haul     = IndexOfJob(jobs, "HaulToCell");
            var delivery = IndexOfJob(jobs, deliveryDef);
            s.Check(haul >= 0, "the pawn should haul " + what + " to storage first; jobs were: " + Sequence(s, pawn));
            s.Check(delivery >= 0, "the pawn should then do " + deliveryDef + "; jobs were: " + Sequence(s, pawn));
            s.Check(haul >= 0 && delivery >= 0 && haul < delivery, "the haul should come before " + deliveryDef + "; jobs were: " + Sequence(s, pawn));
        }

        static void CheckCloserToText(ScenarioContext s, Pawn pawn) {
            var haul = s.JobsOf(pawn).FirstOrDefault(j => j.Def == "HaulToCell");
            s.Check(haul != null, "there should be a HaulToCell job to read the text of; jobs were: " + Sequence(s, pawn));
            if (haul == null) return;
            s.Check(haul.Reports.Any(r => r.Contains("closer to")), "the haul's job report should say \"closer to\", was: " + string.Join(" | ", haul.Reports));
        }

        static void Setup(ScenarioContext s, string itemDef, out Pawn pawn, out Zone_Stockpile farZone, out Zone_Stockpile nearZone) {
            pawn     = s.SpawnColonist(s.Cell(0, 0));
            farZone  = s.Stockpile(CellRect.CenteredOn(s.Cell(-20, 0), 1), itemDef);
            nearZone = s.Stockpile(CellRect.CenteredOn(s.Cell(9, 0), 1), itemDef);
        }

        [Scenario("closer-to-supplies-move-nearer-the-blueprint", MapSize = 60)]
        static void SuppliesMoveNearerTheBlueprint(ScenarioContext s) {
            Setup(s, "Steel", out var pawn, out var farZone, out var nearZone);
            s.SpawnItem("Steel", 75, s.Cell(-20, 0));
            s.PlaceBlueprint("Wall", s.Cell(16, 0), "Steel");

            var movedNearer = s.RunUntil(() => s.Map.listerThings.ThingsOfDef(ScenarioContext.Def("Steel")).Any(t => s.StorageOf(t) == nearZone.slotGroup), 4000);
            s.Check(movedNearer, "steel should be hauled into the stockpile nearer the blueprint; jobs were: " + Sequence(s, pawn));

            var built = s.RunUntil(() => s.Map.listerThings.ThingsOfDef(ScenarioContext.Def("Wall")).Any(), 6000);
            s.Check(built, "the wall should be built; jobs were: " + Sequence(s, pawn));

            CheckHaulCameFirst(s, pawn, "HaulToContainer", "steel");
            CheckCloserToText(s, pawn);
        }

        [Scenario("closer-to-ingredients-move-nearer-the-workbench", MapSize = 60)]
        static void IngredientsMoveNearerTheWorkbench(ScenarioContext s) {
            Setup(s, "Cloth", out var pawn, out var farZone, out var nearZone);
            s.SpawnItem("Cloth", 75, s.Cell(-20, 0));
            var bench = (Building_WorkTable)s.SpawnBuilding("HandTailoringBench", s.Cell(15, 0));
            s.AddBill(bench, "Make_Apparel_Pants", 1);

            var movedNearer = s.RunUntil(() => s.Map.listerThings.ThingsOfDef(ScenarioContext.Def("Cloth")).Any(t => s.StorageOf(t) == nearZone.slotGroup), 4000);
            s.Check(movedNearer, "cloth should be hauled into the stockpile nearer the workbench; jobs were: " + Sequence(s, pawn));

            var made = s.RunUntil(() => s.JobDefsOf(pawn).Contains("DoBill") && s.Map.listerThings.ThingsOfDef(ScenarioContext.Def("Apparel_Pants")).Any(), 15000);
            s.Check(made, "the pants should be made; jobs were: " + Sequence(s, pawn));

            CheckHaulCameFirst(s, pawn, "DoBill", "cloth");
            CheckCloserToText(s, pawn);
        }
    }
}
