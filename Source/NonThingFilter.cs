using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CodeOptimist;
using HarmonyLib;
using Patch = CodeOptimist.Patch;
using RimWorld;
using Verse;
// ReSharper disable once RedundantUsingDirective
using Debug = System.Diagnostics.Debug;

// for Harmony patches
// ReSharper disable UnusedType.Local
// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local

namespace WhileYoureUp
{
    [HarmonyPatch]
    [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
    public class NonThingFilter : IExposable
    {
        static readonly Dictionary<Type, Type> _subs = new() {
            { typeof(Listing_TreeThingFilter), typeof(Listing_TreeNonThingFilter) },
            { typeof(ThingFilter), typeof(NonThingFilter) },
        };

        internal static readonly HashSet<string> _keep = new() {
            "Verse.Listing_TreeThingFilter.<DoCategoryChildren>g__HideCategoryDueToSearch",
            "Verse.Listing_TreeThingFilter.<DoCategoryChildren>g__HideThingDueToSearch",
            "Verse.Listing_TreeThingFilter.DoThingDef",
            "Verse.Listing_TreeThingFilter.DoUndiscoveredEntry",
            "Verse.Listing_TreeThingFilter.<ThisOrDescendantsVisibleAndMatchesSearch>g__SpecialFilterVisibleAndMatches",
            "Verse.Listing_TreeThingFilter.<ThisOrDescendantsVisibleAndMatchesSearch>g__ThingDefVisibleAndMatches",
            "Verse.Listing_TreeThingFilter.Visible",
            "Verse.ThingFilter.DisableSpecialFilters",
            "Verse.ThingFilter.get_CaresAboutHitPoints",
            "Verse.ThingFilter.get_OnlySpecialFilters",
            "Verse.ThingFilter.get_RootNode",
        };

        [Unsaved] public Action settingsChangedCallback;

        [Unsaved] public TreeNode_ThingCategory displayRootCategoryInt;

        [Unsaved] public HashSet<ThingDef> allowedDefs = new();

        [Unsaved] public List<SpecialThingFilterDef> disallowedSpecialFilters = new();

        [Unsaved] public List<SpecialThingFilterDef> hiddenSpecialFilters = new();

        public ThingCategoryDef overrideRootDef;
        public bool             onlySpecialFilters;

        public FloatRange   allowedHitPointsPercents     = FloatRange.ZeroToOne;
        public FloatRange   allowedMentalBreakChance     = FloatRange.ZeroToOne;
        public bool         allowedHitPointsConfigurable = true;
        public QualityRange allowedQualities             = QualityRange.All;
        public bool         allowedQualitiesConfigurable = true;

        [MustTranslate] public string customSummary;

        public List<ThingDef> thingDefs;

        [NoTranslate] public List<string> categories;

        [NoTranslate] public List<string> tradeTagsToAllow;

        [NoTranslate] public List<string> tradeTagsToDisallow;

        [NoTranslate] public List<string> thingSetMakerTagsToAllow;

        [NoTranslate] public List<string> thingSetMakerTagsToDisallow;

        [NoTranslate] public List<string> disallowedCategories;

        [NoTranslate] public List<string> specialFiltersToAllow;

        [NoTranslate] public List<string> specialFiltersToDisallow;

        public List<StuffCategoryDef> stuffCategoriesToAllow;
        public List<ThingDef>         allowAllWhoCanMake;
        public FoodPreferability      disallowWorsePreferability;
        public bool                   disallowInedibleByHuman;
        public bool                   disallowDoesntProduceMeat;
        public bool                   disallowMedicalDrugs;
        public bool                   disallowNotEverStorable;
        public Type                   allowWithComp;
        public Type                   disallowWithComp;
        public float                  disallowCheaperThan = float.MinValue;
        public List<ThingDef>         disallowedThingDefs;

        public NonThingFilter() {
        }

        public NonThingFilter(Action settingsChangedCallback) {
            _construct_ThingFilter(this, settingsChangedCallback);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.ExposeData))]
        public virtual void ExposeData() {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), MethodType.Constructor)]
        static void _construct_ThingFilter(object instance, Action settingsChangedCallback) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        public string Summary => _get_Summary(this);

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.Summary), MethodType.Getter)]
        static string _get_Summary(object instance) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        public ThingRequest BestThingRequest => _get_BestThingRequest(this);

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.BestThingRequest), MethodType.Getter)]
        static ThingRequest _get_BestThingRequest(object instance) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }


        public ThingDef AnyAllowedDef => _get_AnyAllowedDef(this);

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.AnyAllowedDef), MethodType.Getter)]
        static ThingDef _get_AnyAllowedDef(object instance) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }


        public IEnumerable<ThingDef> AllowedThingDefs => _get_AllowedThingDefs(this);

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.AllowedThingDefs), MethodType.Getter)]
        static IEnumerable<ThingDef> _get_AllowedThingDefs(object instance) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        public static IEnumerable<ThingDef> AllStorableThingDefs => _get_AllStorableThingDefs();

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.AllStorableThingDefs), MethodType.Getter)]
        static IEnumerable<ThingDef> _get_AllStorableThingDefs() {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        public int AllowedDefCount => _get_AllowedDefCount(this);

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.AllowedDefCount), MethodType.Getter)]
        static int _get_AllowedDefCount(object instance) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        public FloatRange AllowedHitPointsPercents {
            get => _get_AllowedHitPointsPercents(this);
            set => _set_AllowedHitPointsPercents(this, value);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.AllowedHitPointsPercents), MethodType.Getter)]
        static FloatRange _get_AllowedHitPointsPercents(object instance) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.AllowedHitPointsPercents), MethodType.Setter)]
        static void _set_AllowedHitPointsPercents(object instance, FloatRange value) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        public QualityRange AllowedQualityLevels {
            get => _get_AllowedQualityLevels(this);
            set => _set_AllowedQualityLevels(this, value);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.AllowedQualityLevels), MethodType.Getter)]
        static QualityRange _get_AllowedQualityLevels(object instance) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.AllowedQualityLevels), MethodType.Setter)]
        static void _set_AllowedQualityLevels(object instance, QualityRange value) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        public TreeNode_ThingCategory DisplayRootCategory {
            get => _get_DisplayRootCategory(this);
            set => _set_DisplayRootCategory(this, value);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.DisplayRootCategory), MethodType.Getter)]
        static TreeNode_ThingCategory _get_DisplayRootCategory(object instance) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.DisplayRootCategory), MethodType.Setter)]
        static void _set_DisplayRootCategory(object instance, TreeNode_ThingCategory value) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.ResolveReferences))]
        public void ResolveReferences() {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.RecalculateDisplayRootCategory))]
        public void RecalculateDisplayRootCategory() {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.RecalculateSpecialFilterConfigurability))]
        public void RecalculateSpecialFilterConfigurability() {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.IsAlwaysDisallowedDueToSpecialFilters))]
        public bool IsAlwaysDisallowedDueToSpecialFilters(ThingDef def) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.CopyAllowancesFrom))]
        public virtual void CopyAllowancesFrom(NonThingFilter other) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.SetAllow), typeof(ThingDef), typeof(bool))]
        public void SetAllow(ThingDef thingDef, bool allow) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.SetAllow), typeof(SpecialThingFilterDef), typeof(bool))]
        public void SetAllow(SpecialThingFilterDef sfDef, bool allow) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(
            typeof(ThingFilter),      nameof(ThingFilter.SetAllow),
            typeof(ThingCategoryDef), typeof(bool), typeof(IEnumerable<ThingDef>), typeof(IEnumerable<SpecialThingFilterDef>))]
        public void SetAllow(ThingCategoryDef categoryDef,
            bool allow,
            IEnumerable<ThingDef> exceptedDefs = null,
            IEnumerable<SpecialThingFilterDef> exceptedFilters = null) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.SetAllow), typeof(StuffCategoryDef), typeof(bool))]
        public void SetAllow(StuffCategoryDef cat, bool allow) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.SetAllowAllWhoCanMake))]
        public void SetAllowAllWhoCanMake(ThingDef thing) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.SetFromPreset))]
        public void SetFromPreset(StorageSettingsPreset preset) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.SetDisallowAll))]
        public void SetDisallowAll(IEnumerable<ThingDef> exceptedDefs = null,
            IEnumerable<SpecialThingFilterDef> exceptedFilters = null) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.SetAllowAll))]
        public void SetAllowAll(NonThingFilter parentFilter, bool includeNonStorable = false) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.Allows), typeof(Thing))]
        public virtual bool Allows(Thing t) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.Allows), typeof(ThingDef))]
        public bool Allows(ThingDef def) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.Allows), typeof(SpecialThingFilterDef))]
        public bool Allows(SpecialThingFilterDef sf) {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.GetThingRequest))]
        public ThingRequest GetThingRequest() {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.ToString))]
        public override string ToString() {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.CreateOnlyEverStorableThingFilter))]
        public static NonThingFilter CreateOnlyEverStorableThingFilter() {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) => TranspilerHelper.ReplaceTypes(codes, _subs, _keep);
            var _ = Transpiler(null);
            return default;
        }
    }

    public class NonThingFilter_LoadingContext : IDisposable
    {
        static readonly Harmony      harmony = new("CodeOptimist.NonThingFilter");
        static          bool         active;
        static          LoadSaveMode scribeMode;
        readonly        bool         patched;

        public NonThingFilter_LoadingContext() {
            if (!patched) {
                harmony.Patch(
                    AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }),
                    prefix: new HarmonyMethod(typeof(Log__Error_Patch), nameof(Log__Error_Patch.IgnoreCouldNotLoadReference)));
                patched = true;
            }

            scribeMode  = Scribe.mode;
            Scribe.mode = LoadSaveMode.LoadingVars;
            active      = true;
        }

        public void Dispose() {
            active      = false;
            Scribe.mode = scribeMode;
        }

        static class Log__Error_Patch
        {
            // full class path to this method name should be descriptive enough
            public static bool IgnoreCouldNotLoadReference(string text) {
                if (active && text.StartsWith("Could not load reference to "))
                    return Patch.Halt();
                return Patch.Continue();
            }
        }
    }
}
