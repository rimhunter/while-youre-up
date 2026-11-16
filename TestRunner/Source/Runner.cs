using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace WyuTestRunner
{
    public class RunnerMod : Mod
    {
        public const string WorldSeed = "wyu-runner-world";

        public RunnerMod(ModContentPack content) : base(content) {
            LogErrorSink.Hook();
#if RW_1_7
            if (Environment.GetEnvironmentVariable("WYU_SCENARIOS") == Scenarios.JitReentryCrash.Name) Scenarios.JitReentryCrash.Trigger();
#endif
            var harmony = new Harmony("rimhunter.whileyoureup.testrunner");
            UiRecorder.Patch(harmony);
            harmony.Patch(
                AccessTools.Method(typeof(GenText), nameof(GenText.RandomSeedString)),
                prefix: new HarmonyMethod(typeof(RunnerMod), nameof(FixedWorldSeed)));
        }

        static bool FixedWorldSeed(ref string __result) {
            __result = WorldSeed;
            return false;
        }
    }

    public class RunnerComponent : GameComponent
    {
        static bool started;
        static IEnumerator run;

        public RunnerComponent(Game game) { }

        public override void GameComponentUpdate() {
            if (run is not null) {
                Step();
                return;
            }
            if (started) return;
            if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            started = true;
            run = RunAndQuit();
            Step();
        }

        static void Step() {
            if (!run.MoveNext()) run = Done;
        }

        static readonly IEnumerator Done = Enumerable.Empty<object>().GetEnumerator();

        static IEnumerator RunAndQuit() {
            RunConfig config = null;
            ResultWriter writer = null;
            List<ScenarioResult> results = null;
            Exception fatal = null;
            IEnumerator selected = null;
            try {
                config = RunConfig.FromEnvironment();
                writer = new ResultWriter(config);
                writer.Begin();
                results = new List<ScenarioResult>();
                selected = RunSelected(config, writer, results);
            }
            catch (Exception e) {
                fatal = e;
            }
            while (fatal is null) {
                bool more;
                try {
                    more = selected.MoveNext();
                }
                catch (Exception e) {
                    fatal = e;
                    break;
                }
                if (!more) break;
                yield return null;
            }
            var exitCode = 2;
            if (fatal is null) {
                writer.Finish(null);
                exitCode = results.All(r => r.Passed) ? 0 : 1;
            }
            else {
                Log.Error("scenario runner failed: " + fatal);
                if (writer is not null) writer.Finish(fatal.ToString());
                else Console.Error.WriteLine("scenario runner failed before it could write a result file: " + fatal);
            }
            Application.Quit(exitCode);
        }

        static IEnumerator RunSelected(RunConfig config, ResultWriter writer, List<ScenarioResult> results) {
            var known = ScenarioCatalog.Discover();
            var names = config.SelectAll ? known.Values.Where(k => !k.Attribute.SelfTest && !k.Attribute.NeedsWindow && k.Attribute.Mods is null && (k.Attribute.OnlyOn is null || k.Attribute.OnlyOn == VersionControl.CurrentMajor + "." + VersionControl.CurrentMinor)).Select(k => k.Attribute.Name).OrderBy(n => n).ToList() : config.Selected;
            foreach (var name in names) {
                var holder = new ScenarioResult[1];
                if (!known.TryGetValue(name, out var entry))
                    holder[0] = ScenarioResult.Unknown(name, known.Keys);
                else {
                    var frames = ScenarioExecutor.Run(entry, config, holder);
                    while (frames.MoveNext()) yield return null;
                }
                results.Add(holder[0]);
                writer.Add(holder[0]);
            }
        }
    }

    sealed class ScenarioEntry
    {
        public ScenarioAttribute Attribute;
        public MethodInfo Method;
    }

    static class ScenarioCatalog
    {
        public static Dictionary<string, ScenarioEntry> Discover() {
            var map = new Dictionary<string, ScenarioEntry>(StringComparer.Ordinal);
            foreach (var type in typeof(ScenarioCatalog).Assembly.GetTypes())
                foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
                    var attr = m.GetCustomAttribute<ScenarioAttribute>();
                    if (attr is null) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1 || ps[0].ParameterType != typeof(ScenarioContext) || (m.ReturnType != typeof(void) && m.ReturnType != typeof(IEnumerator)))
                        throw new InvalidOperationException($"{type.FullName}.{m.Name} is marked [Scenario] but is not 'static void (ScenarioContext)' or 'static IEnumerator (ScenarioContext)'");
                    if (attr.NeedsWindow != (m.ReturnType == typeof(IEnumerator)))
                        throw new InvalidOperationException($"{type.FullName}.{m.Name}: a scenario returns IEnumerator exactly when it needs a window on this game version (Windowed, or WindowedOnUnity on 1.6): it yields frames to the game so they are rendered");
                    if (map.ContainsKey(attr.Name))
                        throw new InvalidOperationException($"two scenarios are named '{attr.Name}'");
                    map[attr.Name] = new ScenarioEntry { Attribute = attr, Method = m };
                }
            return map;
        }
    }

    sealed class ScenarioResult
    {
        public string Name;
        public bool   Passed;
        public long   DurationMs;
        public int    Ticks;
        public int    MapSeed;
        public string MapFingerprint;
        public List<string>      Failures = new List<string>();
        public List<RecordedJob> Jobs     = new List<RecordedJob>();
        public List<string>      Screenshots = new List<string>();
        public SortedDictionary<string, double> Metrics = new SortedDictionary<string, double>();

        public static ScenarioResult Unknown(string name, IEnumerable<string> known) => new ScenarioResult {
            Name     = name,
            Passed   = false,
            Failures = { $"unknown scenario '{name}'; known scenarios: {string.Join(", ", known.OrderBy(k => k))}" },
        };
    }

    static class ScenarioExecutor
    {
        public static IEnumerator Run(ScenarioEntry entry, RunConfig config, ScenarioResult[] holder) {
            var attr   = entry.Attribute;
            var result = new ScenarioResult { Name = attr.Name };
            holder[0]  = result;
            var sw     = Stopwatch.StartNew();
            Map map = null;
            MapParent parent = null;
            ScenarioContext ctx = null;
            FrameDriver frames = null;
            try {
                var line = VersionControl.CurrentMajor + "." + VersionControl.CurrentMinor;
                if (attr.OnlyOn is not null && attr.OnlyOn != line)
                    throw new ScenarioFailure($"this scenario runs only on RimWorld {attr.OnlyOn}; this is {line}");
                if (attr.NeedsWindow && Headless)
                    throw new ScenarioFailure("this scenario takes screenshots and needs a window: run it with scripts/run-scenarios --windowed");
                result.MapSeed = MapFactory.SeedFor(attr);
                (map, parent) = MapFactory.Fresh(attr, result.MapSeed, out result.MapFingerprint);
                ctx = new ScenarioContext(map, attr.MaxSeconds, config.PlayerLog);
                if (attr.NeedsWindow) {
                    ctx.PrepareScreen();
                    frames = new FrameDriver((IEnumerator)entry.Method.Invoke(null, new object[] { ctx }));
                }
                else entry.Method.Invoke(null, new object[] { ctx });
            }
            catch (Exception e) {
                result.Failures.Add("scenario threw: " + Unwrap(e));
                frames = null;
            }
            while (frames is not null) {
                bool more;
                try {
                    more = frames.Step();
                }
                catch (Exception e) {
                    result.Failures.Add("scenario threw: " + Unwrap(e));
                    break;
                }
                if (!more) break;
                yield return null;
            }
            if (ctx is not null) {
                ctx.RequireNoLogErrors();
                result.Failures.AddRange(ctx.Failures);
                result.Jobs.AddRange(ctx.JobLog);
                result.Ticks = ctx.TicksRun;
                result.Screenshots.AddRange(ctx.ScreenshotFiles);
                foreach (var m in ctx.MetricValues) result.Metrics[m.Key] = m.Value;
                ctx.LeaveScreen();
            }
            if (map is not null) MapFactory.Remove(map, parent);
            result.DurationMs = sw.ElapsedMilliseconds;
            result.Passed     = result.Failures.Count == 0;
        }

#if RW_1_6
        static bool Headless => Application.isBatchMode;
#else
        static bool Headless => GenCommandLine.CommandLineArgPassed("Headless");
#endif

        static Exception Unwrap(Exception e) => e is TargetInvocationException t && t.InnerException is not null ? t.InnerException : e;
    }

    sealed class FrameDriver
    {
        readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();

        public FrameDriver(IEnumerator root) {
            if (root is null) throw new InvalidOperationException("a windowed scenario returned no enumerator");
            stack.Push(root);
        }

        public bool Step() {
            while (stack.Count > 0) {
                var top = stack.Peek();
                if (!top.MoveNext()) {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested) {
                    stack.Push(nested);
                    continue;
                }
                if (top.Current is not null)
                    throw new InvalidOperationException("a windowed scenario may yield only null (one frame) or an IEnumerator, not " + top.Current.GetType().Name);
                return true;
            }
            return false;
        }
    }

    static class MapFactory
    {
        public static int SeedFor(ScenarioAttribute attr) => GenText.StableStringHash(attr.Name);

        static string Fingerprint(Map map, PlanetTile tile) {
            var hash = Gen.HashCombineInt(tile.tileId, map.Size.x);
            foreach (var c in map.AllCells) {
                hash = Gen.HashCombineInt(hash, GenText.StableStringHash(map.terrainGrid.TerrainAt(c).defName));
                hash = Gen.HashCombineInt(hash, GenText.StableStringHash(c.GetEdifice(map)?.def.defName ?? ""));
            }
            foreach (var t in map.listerThings.AllThings)
                hash = Gen.HashCombineInt(hash, GenText.StableStringHash(t.def.defName) ^ t.Position.GetHashCode());
            return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static (Map, MapParent) Fresh(ScenarioAttribute attr, int seed, out string fingerprint) {
            PlanetTile tile;
            Rand.PushState(seed);
            try {
                do tile = TileFinder.RandomStartingTile();
                while (Find.WorldObjects.AnyWorldObjectAt(tile));
            }
            finally {
                Rand.PopState();
            }
            var parent = (MapParent) WorldObjectMaker.MakeWorldObject(tile.LayerDef.SettlementWorldObjectDef);
            parent.Tile = tile;
            parent.SetFaction(Faction.OfPlayer);
            Find.WorldObjects.Add(parent);
            var map = GetOrGenerateMapUtility.GetOrGenerateMap(tile, new IntVec3(attr.MapSize, 1, attr.MapSize), null);
            Current.Game.CurrentMap = map;
            map.fogGrid.ClearAllFog();
            fingerprint = Fingerprint(map, tile);
            if (!attr.KeepMapNatural) Clear(map);
            Current.Game.storyteller.storytellerComps.Clear();
            return (map, parent);
        }

        static void Clear(Map map) {
            var previous = Thing.allowDestroyNonDestroyable;
            Thing.allowDestroyNonDestroyable = true;
            foreach (var c in map.AllCells) map.roofGrid.SetRoof(c, null);
            foreach (var t in map.listerThings.AllThings.ToList())
                if (t.Spawned) t.Destroy();
            foreach (var c in map.AllCells) map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
            Thing.allowDestroyNonDestroyable = previous;
        }

        public static void Remove(Map map, MapParent parent) {
            Current.Game.DeinitAndRemoveMap(map, false);
            Find.WorldObjects.Remove(parent);
        }
    }

    sealed class ResultWriter
    {
        readonly RunConfig config;
        readonly List<ScenarioResult> results = new List<ScenarioResult>();
        readonly DateTime startedUtc = DateTime.UtcNow;

        public ResultWriter(RunConfig config) { this.config = config; }

        public void Begin() => Write(false, null);
        public void Add(ScenarioResult r) { results.Add(r); Write(false, null); }
        public void Finish(string fatal) => Write(true, fatal);

        void Write(bool complete, string fatal) {
            var scenarios = results.Select(r => Json.Obj(
                ("name", Json.Quote(r.Name)),
                ("status", Json.Quote(r.Passed ? "pass" : "fail")),
                ("duration_ms", Json.Num(r.DurationMs)),
                ("ticks", Json.Num(r.Ticks)),
                ("map_seed", Json.Num(r.MapSeed)),
                ("map_fingerprint", Json.Quote(r.MapFingerprint)),
                ("failures", Json.StringArray(r.Failures)),
                ("screenshots", Json.StringArray(r.Screenshots)),
                ("metrics", Json.Obj(r.Metrics.Select(m => (m.Key, Json.Dbl(m.Value))).ToArray())),
                ("jobs", Json.Array(r.Jobs.Select(j => Json.Obj(
                    ("pawn", Json.Quote(j.Pawn)),
                    ("tick", Json.Num(j.Tick)),
                    ("job", Json.Quote(j.Def)),
                    ("target_a", Json.Quote(j.TargetA)),
                    ("target_b", Json.Quote(j.TargetB)),
                    ("reports", Json.StringArray(j.Reports))))))));
            var doc = Json.Obj(
                ("schema", Json.Num(1)),
                ("complete", Json.Bool(complete)),
                ("fatal", Json.Quote(fatal)),
                ("game_version", Json.Quote(VersionControl.CurrentVersionStringWithRev)),
                ("language", Json.Quote(LanguageDatabase.activeLanguage?.folderName)),
                ("mods", Json.StringArray(ModsConfig.ActiveModsInLoadOrder.Select(m => m.PackageId))),
                ("started_utc", Json.Quote(startedUtc.ToString("o"))),
                ("scenarios", Json.Array(scenarios)));
            var tmp = config.ResultFile + ".tmp";
            File.WriteAllText(tmp, doc);
            if (File.Exists(config.ResultFile)) File.Delete(config.ResultFile);
            File.Move(tmp, config.ResultFile);
        }
    }
}
