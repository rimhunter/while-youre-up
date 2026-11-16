using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class SettingsImport
    {
        const string PackageId = "rimhunter.whileyoureup";
        const string OldFile   = "Mod_2034960453_Mod.xml";
        const string UnrelatedFile = "Mod_SomeOtherMod_Mod.xml";

        static Verse.Mod TheMod() => LoadedModManager.ModHandles.First(m => m.Content.PackageId == PackageId);

        static string OwnFilePath(Verse.Mod mod) =>
            Path.Combine(GenFilePaths.ConfigFolderPath, GenText.SanitizeFilename($"Mod_{mod.Content.FolderName}_{mod.GetType().Name}.xml"));

        sealed class Held
        {
            public Dictionary<string, string> Values;
            public string                     Xml;
            public string Get(string name, string whenDefault) => Values.TryGetValue(name, out var v) ? v : whenDefault;
        }

        static Held HeldSettings()
        {
            var mod  = TheMod();
            var path = OwnFilePath(mod);
            mod.WriteSettings();
            var xml = File.ReadAllText(path);
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var values = new Dictionary<string, string>();
            foreach (XmlNode n in doc.DocumentElement["ModSettings"].ChildNodes)
                if (n is XmlElement e) values[e.Name] = e.InnerText.Trim();
            return new Held { Values = values, Xml = xml };
        }

        static void CheckOriginalValues(ScenarioContext s, Held h)
        {
            s.AreEqual("False", h.Get("Enabled", "True"), "Enabled");
            s.AreEqual("True", h.Get("DrawSpecialHauls", "False"), "DrawSpecialHauls");
            s.AreEqual("Pathfinding", h.Get("Opportunity_PathChecker", "Default"), "Opportunity_PathChecker");
            s.AreEqual("False", h.Get("Opportunity_ToStockpiles", "True"), "Opportunity_ToStockpiles");
            s.AreEqual("45", h.Get("Opportunity_MaxStartToThing", "30"), "Opportunity_MaxStartToThing");
            s.AreEqual("0.75", h.Get("Opportunity_MaxStartToThingPctOrigTrip", "0.5"), "Opportunity_MaxStartToThingPctOrigTrip");
            s.AreEqual("2.25", h.Get("Opportunity_MaxTotalTripPctOrigTrip", "1.7"), "Opportunity_MaxTotalTripPctOrigTrip");
            s.AreEqual("40", h.Get("Opportunity_MaxStoreToJobRegionLookCount", "25"), "Opportunity_MaxStoreToJobRegionLookCount");
            s.AreEqual("False", h.Get("HaulBeforeCarry_Bills", "True"), "HaulBeforeCarry_Bills");
            s.AreEqual("False", h.Get("HaulBeforeCarry_AutoBuildings", "True"), "HaulBeforeCarry_AutoBuildings");
            s.AreEqual("50", h.Get("Opportunity_MaxStoreToJob", "50"), "Opportunity_MaxStoreToJob (not in the old file, stays default)");
            s.AreEqual("True", h.Get("UsePickUpAndHaulPlus", "True"), "UsePickUpAndHaulPlus (not in the old file, stays default)");
            s.Check(!h.Xml.Contains("FutureSetting"), "a value with no matching setting should not be carried over");
        }

        static void CheckAllDefaults(ScenarioContext s, Held h)
        {
            var nonDefault = h.Values.Where(kv => kv.Key != "hbcBuildingFilter" && kv.Key != "opportunityBuildingFilter").Select(kv => kv.Key + "=" + kv.Value).ToList();
            s.Check(nonDefault.Count == 0, "no value should have been imported; non-default values held: " + string.Join(", ", nonDefault));
        }

        static void CheckNoPlayerLogErrors(ScenarioContext s) => s.RequireNoPlayerLogErrors();

        [Scenario("settings-import-copies-original-values", SelfTest = true)]
        static void CopiesOriginalValues(ScenarioContext s)
        {
            s.Check(File.Exists(OwnFilePath(TheMod())), "the import should have been recorded by writing the mod's own settings file at startup");
            CheckOriginalValues(s, HeldSettings());
            CheckNoPlayerLogErrors(s);
        }

        [Scenario("settings-import-from-local-copy", SelfTest = true)]
        static void FromLocalCopy(ScenarioContext s)
        {
            var h = HeldSettings();
            s.AreEqual("Vanilla", h.Get("Opportunity_PathChecker", "Default"), "Opportunity_PathChecker");
            s.AreEqual("12", h.Get("Opportunity_MaxStartToThing", "30"), "Opportunity_MaxStartToThing");
            s.AreEqual("False", h.Get("HaulBeforeCarry_Supplies", "True"), "HaulBeforeCarry_Supplies");
            CheckNoPlayerLogErrors(s);
        }

        [Scenario("settings-import-skips-an-unrelated-unreadable-file", SelfTest = true)]
        static void SkipsAnUnrelatedUnreadableFile(ScenarioContext s)
        {
            var unrelated = Path.Combine(GenFilePaths.ConfigFolderPath, UnrelatedFile);
            s.Check(File.Exists(unrelated), "the launch should have seeded " + UnrelatedFile);
            var readable = true;
            try { File.ReadAllText(unrelated); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { readable = false; }
            s.Check(!readable, UnrelatedFile + " should be unreadable in this launch");
            var h = HeldSettings();
            s.AreEqual("Vanilla", h.Get("Opportunity_PathChecker", "Default"), "Opportunity_PathChecker (imported from the readable local copy)");
            s.AreEqual("12", h.Get("Opportunity_MaxStartToThing", "30"), "Opportunity_MaxStartToThing (imported from the readable local copy)");
            CheckNoPlayerLogErrors(s);
        }

        [Scenario("settings-import-prefers-workshop-original", SelfTest = true)]
        static void PrefersWorkshopOriginal(ScenarioContext s)
        {
            CheckOriginalValues(s, HeldSettings());
            CheckNoPlayerLogErrors(s);
        }

        [Scenario("settings-import-never-runs-again", SelfTest = true)]
        static void NeverRunsAgain(ScenarioContext s)
        {
            var h = HeldSettings();
            s.AreEqual("Pathfinding", h.Get("Opportunity_PathChecker", "Default"), "Opportunity_PathChecker (the old file changed since the import)");
            s.AreEqual("45", h.Get("Opportunity_MaxStartToThing", "30"), "Opportunity_MaxStartToThing (the old file changed since the import)");
            s.AreEqual("False", h.Get("HaulBeforeCarry_Bills", "True"), "HaulBeforeCarry_Bills (the old file changed since the import)");
            s.AreEqual("False", h.Get("Enabled", "True"), "Enabled (the old file changed since the import)");
            CheckNoPlayerLogErrors(s);
        }

        [Scenario("settings-import-missing-original-is-silent", SelfTest = true)]
        static void MissingOriginalIsSilent(ScenarioContext s)
        {
            s.Check(!File.Exists(OwnFilePath(TheMod())), "with nothing to import, the mod should not have written settings of its own");
            CheckAllDefaults(s, HeldSettings());
            CheckNoPlayerLogErrors(s);
        }

        [Scenario("settings-import-unreadable-original-is-an-error", SelfTest = true)]
        static void UnreadableOriginalIsAnError(ScenarioContext s)
        {
            s.Check(!File.Exists(OwnFilePath(TheMod())), "a failed import must not be recorded: the mod's own settings file should not exist");
            var errors = s.PlayerLogErrorLines();
            s.Check(errors.Any(l => l.Contains(OldFile)), "Player.log should hold an error naming " + OldFile + "; error lines were: " + string.Join(" || ", errors.Select(l => l.Length > 200 ? l.Substring(0, 200) : l)));
            CheckAllDefaults(s, HeldSettings());
        }
    }
}
