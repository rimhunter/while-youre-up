using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class ModSettingsAccess
    {
        const string PackageId = "rimhunter.whileyoureup";
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static Verse.Mod TheMod => LoadedModManager.ModHandles.First(m => m.Content.PackageId == PackageId);

        internal static object Settings => TheMod.GetType().GetField("settings", All).GetValue(null);

        internal static T Get<T>(string field) => (T)Settings.GetType().GetField(field, All).GetValue(Settings);

        internal static void Set(string field, object value) {
            var f = Settings.GetType().GetField(field, All);
            f.SetValue(Settings, f.FieldType.IsEnum ? Enum.Parse(f.FieldType, (string)value) : value);
        }

        internal static IEnumerable<string> ScalarFields =>
            Settings.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum).Select(f => f.Name);

        internal static Dictionary<string, object> Snapshot() => ScalarFields.ToDictionary(n => n, n => Settings.GetType().GetField(n).GetValue(Settings));

        internal static void Restore(Dictionary<string, object> snapshot) {
            foreach (var kv in snapshot) Settings.GetType().GetField(kv.Key).SetValue(Settings, kv.Value);
        }

        internal static void With(Action body) {
            var snapshot = Snapshot();
            try { body(); }
            finally { Restore(snapshot); }
        }
    }

    static class SettingsScenarios
    {
        static Type WindowType => ModSettingsAccess.TheMod.GetType().GetNestedType("SettingsWindow", BindingFlags.Public | BindingFlags.NonPublic);

        // Kept as reflection: nothing the player sees says which tab is selected, and a click needs the game's tab layout.
        static void SetWindowTab(string tab) {
            var f = WindowType.GetField("tab", BindingFlags.Static | BindingFlags.NonPublic);
            f.SetValue(null, Enum.Parse(f.FieldType, tab));
        }

        // Kept as reflection: the tree's drawn height is held only in the private Listing; the game exposes no row count.
        static float TreeHeight(string field) =>
            ((Listing)WindowType.GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))?.CurHeight ?? 0f;

        static readonly Rect WindowRect = new Rect(0, 0, 900, 700);

        static void DrawFrame(Dialog_ModSettings dialog, EventType type) {
            Event.current = new Event { type = type };
            dialog.DoWindowContents(WindowRect);
        }

        [Scenario("settings-window-opens-and-draws-every-tab", MapSize = 40, WindowedOnUnity = true)]
#if RW_1_6
        static System.Collections.IEnumerator WindowDraws(ScenarioContext s) {
            yield return ScenarioContext.InGuiPass(() => DrawEveryTab(s));
        }
#else
        static void WindowDraws(ScenarioContext s) => DrawEveryTab(s);
#endif

        static void DrawEveryTab(ScenarioContext s) {
            ModSettingsAccess.With(() => {
                ModSettingsAccess.Set("Opportunity_TweakVanilla", true);
                ModSettingsAccess.Set("Opportunity_AutoBuildings", false);
                ModSettingsAccess.Set("HaulBeforeCarry_AutoBuildings", false);
                var dialog = new Dialog_ModSettings(ModSettingsAccess.TheMod);
                foreach (var tab in new[] { "Opportunity", "OpportunityAdvanced", "BeforeCarryDetour", "PickUpAndHaul" }) {
                    SetWindowTab(tab);
                    for (var frame = 0; frame < 3; frame++) {
                        DrawFrame(dialog, EventType.Layout);
                        DrawFrame(dialog, EventType.Repaint);
                    }
                }
                var rows = 1 + DefDatabase<ThingDef>.AllDefsListForReading.Count(d => typeof(RimWorld.Building_Storage).IsAssignableFrom(d.thingClass));
                s.Check(TreeHeight("opportunityTreeFilter") >= rows * 20f, "the Opportunity building tree should list the Core category and its " + (rows - 1) + " storage buildings, height " + TreeHeight("opportunityTreeFilter"));
                s.Check(TreeHeight("hbcTreeFilter") >= rows * 20f, "the Haul-before-carry building tree should list the Core category and its " + (rows - 1) + " storage buildings, height " + TreeHeight("hbcTreeFilter"));
            });
            var notice = "CodeOptimist.WhileYoureUp_License_Notice".Translate().Resolve();
            var column = (float)System.Math.Round((WindowRect.width - 17 * 2) / 3);
            Text.Font = GameFont.Tiny;
            var height = Text.CalcHeight(notice, WindowRect.width - column - 17f);
            Text.Font = GameFont.Small;
            s.Check(height <= 36f, "the license notice should fit its 36 unit row beside the restore button, needs " + height);
        }

        static bool Allows(object filter, string defName) =>
            (bool)filter.GetType().GetMethod("Allows", new[] { typeof(ThingDef) }).Invoke(filter, new object[] { ScenarioContext.Def(defName) });

        static object SettingsField(string name) =>
            ModSettingsAccess.Settings.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(ModSettingsAccess.Settings);

        static void CheckHauled(ScenarioContext s, Opportunity.OnRouteLayout l, bool expected, string why) {
            var hauled = Opportunity.IndexOfJob(s, l.Pawn, "HaulToCell") >= 0;
            s.Check(hauled == expected, why + (expected ? ": the pawn should haul on the way" : ": the pawn should not haul on the way") + "; jobs were: " + Opportunity.Sequence(s, l.Pawn));
        }

        [Scenario("settings-defaults-match-the-original", MapSize = 40)]
        static void DefaultsMatchTheOriginal(ScenarioContext s) {
            var original = new Dictionary<string, object> {
                { "Enabled", true }, { "DrawSpecialHauls", false }, { "UsePickUpAndHaulPlus", true },
                { "Opportunity_PathChecker", "Default" }, { "Opportunity_TweakVanilla", false },
                { "Opportunity_MaxStartToThing", 30f }, { "Opportunity_MaxStartToThingPctOrigTrip", 0.5f },
                { "Opportunity_MaxStoreToJob", 50f }, { "Opportunity_MaxStoreToJobPctOrigTrip", 0.6f },
                { "Opportunity_MaxTotalTripPctOrigTrip", 1.7f }, { "Opportunity_MaxNewLegsPctOrigTrip", 1.0f },
                { "Opportunity_MaxStartToThingRegionLookCount", 25 }, { "Opportunity_MaxStoreToJobRegionLookCount", 25 },
                { "Opportunity_ToStockpiles", true }, { "Opportunity_AutoBuildings", true },
                { "HaulBeforeCarry_Supplies", true }, { "HaulBeforeCarry_Bills", true }, { "HaulBeforeCarry_Bills_NeedsInitForCs", true },
                { "HaulBeforeCarry_ToEqualPriority", true }, { "HaulBeforeCarry_ToStockpiles", true }, { "HaulBeforeCarry_AutoBuildings", true },
            };
            var present = ModSettingsAccess.ScalarFields.ToList();
            s.AreEqual(original.Count, present.Count, "number of settings");
            foreach (var kv in original) {
                s.Check(present.Contains(kv.Key), "setting " + kv.Key + " is missing");
                if (!present.Contains(kv.Key)) continue;
                var actual = ModSettingsAccess.Settings.GetType().GetField(kv.Key).GetValue(ModSettingsAccess.Settings);
                s.AreEqual(kv.Value.ToString(), actual.ToString(), "default of " + kv.Key);
            }
            s.Check(SettingsField("opportunityBuildingFilter") != null, "the Opportunity storage-building filter should exist");
            s.Check(SettingsField("hbcBuildingFilter") != null, "the Haul-before-carry storage-building filter should exist");
        }

        [Scenario("settings-turning-the-mod-off-and-on-mid-game", MapSize = 100)]
        static void TurningTheModOffAndOn(ScenarioContext s) {
            ModSettingsAccess.With(() => {
                ModSettingsAccess.Set("Enabled", false);
                var off = Opportunity.SetupOnRoute(s, pawnX: -45, itemX: -5, itemZ: 2, zoneX: 20, jobX: 45);
                Opportunity.RunToJobTarget(s, off, 6000);
                CheckHauled(s, off, false, "with the mod off (the item is beyond the game's own 30 cell limit)");
                s.Check(s.StorageOf(off.Item) == null, "with the mod off the steel should stay where it was");
                off.Item.Destroy();

                ModSettingsAccess.Set("Enabled", true);
                off.Pawn.Position = s.Cell(-45, 0);
                off.Pawn.Notify_Teleported(true, true);
                var item = s.SpawnItem("Steel", 15, s.Cell(-5, 2));
                var job  = Opportunity.DesignateDeconstruct(s, s.Cell(45, 0));
                var on   = new Opportunity.OnRouteLayout { Pawn = off.Pawn, Item = item, Zone = off.Zone, JobTarget = job };
                var firstOn = s.JobsOf(off.Pawn).Count();
                Opportunity.RunToJobTarget(s, on, 6000);
                var hauledAgain = s.JobsOf(off.Pawn).Skip(firstOn).Any(j => j.Def == "HaulToCell");
                s.Check(hauledAgain, "with the mod turned on again the pawn should haul on the way, without a restart; jobs were: " + Opportunity.Sequence(s, off.Pawn));
                s.Check(item.Destroyed || s.StorageOf(item) == off.Zone.slotGroup, "the steel should end in the stockpile on the route; it is at " + s.CellOf(item));
            });
        }

        static void PathCheckerHauls(ScenarioContext s, string checker) {
            ModSettingsAccess.With(() => {
                ModSettingsAccess.Set("Opportunity_PathChecker", checker);
                s.AreEqual(checker, ModSettingsAccess.Get<object>("Opportunity_PathChecker").ToString(), "selected path checker");
                var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
                Opportunity.RunToJobTarget(s, l);
                Opportunity.CheckHauledThenDidJob(s, l);
                var haul = s.JobsOf(l.Pawn).FirstOrDefault(j => j.Def == "HaulToCell");
                s.Check(haul != null && haul.Reports.Any(r => r.Contains("on the way to")), "the haul should be this mod's Opportunity (report says \"on the way to\")");
            });
        }

        [Scenario("settings-path-checker-vanilla-hauls-on-the-route", MapSize = 80)]
        static void PathCheckerVanilla(ScenarioContext s) => PathCheckerHauls(s, "Vanilla");

        [Scenario("settings-path-checker-default-hauls-on-the-route", MapSize = 80)]
        static void PathCheckerDefault(ScenarioContext s) => PathCheckerHauls(s, "Default");

        [Scenario("settings-path-checker-pathfinding-hauls-on-the-route", MapSize = 80)]
        static void PathCheckerPathfinding(ScenarioContext s) => PathCheckerHauls(s, "Pathfinding");

        static void RangeLimit(ScenarioContext s, float margin, bool expectHaul) {
            ModSettingsAccess.With(() => {
                ModSettingsAccess.Set("Opportunity_PathChecker", "Vanilla");
                var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
                var distance = s.CellOf(l.Pawn).DistanceTo(s.CellOf(l.Item));
                ModSettingsAccess.Set("Opportunity_MaxStartToThing", distance + margin);
                Opportunity.RunToJobTarget(s, l);
                CheckHauled(s, l, expectHaul, "with the start-to-item limit " + (distance + margin) + " and the item " + distance + " away");
            });
        }

        [Scenario("settings-range-limit-just-above-the-item-still-hauls", MapSize = 80)]
        static void RangeLimitJustAbove(ScenarioContext s) => RangeLimit(s, 0.5f, true);

        [Scenario("settings-lowering-a-range-limit-stops-an-item-just-outside-it", MapSize = 80)]
        static void RangeLimitJustBelow(ScenarioContext s) => RangeLimit(s, -0.5f, false);

        [Scenario("settings-lowering-the-total-trip-limit-stops-a-detour", MapSize = 80)]
        static void TotalTripLimit(ScenarioContext s) {
            ModSettingsAccess.With(() => {
                ModSettingsAccess.Set("Opportunity_MaxTotalTripPctOrigTrip", 1.0f);
                var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
                Opportunity.RunToJobTarget(s, l);
                CheckHauled(s, l, false, "with a total-trip limit of 100% of the original trip, a detour off the straight line");
            });
        }

        static void StorageFilterScenario(ScenarioContext s, bool allowShelf) {
            ModSettingsAccess.With(() => {
                ModSettingsAccess.Set("Opportunity_AutoBuildings", false);
                var filter = SettingsField("opportunityBuildingFilter");
                filter.GetType().GetMethod("SetAllow", new[] { typeof(ThingDef), typeof(bool) }).Invoke(filter, new object[] { ScenarioContext.Def("Shelf"), allowShelf });
                var pawn  = Opportunity.SpawnWorker(s, s.Cell(-15, 0));
                var item  = s.SpawnItem("Steel", 15, s.Cell(-5, 3));
                var shelf = s.Shelf(s.Cell(8, 0), "Steel");
                var job   = Opportunity.DesignateDeconstruct(s, s.Cell(25, 0));
                var l = new Opportunity.OnRouteLayout { Pawn = pawn, Item = item, JobTarget = job };
                Opportunity.RunToJobTarget(s, l);
                CheckHauled(s, l, allowShelf, allowShelf ? "with the shelf's mod allowed in the storage-building filter" : "with the shelf's mod excluded in the storage-building filter");
                s.Check((s.StorageOf(item) == shelf.slotGroup) == allowShelf || item.Destroyed, "the steel should " + (allowShelf ? "" : "not ") + "be on the shelf; it is at " + s.CellOf(item));
            });
        }

        [Scenario("settings-storage-allowed-by-the-building-filter-is-chosen", MapSize = 80)]
        static void StorageAllowed(ScenarioContext s) => StorageFilterScenario(s, true);

        [Scenario("settings-storage-excluded-by-the-building-filter-is-never-chosen", MapSize = 80)]
        static void StorageExcluded(ScenarioContext s) => StorageFilterScenario(s, false);

        [Scenario("settings-colored-detour-drawing-draws-with-no-error", MapSize = 80)]
        static void ColoredDetourDrawing(ScenarioContext s) {
            var drawFlag = DebugViewSettings.drawOpportunisticJobs;
            try {
                ModSettingsAccess.With(() => {
                    ModSettingsAccess.Set("DrawSpecialHauls", true);
                    ModSettingsAccess.TheMod.WriteSettings();
                    s.Check(DebugViewSettings.drawOpportunisticJobs, "turning on the detour drawing should turn on the game's drawing of opportunistic jobs");
                    var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
                    var started = s.RunUntil(() => Opportunity.IndexOfJob(s, l.Pawn, "HaulToCell") >= 0, 1000);
                    s.Check(started, "the pawn should start the haul; jobs were: " + Opportunity.Sequence(s, l.Pawn));
                    // Kept as reflection: the flashed cells and lines are held only in DebugCellDrawer's private lists, not in the log or the world.
                    var drawer = s.Map.debugDrawer;
                    var lines  = ((System.Collections.ICollection)typeof(DebugCellDrawer).GetField("debugLines", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(drawer)).Count;
                    var cells  = ((System.Collections.ICollection)typeof(DebugCellDrawer).GetField("debugCells", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(drawer)).Count;
                    s.Check(lines >= 4 && cells >= 4, "the detour should be flashed as colored cells and lines; cells " + cells + " lines " + lines);
                    Event.current = new Event { type = EventType.Repaint };
                    drawer.DebugDrawerUpdate();
                    drawer.DebugDrawerOnGUI();
                    Opportunity.RunToJobTarget(s, l);
                    ModSettingsAccess.Set("DrawSpecialHauls", false);
                    ModSettingsAccess.TheMod.WriteSettings();
                });
            } finally {
                DebugViewSettings.drawOpportunisticJobs = drawFlag;
            }
        }
    }
}
