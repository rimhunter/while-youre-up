using System.Reflection;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class DeepStorage
    {
        const string Pallet = "ASF_Pallet";

        static void RequireDeepStorage() {
            if (DefDatabase<ThingDef>.GetNamedSilentFail(Pallet) == null)
                throw new ScenarioFailure("LWM's Adaptive Deep Storage is not loaded; launch with --mod 2023507013 --mod 3033901359 --mod 3373064575");
        }

        static object OpportunityFilter() {
            var settings = ModSettingsAccess.Settings;
            return settings.GetType().GetField("opportunityDefaultBuildingFilter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(settings);
        }

        static bool Allows(object filter, ThingDef def) => (bool) filter.GetType().GetMethod("Allows", new[] { typeof(ThingDef) }).Invoke(filter, new object[] { def });

        static void SetAllow(object filter, ThingDef def, bool allow) => filter.GetType().GetMethod("SetAllow", new[] { typeof(ThingDef), typeof(bool) }).Invoke(filter, new object[] { def, allow });

        static void Run(ScenarioContext s, bool allowDeepStorage) {
            RequireDeepStorage();
            var def    = ScenarioContext.Def(Pallet);
            var filter = OpportunityFilter();
            var before = Allows(filter, def);
            if (allowDeepStorage) SetAllow(filter, def, true);
            try {
                var pawn   = Opportunity.SpawnWorker(s, s.Cell(-15, 0));
                var item   = s.SpawnItem("Steel", 15, s.Cell(-5, 3));
                var pallet = (Building_Storage) s.SpawnBuilding(Pallet, s.Cell(10, 0));
                pallet.settings.filter.SetAllowAll(null);
                var zone   = s.Stockpile(CellRect.CenteredOn(s.Cell(18, 0), 1), "Steel");
                var job    = Opportunity.DesignateDeconstruct(s, s.Cell(25, 0));
                var l = new Opportunity.OnRouteLayout { Pawn = pawn, Item = item, JobTarget = job, Zone = zone };

                Opportunity.RunToJobTarget(s, l);
                s.Check(Opportunity.IndexOfJob(s, pawn, "HaulToCell") >= 0, "the pawn should haul the steel on the way; jobs were: " + Opportunity.Sequence(s, pawn));
                var inPallet = s.StorageOf(item) == pallet.GetSlotGroup();
                var inZone   = s.StorageOf(item) == zone.slotGroup;
                if (allowDeepStorage)
                    s.Check(inPallet, "with Deep Storage allowed in the storage-building filter, the steel should go to the pallet, which is closest to halfway; it is at " + s.CellOf(item));
                else {
                    s.Check(!inPallet, "with the default settings, the steel should never go to Deep Storage, whose storing is slow");
                    s.Check(inZone, "the steel should go to the ordinary stockpile on the route; it is at " + s.CellOf(item));
                }
            } finally {
                SetAllow(filter, def, before);
            }
        }

        [Scenario("deep-storage-is-never-chosen-for-an-opportunity-by-default", MapSize = 80, Mods = "2023507013 3033901359 3373064575")]
        static void NeverByDefault(ScenarioContext s) => Run(s, false);

        [Scenario("deep-storage-allowed-in-the-building-filter-is-chosen", MapSize = 80, Mods = "2023507013 3033901359 3373064575")]
        static void AllowedIsChosen(ScenarioContext s) => Run(s, true);
    }
}
