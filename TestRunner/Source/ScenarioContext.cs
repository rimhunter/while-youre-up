using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WyuTestRunner
{
    public sealed class RecordedJob
    {
        public string Pawn;
        public int    Tick;
        public string Def;
        public string TargetA;
        public string TargetB;
        public List<string> Reports = new List<string>();

        public override string ToString() => $"{Pawn}@{Tick} {Def}" + (TargetA is null ? "" : $" A={TargetA}") + (TargetB is null ? "" : $" B={TargetB}") + (Reports.Count == 0 ? "" : $" [{string.Join(" | ", Reports)}]");
    }

    public sealed partial class ScenarioContext
    {
        readonly Stopwatch             clock = Stopwatch.StartNew();
        readonly int                   maxSeconds;
        readonly Dictionary<Pawn, Job> lastJob = new Dictionary<Pawn, Job>();
        readonly Dictionary<Pawn, int> lastJobId = new Dictionary<Pawn, int>();
        readonly Dictionary<Pawn, RecordedJob> openJob = new Dictionary<Pawn, RecordedJob>();
        readonly List<Pawn>            colonists = new List<Pawn>();
        readonly int                   firstLogError;

        internal readonly List<string>      Failures = new List<string>();
        internal readonly List<RecordedJob> JobLog   = new List<RecordedJob>();
        internal int TicksRun;
        internal readonly SortedDictionary<string, double> MetricValues = new SortedDictionary<string, double>();
        readonly Stopwatch tickClock = new Stopwatch();

        public Map Map { get; }
        public IReadOnlyList<RecordedJob> Jobs => JobLog;
        public IReadOnlyList<Pawn> Colonists => colonists;
        public bool KeepNeedsFull { get; set; } = true;
        public bool RecordJobs { get; set; } = true;
        public double TickMilliseconds => tickClock.Elapsed.TotalMilliseconds;
        public void Metric(string name, double value) => MetricValues[name] = value;
        public string PlayerLogPath { get; }

        internal ScenarioContext(Map map, int maxSeconds, string playerLogPath) {
            Map             = map;
            this.maxSeconds = maxSeconds;
            PlayerLogPath   = playerLogPath;
            firstLogError   = LogErrorSink.Count;
        }

        public IReadOnlyList<string> LogErrors => LogErrorSink.Since(firstLogError);

        public IntVec3 Center => Map.Center;
        public IntVec3 Cell(int dx, int dz) => new IntVec3(Map.Center.x + dx, 0, Map.Center.z + dz);

        public static ThingDef Def(string defName) => DefDatabase<ThingDef>.GetNamed(defName, true);

        public Pawn SpawnColonist(IntVec3 cell, string label = null) {
            var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.PlayerStarter, forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                validatorPostGear: p => p.CombinedDisabledWorkTags == WorkTags.None);
            var pawn = PawnGenerator.GeneratePawn(req);
            GenSpawn.Spawn(pawn, cell, Map);
            pawn.workSettings.EnableAndInitialize();
            foreach (var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                if (!pawn.WorkTypeIsDisabled(w))
                    pawn.workSettings.SetPriority(w, 3);
            for (var h = 0; h < 24; h++)
                pawn.timetable.SetAssignment(h, TimeAssignmentDefOf.Work);
            colonists.Add(pawn);
            return pawn;
        }

        public Thing SpawnItem(string defName, int count, IntVec3 cell) => SpawnItem(Def(defName), count, cell);

        public Thing SpawnItem(ThingDef def, int count, IntVec3 cell) {
            var thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            if (!GenPlace.TryPlaceThing(thing, cell, Map, ThingPlaceMode.Direct))
                throw new ScenarioFailure($"could not place {count} {def.defName} at {cell}");
            return thing;
        }

        public Zone_Stockpile Stockpile(CellRect rect, params string[] onlyDefNames) {
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, Map.zoneManager);
            Map.zoneManager.RegisterZone(zone);
            foreach (var c in rect) zone.AddCell(c);
            if (onlyDefNames.Length > 0) {
                zone.settings.filter.SetDisallowAll();
                foreach (var n in onlyDefNames) zone.settings.filter.SetAllow(Def(n), true);
            }
            return zone;
        }

        public Building SpawnBuilding(string defName, IntVec3 cell, string stuffDefName = null, Rot4? rot = null) {
            var def   = Def(defName);
            var stuff = stuffDefName is null ? GenStuff.DefaultStuffFor(def) : Def(stuffDefName);
            var b     = (Building) ThingMaker.MakeThing(def, stuff);
            b.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(b, cell, Map, rot ?? Rot4.North);
            return b;
        }

        public Building_Storage Shelf(IntVec3 cell, params string[] onlyDefNames) {
            var shelf = (Building_Storage) SpawnBuilding("Shelf", cell);
            if (onlyDefNames.Length > 0) {
                shelf.settings.filter.SetDisallowAll();
                foreach (var n in onlyDefNames) shelf.settings.filter.SetAllow(Def(n), true);
            }
            else shelf.settings.filter.SetAllowAll(null);
            return shelf;
        }

        public Blueprint_Build PlaceBlueprint(string defName, IntVec3 cell, string stuffDefName = null, Rot4? rot = null) {
            var def   = (BuildableDef) DefDatabase<ThingDef>.GetNamed(defName, true);
            var stuff = stuffDefName is null ? GenStuff.DefaultStuffFor(def) : Def(stuffDefName);
            return GenConstruct.PlaceBlueprintForBuild(def, cell, Map, rot ?? Rot4.North, Faction.OfPlayer, stuff);
        }

        public Bill_Production AddBill(Building_WorkTable bench, string recipeDefName, int count) {
            var recipe = DefDatabase<RecipeDef>.GetNamed(recipeDefName, true);
            var bill   = (Bill_Production) recipe.MakeNewBill();
            bill.repeatMode  = BillRepeatModeDefOf.RepeatCount;
            bill.repeatCount = count;
            bench.billStack.AddBill(bill);
            return bill;
        }

        public IntVec3 CellOf(Thing thing)               => thing.PositionHeld;
        public SlotGroup StorageOf(Thing thing)          => thing.GetSlotGroup();
        public bool IsStored(Thing thing)                => StoreUtility.IsInAnyStorage(thing);
        public bool IsCarriedBy(Thing thing, Pawn pawn)  => pawn.carryTracker.CarriedThing == thing || pawn.inventory.innerContainer.Contains(thing);
        public string JobReport(Pawn pawn)               => pawn.jobs.curDriver?.GetReport();
        public IEnumerable<RecordedJob> JobsOf(Pawn pawn) => JobLog.Where(j => j.Pawn == pawn.LabelShort);
        public IEnumerable<string> JobDefsOf(Pawn pawn)   => JobsOf(pawn).Select(j => j.Def);

        public void Tick(int count) {
            for (var i = 0; i < count; i++) TickOnce();
        }

        public bool RunUntil(Func<bool> done, int maxTicks) {
            for (var i = 0; i < maxTicks; i++) {
                if (done()) return true;
                TickOnce();
            }
            return done();
        }

        public void Check(bool condition, string failureMessage) {
            if (!condition) Failures.Add(failureMessage);
        }

        public void AreEqual<T>(T expected, T actual, string what) {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) Failures.Add($"{what}: expected <{expected}> but was <{actual}>");
        }

        public void Fail(string message) => Failures.Add(message);

        public void RequireNoLogErrors() {
            var errors = LogErrors;
            if (errors.Count > 0) Failures.Add($"{errors.Count} error(s) reached the game log: " + string.Join(" || ", errors.Select(Shorten)));
        }

        static readonly System.Text.RegularExpressions.Regex ErrorLine =
            new System.Text.RegularExpressions.Regex(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+\s+(ERROR|FATAL|CRITICAL)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

        public IReadOnlyList<string> PlayerLogErrorLines() {
#if RW_1_6
            return GameLogErrors();
#else
            System.Threading.Thread.Sleep(1600);
            using var fs = new System.IO.FileStream(PlayerLogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
            using var reader = new System.IO.StreamReader(fs);
            var found = new List<string>();
            string line;
            while ((line = reader.ReadLine()) is not null)
                if (ErrorLine.IsMatch(line)) found.Add(line);
            return found;
#endif
        }

#if RW_1_6
        // Unity's Player.log has no level on its lines (the game logs without stack traces), so 1.6 reads the game's own log record.
        static IReadOnlyList<string> GameLogErrors() {
            List<LogMessage> messages;
            using (Log.LockMessages()) messages = Log.Messages.ToList();
            if (messages.Count >= new LogMessageQueue().maxMessages)
                throw new ScenarioFailure($"the game's log holds {messages.Count} messages, its limit, so earlier errors may have been dropped");
            return messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToList();
        }
#endif

        public void RequireNoPlayerLogErrors() {
            var lines = PlayerLogErrorLines();
            if (lines.Count > 0) Failures.Add($"{lines.Count} error line(s) in Player.log: " + string.Join(" || ", lines.Take(5).Select(Shorten)));
        }

        static string Shorten(string s) => s.Length <= 300 ? s : s.Substring(0, 300) + "...";

        void TickOnce() {
            if (clock.Elapsed.TotalSeconds > maxSeconds)
                throw new ScenarioFailure($"scenario exceeded its {maxSeconds} s wall-clock budget after {TicksRun} ticks");
            if (KeepNeedsFull)
                foreach (var p in colonists)
                    if (!p.Dead && p.needs is not null)
                        foreach (var n in p.needs.AllNeeds)
                            n.CurLevelPercentage = 1f;
            RealTime.frameCount++;
            tickClock.Start();
            Find.TickManager.DoSingleTick();
            tickClock.Stop();
            TicksRun++;
            if (RecordJobs)
                foreach (var p in colonists) Record(p);
        }

        void Record(Pawn pawn) {
            var job = pawn.CurJob;
            lastJob.TryGetValue(pawn, out var prev);
            var jobId = job?.loadID ?? -1;
            lastJobId.TryGetValue(pawn, out var prevId);
            if (!ReferenceEquals(job, prev) || jobId != prevId) {
                lastJob[pawn] = job;
                lastJobId[pawn] = jobId;
                openJob.Remove(pawn);
                if (job is not null) {
                    var rec = new RecordedJob {
                        Pawn    = pawn.LabelShort,
                        Tick    = Find.TickManager.TicksGame,
                        Def     = job.def.defName,
                        TargetA = job.targetA.IsValid ? job.targetA.ToString() : null,
                        TargetB = job.targetB.IsValid ? job.targetB.ToString() : null,
                    };
                    JobLog.Add(rec);
                    openJob[pawn] = rec;
                }
            }
            if (job is not null && openJob.TryGetValue(pawn, out var open)) {
                var report = pawn.jobs.curDriver?.GetReport();
                if (!string.IsNullOrEmpty(report) && (open.Reports.Count == 0 || open.Reports[open.Reports.Count - 1] != report))
                    open.Reports.Add(report);
            }
        }
    }
}
