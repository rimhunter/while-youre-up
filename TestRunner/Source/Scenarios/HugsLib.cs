using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class HugsLibScenarios
    {
        const string HugsLibPackageId = "unlimitedhugs.hugslib";

        // HugsLib 12 has no Dialog_VanillaModSettings (the original's patch target); its options-dialog transpiler opens a plain mod's
        // settings through OptionsDialogExtensions.GetModSettingsWindow, the call that replaces the game's own "new Dialog_ModSettings(mod)".
        [Scenario("hugslib-opens-the-mod-settings-with-the-mod-patches-applied-and-no-error", MapSize = 40, Mods = "818773962", WindowedOnUnity = true, OnlyOn = "1.6")]
#if RW_1_6
        static IEnumerator OpensThroughHugsLib(ScenarioContext s) {
#else
        static void OpensThroughHugsLib(ScenarioContext s) {
#endif
            if (!LoadedModManager.RunningModsListForReading.Any(m => m.PackageId == HugsLibPackageId))
                throw new ScenarioFailure("HugsLib is not loaded; launch with --mod 818773962");
            s.RequireNoPlayerLogErrors();

            var hugs = LoadedModManager.RunningModsListForReading.First(m => m.PackageId == HugsLibPackageId);
            var extensions = hugs.assemblies.loadedAssemblies.Select(a => a.GetType("HugsLib.Settings.OptionsDialogExtensions")).FirstOrDefault(t => t != null)
                ?? throw new ScenarioFailure("HugsLib has no HugsLib.Settings.OptionsDialogExtensions");
            var open = extensions.GetMethod("GetModSettingsWindow", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new ScenarioFailure("HugsLib has no OptionsDialogExtensions.GetModSettingsWindow");
            var doModOptions = AccessTools.Method(typeof(Dialog_Options), "DoModOptions", new[] { typeof(Listing_Standard) });
            var transpilers = Harmony.GetPatchInfo(doModOptions)?.Transpilers.Select(p => p.owner).ToList() ?? new System.Collections.Generic.List<string>();
            s.Check(transpilers.Contains("UnlimitedHugs.HugsLib"), "HugsLib's transpiler should be on Dialog_Options.DoModOptions, so the options dialog opens settings through it; transpilers: " + string.Join(", ", transpilers));

            var drawFlag = DebugViewSettings.drawOpportunisticJobs;
            var snapshot = ModSettingsAccess.Snapshot();
            Window window = null;
            try {
                ModSettingsAccess.Set("DrawSpecialHauls", false);
                DebugViewSettings.drawOpportunisticJobs = true;
                window = (Window)open.Invoke(null, new object[] { ModSettingsAccess.TheMod });
                s.Check(window is Dialog_ModSettings, "HugsLib should open this mod's settings in the game's Dialog_ModSettings, opened " + window?.GetType().FullName);
                s.Check(ModSettingsAccess.Get<bool>("DrawSpecialHauls"), "the mod's patch on the settings dialog should copy the game's opportunistic-jobs drawing (on) into its Draw colored path detours setting");
#if RW_1_6
                Find.WindowStack.Add(window);
                UiRecorder.Begin();
                yield return ScenarioContext.Frames(10);
                var texts = UiRecorder.End();
                s.Check(window.IsOpen, "the settings window should stay open");
                s.Check(texts.Any(t => t.Text.Contains(ModSettingsAccess.TheMod.SettingsCategory())), "the settings window should draw the mod's title " + ModSettingsAccess.TheMod.SettingsCategory() + "; drew " + texts.Count + " texts");
#endif
            }
            finally {
                if (window is not null && window.IsOpen) window.Close(doCloseSound: false);
                ModSettingsAccess.Restore(snapshot);
                DebugViewSettings.drawOpportunisticJobs = drawFlag;
            }
        }
    }
}
