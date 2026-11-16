using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class ScreenshotCheck
    {
        const int MaxSeconds = 150;

        static bool Says(ScenarioContext s, Pawn pawn, string words) => (s.JobReport(pawn) ?? "").Contains(words);

        [Scenario("screenshot-pawn-picks-up-an-item-on-the-way-to-its-job", MapSize = 80, Windowed = true, MaxSeconds = MaxSeconds)]
        static IEnumerator OnTheWayTo(ScenarioContext s) {
            var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            var carrying = s.RunUntil(() => s.IsCarriedBy(l.Item, l.Pawn) && Says(s, l.Pawn, "on the way to"), 3000);
            s.Check(carrying, "the pawn should be carrying the steel with a job text that says \"on the way to\"; jobs were: " + Opportunity.Sequence(s, l.Pawn));
            yield return ShowSelected(s, l.Pawn, s.Cell(5, 0), 50, "01-on-the-way-to", "on the way to");
        }

        static IEnumerator CloserTo(ScenarioContext s, string itemDef, string file, System.Action<ScenarioContext> placeJob) {
            var pawn = s.SpawnColonist(s.Cell(0, 0));
            s.Stockpile(CellRect.CenteredOn(s.Cell(-20, 0), 1), itemDef);
            s.Stockpile(CellRect.CenteredOn(s.Cell(9, 0), 1), itemDef);
            s.SpawnItem(itemDef, 75, s.Cell(-20, 0));
            placeJob(s);
            var carrying = s.RunUntil(() => pawn.carryTracker.CarriedThing?.def.defName == itemDef && Says(s, pawn, "closer to"), 4000);
            s.Check(carrying, "the pawn should be carrying " + itemDef + " with a job text that says \"closer to\"; jobs were: " + Opportunity.Sequence(s, pawn));
            yield return ShowSelected(s, pawn, s.Cell(-2, 0), 46, file, "closer to");
        }

        [Scenario("screenshot-builder-moves-supplies-closer-to-the-blueprint", MapSize = 60, Windowed = true, MaxSeconds = MaxSeconds)]
        static IEnumerator BuilderCloserTo(ScenarioContext s) =>
            CloserTo(s, "Steel", "02-builder-closer-to", c => c.PlaceBlueprint("Wall", c.Cell(16, 0), "Steel"));

        [Scenario("screenshot-crafter-moves-ingredients-closer-to-the-workbench", MapSize = 60, Windowed = true, MaxSeconds = MaxSeconds)]
        static IEnumerator CrafterCloserTo(ScenarioContext s) =>
            CloserTo(s, "Cloth", "03-crafter-closer-to", c => c.AddBill((Building_WorkTable)c.SpawnBuilding("HandTailoringBench", c.Cell(15, 0)), "Make_Apparel_Pants", 1));

        static bool Green((byte r, byte g, byte b) c) => c.g > 60 && c.g > 2 * c.r && c.g > 2 * c.b;
        static bool Red((byte r, byte g, byte b) c) => c.r > 60 && c.r > 2 * c.g && c.r > 2 * c.b;

        static readonly FieldInfo DebugLines = typeof(DebugCellDrawer).GetField("debugLines", BindingFlags.Instance | BindingFlags.NonPublic);

        // Kept as reflection: the flashed lines are held only in DebugCellDrawer's private list; the pixels are checked against them.
        static List<(Vector3 a, Vector3 b, string color)> FlashedLines(ScenarioContext s) {
            var list = new List<(Vector3, Vector3, string)>();
            foreach (var line in (IEnumerable)DebugLines.GetValue(s.Map.debugDrawer)) {
                var t = line.GetType();
                list.Add(((Vector3)t.GetField("a").GetValue(line), (Vector3)t.GetField("b").GetValue(line),
                    t.GetField("color", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(line).ToString()));
            }
            return list;
        }

        static bool Joins((Vector3 a, Vector3 b, string color) line, IntVec3 from, IntVec3 to) =>
            line.color == "Green" && line.a.ToIntVec3() == from && line.b.ToIntVec3() == to;

        [Scenario("screenshot-colored-detour-drawing-shows-start-item-storage-job", MapSize = 80, Windowed = true, MaxSeconds = MaxSeconds)]
        static IEnumerator DetourDrawing(ScenarioContext s) {
            var drawFlag = DebugViewSettings.drawOpportunisticJobs;
            var snapshot = Scenarios.ModSettingsAccess.Snapshot();
            try {
                Scenarios.ModSettingsAccess.Set("DrawSpecialHauls", true);
                Scenarios.ModSettingsAccess.TheMod.WriteSettings();
                var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
                var start  = s.CellOf(l.Pawn);
                var item   = s.CellOf(l.Item);
                var job    = l.JobTarget.Position;
                var hauled = s.RunUntil(() => Opportunity.IndexOfJob(s, l.Pawn, "HaulToCell") >= 0, 1000);
                s.Check(hauled, "the pawn should start the haul on the way; jobs were: " + Opportunity.Sequence(s, l.Pawn));
                var lines = FlashedLines(s);
                var toItem  = lines.FirstOrDefault(x => Joins(x, start, item));
                var toStore = lines.FirstOrDefault(x => x.color == "Green" && x.a.ToIntVec3() == item && l.Zone.Cells.Contains(x.b.ToIntVec3()));
                var toJob   = lines.FirstOrDefault(x => x.color == "Green" && x.b.ToIntVec3() == job && l.Zone.Cells.Contains(x.a.ToIntVec3()));
                var direct  = lines.FirstOrDefault(x => x.color == "Red" && x.a.ToIntVec3() == start && x.b.ToIntVec3() == job);
                var drawn = string.Join(", ", lines.Select(x => x.color + " " + x.a.ToIntVec3() + "->" + x.b.ToIntVec3()).Distinct());
                s.Check(toItem.color != null && toStore.color != null && toJob.color != null, "the detour should be drawn green start " + start + " -> item " + item + " -> storage -> job " + job + "; drawn: " + drawn);
                s.Check(direct.color != null, "the original trip should be drawn red start -> job; drawn: " + drawn);
                if (toItem.color == null || toStore.color == null || toJob.color == null || direct.color == null) yield break;

                Find.Selector.ClearSelection();
                s.LookAt(s.Cell(5, 1), 48);
                var shot = s.Screenshot("04-colored-detour-drawing");
                yield return shot;
                foreach (var (name, line, color, t0, t1) in new[] {
                             ("green start -> item", toItem, (Func<(byte, byte, byte), bool>)Green, 0.25f, 0.75f),
                             ("green item -> storage", toStore, Green, 0.25f, 0.75f),
                             ("green storage -> job", toJob, Green, 0.25f, 0.75f),
                             ("red start -> job, before the storage", direct, Red, 0.1f, 0.45f),
                         }) {
                    var a = s.ScreenPixelOf(line.a);
                    var b = s.ScreenPixelOf(line.b);
                    var onLine = shot.FractionAlong(a, b, color, t0, t1);
                    var offset = new Vector2(0, (s.ScreenPixelOf(IntVec3.Zero) - s.ScreenPixelOf(new IntVec3(0, 0, 4))).magnitude);
                    var beside = shot.FractionAlong(a + offset, b + offset, color, t0, t1);
                    s.Check(onLine >= 0.8f, $"the {name} line should be drawn on screen from {a} to {b}: only {onLine:P0} of the samples show its color");
                    s.Check(beside <= 0.2f, $"the color test for the {name} line should not match the plain map 4 cells below it, matched {beside:P0}");
                }
            }
            finally {
                Scenarios.ModSettingsAccess.Restore(snapshot);
                Scenarios.ModSettingsAccess.TheMod.WriteSettings();
                DebugViewSettings.drawOpportunisticJobs = drawFlag;
            }
        }

        static readonly System.Text.RegularExpressions.Regex RawKey = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9]+(\.[A-Za-z0-9]+)*_[A-Za-z0-9_]+$|WhileYoureUp_");

        static Rect ScreenRectOf(Window w) {
            var r = w.windowRect;
            return new Rect(r.x * Prefs.UIScale, r.y * Prefs.UIScale, r.width * Prefs.UIScale, r.height * Prefs.UIScale);
        }

        static bool Inside(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1 && inner.yMin >= outer.yMin - 1 && inner.xMax <= outer.xMax + 1 && inner.yMax <= outer.yMax + 1;

        static void CheckReadable(ScenarioContext s, ScenarioContext.Shot shot, Window window, string what) {
            var area  = ScreenRectOf(window);
            var texts = shot.Texts.Where(t => Inside(area, t.Box)).ToList();
            s.Check(texts.Count > 5, what + " should show its text; " + texts.Count + " texts were drawn inside it");
            foreach (var t in texts.Where(t => RawKey.IsMatch(t.Text.Trim())))
                s.Fail(what + " shows a raw translation key: " + t);
            var top = texts.Where(t => !t.InScrollView).ToList();
            for (var i = 0; i < top.Count; i++) {
                if (top[i].Overflows) s.Fail(what + ": text does not fit its box and is cut or spills over: " + top[i] + " in box " + top[i].Box);
                for (var j = i + 1; j < top.Count; j++)
                    if (top[i].Ink.Overlaps(top[j].Ink) && top[i].Text != top[j].Text)
                        s.Fail(what + ": two texts overlap: " + top[i] + " and " + top[j]);
            }
        }

        [Scenario("screenshot-settings-window-shows-the-license-line-beside-the-restore-button", MapSize = 40, Windowed = true, MaxSeconds = MaxSeconds)]
        static IEnumerator SettingsWindow(ScenarioContext s) {
            var dialog = new Dialog_ModSettings(ModSettingsAccess.TheMod);
            Find.WindowStack.Add(dialog);
            var shot = s.Screenshot("06-settings-window");
            yield return shot;
            s.Check(dialog.IsOpen, "the settings window should be open");
            var notice  = shot.Texts.FirstOrDefault(t => t.Text == "CodeOptimist.WhileYoureUp_License_Notice".Translate().Resolve());
            var restore = shot.Texts.FirstOrDefault(t => t.Text == "RestoreToDefaultSettings".Translate().Resolve());
            s.Check(notice != null, "the settings window should show the license line; it showed: " + shot.TextsMatching("license"));
            s.Check(restore != null, "the settings window should show the restore button; it showed: " + shot.TextsMatching("default"));
            if (notice != null && restore != null) {
                var beside = notice.Ink.xMin >= restore.Box.xMax && notice.Ink.center.y > restore.Box.yMin && notice.Ink.center.y < restore.Box.yMax;
                s.Check(beside, "the license line " + notice + " should sit to the right of the restore button " + restore.Box + ", on the same row");
            }
            CheckReadable(s, shot, dialog, "the settings window");
            dialog.Close(doCloseSound: false);
        }

        static IEnumerator SettingsCheckbox(ScenarioContext s, bool enabled, string file) {
            ModSettingsAccess.Set("Enabled", enabled);
            var dialog = new Dialog_ModSettings(ModSettingsAccess.TheMod);
            Find.WindowStack.Add(dialog);
            var shot = s.Screenshot(file);
            yield return shot;
            var label = shot.Texts.FirstOrDefault(t => t.Text == "CodeOptimist.WhileYoureUp_Label_Enabled".Translate().Resolve());
            s.Check(label != null, "the settings window should show the Enabled checkbox; it showed: " + shot.TextsMatching("enabled"));
            if (label != null) {
                var size = 24f * Prefs.UIScale;
                var box  = new Rect(label.Box.xMax, label.Box.center.y - size / 2, size, size);
                int green = shot.CountIn(box, Green), red = shot.CountIn(box, Red);
                var shows = enabled ? green >= 30 && green > 3 * red : red >= 30 && red > 3 * green;
                s.Check(shows, $"the Enabled checkbox at {box} should show {(enabled ? "a green check" : "a red cross")}: {green} green and {red} red pixels");
            }
            dialog.Close(doCloseSound: false);
            yield return null;
        }

        [Scenario("screenshot-turning-the-mod-off-and-on-in-settings-takes-effect-mid-game", MapSize = 100, Windowed = true, MaxSeconds = MaxSeconds)]
        static IEnumerator OffAndOn(ScenarioContext s) {
            var snapshot = ModSettingsAccess.Snapshot();
            try {
                yield return SettingsCheckbox(s, false, "05a-settings-mod-off");
                var off = Opportunity.SetupOnRoute(s, pawnX: -45, itemX: -5, itemZ: 2, zoneX: 20, jobX: 45);
                var past = s.RunUntil(() => s.CellOf(off.Pawn).x > s.CellOf(off.Item).x + 3, 4000);
                s.Check(past, "with the mod off the pawn should walk on past the steel; jobs were: " + Opportunity.Sequence(s, off.Pawn));
                s.Check(!s.IsCarriedBy(off.Item, off.Pawn) && Opportunity.IndexOfJob(s, off.Pawn, "HaulToCell") < 0, "with the mod off the pawn should not haul the steel (it is beyond the game's own 30 cell limit); jobs were: " + Opportunity.Sequence(s, off.Pawn));
                s.Check(!Says(s, off.Pawn, "on the way to"), "with the mod off the job text should not say \"on the way to\", was: " + s.JobReport(off.Pawn));
                yield return ShowSelected(s, off.Pawn, s.Cell(0, 0), 60, "05b-mod-off-pawn-walks-past-the-item", "Deconstructing");
                Opportunity.RunToJobTarget(s, off, 6000);
                off.Item.Destroy();

                yield return SettingsCheckbox(s, true, "05c-settings-mod-on");
                off.Pawn.Position = s.Cell(-45, 0);
                off.Pawn.Notify_Teleported(true, true);
                var item = s.SpawnItem("Steel", 15, s.Cell(-5, 2));
                Opportunity.DesignateDeconstruct(s, s.Cell(45, 0));
                var carrying = s.RunUntil(() => s.IsCarriedBy(item, off.Pawn) && Says(s, off.Pawn, "on the way to"), 6000);
                s.Check(carrying, "with the mod turned on again, without a restart, the pawn should carry the steel on the way to its job; jobs were: " + Opportunity.Sequence(s, off.Pawn));
                yield return ShowSelected(s, off.Pawn, s.Cell(0, 0), 60, "05d-mod-on-pawn-hauls-on-the-way", "on the way to");
            }
            finally {
                ModSettingsAccess.Restore(snapshot);
            }
        }

        const string OriginalPackageId = "codeoptimist.jobsofopportunity";

        [Scenario("screenshot-mod-list-warns-when-the-original-while-youre-up-is-also-enabled", MapSize = 40, Windowed = true, MaxSeconds = MaxSeconds, StageMods = "2034960453")]
        static IEnumerator ModListWarning(ScenarioContext s) {
            var page = new Page_ModsConfig();
            Find.WindowStack.Add(page);
            yield return null;
            var original = ModLister.GetModWithIdentifier(OriginalPackageId, ignorePostfix: true);
            if (original == null) throw new ScenarioFailure("the original While You're Up (Workshop 2034960453) is not in the Mods folder; launch with --stage-mod 2034960453");
            var ours = ModLister.GetModWithIdentifier(ModSettingsAccess.TheMod.Content.PackageId, ignorePostfix: true);
            s.Check(!original.Active, "the original should start inactive");
            try {
                ModsConfig.SetActive(original, true);
                page.SelectMod(ours);
                var shot = s.Screenshot("07-mod-list-incompatibility-warning");
                yield return shot;
                var warnings = ModsConfig.GetModWarnings();
                warnings.TryGetValue(ours.PackageId, out var ourWarning);
                s.Check((ourWarning ?? "").Contains(original.Name), "the game's warning for this mod should name the original, was: " + ourWarning);
                var incompatible = "ModIncompatibleWith".Translate("").Resolve().Trim();
                s.Check(shot.ShowsText(incompatible), "the mod list should show \"" + incompatible + "\" for this mod; it showed: " + shot.TextsMatching("incompatible"));
                var shortName = original.Name.Substring(0, 20);
                s.Check(shot.Texts.Count(t => t.Text.StartsWith(shortName)) >= 2, "the mod list should show the original (" + original.Name + ") among the enabled mods and in the incompatibility row; it showed: " + shot.TextsMatching(shortName));
                var ourRow = shot.Texts.Where(t => t.Text == ours.Name).OrderBy(t => t.Ink.y).FirstOrDefault();
                s.Check(ourRow != null, "the enabled list should show " + ours.Name + "; it showed: " + shot.TextsMatching("rimhunter"));
                if (ourRow != null)
                    s.Check(shot.CountIn(ourRow.Ink, Red) >= 30, "the enabled list should draw " + ours.Name + " in red as a mod with an error, at " + ourRow.Ink + ": " + shot.CountIn(ourRow.Ink, Red) + " red pixels");
                CheckReadable(s, shot, page, "the mod list");
            }
            finally {
                ModsConfig.SetActive(original, false);
                page.Close(doCloseSound: false);
            }
            yield return null;
            s.Check(!page.IsOpen, "the mod list should close without saving once the original is turned off again");
        }

        static IEnumerator ShowGear(ScenarioContext s, Pawn pawn, IntVec3 center, float cellsAcross, string file, string[] shown, string[] notShown) {
            s.SelectOnly(pawn);
            s.LookAt(center, cellsAcross);
            yield return null;
            InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Gear));
            var shot = s.Screenshot(file);
            yield return shot;
            var inventory = string.Join(", ", pawn.inventory.innerContainer.Select(t => t.LabelCap));
            s.Check(shot.ShowsText("Inventory".Translate().Resolve()), "the gear tab should be open with an inventory section; it showed: " + shot.TextsMatching("inventory"));
            foreach (var def in shown)
                s.Check(shot.Texts.Any(t => t.InScrollView && t.Text.StartsWith(ScenarioContext.Def(def).LabelCap)), "the gear tab should list " + def + " in the inventory (inventory: " + inventory + "); it showed: " + shot.TextsMatching(def));
            foreach (var def in notShown)
                s.Check(!shot.Texts.Any(t => t.InScrollView && t.Text.StartsWith(ScenarioContext.Def(def).LabelCap)), "the gear tab should no longer list " + def + " (inventory: " + inventory + ")");
        }

        static bool Holds(Pawn pawn, string def) => pawn.inventory.innerContainer.Any(t => t.def.defName == def);

        [Scenario("screenshot-puah-inventory-fills-with-items-on-the-way", MapSize = 100, Windowed = true, MaxSeconds = MaxSeconds, Mods = "puah-continued")]
        static IEnumerator PuahInventoryFills(ScenarioContext s) {
            var pawn = Opportunity.SpawnWorker(s, s.Cell(-15, 0));
            s.SpawnItem("Steel", 10, s.Cell(-8, 3));
            s.SpawnItem("Plasteel", 10, s.Cell(-8, -3));
            s.Stockpile(CellRect.CenteredOn(s.Cell(10, 0), 1), "Steel");
            s.Stockpile(CellRect.CenteredOn(s.Cell(20, 0), 1), "Plasteel");
            Opportunity.DesignateDeconstruct(s, s.Cell(32, 0));
            var filled = s.RunUntil(() => Holds(pawn, "Steel") && Holds(pawn, "Plasteel"), 3000);
            s.Check(filled, "the pawn should carry both the steel and the plasteel in its inventory on the way; jobs were: " + Opportunity.Sequence(s, pawn));
            s.Check(s.JobsOf(pawn).Any(j => j.Def == "HaulToInventory"), "the pawn should use Pick Up And Haul's HaulToInventory; jobs were: " + Opportunity.Sequence(s, pawn));
            yield return ShowGear(s, pawn, s.CellOf(pawn) + new IntVec3(14, 0, 0), 60, "08a-puah-inventory-fills-on-the-way", new[] { "Steel", "Plasteel" }, new string[0]);
        }

        [Scenario("screenshot-puah-unloads-closest-storage-first", MapSize = 100, Windowed = true, MaxSeconds = MaxSeconds, Mods = "puah-continued")]
        static IEnumerator PuahUnloadsClosestFirst(ScenarioContext s) {
            var pawn = s.SpawnColonist(s.Cell(-20, 0));
            var a = s.Stockpile(CellRect.CenteredOn(s.Cell(-8, 0), 1), "Steel", "Plasteel");
            var b = s.Stockpile(CellRect.CenteredOn(s.Cell(12, 0), 1), "Silver");
            s.Stockpile(CellRect.CenteredOn(s.Cell(32, 0), 1), "Gold");
            s.SpawnItem("Gold", 10, s.Cell(-18, 2));
            s.SpawnItem("Silver", 20, s.Cell(-17, -2));
            s.SpawnItem("Plasteel", 10, s.Cell(-18, -1));
            s.SpawnItem("Steel", 10, s.Cell(-17, 1));
            bool StoredIn(Zone_Stockpile z, string def) => z.slotGroup.HeldThings.Any(t => t.def.defName == def);

            var atA = s.RunUntil(() => StoredIn(a, "Steel") && StoredIn(a, "Plasteel") && Holds(pawn, "Silver") && Holds(pawn, "Gold"), 6000);
            s.Check(atA, "the pawn should unload steel and plasteel at the closest storage first, still holding silver and gold; jobs were: " + Opportunity.Sequence(s, pawn));
            s.Check(Opportunity.IndexOfJob(s, pawn, "UnloadYourHauledInventory") >= 0, "the pawn should be unloading with Pick Up And Haul; jobs were: " + Opportunity.Sequence(s, pawn));
            yield return ShowGear(s, pawn, s.Cell(6, 0), 60, "08b-puah-unloads-closest-storage-first", new[] { "Silver", "Gold" }, new[] { "Steel", "Plasteel" });

            var atB = s.RunUntil(() => StoredIn(b, "Silver") && Holds(pawn, "Gold"), 3000);
            s.Check(atB, "the pawn should unload silver at the next closest storage, still holding gold; jobs were: " + Opportunity.Sequence(s, pawn));
            yield return ShowGear(s, pawn, s.Cell(14, 0), 60, "08c-puah-unloads-next-closest-storage", new[] { "Gold" }, new[] { "Steel", "Plasteel", "Silver" });
        }

        static IEnumerator ShowSelected(ScenarioContext s, Pawn pawn, IntVec3 center, float cellsAcross, string file, string words) {
            s.SelectOnly(pawn);
            s.LookAt(center, cellsAcross);
            var shot = s.Screenshot(file);
            yield return shot;
            s.Check(Find.Selector.IsSelected(pawn), "the pawn should be selected in the screenshot");
            s.Check((pawn.DrawPos - pawn.Position.ToVector3Shifted()).MagnitudeHorizontal() < 1f, "the pawn should be drawn where it stands, " + pawn.Position + ", not at " + pawn.DrawPos);
            s.Check(shot.ShowsText(words), "the screen should show the job text \"" + words + "\" in the selected pawn's inspect pane; it showed: " + shot.TextsMatching(pawn.LabelShort));
        }
    }
}
