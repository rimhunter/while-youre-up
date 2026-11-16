using System.Reflection;
using System.Reflection.Emit;
using CodeOptimist;
using HarmonyLib;
using Xunit;

namespace WhileYoureUp.Tests;

public class ReplaceTypesTests
{
#pragma warning disable 649, 169
    class Original
    {
        public int           shared;
        public List<Original> children;
        public static int    count;
        public Original() { }
        public Original(int capacity) { }
        public void CopyFrom(Original other) { }
        public void OnlyOnOriginal() { }
        public void Mixed() { }
        public static int Unrelated(int x) => x;
    }

    class Twin
    {
        public int        shared;
        public List<Twin> children;
        public Twin() { }
        public void CopyFrom(Twin other) { }
        public static void Mixed() { }
    }

    class Bigger
    {
        public int              shared;
        public List<Bigger>     children;
        public float            onlyOnBigger;
    }

    class Reordered
    {
        public List<Reordered> children;
        public int             shared;
    }
#pragma warning restore 649, 169

    static readonly Dictionary<Type, Type> subs = new() { { typeof(Original), typeof(Twin) } };

    static FieldInfo Field<T>(string name) => typeof(T).GetField(name);

    static MethodBase Method<T>(string name) => typeof(T).GetMethod(name);

    static List<CodeInstruction> Replace(params CodeInstruction[] codes) => TranspilerHelper.ReplaceTypes(codes, subs).ToList();

    [Fact]
    public void Field_of_the_original_type_becomes_the_twins_field_of_the_same_name() {
        var codes = Replace(new CodeInstruction(OpCodes.Ldfld, Field<Original>("shared")), new CodeInstruction(OpCodes.Stfld, Field<Original>("children")));

        Assert.Same(Field<Twin>("shared"),   codes[0].operand);
        Assert.Same(Field<Twin>("children"), codes[1].operand);
    }

    [Fact]
    public void Original_with_an_instance_field_the_twin_lacks_is_refused_with_its_name() {
        var error = Assert.Throws<InvalidOperationException>(() =>
            TranspilerHelper.ReplaceTypes(new[] { new CodeInstruction(OpCodes.Ret) }, new Dictionary<Type, Type> { { typeof(Bigger), typeof(Twin) } }).ToList());

        Assert.Contains("onlyOnBigger", error.Message);
    }

    [Fact]
    public void Twin_declaring_the_same_fields_in_another_order_is_refused() {
        var error = Assert.Throws<InvalidOperationException>(() =>
            TranspilerHelper.ReplaceTypes(new[] { new CodeInstruction(OpCodes.Ret) }, new Dictionary<Type, Type> { { typeof(Twin), typeof(Reordered) } }).ToList());

        Assert.Contains("children", error.Message);
    }

    [Fact]
    public void Static_field_the_twin_lacks_is_refused_with_its_name() {
        var error = Assert.Throws<InvalidOperationException>(() => Replace(new CodeInstruction(OpCodes.Ldsfld, Field<Original>("count"))));

        Assert.Contains("count", error.Message);
    }

    [Fact]
    public void Method_whose_parameters_name_the_original_type_becomes_the_twins_method() {
        var codes = Replace(new CodeInstruction(OpCodes.Call, Method<Original>("CopyFrom")));

        Assert.Same(Method<Twin>("CopyFrom"), codes[0].operand);
    }

    [Fact]
    public void Method_the_twin_lacks_is_refused_with_its_name() {
        var error = Assert.Throws<InvalidOperationException>(() => Replace(new CodeInstruction(OpCodes.Call, Method<Original>("OnlyOnOriginal"))));

        Assert.Contains("OnlyOnOriginal", error.Message);
    }

    [Fact]
    public void Method_the_twin_has_with_another_static_flag_is_refused() {
        var error = Assert.Throws<InvalidOperationException>(() => Replace(new CodeInstruction(OpCodes.Call, Method<Original>("Mixed"))));

        Assert.Contains("Mixed", error.Message);
    }

    [Fact]
    public void Method_the_twin_lacks_stays_a_call_to_the_original_only_when_it_is_in_the_allowlist() {
        var keep  = new HashSet<string> { TranspilerHelper.MethodKey(Method<Original>("OnlyOnOriginal")), TranspilerHelper.MethodKey(Method<Original>("Unrelated")) };
        var codes = TranspilerHelper.ReplaceTypes(
            new[] { new CodeInstruction(OpCodes.Call, Method<Original>("OnlyOnOriginal")), new CodeInstruction(OpCodes.Call, Method<Original>("Unrelated")) }, subs, keep).ToList();

        Assert.Same(Method<Original>("OnlyOnOriginal"), codes[0].operand);
        Assert.Same(Method<Original>("Unrelated"),      codes[1].operand);
    }

    static int WithLocal() {
        int Inner() => 1;
        return Inner();
    }

    [Fact]
    public void Key_of_a_compiler_generated_local_function_ignores_its_per_build_number() {
        var local = typeof(ReplaceTypesTests).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Single(x => x.Name.Contains("g__Inner|"));

        Assert.EndsWith("<WithLocal>g__Inner", TranspilerHelper.MethodKey(local));
    }

    [Fact]
    public void Method_outside_the_allowlist_is_still_refused_when_an_allowlist_is_given() {
        var keep = new HashSet<string> { TranspilerHelper.MethodKey(Method<Original>("Unrelated")) };

        Assert.Throws<InvalidOperationException>(() =>
            TranspilerHelper.ReplaceTypes(new[] { new CodeInstruction(OpCodes.Call, Method<Original>("OnlyOnOriginal")) }, subs, keep).ToList());
    }

    [Fact]
    public void Constructor_of_the_original_type_becomes_the_twins_constructor() {
        var codes = Replace(new CodeInstruction(OpCodes.Newobj, typeof(Original).GetConstructor(Type.EmptyTypes)));

        Assert.Same(typeof(Twin).GetConstructor(Type.EmptyTypes), codes[0].operand);
    }

    [Fact]
    public void Constructor_the_twin_lacks_is_refused() {
        var error = Assert.Throws<InvalidOperationException>(() => Replace(new CodeInstruction(OpCodes.Newobj, typeof(Original).GetConstructor(new[] { typeof(int) }))));

        Assert.Contains("constructor", error.Message);
    }
}
