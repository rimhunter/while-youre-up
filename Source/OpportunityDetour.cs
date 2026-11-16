using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CodeOptimist;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
// ReSharper disable once RedundantUsingDirective
using Debug = System.Diagnostics.Debug;

namespace WhileYoureUp;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
partial class Mod
{
    static readonly Dictionary<Thing, IntVec3> opportunityStoreCellCache = new(64);

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryOpportunisticJob))]
    static class Pawn_JobTracker__TryOpportunisticJob_Patch
    {
        static bool IsEnabled() => settings.Enabled;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> _TryOpportunisticJob(IEnumerable<CodeInstruction> codes, ILGenerator generator, MethodBase __originalMethod) {
            var parameters = __originalMethod.GetParameters();
            var jobIndex   = Array.FindIndex(parameters, x => x.Name == "job");
            if (jobIndex < 0)
                throw new MissingMemberException($"{__originalMethod.NameWithType()} has no parameter 'job'; the Opportunity patch cannot be applied.");

            var pawnField = AccessTools.DeclaredField(typeof(Pawn_JobTracker), "pawn")
                            ?? throw new MissingFieldException("Pawn_JobTracker.pawn was not found; the Opportunity patch cannot be applied.");
            var getMap = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Map))
                         ?? throw new MissingMethodException("Thing.Map getter was not found; the Opportunity patch cannot be applied.");
            var listerField = AccessTools.DeclaredField(typeof(Map), nameof(Map.listerHaulables))
                              ?? throw new MissingFieldException("Map.listerHaulables was not found; the Opportunity patch cannot be applied.");

            var skipMod = generator.DefineLabel();
            var matcher = new CodeMatcher(codes, generator)
                .MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, pawnField),
                    new CodeMatch(OpCodes.Callvirt, getMap),
                    new CodeMatch(OpCodes.Ldfld, listerField))
                .ThrowIfInvalid($"{__originalMethod.NameWithType()}: no 'pawn.Map.listerHaulables' load was found; the Opportunity patch cannot be applied.");

            var openTryBlocks = 0;
            var allCodes = matcher.Instructions();
            for (var i = 0; i < matcher.Pos; i++)
                foreach (var block in allCodes[i].blocks) {
                    if (block.blockType == ExceptionBlockType.BeginExceptionBlock) openTryBlocks++;
                    else if (block.blockType == ExceptionBlockType.EndExceptionBlock) openTryBlocks--;
                }
            if (openTryBlocks > 0)
                throw new InvalidProgramException($"{__originalMethod.NameWithType()}: 'pawn.Map.listerHaulables' is inside an exception block, where the Opportunity patch cannot return; the patch cannot be applied.");

            var anchor        = matcher.Instruction;
            var anchorLabels  = anchor.labels.ToList();
            anchor.labels.Clear();
            anchor.labels.Add(skipMod);

            matcher.Insert(
                new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(Pawn_JobTracker__TryOpportunisticJob_Patch), nameof(IsEnabled))).WithLabels(anchorLabels),
                new CodeInstruction(OpCodes.Brfalse, skipMod),
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.LoadArgument(jobIndex + 1),
                new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(Pawn_JobTracker__TryOpportunisticJob_Patch), nameof(TryOpportunisticJob))),
                new CodeInstruction(OpCodes.Ret));

            return matcher.InstructionEnumeration();
        }

        static JobDef[] prepareCaravanJobDefs;

        // vanilla checks for `job.def.allowOpportunisticPrefix` and lots of other things before this
        // our `settings.Enabled` check is done prior to this in the transpiler
        static Job TryOpportunisticJob(Pawn_JobTracker jobTracker, Job job) {
            // Debug.WriteLine($"Opportunity checking {job}");
            var pawn = Traverse.Create(jobTracker).Field("pawn").GetValue<Pawn>(); // `Traverse` is cached
            if (AlreadyHauling(pawn)) return null;

            Job puahOrHtcJob;

            // we had to transpile to implement :BeforeSupplyDetour, but lucky for us we can implement
            //  :BeforeBillDetour right here from `TryOpportunisticJob()` that we're already modifying
            if (job.def == JobDefOf.DoBill && settings.HaulBeforeCarry_Bills) {
                // Debug.WriteLine($"Bill: '{job.bill}' label: '{job.bill.Label}'");
                // Debug.WriteLine($"Recipe: '{job.bill.recipe}' workerClass: '{job.bill.recipe.workerClass}'");
                for (var i = 0; i < job.targetQueueB.Count; i++) {
                    var ingredient = job.targetQueueB[i];
                    if (ingredient.Thing is null) continue;

                    if (!havePuah || !settings.UsePickUpAndHaulPlus) { // too difficult to know in advance if there are no extras for PUAH
                        if (ingredient.Thing.stackCount <= job.countQueue[i])
                            continue; // there are no extras
                    }

                    if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, ingredient.Thing, false)) continue; // fast check

                    // Permitted when bleeding because facilitates whatever bill is important enough to do while bleeding;
                    //  may save precious time going back for ingredients, unless we want only 1 medicine ASAP. It's a trade-off. :Bleeding
                    
                    puahOrHtcJob = BeforeCarryDetour_Job(pawn, job.targetA, ingredient.Thing); // :BeforeBillDetour
                    if (puahOrHtcJob is not null)
                        return BaseDetour.CatchLoop_Job(pawn, puahOrHtcJob);
                }
            }

            prepareCaravanJobDefs ??= new[] {
                JobDefOf.PrepareCaravan_CollectAnimals, JobDefOf.PrepareCaravan_GatherAnimals,
                JobDefOf.PrepareCaravan_GatherDownedPawns, JobDefOf.PrepareCaravan_GatherItems,
            };
            if (prepareCaravanJobDefs.Contains(job.def)) return null;
            if (pawn.GetLord()?.LordJob is LordJob_FormAndSendCaravan) return null;
            if (AmBleeding(pawn)) return null; // :Bleeding

            var detour = detours.GetValueSafe(pawn);
            // We'll check for a repeat opportunity within 5 ticks.
            // I expected repeats to be exactly 1 tick later, but it's sometimes 2, I'm not sure why.
            //  A PUAH job re-triggering maybe? :RepeatOpportunity
            if (detour?.opportunity.puah.unloadedTick > 0 && RealTime.frameCount - detour.opportunity.puah.unloadedTick <= 5) return null;

            // use first ingredient location if bill because our pawn will go directly to it
            var jobTarget = job.def == JobDefOf.DoBill ? job.targetQueueB?.FirstOrDefault() ?? job.targetA : job.targetA;
            puahOrHtcJob = Opportunity_Job(pawn, jobTarget);
            return BaseDetour.CatchLoop_Job(pawn, puahOrHtcJob);
        }
    }

    enum CanHaulResult { RangeFail, HardFail, FullStop, Success }

    struct MaxRanges
    {
        [TweakValue("WhileYoureUp.Opportunity", 1.1f, 3f)]
        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        public static float heuristicRangeExpandFactor = 2f;

        public int   expandCount;
        public float startToThing, startToThingPctOrigTrip;
        public float storeToJob,   storeToJobPctOrigTrip;

        public void Reset() {
            expandCount             = 0;
            startToThing            = settings.Opportunity_MaxStartToThing;
            startToThingPctOrigTrip = settings.Opportunity_MaxStartToThingPctOrigTrip;
            storeToJob              = settings.Opportunity_MaxStoreToJob;
            storeToJobPctOrigTrip   = settings.Opportunity_MaxStoreToJobPctOrigTrip;
        }

        public static MaxRanges operator *(MaxRanges maxRanges, float multiplier) {
            maxRanges.expandCount             += 1;
            maxRanges.startToThing            *= multiplier;
            maxRanges.startToThingPctOrigTrip *= multiplier;
            maxRanges.storeToJob              *= multiplier;
            maxRanges.storeToJobPctOrigTrip   *= multiplier;
            return maxRanges;
        }
    }

    static          MaxRanges   maxRanges;

    struct Haulable
    {
        public Thing thing;
        public int   level;
    }

    static readonly List<Haulable> haulables = new(32);

    const int MaxRangeLevels = 64;

    static float StartToThingLimitSquared(int pawnToJobSquared, in MaxRanges ranges) =>
        Math.Min(ranges.startToThing.Squared(), pawnToJobSquared * ranges.startToThingPctOrigTrip.Squared());

    static bool StartToThingInRange(int startToThingSquared, int pawnToJobSquared, in MaxRanges ranges) =>
        startToThingSquared <= StartToThingLimitSquared(pawnToJobSquared, ranges);

    static readonly List<float> rangeThresholds = new(16);

    static bool ExceedsMaxTotalTrip(IntVec3 start, IntVec3 thingPosition, IntVec3 jobCell) =>
        start.DistanceTo(thingPosition) + thingPosition.DistanceTo(jobCell) > start.DistanceTo(jobCell) * settings.Opportunity_MaxTotalTripPctOrigTrip;

    // Sets each haulable's first expansion level that passes the start-to-thing range checks, so earlier passes skip it.
    // Drops, in order, a haulable past the maximum total trip: it is that HardFail at any level.
    static void ComputeRangeLevels(Pawn pawn, LocalTargetInfo jobTarget) {
        var start            = pawn.Position;
        var pawnToJobSquared = start.DistanceToSquared(jobTarget.Cell);
        var farthestSquared  = 0;
        for (var i = 0; i < haulables.Count; i++)
            farthestSquared = Math.Max(farthestSquared, start.DistanceToSquared(haulables[i].thing.Position));

        rangeThresholds.Clear();
        var ranges = maxRanges;
        while (true) {
            var threshold = StartToThingLimitSquared(pawnToJobSquared, ranges);
            rangeThresholds.Add(threshold);
            if (farthestSquared <= threshold) break;
            if (rangeThresholds.Count > MaxRangeLevels)
                throw new InvalidOperationException(
                    $"The farthest haulable ({farthestSquared} squared) never comes within the Opportunity range limits after {MaxRangeLevels} expansions "
                    + $"(start range {maxRanges.startToThing}, percent of trip {maxRanges.startToThingPctOrigTrip}, expand factor {MaxRanges.heuristicRangeExpandFactor}).");
            ranges *= MaxRanges.heuristicRangeExpandFactor;
        }

        var write = 0;
        for (var read = 0; read < haulables.Count; read++) {
            var entry = haulables[read];
            if (ExceedsMaxTotalTrip(start, entry.thing.Position, jobTarget.Cell)) continue;

            var startToThingSquared = start.DistanceToSquared(entry.thing.Position);
            int low = 0, high = rangeThresholds.Count - 1;
            while (low < high) {
                var mid = (low + high) / 2;
                if (startToThingSquared <= rangeThresholds[mid]) high = mid;
                else low = mid + 1;
            }
            entry.level      = low;
            haulables[write] = entry;
            write++;
        }
        haulables.RemoveRange(write, haulables.Count - write);
    }

    static Job Opportunity_Job(Pawn pawn, LocalTargetInfo jobTarget) {
        Job _Opportunity_Job() {
            maxRanges.Reset();
            haulables.Clear();
            foreach (var thing in pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling())
                haulables.Add(new Haulable { thing = thing });
            var vanillaPathChecker = settings.Opportunity_PathChecker == Settings.PathCheckerEnum.Vanilla;
            if (!vanillaPathChecker)
                ComputeRangeLevels(pawn, jobTarget);
            var level = 0;
            while (haulables.Count > 0) {
                var count = haulables.Count;
                var write = 0;
                for (var read = 0; read < count; read++) {
                    var entry = haulables[read];
                    var thing = entry.thing;
                    if (entry.level > level) {
                        haulables[write] = entry;
                        write++;
                        continue;
                    }

                    var canHaul = CanHaul(pawn, thing, jobTarget, out var storeCell);
                    switch (canHaul) {
                    case CanHaulResult.RangeFail:
                        if (vanillaPathChecker)
                            continue;

                        haulables[write] = entry;
                        write++;
                        continue;
                    case CanHaulResult.HardFail:
                        continue;
                    case CanHaulResult.FullStop:
                        return null;
                    case CanHaulResult.Success:
                        // todo test our heuristic expand factor more thoroughly
                        // Debug.WriteLine($"Checked: {1 - (haulables.Count - 1) / (float)pawn.Map.listerHaulables.haulables.Count:P}. Expansions: {maxRanges.expandCount}");

                        if (DebugViewSettings.drawOpportunisticJobs) {
                            for (var _ = 0; _ < 3; _++) { // for bolder lines
                                var duration = 600;
                                pawn.Map.debugDrawer.FlashCell(pawn.Position,  0.50f, pawn.Name.ToStringShort, duration); // green
                                pawn.Map.debugDrawer.FlashCell(thing.Position, 0.62f, pawn.Name.ToStringShort, duration); // cyan
                                pawn.Map.debugDrawer.FlashCell(storeCell,      0.22f, pawn.Name.ToStringShort, duration); // orange
                                pawn.Map.debugDrawer.FlashCell(jobTarget.Cell, 0.0f,  pawn.Name.ToStringShort, duration); // red

                                // red: shorter old; green: longer new
                                pawn.Map.debugDrawer.FlashLine(pawn.Position,  jobTarget.Cell, duration, SimpleColor.Red);
                                pawn.Map.debugDrawer.FlashLine(pawn.Position,  thing.Position, duration, SimpleColor.Green);
                                pawn.Map.debugDrawer.FlashLine(thing.Position, storeCell,      duration, SimpleColor.Green);
                                // first ingredient if a bill
                                pawn.Map.debugDrawer.FlashLine(storeCell, jobTarget.Cell, duration, SimpleColor.Green);
                            }
                        }

                        if (havePuah && settings.UsePickUpAndHaulPlus) {
                            var detour = SetOrAddDetour(pawn, DetourType.PuahOpportunity, startCell: pawn.Position, jobTarget: jobTarget);
                            detour.TrackPuahThing(thing, storeCell);
                            var puahJob = PuahJob(pawn, thing);
                            if (puahJob is not null) return puahJob;
                        }

                        SetOrAddDetour(pawn, DetourType.HtcOpportunity, jobTarget: jobTarget);
                        var htcJob = HaulAIUtility.HaulToCellStorageJob(pawn, thing, storeCell, false);
                        return htcJob;
                }
                }

                haulables.RemoveRange(write, count - write);
                if (haulables.Count == 0) break;

                // By expanding our cheap range checks gradually, our expensive checks will be performed in the most optimistic order.
                // It won't always help—a detour can be far away yet still perfectly along our path—but it's a good performance heuristic.
                // todo A smaller number might perform better in the worst (but rarer) cases? It's a TweakValue now.
                maxRanges *= MaxRanges.heuristicRangeExpandFactor;
                level++;
            }

            return null;
        }

        var result = _Opportunity_Job();
        opportunityStoreCellCache.Clear(); // :Cache
        return result;
    }

    static CanHaulResult CanHaul(Pawn pawn, Thing thing, LocalTargetInfo jobTarget, out IntVec3 storeCell) {
        storeCell = IntVec3.Invalid;

        // I don't know if avoiding `Sqrt()` is currently faster in Unity, but it's easy enough (when not summing distances).
        // https://www.youtube.com/watch?v=pgoetgxecw8&t=370s
        // https://dev.to/iamscottcab/exploring-the-myth-calculating-square-root-is-expensive-44ka :Sqrt
        var startToThingSquared = pawn.Position.DistanceToSquared(thing.Position);
        var pawnToJobSquared    = pawn.Position.DistanceToSquared(jobTarget.Cell);
        if (!StartToThingInRange(startToThingSquared, pawnToJobSquared, maxRanges)) return CanHaulResult.RangeFail;

        // if this one exceeds the maximum the next `maxTotalTripPctOrigTrip` check certainly will
        if (ExceedsMaxTotalTrip(pawn.Position, thing.Position, jobTarget.Cell))
            return CanHaulResult.HardFail;
        var startToThing = pawn.Position.DistanceTo(thing.Position);
        var thingToJob   = thing.Position.DistanceTo(jobTarget.Cell);
        var pawnToJob    = pawn.Position.DistanceTo(jobTarget.Cell);
        if (pawn.Map.reservationManager.FirstRespectedReserver(thing, pawn) is not null) return CanHaulResult.HardFail;
        if (thing.IsForbidden(pawn)) return CanHaulResult.HardFail;
        if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, false)) return CanHaulResult.HardFail;

        var currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);
        if (!opportunityStoreCellCache.TryGetValue(thing, out storeCell)) {
            if (!TryFindBestBetterStoreCellFor_MidwayToTarget(
                    thing, jobTarget, IntVec3.Invalid, pawn, pawn.Map, currentPriority, pawn.Faction, out storeCell, maxRanges.expandCount == 0))
                return CanHaulResult.HardFail;
        }

        // this won't change for a given thing as our same unmoved pawn loops through haulables, so cache it
        opportunityStoreCellCache.SetOrAdd(thing, storeCell);

        var storeToJobSquared = storeCell.DistanceToSquared(jobTarget.Cell); // :Sqrt
        if (storeToJobSquared > maxRanges.storeToJob.Squared()) return CanHaulResult.RangeFail;
        if (storeToJobSquared > pawnToJobSquared * maxRanges.storeToJobPctOrigTrip.Squared()) return CanHaulResult.RangeFail;

        var storeToJob = storeCell.DistanceTo(jobTarget.Cell);
        if (startToThing + storeToJob > pawnToJob * settings.Opportunity_MaxNewLegsPctOrigTrip) // :MaxNewLeg
            return CanHaulResult.HardFail;
        var thingToStore = thing.Position.DistanceTo(storeCell);
        if (startToThing + thingToStore + storeToJob > pawnToJob * settings.Opportunity_MaxTotalTripPctOrigTrip) // :MaxTotalTrip
            return CanHaulResult.HardFail;

        bool WithinRegionCount(IntVec3 storeCell) {
            if (!pawn.Position.WithinRegions(thing.Position, pawn.Map, settings.Opportunity_MaxStartToThingRegionLookCount, TraverseParms.For(pawn)))
                return false;

            if (!storeCell.WithinRegions(jobTarget.Cell, pawn.Map, settings.Opportunity_MaxStoreToJobRegionLookCount, TraverseParms.For(pawn)))
                return false;

            return true;
        }

        bool WithinPathCost(IntVec3 storeCell) {
            float? GetPathCost(IntVec3 start, LocalTargetInfo destTarget, PathEndMode peMode) {
                // Vanilla always calls `FindPath` with an `IntVec3` for the `LocalTargetInfo`.
                // Without `.Cell` we get: "System.IndexOutOfRangeException: Index was outside the bounds of the array.
                //  at Verse.EdificeGrid.get_Item(Verse.IntVec3c)"
                var pawnPath = pawn.Map.pathFinder.FindPathNow(start, destTarget.Cell, TraverseParms.For(pawn), peMode: peMode);
                var result   = pawnPath.Found ? pawnPath.TotalCost : (float?)null;
                pawnPath.ReleaseToPool();
                return result;
            }

            if (GetPathCost(pawn.Position, thing, PathEndMode.ClosestTouch) is not { } pawnToThingPathCost) return false;
            if (GetPathCost(storeCell, jobTarget, PathEndMode.Touch) is not { } storeToJobPathCost) return false;
            if (GetPathCost(pawn.Position, jobTarget, PathEndMode.Touch) is not { } pawnToJobPathCost) return false;

            if (pawnToThingPathCost + storeToJobPathCost > pawnToJobPathCost * settings.Opportunity_MaxNewLegsPctOrigTrip)
                return false;

            if (GetPathCost(thing.Position, storeCell, PathEndMode.ClosestTouch) is not { } thingToStorePathCost) return false;

            if (pawnToThingPathCost + thingToStorePathCost + storeToJobPathCost > pawnToJobPathCost * settings.Opportunity_MaxTotalTripPctOrigTrip)
                return false;

            return true;
        }

        return settings.Opportunity_PathChecker switch {
            Settings.PathCheckerEnum.Vanilla     => WithinRegionCount(storeCell) ? CanHaulResult.Success : CanHaulResult.HardFail,
            Settings.PathCheckerEnum.Pathfinding => WithinPathCost(storeCell) ? CanHaulResult.Success : CanHaulResult.HardFail,
            Settings.PathCheckerEnum.Default     => WithinPathCost(storeCell) ? CanHaulResult.Success : CanHaulResult.FullStop,
            _                                    => throw new ArgumentOutOfRangeException(),
        };
    }

    partial record BaseDetour
    {
        public bool TrackPuahThingIfOpportune(Thing thing, Pawn pawn, ref IntVec3 foundCell) {
            var isPrepend = pawn.carryTracker?.CarriedThing == thing;
            TrackPuahThing(thing, foundCell, isPrepend, trackDef: false); // :TrackDef

            var startToLastThing = 0f;
            {
                var curPos = opportunity.puah.startCell;
                foreach (var (t, _) in opportunity.hauls) {
                    startToLastThing += curPos.DistanceTo(t.Position);
                    curPos           =  t.Position;
                }
            }

            // initialize here since might not have PUAH
            var haulsByUnloadDistance = Opportunity.OpportunityPuah.haulsByUnloadDistanceOrdered ??= new List<(Thing thing, IntVec3 storeCell)>(16);
            {
                var pending = Opportunity.OpportunityPuah.haulsByUnloadDistancePending ??= new List<(Thing thing, IntVec3 storeCell)>(16);
                haulsByUnloadDistance.Clear();                        // every time, since our foundCell could fit anywhere
                haulsByUnloadDistance.Add(opportunity.hauls.First()); // PUAH WorkGiver always takes us to the first
                pending.Clear();
                pending.AddRange(opportunity.hauls.GetRange(1, opportunity.hauls.Count - 1));

                while (pending.Count > 0) {
                    // It's perfectly correct if due to close-together or oddly-shaped stockpiles these cells aren't ordered by their parent stockpile.
                    // Actual unloading cells are determined on-the-fly (even extras if don't fit, etc.) but this represents them with equal correctness.
                    var closestHaul = pending.MinBy(x => x.storeCell.DistanceToSquared(haulsByUnloadDistance.Last().storeCell)); // :Sqrt
                    haulsByUnloadDistance.Add(closestHaul);
                    pending.Remove(closestHaul);
                }
            }

            var lastThingToFirstStore = opportunity.hauls.Last().thing.Position.DistanceTo(haulsByUnloadDistance.First().storeCell);

            var firstStoreToLastStore = 0f;
            {
                var curPos = haulsByUnloadDistance.First().storeCell;
                foreach (var (_, storeCell) in haulsByUnloadDistance) {
                    firstStoreToLastStore += curPos.DistanceTo(storeCell);
                    curPos                =  storeCell;
                }
            }

            var lastStoreToJob = haulsByUnloadDistance.Last().storeCell.DistanceTo(opportunity.jobTarget.Cell);

            var origTrip     = opportunity.puah.startCell.DistanceTo(opportunity.jobTarget.Cell);
            var totalTrip    = startToLastThing + lastThingToFirstStore + firstStoreToLastStore + lastStoreToJob;
            var maxTotalTrip = origTrip * settings.Opportunity_MaxTotalTripPctOrigTrip;
            var newLegs      = startToLastThing + firstStoreToLastStore + lastStoreToJob;
            var maxNewLegs   = origTrip * settings.Opportunity_MaxNewLegsPctOrigTrip;

            if (totalTrip > maxTotalTrip || newLegs > maxNewLegs) {
                foundCell = IntVec3.Invalid;
                opportunity.hauls.RemoveAt(isPrepend ? 0 : opportunity.hauls.Count - 1);
                return false;
            }

            puah.defHauls.SetOrAdd(thing!.def, foundCell); // :TrackDef

#if false
                var storeCells = haulsByUnloadDistance.Select(x => x.storeCell).ToList();
                Debug.WriteLine($"APPROVED {thing} -> {foundCell} for {pawn}");
                Debug.WriteLine(
                    $"\tstartToLastThing: {pawn} {opportunity.Puah.startCell} -> {string.Join(" -> ", opportunity.hauls.Select(x => $"{x.thing} {x.thing.Position}"))} = {startToLastThing}");
                Debug.WriteLine($"\tlastThingToFirstStore: {thing} {thing.Position} -> {storeCells.First().GetSlotGroup(pawn.Map)} {storeCells.First()} = {lastThingToFirstStore}");
                Debug.WriteLine($"\tfirstStoreToLastStore: {string.Join(" -> ", storeCells.Select(x => $"{x.GetSlotGroup(pawn.Map)} {x}"))} = {firstStoreToLastStore}");
                Debug.WriteLine(
                    $"\tlastStoreToJob: {storeCells.Last().GetSlotGroup(pawn.Map)} {storeCells.Last()} -> {opportunity.jobTarget} {opportunity.jobTarget.Cell} = {lastStoreToJob}");
                Debug.WriteLine($"\torigTrip: {pawn} {opportunity.Puah.startCell} -> {opportunity.jobTarget} {opportunity.jobTarget.Cell} = {origTrip}");
                Debug.WriteLine($"\ttotalTrip: {startToLastThing} + {lastThingToFirstStore} + {firstStoreToLastStore} + {lastStoreToJob}  = {totalTrip}");
                Debug.WriteLine($"\tmaxTotalTrip: {origTrip} * {settings.Opportunity_MaxTotalTripPctOrigTrip} = {maxTotalTrip}");
                Debug.WriteLine($"\tnewLegs: {startToLastThing} + {firstStoreToLastStore} + {lastStoreToJob} = {newLegs}");
                Debug.WriteLine($"\tmaxNewLegs: {origTrip} * {settings.Opportunity_MaxNewLegsPctOrigTrip} = {maxNewLegs}");
                Debug.WriteLine("");

                // true unload order depends on distance to pawn at the time
                var slotGroupUnloadOrder = haulsByUnloadDistance.Select(x => x.storeCell.GetSlotGroup(pawn.Map)).Distinct().ToList();
                var haulsByUnloadOrder = haulsByUnloadDistance.OrderBy(x => slotGroupUnloadOrder.IndexOf(x.storeCell.GetSlotGroup(pawn.Map)))
                    .ThenBy(x => x.thing.def.FirstThingCategory?.index)
                    .ThenBy(x => x.thing.def.defName)
                    .ToList();

                Debug.WriteLine($"{haulsByUnloadOrder.Count} Hauls:");
                foreach (var haul in haulsByUnloadOrder)
                    Debug.WriteLine(haul);

                Debug.WriteLine($"{haulsByUnloadOrder.Count} Unloads:");
                foreach (var haul in haulsByUnloadOrder)
                    Debug.WriteLine($"{haul.storeCell.GetSlotGroup(pawn.Map)}");

                Debug.WriteLine("");
#endif

            return true;
        }
    }
}
