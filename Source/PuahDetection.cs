using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace WhileYoureUp;

partial class Mod
{
#region Pick Up And Haul
    const string PuahReworkedMarkerTypeName = "Dreadarm.PickUpAndHaul.Hauling.WorkGiver_BatchHaul";

    static readonly Type PuahType_CompHauledToInventory               = GenTypes.GetTypeInAnyAssembly("PickUpAndHaul.CompHauledToInventory"); // GenTypes has a cache
    static readonly Type PuahType_WorkGiver_HaulToInventory           = GenTypes.GetTypeInAnyAssembly("PickUpAndHaul.WorkGiver_HaulToInventory");
    static readonly Type PuahType_JobDriver_HaulToInventory           = GenTypes.GetTypeInAnyAssembly("PickUpAndHaul.JobDriver_HaulToInventory");
    static readonly Type PuahType_JobDriver_UnloadYourHauledInventory = GenTypes.GetTypeInAnyAssembly("PickUpAndHaul.JobDriver_UnloadYourHauledInventory");

    static readonly MethodInfo PuahMethod_WorkGiver_HaulToInventory_HasJobOnThing = AccessTools.DeclaredMethod(PuahType_WorkGiver_HaulToInventory, "HasJobOnThing");
    static readonly MethodInfo PuahMethod_WorkGiver_HaulToInventory_JobOnThing    = AccessTools.DeclaredMethod(PuahType_WorkGiver_HaulToInventory, "JobOnThing");

    static readonly MethodInfo PuahMethod_WorkGiver_HaulToInventory_TryFindBestBetterStoreCellFor =
        AccessTools.DeclaredMethod(PuahType_WorkGiver_HaulToInventory, "TryFindBestBetterStoreCellFor");

    // todo https://github.com/Mehni/PickUpAndHaul/commit/fd2dd37d48af136600b220b5d9c141957b377e8c
    static readonly MethodInfo PuahMethod_WorkGiver_HaulToInventory_AllocateThingAt =
        AccessTools.DeclaredMethod(PuahType_WorkGiver_HaulToInventory,    "AllocateThingAtStoreTarget")
        ?? AccessTools.DeclaredMethod(PuahType_WorkGiver_HaulToInventory, "AllocateThingAtCell");

    static readonly MethodInfo PuahMethod_JobDriver_UnloadYourHauledInventory_FirstUnloadableThing =
        AccessTools.DeclaredMethod(PuahType_JobDriver_UnloadYourHauledInventory, "FirstUnloadableThing");

    static readonly MethodInfo PuahMethod_JobDriver_UnloadYourHauledInventory_MakeNewToils =
        AccessTools.DeclaredMethod(PuahType_JobDriver_UnloadYourHauledInventory, "MakeNewToils");

    static readonly MethodInfo PuahMethod_CompHauledToInventory_GetHashSet = AccessTools.DeclaredMethod(PuahType_CompHauledToInventory, "GetHashSet");

    // todo support changing this to "skipTargets" in the future? https://github.com/Mehni/PickUpAndHaul/commit/d48fdfff9e3c9a072b160871676e813258dee584
    static readonly FieldInfo PuahField_WorkGiver_HaulToInventory_SkipCells = AccessTools.DeclaredField(PuahType_WorkGiver_HaulToInventory, "skipCells");

    static readonly FieldInfo PuahField_CompHauledToInventory_TakenToInventory = AccessTools.DeclaredField(PuahType_CompHauledToInventory, "takenToInventory");

    static readonly Type puahReworkedMarkerType = GenTypes.GetTypeInAnyAssembly(PuahReworkedMarkerTypeName);

    static readonly List<(string name, object member)> puahNeeds = new() {
        ("type PickUpAndHaul.CompHauledToInventory", PuahType_CompHauledToInventory),
        ("type PickUpAndHaul.WorkGiver_HaulToInventory", PuahType_WorkGiver_HaulToInventory),
        ("type PickUpAndHaul.JobDriver_HaulToInventory", PuahType_JobDriver_HaulToInventory),
        ("type PickUpAndHaul.JobDriver_UnloadYourHauledInventory", PuahType_JobDriver_UnloadYourHauledInventory),
        ("method PickUpAndHaul.WorkGiver_HaulToInventory.HasJobOnThing", PuahMethod_WorkGiver_HaulToInventory_HasJobOnThing),
        ("method PickUpAndHaul.WorkGiver_HaulToInventory.JobOnThing", PuahMethod_WorkGiver_HaulToInventory_JobOnThing),
        ("method PickUpAndHaul.WorkGiver_HaulToInventory.TryFindBestBetterStoreCellFor", PuahMethod_WorkGiver_HaulToInventory_TryFindBestBetterStoreCellFor),
        ("method PickUpAndHaul.WorkGiver_HaulToInventory.AllocateThingAtStoreTarget or AllocateThingAtCell", PuahMethod_WorkGiver_HaulToInventory_AllocateThingAt),
        ("method PickUpAndHaul.JobDriver_UnloadYourHauledInventory.FirstUnloadableThing", PuahMethod_JobDriver_UnloadYourHauledInventory_FirstUnloadableThing),
        ("method PickUpAndHaul.JobDriver_UnloadYourHauledInventory.MakeNewToils", PuahMethod_JobDriver_UnloadYourHauledInventory_MakeNewToils),
        ("method PickUpAndHaul.CompHauledToInventory.GetHashSet", PuahMethod_CompHauledToInventory_GetHashSet),
        ("field PickUpAndHaul.WorkGiver_HaulToInventory.skipCells", PuahField_WorkGiver_HaulToInventory_SkipCells),
        ("field PickUpAndHaul.CompHauledToInventory.takenToInventory", PuahField_CompHauledToInventory_TakenToInventory),
    };

    static readonly bool puahForkPresent = new[] {
        PuahType_CompHauledToInventory, PuahType_WorkGiver_HaulToInventory, PuahType_JobDriver_HaulToInventory, PuahType_JobDriver_UnloadYourHauledInventory,
    }.Any(t => t is not null);

    static readonly List<string> puahMissing = puahNeeds.Where(x => x.member is null).Select(x => x.name).ToList();

    static readonly bool havePuah = puahForkPresent && puahMissing.Count == 0;

    static readonly bool havePuahReworked = puahReworkedMarkerType is not null;

    static readonly MethodInfo PuahMethod_CompHauledToInventory_GetComp =
        havePuah ? AccessTools.DeclaredMethod(typeof(ThingWithComps), "GetComp").MakeGenericMethod(PuahType_CompHauledToInventory) : null;

    static string OwnerLabel(Assembly asm) {
        var pack = asm is null ? null : LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.assemblies.loadedAssemblies.Contains(asm));
        return pack is null ? $"assembly {asm?.GetName().Name}" : $"\"{pack.Name}\" (packageId {pack.PackageIdPlayerFacing})";
    }

    static string PuahForkOwner() =>
        OwnerLabel(puahNeeds.Select(x => x.member).OfType<Type>().Concat(puahNeeds.Select(x => (x.member as MemberInfo)?.DeclaringType)).FirstOrDefault(t => t is not null)?.Assembly);

    static string PuahReworkedOwner() => OwnerLabel(puahReworkedMarkerType.Assembly);

    static void ReportPuahStatus() {
        if (puahForkPresent && puahMissing.Count > 0)
            Log.Error($"[{mod.Content.Name}] Pick Up And Haul {PuahForkOwner()} lacks what this mod needs: {string.Join("; ", puahMissing)}. " +
                      "The Pick Up And Haul features of this mod are off; everything else works.");
        else if (havePuahReworked)
            Log.Message($"[{mod.Content.Name}] Pick Up And Haul {PuahReworkedOwner()} already hauls on the way, unloads closest first and carries materials closer itself, " +
                        "so this mod leaves its inventory hauling to it.");
    }
#endregion
}
