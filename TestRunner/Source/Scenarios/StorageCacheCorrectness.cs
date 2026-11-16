using System.Linq;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class StorageCacheCorrectness
    {
        static Opportunity.OnRouteLayout Layout(ScenarioContext s, out Zone_Stockpile second) {
            var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            second = s.Stockpile(CellRect.CenteredOn(s.Cell(16, 0), 1), "Steel");
            return l;
        }

        static void CheckNeverHauledToRemoved(ScenarioContext s, Opportunity.OnRouteLayout l, Zone_Stockpile removed) {
            Opportunity.RunToJobTarget(s, l);
            s.Check(s.StorageOf(l.Item) != removed.slotGroup, "the steel must not be stored in the removed stockpile");
            s.Check(!s.Map.zoneManager.AllZones.Contains(removed), "the removed stockpile should be gone from the map");
        }

        [Scenario("storage-cache-offers-the-other-storage-when-the-closer-one-is-removed", MapSize = 80)]
        static void OffersOtherStorageWhenCloserIsRemoved(ScenarioContext s) {
            var l = Layout(s, out var other);
            l.Zone.Delete();
            CheckNeverHauledToRemoved(s, l, l.Zone);
            Opportunity.CheckHauledThenDidJob(s, new Opportunity.OnRouteLayout { Pawn = l.Pawn, Item = l.Item, JobTarget = l.JobTarget, Zone = other });
        }

        [Scenario("storage-cache-offers-the-other-storage-when-the-closer-one-stops-accepting-the-item", MapSize = 80)]
        static void OffersOtherStorageWhenCloserStopsAccepting(ScenarioContext s) {
            var l = Layout(s, out var other);
            l.Zone.settings.filter.SetAllow(ScenarioContext.Def("Steel"), false);
            Opportunity.RunToJobTarget(s, l);
            s.Check(s.StorageOf(l.Item) != l.Zone.slotGroup, "the steel must not be stored in the stockpile that no longer accepts steel");
            Opportunity.CheckHauledThenDidJob(s, new Opportunity.OnRouteLayout { Pawn = l.Pawn, Item = l.Item, JobTarget = l.JobTarget, Zone = other });
        }

        [Scenario("storage-cache-does-not-haul-to-removed-storage", MapSize = 80)]
        static void DoesNotHaulToRemovedStorage(ScenarioContext s) {
            var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            l.Zone.Delete();
            CheckNeverHauledToRemoved(s, l, l.Zone);
            s.Check(Opportunity.IndexOfHaul(s, l.Pawn) < 0, "no stockpile is left, so nothing should be hauled; jobs were: " + Opportunity.Sequence(s, l.Pawn));
            s.Check(s.StorageOf(l.Item) == null, "the steel should stay where it was");
        }

        [Scenario("storage-cache-does-not-haul-to-storage-that-no-longer-accepts-the-item", MapSize = 80)]
        static void DoesNotHaulToStorageThatStoppedAccepting(ScenarioContext s) {
            var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            l.Zone.settings.filter.SetAllow(ScenarioContext.Def("Steel"), false);
            Opportunity.RunToJobTarget(s, l);
            s.Check(Opportunity.IndexOfHaul(s, l.Pawn) < 0, "the only stockpile refuses steel, so nothing should be hauled; jobs were: " + Opportunity.Sequence(s, l.Pawn));
            s.Check(s.StorageOf(l.Item) == null, "the steel should stay where it was");
        }

        [Scenario("puah-storage-cache-offers-the-other-storage-when-the-closer-one-is-removed", MapSize = 80, Mods = "puah-fork")]
        static void PuahOffersOtherStorageWhenCloserIsRemoved(ScenarioContext s) => OffersOtherStorageWhenCloserIsRemoved(s);

        [Scenario("puah-storage-cache-offers-the-other-storage-when-the-closer-one-stops-accepting-the-item", MapSize = 80, Mods = "puah-fork")]
        static void PuahOffersOtherStorageWhenCloserStopsAccepting(ScenarioContext s) => OffersOtherStorageWhenCloserStopsAccepting(s);

        [Scenario("puah-storage-cache-does-not-haul-to-removed-storage", MapSize = 80, Mods = "puah-fork")]
        static void PuahDoesNotHaulToRemovedStorage(ScenarioContext s) => DoesNotHaulToRemovedStorage(s);

        [Scenario("puah-storage-cache-does-not-haul-to-storage-that-no-longer-accepts-the-item", MapSize = 80, Mods = "puah-fork")]
        static void PuahDoesNotHaulToStorageThatStoppedAccepting(ScenarioContext s) => DoesNotHaulToStorageThatStoppedAccepting(s);
    }
}
