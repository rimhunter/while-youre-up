using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CodeOptimist;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
// ReSharper disable once RedundantUsingDirective
using Debug = System.Diagnostics.Debug;

namespace WhileYoureUp;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
partial class Mod
{
    // `HasJobOnThing()` is inherited by some work givers and overridden by the construction ones, which call `ResourceDeliverJobFor()` themselves
    [HarmonyPatch]
    static class WorkGiver_Scanner__HasJobOnThing_Patch
    {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.DeclaredMethod(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnThing)) ?? throw MissingMethod(typeof(WorkGiver_Scanner));
            foreach (var type in new[] { typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), typeof(WorkGiver_ConstructDeliverResourcesToFrames) })
                yield return AccessTools.DeclaredMethod(type, nameof(WorkGiver_Scanner.HasJobOnThing)) ?? throw MissingMethod(type);
        }

        static MissingMethodException MissingMethod(Type type) => new($"{type.FullName}.HasJobOnThing was not found; the Closer-to supplies patch cannot be applied.");

        [HarmonyPostfix]
        // clear this so our code that ran for `HasJobOnThing()` can re-run for `JobOnThing()`
        static void ClearTempDetour(Pawn pawn) {
            detours.GetValueSafe(pawn)?.Deactivate();
            BaseDetour.ClearLoopCatch(pawn);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    static class WorkGiver_ConstructDeliverResources__ResourceDeliverJobFor_Patch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> BeforeSupplyDetour(IEnumerable<CodeInstruction> codes, ILGenerator generator, MethodBase __originalMethod) {
            var findNearby = AccessTools.DeclaredMethod(typeof(WorkGiver_ConstructDeliverResources), "FindAvailableNearbyResources");
            if (findNearby is null)
                throw new MissingMethodException("WorkGiver_ConstructDeliverResources.FindAvailableNearbyResources was not found; the Closer-to supplies patch cannot be applied.");

            var parameters = __originalMethod.GetParameters();
            int ArgIndex(string name) {
                var index = System.Array.FindIndex(parameters, x => x.Name == name);
                if (index < 0)
                    throw new MissingMemberException($"{__originalMethod.NameWithType()} has no parameter '{name}'; the Closer-to supplies patch cannot be applied.");
                return index + 1;
            }

            var jobVar     = generator.DeclareLocal(typeof(Job));
            var afterLabel = generator.DefineLabel();
            var leaveLabel = generator.DefineLabel();

            var matcher = new CodeMatcher(codes, generator)
                .MatchEndForward(new CodeMatch(code => code.Calls(findNearby)))
                .ThrowIfInvalid($"{__originalMethod.NameWithType()}: no call to FindAvailableNearbyResources was found; the Closer-to supplies patch cannot be applied.")
                .Advance(1);
            matcher.Instruction.labels.Add(afterLabel);
            matcher.Insert(
                CodeInstruction.LoadArgument(ArgIndex("pawn")),
                CodeInstruction.LoadArgument(ArgIndex("c")),
                CodeInstruction.LoadArgument(ArgIndex("forced")),
                new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(WorkGiver_ConstructDeliverResources__ResourceDeliverJobFor_Patch), nameof(BeforeSupplyDetour_Job))),
                new CodeInstruction(OpCodes.Stloc, jobVar),
                new CodeInstruction(OpCodes.Ldloc, jobVar),
                new CodeInstruction(OpCodes.Brfalse, afterLabel),
                new CodeInstruction(OpCodes.Leave, leaveLabel));

            matcher.End();
            if (matcher.Opcode != OpCodes.Ret)
                throw new InvalidProgramException($"{__originalMethod.NameWithType()} does not end with 'ret'; the Closer-to supplies patch cannot be applied.");
            matcher.InsertAfter(
                new CodeInstruction(OpCodes.Ldloc, jobVar).WithLabels(leaveLabel),
                new CodeInstruction(OpCodes.Ret));

            return matcher.InstructionEnumeration();
        }

        static Job BeforeSupplyDetour_Job(Pawn pawn, IConstructible c, bool forced) {
            if (!settings.Enabled || !settings.HaulBeforeCarry_Supplies || AlreadyHauling(pawn)) return null;
            if (pawn.WorkTagIsDisabled(WorkTags.ManualDumb | WorkTags.Hauling | WorkTags.AllWork)) return null; // like vanilla `TryOpportunisticJob()`

            var foundRes      = WorkGiver_ConstructDeliverResources.resourcesAvailable[0];
            var mostThing     = WorkGiver_ConstructDeliverResources.resourcesAvailable.MaxBy(x => x.stackCount);
            var constructible = (Thing)c;
            if (!havePuah || !settings.UsePickUpAndHaulPlus) { // too difficult to know in advance if there are no extras for PUAH
                var needed = forced || c is not IHaulEnroute enroute ? c.ThingCountNeeded(foundRes.def) : enroute.GetSpaceRemainingWithEnroute(foundRes.def, pawn);
                if (mostThing.stackCount <= needed)
                    return null; // there are no extras
            }

            var puahOrHtcJob = BeforeCarryDetour_Job(pawn, constructible.Position, mostThing); // :BeforeSupplyDetour
            return BaseDetour.CatchLoop_Job(pawn, puahOrHtcJob);
        }
    }

    // :BeforeBillDetour :BeforeSupplyDetour
    static Job BeforeCarryDetour_Job(Pawn pawn, LocalTargetInfo carryTarget, Thing thing) {
        if (thing.ParentHolder is Pawn_InventoryTracker) return null;

        // try to avoid haul-before-carry when there are no extras to grab
        // proper way is to recheck after grabbing everything, but here's a simple hack to at least avoid it with stone chunks
        if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 2)) return null; // already going for 1, so 2 to check for another

        if (!TryFindBestBetterStoreCellFor_MidwayToTarget(
                thing, LocalTargetInfo.Invalid, carryTarget, pawn, pawn.Map, StoreUtility.CurrentStoragePriorityOf(thing), pawn.Faction, out var storeCell, true)) return null;

        var fromHereSquared  = thing.Position.DistanceToSquared(carryTarget.Cell);
        var fromStoreSquared = storeCell.DistanceToSquared(carryTarget.Cell);
        // Debug.WriteLine($"Carry from here: {thing.Position.DistanceTo(carryTarget.Cell)}; carry from store: {storeCell.DistanceTo(carryTarget.Cell)}");

        if (fromStoreSquared < fromHereSquared) {
            // Debug.WriteLine(
            //     $"'{pawn}' prefixed job with haul for '{thing.Label}'"
            //     + $" because '{storeCell.GetSlotGroup(pawn.Map)}' is closer to original destination '{carryTarget} {carryTarget.Cell}'.");

            if (DebugViewSettings.drawOpportunisticJobs) {
                for (var _ = 0; _ < 3; _++) { // for bolder lines
                    var duration = 600;
                    pawn.Map.debugDrawer.FlashCell(thing.Position,   0.62f, pawn.Name.ToStringShort, duration); // cyan
                    pawn.Map.debugDrawer.FlashCell(storeCell,        0.22f, pawn.Name.ToStringShort, duration); // orange
                    pawn.Map.debugDrawer.FlashCell(carryTarget.Cell, 0.0f,  pawn.Name.ToStringShort, duration); // red

                    // magenta: shorter old; cyan: longer new
                    pawn.Map.debugDrawer.FlashLine(thing.Position, carryTarget.Cell, duration, SimpleColor.Magenta);
                    pawn.Map.debugDrawer.FlashLine(thing.Position, storeCell,        duration, SimpleColor.Cyan);
                    pawn.Map.debugDrawer.FlashLine(storeCell,      carryTarget.Cell, duration, SimpleColor.Cyan);
                }
            }

            if (havePuah && settings.UsePickUpAndHaulPlus) {
                var detour = SetOrAddDetour(pawn, DetourType.PuahBeforeCarry, storeCell: storeCell, carryTarget: carryTarget);
                detour.TrackPuahThing(thing, storeCell);
                var puahJob = PuahJob(pawn, thing);
                if (puahJob is not null) return puahJob;
            }

            SetOrAddDetour(pawn, DetourType.HtcBeforeCarry, carryTarget: carryTarget);
            var htcJob = HaulAIUtility.HaulToCellStorageJob(pawn, thing, storeCell, false);
            return htcJob;
        }

        return null;
    }
}
