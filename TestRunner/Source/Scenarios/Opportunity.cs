using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class Opportunity
    {
        internal static string Sequence(ScenarioContext s, Pawn pawn) => string.Join(", ", s.JobsOf(pawn).Select(j => j.ToString()));

        internal static readonly string[] HaulDefs = { "HaulToCell", "HaulToInventory", "UnloadYourHauledInventory", "Dreadarm_PickUpAndHaul_Batch", "Dreadarm_PickUpAndHaul_Unload" };

        internal static int IndexOfHaul(ScenarioContext s, Pawn pawn) {
            var jobs = s.JobsOf(pawn).ToList();
            for (var i = 0; i < jobs.Count; i++)
                if (HaulDefs.Contains(jobs[i].Def)) return i;
            return -1;
        }

        internal static int IndexOfJob(ScenarioContext s, Pawn pawn, string def) {
            var jobs = s.JobsOf(pawn).ToList();
            for (var i = 0; i < jobs.Count; i++)
                if (jobs[i].Def == def) return i;
            return -1;
        }

        internal static Pawn SpawnWorker(ScenarioContext s, IntVec3 cell) {
            var pawn = s.SpawnColonist(cell);
            foreach (var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                if ((w.workTags & WorkTags.Hauling) != 0 && !pawn.WorkTypeIsDisabled(w))
                    pawn.workSettings.SetPriority(w, 0);
            return pawn;
        }

        internal static Building DesignateDeconstruct(ScenarioContext s, IntVec3 cell) {
            var stool = s.SpawnBuilding("Stool", cell);
            s.Map.designationManager.AddDesignation(new Designation(stool, DesignationDefOf.Deconstruct));
            return stool;
        }

        internal sealed class OnRouteLayout
        {
            public Pawn Pawn;
            public Thing Item;
            public Building JobTarget;
            public Zone_Stockpile Zone;
        }

        internal static OnRouteLayout SetupOnRoute(ScenarioContext s, int pawnX, int itemX, int itemZ, int zoneX, int jobX) {
            var layout = new OnRouteLayout();
            layout.Pawn      = SpawnWorker(s, s.Cell(pawnX, 0));
            layout.Item      = s.SpawnItem("Steel", 15, s.Cell(itemX, itemZ));
            layout.Zone      = s.Stockpile(CellRect.CenteredOn(s.Cell(zoneX, 0), 1), "Steel");
            layout.JobTarget = DesignateDeconstruct(s, s.Cell(jobX, 0));
            return layout;
        }

        internal static void RunToJobTarget(ScenarioContext s, OnRouteLayout l, int maxTicks = 4000) {
            var deconstructed = s.RunUntil(() => l.JobTarget.Destroyed, maxTicks);
            s.Check(deconstructed, "the pawn should still do the job (deconstruct the stool); jobs were: " + Sequence(s, l.Pawn));
        }

        internal static void CheckHauledThenDidJob(ScenarioContext s, OnRouteLayout l) {
            var haul = IndexOfHaul(s, l.Pawn);
            var job  = IndexOfJob(s, l.Pawn, "Deconstruct");
            s.Check(haul >= 0, "the pawn should haul the steel on the way; jobs were: " + Sequence(s, l.Pawn));
            s.Check(job >= 0, "the pawn should do the Deconstruct job; jobs were: " + Sequence(s, l.Pawn));
            s.Check(haul >= 0 && job >= 0 && haul < job, "the haul should come before the job; jobs were: " + Sequence(s, l.Pawn));
            s.Check(l.Item.Destroyed || s.StorageOf(l.Item) == l.Zone.slotGroup, "the steel should be in the stockpile on the route; it is at " + s.CellOf(l.Item));
        }

        [Scenario("opportunity-hauls-an-item-on-the-route-to-storage-on-the-route", MapSize = 80)]
        static void HaulsAnItemOnTheRoute(ScenarioContext s) {
            var l = SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            RunToJobTarget(s, l);
            CheckHauledThenDidJob(s, l);
        }

        [Scenario("opportunity-hauls-an-item-far-from-the-start-but-on-the-route", MapSize = 100)]
        static void HaulsAnItemFarFromTheStart(ScenarioContext s) {
            var l = SetupOnRoute(s, pawnX: -45, itemX: -5, itemZ: 2, zoneX: 20, jobX: 45);
            var startToItem = s.CellOf(l.Pawn).DistanceTo(s.CellOf(l.Item));
            s.Check(startToItem > 30f, "the item should start farther than the first range limit of 30 from the pawn, was " + startToItem);
            RunToJobTarget(s, l, 6000);
            CheckHauledThenDidJob(s, l);
        }

        [Scenario("opportunity-picks-storage-partway-to-the-job-over-storage-off-the-route", MapSize = 100)]
        static void PicksStoragePartwayToTheJob(ScenarioContext s) {
            var pawn      = SpawnWorker(s, s.Cell(-10, 0));
            var item      = s.SpawnItem("Steel", 15, s.Cell(0, 0));
            var offRoute  = s.Stockpile(CellRect.CenteredOn(s.Cell(0, -6), 1), "Steel");
            var partway   = s.Stockpile(CellRect.CenteredOn(s.Cell(20, 0), 1), "Steel");
            var jobTarget = DesignateDeconstruct(s, s.Cell(40, 0));
            var l = new OnRouteLayout { Pawn = pawn, Item = item, Zone = partway, JobTarget = jobTarget };

            RunToJobTarget(s, l);
            CheckHauledThenDidJob(s, l);
            s.Check(s.StorageOf(item) != offRoute.slotGroup, "the steel should not go to the storage off the route");
        }

        [Scenario("opportunity-is-skipped-while-the-pawn-is-bleeding", MapSize = 80)]
        static void SkippedWhileBleeding(ScenarioContext s) {
            var l = SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, l.Pawn, l.Pawn.RaceProps.body.corePart);
            cut.Severity = 2f;
            l.Pawn.health.AddHediff(cut);
            s.Check(l.Pawn.health.hediffSet.BleedRateTotal > 0.001f, "the pawn should be bleeding to begin with");

            RunToJobTarget(s, l);
            s.Check(l.Pawn.health.hediffSet.BleedRateTotal > 0.001f, "the pawn should still be bleeding at the end");
            s.Check(IndexOfJob(s, l.Pawn, "HaulToCell") < 0, "a bleeding pawn should not haul on the way; jobs were: " + Sequence(s, l.Pawn));
            s.Check(IndexOfJob(s, l.Pawn, "Deconstruct") >= 0, "the pawn should go straight to the job; jobs were: " + Sequence(s, l.Pawn));
            s.AreEqual(s.StorageOf(l.Item) == null, true, "the steel should stay where it was, not be stored");
        }

        [Scenario("opportunity-is-skipped-while-the-pawn-is-forming-a-caravan", MapSize = 80)]
        static void SkippedWhileFormingACaravan(ScenarioContext s) {
            var pawn   = SpawnWorker(s, s.Cell(-15, 0));
            var item   = s.SpawnItem("Steel", 15, s.Cell(-5, 3));
            s.Stockpile(CellRect.CenteredOn(s.Cell(8, 0), 1), "Steel");
            var muffalo = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named("Muffalo"), Faction.OfPlayer, forceGenerateNewPawn: true));
            GenSpawn.Spawn(muffalo, s.Cell(25, 0), s.Map);

            var transferable = new TransferableOneWay();
            transferable.things.Add(muffalo);
            transferable.AdjustTo(1);
            var destination = Enumerable.Range(0, Find.WorldGrid.TilesCount).Select(i => new PlanetTile(i, s.Map.Tile.Layer)).First(t => t != s.Map.Tile && !Find.WorldGrid[t].PrimaryBiome.impassable);
            CaravanFormingUtility.StartFormingCaravan(new List<Pawn> { pawn }, new List<Pawn>(), Faction.OfPlayer, new List<TransferableOneWay> { transferable },
                s.Cell(30, 0), s.Cell(35, 0), s.Map.Tile, destination);

            var gathering = s.RunUntil(() => s.JobDefsOf(pawn).Any(d => d == "Goto" || d.StartsWith("PrepareCaravan_")), 1500);
            s.Check(gathering, "the pawn should head for the caravan meeting point; jobs were: " + Sequence(s, pawn));
            s.Tick(300);
            s.Check(IndexOfJob(s, pawn, "HaulToCell") < 0, "a pawn forming a caravan should not haul on the way; jobs were: " + Sequence(s, pawn));
            s.Check(s.StorageOf(item) == null, "the steel should stay where it was");
        }

        [Scenario("opportunity-job-report-says-on-the-way-to", MapSize = 80)]
        static void JobReportSaysOnTheWayTo(ScenarioContext s) {
            var l = SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            RunToJobTarget(s, l);
            var haul = s.JobsOf(l.Pawn).FirstOrDefault(j => j.Def == "HaulToCell");
            s.Check(haul != null, "there should be a HaulToCell job to read the text of; jobs were: " + Sequence(s, l.Pawn));
            if (haul == null) return;
            s.Check(haul.Reports.Any(r => r.Contains("on the way to")), "the haul's job report should say \"on the way to\", was: " + string.Join(" | ", haul.Reports));
        }

        [Scenario("opportunity-without-pick-up-and-haul-logs-nothing-about-it", MapSize = 80)]
        static void LogsNothingAboutPuah(ScenarioContext s) {
            var l = SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            RunToJobTarget(s, l);
            CheckHauledThenDidJob(s, l);
            System.Threading.Thread.Sleep(1600);
            using var fs     = new System.IO.FileStream(s.PlayerLogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
            using var reader = new System.IO.StreamReader(fs);
            string line;
            while ((line = reader.ReadLine()) != null)
                if (line.IndexOf("puah", System.StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("pick up and haul", System.StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("pickupandhaul", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    s.Fail("Player.log mentions Pick Up And Haul: " + line);
        }
    }
}
