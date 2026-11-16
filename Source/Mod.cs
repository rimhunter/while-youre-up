using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CodeOptimist;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
// ReSharper disable once RedundantUsingDirective
using Debug = System.Diagnostics.Debug;

namespace WhileYoureUp
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    partial class Mod : Verse.Mod
    {
    #region other mods
        static readonly Type HugsType_Dialog_VanillaModSettings = GenTypes.GetTypeInAnyAssembly("HugsLib.Settings.Dialog_VanillaModSettings");
        static readonly bool haveHugs                           = HugsType_Dialog_VanillaModSettings is not null;

        static readonly FieldInfo SettingsCurModField = haveHugs
            ? AccessTools.DeclaredField(HugsType_Dialog_VanillaModSettings, "selectedMod")
            : AccessTools.DeclaredField(typeof(Dialog_ModSettings),         "mod");

        static readonly Type      CsType_CommonSense                = GenTypes.GetTypeInAnyAssembly("CommonSense.CommonSense");
        static readonly Type      CsType_Settings                   = GenTypes.GetTypeInAnyAssembly("CommonSense.Settings");
        static readonly FieldInfo CsField_Settings_HaulingOverBills = AccessTools.DeclaredField(CsType_Settings, "hauling_over_bills");

        static readonly bool haveCommonSense = new List<object> { CsType_CommonSense, CsType_Settings, CsField_Settings_HaulingOverBills }.All(x => x is not null);
    #endregion

        static Verse.Mod mod;
        static Settings  settings; // static reference since `.modSettings` isn't
        static bool      foundConfig;

        // Prefix for our XML keys (language translations); PackageId may change (e.g. "__copy__" suffix).
        const           string  modId   = "CodeOptimist.WhileYoureUp";
        static readonly Harmony harmony = new(modId); // just a unique id

        public Mod(ModContentPack content) : base(content) {
            mod           = this;  // static reference to mod for e.g. mod name in log messages
            Gui.keyPrefix = modId; // setup for CodeOptimist library

            settings = GetSettings<Settings>(); // read settings from file
            if (!foundConfig) {
                settings.ExposeData(); // initialize to defaults
                ImportOriginalSettings();
            }

            ReportPuahStatus();
            harmony.PatchAll();
        }

        // name in "Mod options" and top of settings window
        public override string SettingsCategory() => mod.Content.Name;

        [HarmonyPatch(typeof(JobUtility), nameof(JobUtility.TryStartErrorRecoverJob))]
        static class JobUtility__TryStartErrorRecoverJob_Patch
        {
            [HarmonyPrefix]
            static void OfferSupport(Pawn pawn) {
                if (RealTime.frameCount == BaseDetour.lastFrameCount && pawn == BaseDetour.lastPawn) {
                    Log.Warning(
                        $"[{mod.Content.Name}] You're welcome to 'Share logs' to my Discord: https://discord.gg/pnZGQAN \n" +
                        $"[{mod.Content.Name}] Below \"10 jobs in one tick\" error occurred during {BaseDetour.lastCallerName}, but could be from several mods.");
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool AmBleeding(Pawn pawn) => pawn.health.hediffSet.BleedRateTotal > 0.001f;
    }
}

// todo eliminate this with next expansion after BioTech i.e. international RimWorld mod error day
// Keeps original settings filename to suppress:
//  `Could not find class JobsOfOpportunity.Mod+Settings while resolving node ModSettings. Trying to use WhileYoureUp.Mod+Settings instead.`
namespace JobsOfOpportunity
{
    class Mod
    {
        class Settings : WhileYoureUp.Mod.Settings
        {
        }
    }
}
