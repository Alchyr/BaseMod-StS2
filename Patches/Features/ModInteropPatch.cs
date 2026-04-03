using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BaseLib.Utils.ModInterop;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace BaseLib.Patches.Features;

//Called by PostModInitPatch
internal class ModInterop
{
    private static readonly BindingFlags ValidMemberFlags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    private static readonly FieldInfo WrappedValueField = AccessTools.DeclaredField(typeof(InteropClassWrapper), nameof(InteropClassWrapper.Value));

    private readonly Dictionary<string, Assembly?> _loadedIds;
    internal ModInterop()
    {
        BaseLibMain.Logger.Info("Generating interop methods and properties");
        
        _loadedIds = ModManager.LoadedMods
            .Where(mod => mod.manifest != null && mod.assembly != null)
            .ToDictionary(mod => mod.manifest?.id ?? "", mod => mod.assembly);
    }

    internal void ProcessType(Harmony harmony, Type t)
    {
        var modInterop = t.GetCustomAttribute<ModInteropAttribute>();
        if (modInterop == null) return;

        if (!_loadedIds.TryGetValue(modInterop.ModId, out var assembly)) return;
        if (assembly == null)
        {
            BaseLibMain.Logger.Error($"Cannot generate interop for mod {modInterop.ModId}, assembly not found");
            return;
        }
            
        BaseLibMain.Logger.Info($"Interop type {t} for mod {modInterop.ModId}");

        var members = t.GetMembers(ValidMemberFlags);

        GenInteropMembers(members, harmony, assembly, modInterop.Type, true);
    }

    private static bool GenInteropMembers(MemberInfo[] members, Harmony harmony, Assembly assembly, string? contextTargetType, bool requireStatic)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case PropertyInfo property:
                    if (requireStatic && !(property.SetMethod?.IsStatic ?? true)) continue;
                    if (!GenInteropPropertyOrField(harmony, assembly, contextTargetType, property)) return false;
                    break;
                case MethodInfo method:
                    if (requireStatic && !method.IsStatic) continue;
                    if (method.IsConstructor || method.GetCustomAttribute<CompilerGeneratedAttribute>() != null) continue;
                    if (!GenInteropMethod(harmony, assembly, contextTargetType, method)) return false;
                    break;
                case TypeInfo type:
                    if (!type.IsAssignableTo(typeof(InteropClassWrapper))) continue;

                    if (!GenInteropType(harmony, assembly, contextTargetType, type)) return false;
                    break;
            }
        }

        return true;
    }

    private static bool GenInteropType(Harmony harmony, Assembly targetAssembly, string? contextTargetType, TypeInfo type)
    {
        var constructors = type.GetConstructors();
        if (constructors.Length < 1) throw new Exception($"{type} must have at least one public constructor");

        var targetAttr = type.GetCustomAttribute<InteropTargetAttribute>();
        var targetName = targetAttr?.Type ?? targetAttr?.Name ?? contextTargetType ?? throw new Exception($"No target type provided for Interop type {type}");
        
        try
        {
            var targetType = Type.GetType($"{targetName}, {targetAssembly}") ?? 
                             throw new Exception($"Type {targetName} not found in assembly {targetAssembly}");

            foreach (var constructor in constructors)
            {
                var constructorParams = constructor.GetParameters().Select(p => p.ParameterType).ToArray();
                var constructorMatch = targetType.GetConstructor(constructorParams);
                if (constructorMatch == null)
                    throw new Exception($"Failed to find matching constructor for {constructor.FullDescription()}");

                QuickTranspiler.Insert = [CodeInstruction.LoadArgument(0), //for stfld
                    ..constructorParams.Index().Select(param => CodeInstruction.LoadArgument(param.Index + 1)),
                    new CodeInstruction(OpCodes.Newobj, constructorMatch),
                    new CodeInstruction(OpCodes.Stfld, WrappedValueField)];
                harmony.Patch(constructor, transpiler: new HarmonyMethod(QuickTranspiler.Transpile));
            }

            BaseLibMain.Logger.Info($"Generated interop type {type.FullName}");
            return GenInteropMembers(type.GetMembers(ValidMemberFlags), harmony, targetAssembly, targetName, false);
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Info(e.ToString());
            return false;
        }
    }

    private static bool GenInteropMethod(Harmony harmony, Assembly targetAssembly, string? contextTargetType, MethodInfo method)
    {
        var targetAttr = method.GetCustomAttribute<InteropTargetAttribute>();

        var type = targetAttr?.Type ?? contextTargetType ?? throw new Exception($"Mod interop {method.FullDescription()} does not define target type");
        var methodName = targetAttr?.Name ?? method.Name;

        try
        {
            var targetType = Type.GetType($"{type}, {targetAssembly}") ?? 
                throw new Exception($"Type {type} not found in assembly {targetAssembly}");

            var methodParams = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var nonStaticParams = method.IsStatic ? [..methodParams.Skip(1)] : methodParams;

            MethodInfo? targetMethod = null;
            List<CodeInstruction> loadParams = [];
            foreach (var possibleTarget in targetType.GetDeclaredMethods())
            {
                if (possibleTarget.Name != methodName) continue;
                var targetParams = possibleTarget.GetParameters();
                var checkParams = possibleTarget.IsStatic ? methodParams : nonStaticParams;
                if (!CheckParamMatch(targetParams, checkParams)) continue;
                targetMethod = possibleTarget;

                if (!targetMethod.IsStatic && method.IsStatic)
                {
                    throw new Exception($"Method {method} should not be static to match target {targetMethod}");
                }

                if (targetMethod.ReturnType != typeof(void)) loadParams.Add(new CodeInstruction(OpCodes.Pop));

                int off = 0;
                if (!targetMethod.IsStatic)
                {
                    if (method.IsStatic)
                    {
                        loadParams.Add(CodeInstruction.LoadArgument(0));
                        if (methodParams[0] != targetType)
                        {
                            loadParams.Add(new CodeInstruction(OpCodes.Castclass, targetType));
                        }
                        ++off;
                    }
                    else
                    {
                        loadParams.Add(CodeInstruction.LoadArgument(0)); //this, should be InteropClassWrapper
                        loadParams.Add(new CodeInstruction(OpCodes.Ldfld, WrappedValueField));
                    }
                }

                for (var i = 0; i < targetParams.Length; ++i)
                {
                    loadParams.Add(CodeInstruction.LoadArgument(i + off));
                    if (methodParams[i + off] != targetParams[i].ParameterType)
                    {
                        loadParams.Add(new CodeInstruction(OpCodes.Castclass, targetParams[i].ParameterType));
                    }
                }
                break;
            }

            if (targetMethod == null)
                throw new Exception($"Method {methodName} with matching parameters not found in type {targetType}");
            
            if (targetMethod.ReturnType != method.ReturnType)
                throw new Exception($"Method {methodName} return type {method.ReturnType} does not match target method return type {targetMethod.ReturnType}");

            QuickTranspiler.Insert = [..loadParams, new CodeInstruction(OpCodes.Call, targetMethod)];
            harmony.Patch(method, transpiler: new HarmonyMethod(QuickTranspiler.Transpile));
            BaseLibMain.Logger.Info($"Generated interop method {method.Name}");
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Info(e.ToString());
            return false;
        }

        return true;
    }

    private static bool GenInteropPropertyOrField(Harmony harmony, Assembly targetAssembly, string? contextTargetType, PropertyInfo property)
    {
        var targetAttr = property.GetCustomAttribute<InteropTargetAttribute>();

        var type = targetAttr?.Type ?? contextTargetType ?? throw new Exception($"Mod interop {property} does not define target type");
        var name = targetAttr?.Name ?? property.Name;

        try
        {
            var targetType = Type.GetType($"{type}, {targetAssembly}") ?? 
                throw new Exception($"Type {type} not found in assembly {targetAssembly}");

            var targetProperty = targetType.DeclaredProperty(name);
            if (targetProperty != null && targetProperty.PropertyType == property.PropertyType)
            {
                if (targetProperty.SetMethod == null && targetProperty.GetMethod == null)
                    throw new Exception($"Cannot get or set target property {targetProperty}");
                bool targetStatic = (targetProperty.SetMethod?.IsStatic ?? false) || (targetProperty.GetMethod?.IsStatic ?? false);
                bool sourceStatic = (property.SetMethod?.IsStatic ?? false) || (property.GetMethod?.IsStatic ?? false);
                if (targetStatic && !sourceStatic)
                    throw new Exception($"Target property {targetProperty} is static; interop property must also be static");
                if (sourceStatic && !targetStatic)
                    throw new Exception($"Target property {targetProperty} is not static; interop property should not be static");
                
                if (targetProperty.SetMethod != null)
                {
                    if (property.SetMethod == null)
                        throw new Exception($"Property {property} should have a setter to match target property");
                    
                    if (targetStatic)
                    {
                        QuickTranspiler.Insert = [
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Call, targetProperty.SetMethod)];
                    }
                    else
                    {
                        QuickTranspiler.Insert = [
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Ldfld, WrappedValueField),
                            new CodeInstruction(OpCodes.Ldarg_1),
                            new CodeInstruction(OpCodes.Call, targetProperty.SetMethod)];
                    }
                    harmony.Patch(property.SetMethod, transpiler: new HarmonyMethod(QuickTranspiler.Transpile));
                }

                if (targetProperty.GetMethod != null)
                {
                    if (property.GetMethod == null)
                        throw new Exception($"Property {property} should have a getter to match target property");
                    
                    if (targetStatic)
                    {
                        QuickTranspiler.Insert = [
                            new CodeInstruction(OpCodes.Pop),
                            new CodeInstruction(OpCodes.Call, targetProperty.GetMethod)];
                    }
                    else //Should be in a ClassWrapper.
                    {
                        QuickTranspiler.Insert = [
                            new CodeInstruction(OpCodes.Pop),
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Ldfld, WrappedValueField),
                            new CodeInstruction(OpCodes.Call, targetProperty.GetMethod)];
                    }
                    harmony.Patch(property.GetMethod, transpiler: new HarmonyMethod(QuickTranspiler.Transpile));
                }

                BaseLibMain.Logger.Info($"Generated interop property {property.Name}");
                return true;
            }

            var targetField = targetType.DeclaredField(name);
            if (targetField != null && targetField.FieldType == property.PropertyType)
            {
                if (property.SetMethod == null) throw new Exception($"Interop property {property} should have a setter for field {targetField}");
                if (property.GetMethod == null) throw new Exception($"Interop property {property} should have a getter for field {targetField}");
                
                bool sourceStatic = (property.SetMethod?.IsStatic ?? false) || (property.GetMethod?.IsStatic ?? false);
                if (targetField.IsStatic && !sourceStatic)
                    throw new Exception($"Target field {targetField} is static; interop property must also be static");
                if (sourceStatic && !targetField.IsStatic)
                    throw new Exception($"Target field {targetField} is not static; interop property should not be static");
                
                if (targetField.IsStatic)
                {
                    QuickTranspiler.Insert = [
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Stfld, targetField)];
                }
                else
                {
                    QuickTranspiler.Insert = [
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, WrappedValueField),
                        new CodeInstruction(OpCodes.Ldarg_1),
                        new CodeInstruction(OpCodes.Stfld, targetField)];
                }
                harmony.Patch(property.SetMethod, transpiler: new HarmonyMethod(QuickTranspiler.Transpile));
                
                if (targetField.IsStatic)
                {
                    QuickTranspiler.Insert = [
                        new CodeInstruction(OpCodes.Pop),
                        new CodeInstruction(OpCodes.Ldfld, targetField)];
                }
                else //Should be in a ClassWrapper.
                {
                    QuickTranspiler.Insert = [
                        new CodeInstruction(OpCodes.Pop),
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, WrappedValueField),
                        new CodeInstruction(OpCodes.Ldfld, targetField)];
                }
                harmony.Patch(property.GetMethod, transpiler: new HarmonyMethod(QuickTranspiler.Transpile));
                BaseLibMain.Logger.Info($"Generated interop field property {property.Name}");
                return true;
            }

            throw new Exception($"Could not find property or field for name {name} in type {type}");
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Info(e.ToString());
            return false;
        }
    }

    /// <summary>
    /// Check if checkParams can be passed as targetParams, with object being treated as a wildcard.
    /// </summary>
    /// <param name="targetParams"></param>
    /// <param name="checkParams"></param>
    /// <returns></returns>
    private static bool CheckParamMatch(ParameterInfo[] targetParams, Type[] checkParams)
    {
        if (targetParams.Length != checkParams.Length) return false;
        return !checkParams.Where((t, i) => t != typeof(object) && !t.IsAssignableTo(targetParams[i].ParameterType)).Any();
    }

    private static class QuickTranspiler
    {
        public static List<CodeInstruction> Insert = [];

        public static List<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions) =>
            new InstructionPatcher(instructions)
                .Match(new InstructionMatcher()
                    .ret())
                .Step(-1)
                .Insert(Insert);
    }
}
