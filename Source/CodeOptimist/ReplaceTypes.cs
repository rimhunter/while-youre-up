using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
// ReSharper disable once RedundantUsingDirective
using Debug = System.Diagnostics.Debug;

namespace CodeOptimist
{
    public static partial class TranspilerHelper
    {
        const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // The twin must declare the original's instance fields in the same order, so any original method still
        // called on it reads the same layout; a mismatch wrote past the twin's end on 1.7 (orum/rimhunter#24).
        public static string MethodKey(MethodBase method) => $"{method.DeclaringType?.FullName}.{System.Text.RegularExpressions.Regex.Replace(method.Name, @"\|\d+_\d+$", "")}";

        // A method the twin lacks must be named in keepOnOriginal; any other missing twin method throws.
        public static IEnumerable<CodeInstruction> ReplaceTypes(IEnumerable<CodeInstruction> codes, Dictionary<Type, Type> subs, ISet<string> keepOnOriginal = null) {
            Debug.Assert(codes != null, "Is the patch being applied? Did you remember [HarmonyPatch] on the class?");
            foreach (var pair in subs)
                RequireSameInstanceFields(pair.Key, pair.Value, subs);
            var list = codes.ToList();
            foreach (var instruction in list) {
                // ReSharper disable once AssignNullToNotNullAttribute
                if (instruction.operand is FieldInfo fieldInfo && subs.TryGetValue(fieldInfo.DeclaringType, out var fieldTo))
                    instruction.operand = TwinField(fieldInfo, fieldTo, subs);
                else if (instruction.operand is MethodBase method && subs.TryGetValue(method.DeclaringType, out var methodTo))
                    instruction.operand = TwinMethod(method, methodTo, subs, keepOnOriginal);
                else if (instruction.operand is Type type)
                    instruction.operand = Substitute(type, subs);
            }
            return list.AsEnumerable();
        }

        static Type Substitute(Type type, Dictionary<Type, Type> subs) {
            if (subs.TryGetValue(type, out var to)) return to;
            if (type.HasElementType) {
                var element = Substitute(type.GetElementType(), subs);
                if (type.IsArray) return type == type.GetElementType().MakeArrayType() ? element.MakeArrayType() : element.MakeArrayType(type.GetArrayRank());
                if (type.IsByRef) return element.MakeByRefType();
                if (type.IsPointer) return element.MakePointerType();
            }
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
                return type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(x => Substitute(x, subs)).ToArray());
            return type;
        }

        static void RequireSameInstanceFields(Type from, Type to, Dictionary<Type, Type> subs) {
            string Describe(Type type, Func<Type, Type> map) => string.Join(", ",
                type.GetFields(declared & ~BindingFlags.Static).Select(x => $"{map(x.FieldType).FullName} {x.Name}"));
            var expected = Describe(from, x => Substitute(x, subs));
            var actual   = Describe(to,   x => x);
            if (expected != actual)
                throw new InvalidOperationException(
                    $"{to.FullName} must declare the instance fields of {from.FullName} in the same order.\nExpected: {expected}\nActual:   {actual}");
        }

        static FieldInfo TwinField(FieldInfo field, Type to, Dictionary<Type, Type> subs) {
            var twin = to.GetField(field.Name, declared);
            var type = Substitute(field.FieldType, subs);
            if (twin is null || twin.FieldType != type || twin.IsStatic != field.IsStatic)
                throw new InvalidOperationException(
                    $"{to.FullName} needs a {(field.IsStatic ? "static " : "")}field `{type.FullName} {field.Name}` to stand in for {field.DeclaringType?.FullName}.{field.Name}");
            return twin;
        }

        static MethodBase TwinMethod(MethodBase method, Type to, Dictionary<Type, Type> subs, ISet<string> keepOnOriginal) {
            var paramTypes   = method.GetParameters().Select(x => x.ParameterType).ToArray();
            var twinParams   = paramTypes.Select(x => Substitute(x, subs)).ToArray();
            var genericTypes = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
            MethodBase twin;
            if (method is ConstructorInfo)
                twin = to.GetConstructor(declared & ~BindingFlags.Static, null, twinParams, null);
            else if (method.IsGenericMethod)
                twin = to.GetMethods(declared)
                    .Where(x => x.Name == method.Name && x.IsGenericMethodDefinition && x.GetGenericArguments().Length == genericTypes.Length)
                    .Select(x => x.MakeGenericMethod(genericTypes.Select(t => Substitute(t, subs)).ToArray()))
                    .FirstOrDefault(x => x.GetParameters().Select(p => p.ParameterType).SequenceEqual(twinParams));
            else
                twin = to.GetMethod(method.Name, declared, null, twinParams, null);
            if (twin is not null && twin.IsStatic == method.IsStatic)
                return twin;
            if (method is ConstructorInfo)
                throw new InvalidOperationException(
                    $"{to.FullName} needs a constructor ({string.Join(", ", twinParams.Select(x => x.FullName))}) to stand in for one of {method.DeclaringType?.FullName}");
            if (keepOnOriginal is not null && keepOnOriginal.Contains(MethodKey(method)))
                return method;
            throw new InvalidOperationException(
                $"{to.FullName} needs a {(method.IsStatic ? "static " : "")}method {method.Name}({string.Join(", ", twinParams.Select(x => x.FullName))}) to stand in for {method.DeclaringType?.FullName}.{method.Name}, " +
                $"or {MethodKey(method)} must be in the keep-on-original list");
        }
    }
}
