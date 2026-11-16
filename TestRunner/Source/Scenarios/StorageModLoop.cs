using System;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class StorageModLoop
    {
        const string DeepLaunch = "--mod 2023507013 --mod 3033901359 --mod 3373064575";
        const string NeatLaunch = "--mod 2023507013 --mod 3033901359 --mod 3416243474";

        static void Require(string def, string name, string launch) {
            if (DefDatabase<ThingDef>.GetNamedSilentFail(def) == null)
                throw new ScenarioFailure(name + " is not loaded; launch with " + launch);
        }

        static void AllowForOpportunity(string def) {
            var settings = ModSettingsAccess.Settings;
            var filter   = settings.GetType().GetField("opportunityDefaultBuildingFilter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(settings);
            filter.GetType().GetMethod("SetAllow", new[] { typeof(ThingDef), typeof(bool) }).Invoke(filter, new object[] { ScenarioContext.Def(def), true });
        }

        static Building_Storage Store(ScenarioContext s, string def, int x) {
            var b = (Building_Storage) s.SpawnBuilding(def, s.Cell(x, 0));
            b.settings.filter.SetAllowAll(null);
            return b;
        }

        static void CheckNoLoop(ScenarioContext s, string what) {
            foreach (var p in s.Colonists) {
                var jobs = s.JobsOf(p).ToList();
                var run  = 1;
                for (var i = 1; i < jobs.Count; i++) {
                    run = jobs[i].Def == jobs[i - 1].Def && jobs[i].TargetA == jobs[i - 1].TargetA ? run + 1 : 1;
                    if (run >= 4) {
                        s.Fail(what + ": " + p.LabelShort + " repeated " + jobs[i].Def + " " + jobs[i].TargetA + " " + run + " times in a row");
                        return;
                    }
                }
            }
        }

        static void Many(ScenarioContext s, string def, int pawns) {
            AllowForOpportunity(def);
            Store(s, def, 8);
            for (var i = 0; i < pawns; i++) {
                Opportunity.SpawnWorker(s, s.Cell(-15, i * 2 - pawns));
                s.SpawnItem("Steel", 15, s.Cell(-6, i * 2 - pawns + 1));
                Opportunity.DesignateDeconstruct(s, s.Cell(25, i * 2 - pawns));
            }
            s.RunUntil(() => !s.Map.designationManager.AllDesignations.Any(), 8000);
            s.Check(!s.Map.designationManager.AllDesignations.Any(), "every pawn should finish its Deconstruct job");
            s.Tick(1000);
            CheckNoLoop(s, pawns + " pawns hauling to one " + def);
        }

        static void LongRun(ScenarioContext s, string def) {
            AllowForOpportunity(def);
            Store(s, def, 8);
            Opportunity.SpawnWorker(s, s.Cell(-15, 0));
            Opportunity.SpawnWorker(s, s.Cell(-15, 3));
            var types = new[] { "Steel", "Silver", "Gold", "Cloth" };
            for (var i = 0; i < types.Length; i++) s.SpawnItem(types[i], 40, s.Cell(-6, i * 2));
            for (var i = 0; i < 4; i++) Opportunity.DesignateDeconstruct(s, s.Cell(25, i * 2 - 3));
            s.RunUntil(() => false, 6000);
            CheckNoLoop(s, "6000 ticks with " + def);
        }

        [Scenario("storage-mod-deep-storage-five-pawns-haul-to-one-pallet-without-a-loop", MapSize = 100, MaxSeconds = 150, Mods = "2023507013 3033901359 3373064575")]
        static void DeepMany(ScenarioContext s) {
            Require("ASF_Pallet", "LWM's Adaptive Deep Storage", DeepLaunch);
            Many(s, "ASF_Pallet", 5);
        }

        [Scenario("storage-mod-deep-storage-exactly-full-cells-get-no-haul-and-the-job-is-done", MapSize = 80, Mods = "2023507013 3033901359 3373064575")]
        static void DeepFull(ScenarioContext s) {
            Require("ASF_Pallet", "LWM's Adaptive Deep Storage", DeepLaunch);
            AllowForOpportunity("ASF_Pallet");
            var pawn   = Opportunity.SpawnWorker(s, s.Cell(-15, 0));
            var pallet = Store(s, "ASF_Pallet", 8);
            var others = new[] { "Silver", "Gold", "Plasteel", "Uranium" };
            var cap    = pallet.def.building.maxItemsInCell;
            s.AreEqual(others.Length, cap, "the pallet's items per cell, which the four filler stacks must fill");
            foreach (var c in pallet.AllSlotCells())
                foreach (var o in others) s.SpawnItem(o, 5, c);
            var item = s.SpawnItem("Steel", 15, s.Cell(-5, 3));
            var job  = Opportunity.DesignateDeconstruct(s, s.Cell(25, 0));
            Opportunity.RunToJobTarget(s, new Opportunity.OnRouteLayout { Pawn = pawn, Item = item, JobTarget = job }, 6000);
            s.Tick(600);
            s.Check(Opportunity.IndexOfJob(s, pawn, "HaulToCell") < 0 || s.StorageOf(item) != pallet.GetSlotGroup(), "no steel should be stored in the full pallet; jobs were: " + Opportunity.Sequence(s, pawn));
            CheckNoLoop(s, "exactly full cells");
        }

        [Scenario("storage-mod-deep-storage-two-pawns-four-item-types-run-6000-ticks-without-a-loop", MapSize = 80, MaxSeconds = 150, Mods = "2023507013 3033901359 3373064575")]
        static void DeepLong(ScenarioContext s) {
            Require("ASF_Pallet", "LWM's Adaptive Deep Storage", DeepLaunch);
            LongRun(s, "ASF_Pallet");
        }

        [Scenario("storage-mod-neat-storage-five-pawns-haul-to-one-pallet-without-a-loop", MapSize = 100, MaxSeconds = 150, Mods = "2023507013 3033901359 3416243474")]
        static void NeatMany(ScenarioContext s) {
            Require("sbz_Pallet", "[sbz] Neat Storage", NeatLaunch);
            Many(s, "sbz_Pallet", 5);
        }

        [Scenario("storage-mod-neat-storage-run-6000-ticks-without-a-loop", MapSize = 80, MaxSeconds = 150, Mods = "2023507013 3033901359 3416243474")]
        static void NeatLong(ScenarioContext s) {
            Require("sbz_Pallet", "[sbz] Neat Storage", NeatLaunch);
            LongRun(s, "sbz_Pallet");
        }

        [Scenario("storage-mod-deep-storage-is-chosen-for-a-closer-to-haul-by-default", MapSize = 60, MaxSeconds = 120, Mods = "2023507013 3033901359 3373064575")]
        static void DeepCloserTo(ScenarioContext s) {
            Require("ASF_Pallet", "LWM's Adaptive Deep Storage", DeepLaunch);
            var pawn = s.SpawnColonist(s.Cell(0, 0));
            s.Stockpile(CellRect.CenteredOn(s.Cell(-20, 0), 1), "Steel");
            s.SpawnItem("Steel", 75, s.Cell(-20, 0));
            var pallet = Store(s, "ASF_Pallet", 9);
            s.PlaceBlueprint("Wall", s.Cell(16, 0), "Steel");
            var delivered = s.RunUntil(() => s.JobDefsOf(pawn).Contains("HaulToContainer"), 4000);
            s.Check(delivered, "the pawn should deliver steel to the blueprint; jobs were: " + Opportunity.Sequence(s, pawn));
            s.Check(Opportunity.IndexOfJob(s, pawn, "HaulToCell") >= 0 && Opportunity.IndexOfJob(s, pawn, "HaulToCell") < Opportunity.IndexOfJob(s, pawn, "HaulToContainer"),
                "steel should be hauled into the pallet nearer the blueprint before the delivery, with default settings; jobs were: " + Opportunity.Sequence(s, pawn));
            var haul = s.JobsOf(pawn).FirstOrDefault(j => j.Def == "HaulToCell");
            s.Check(haul != null && haul.Reports.Any(r => r.Contains("closer to")), "the haul's report should say \"closer to\"");
        }
    }
}
