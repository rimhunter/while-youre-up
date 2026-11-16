using System.Collections.Generic;

namespace PickUpAndHaul
{
    public class CompHauledToInventory
    {
        HashSet<object> takenToInventory = new HashSet<object>();
        public HashSet<object> GetHashSet() => takenToInventory;
    }

    public class WorkGiver_HaulToInventory
    {
        public bool HasJobOnThing(object pawn, object thing, bool forced) => false;
        public object JobOnThing(object pawn, object thing, bool forced) => null;
        public static bool TryFindBestBetterStoreCellFor(object thing, object carrier, object map, int priority, object faction, out object cell) { cell = null; return false; }
        public static bool AllocateThingAtCell(object storeCellCapacity, object pawn, object nextThing, object job) => false;
    }

    public class JobDriver_HaulToInventory
    {
    }

    public class JobDriver_UnloadYourHauledInventory
    {
        public System.Collections.Generic.IEnumerable<object> MakeNewToils() { yield break; }
        static object FirstUnloadableThing(object pawn, HashSet<object> carriedThings) => null;
    }
}
