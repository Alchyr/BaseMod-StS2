using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BaseLib.Extensions;
using BaseLib.Patches.Content;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Badges;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Abstracts;

/// <summary>
/// Due to differences in constructor between main and beta branch, does not actually inherit from Badge directly.
/// CustomBadges should have a no-parameter constructor.
/// </summary>
public abstract class CustomBadge(bool requiresWin, bool multiplayerOnly)// : Badge(run, playerId)
{
    private static ConstructorInfo? MainBranchBadgeConstructor =
        typeof(Badge).GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, [typeof(SerializableRun), typeof(ulong)]);
    
    public static readonly SpireField<Badge, string?> CustomBadgeIconPathDict = new(() => null);
    
    public readonly bool RequiresWin = requiresWin;
    public readonly bool MultiplayerOnly = multiplayerOnly;
    
    public virtual string Id => GetType().GetPrefix() + GetType().Name.ToSnakeCase().ToUpperInvariant();
    public virtual string? CustomBadgeIconPath => null;
    public abstract BadgeRarity Rarity(SerializableRun run, SerializablePlayer player);
    public abstract bool IsObtained(SerializableRun run, SerializablePlayer player);

    public Badge ToRealBadge(SerializableRun run, ulong playerId)
    {
        //AAAHHHHHHH
        Badge result;
        if (MainBranchBadgeConstructor == null)
        {
            result = GeneratedNewBadge(this, run, playerId);
        }
        else
        {
            result = GeneratedOldBadge(this, run, playerId);
        }

        BaseLibMain.Logger.Info($"Setting custom badge path {CustomBadgeIconPath} for badge {result}");
        CustomBadgeIconPathDict[result] = CustomBadgeIconPath;
        return result;
    }

    private static ModuleBuilder? _moduleBuilder = null;

    private static ModuleBuilder ModuleBuilder
    {
        get
        {
            if (_moduleBuilder == null)
            {
                AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("BaseLibBadges"), AssemblyBuilderAccess.Run);
                _moduleBuilder = assemblyBuilder.DefineDynamicModule("GeneratedBadges");
            }

            return _moduleBuilder;
        }
    }

    private static readonly Dictionary<Type, Type> GeneratedBadges = [];
    private static Badge GeneratedOldBadge(CustomBadge baseBadge, SerializableRun run, ulong playerId)
    {
        if (!GeneratedBadges.TryGetValue(baseBadge.GetType(), out var generatedType))
        {
            TypeBuilder tb = ModuleBuilder.DefineType(
                baseBadge.GetType().FullName + ".Generated", 
                TypeAttributes.Public | 
                TypeAttributes.Class | 
                TypeAttributes.AutoClass | 
                TypeAttributes.AnsiClass | 
                TypeAttributes.BeforeFieldInit | 
                TypeAttributes.AutoLayout,
                typeof(Badge));

            var runField = typeof(Badge).Field("_run");
            var playerField = typeof(Badge).Field("_localPlayer");

            var customBadgeRarity = typeof(CustomBadge).Method(nameof(CustomBadge.Rarity));
            var customBadgeObtained = typeof(CustomBadge).Method(nameof(CustomBadge.IsObtained));

            var baseBadgeField = tb.DefineField("baseBadge", typeof(CustomBadge), FieldAttributes.Public);

            var ctrBuilder = tb.DefineConstructor(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, 
                CallingConventions.Standard | CallingConventions.HasThis,
                [typeof(SerializableRun), typeof(ulong), typeof(CustomBadge)]);

            ILGenerator generator = ctrBuilder.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldarg_2);
            generator.Emit(OpCodes.Call, MainBranchBadgeConstructor!);

            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_3);
            generator.Emit(OpCodes.Stfld, baseBadgeField);
            
            generator.Emit(OpCodes.Ret);
            
            
            var getAttributes = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual;

            var propertyGetter = tb.DefineMethod("get_Id", getAttributes, typeof(string), Type.EmptyTypes);
            generator = propertyGetter.GetILGenerator();
            generator.Emit(OpCodes.Ldstr, baseBadge.Id);
            generator.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(propertyGetter, typeof(Badge).Method("get_Id")!);
            
            propertyGetter = tb.DefineMethod("get_RequiresWin", getAttributes, typeof(bool), Type.EmptyTypes);
            generator = propertyGetter.GetILGenerator();
            generator.Emit(baseBadge.RequiresWin ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            generator.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(propertyGetter, typeof(Badge).Method("get_RequiresWin")!);
            
            propertyGetter = tb.DefineMethod("get_MultiplayerOnly", getAttributes, typeof(bool), Type.EmptyTypes);
            generator = propertyGetter.GetILGenerator();
            generator.Emit(baseBadge.MultiplayerOnly ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            generator.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(propertyGetter, typeof(Badge).Method("get_MultiplayerOnly")!);

            propertyGetter = tb.DefineMethod("get_Rarity", getAttributes, typeof(BadgeRarity), Type.EmptyTypes);
            generator = propertyGetter.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, baseBadgeField);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, runField);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, playerField);
            generator.Emit(OpCodes.Callvirt, customBadgeRarity);
            generator.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(propertyGetter, typeof(Badge).Method("get_Rarity")!);

            var obtainedOverride =
                tb.DefineMethod("IsObtained", MethodAttributes.Public | MethodAttributes.Virtual, typeof(bool), Type.EmptyTypes);
            generator = obtainedOverride.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, baseBadgeField);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, runField);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, playerField);
            generator.Emit(OpCodes.Callvirt, customBadgeObtained);
            generator.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(obtainedOverride, typeof(Badge).Method("IsObtained")!);
            
            generatedType = tb.CreateType();
            GeneratedBadges[baseBadge.GetType()] = generatedType;
            BaseLibMain.Logger.Info($"Generated main branch badge type for {baseBadge.Id}");
        }
        return (Badge)Activator.CreateInstance(generatedType, run, playerId, baseBadge)!;
    }
    private static Badge GeneratedNewBadge(CustomBadge baseBadge, SerializableRun run, ulong playerId)
    {
        if (!GeneratedBadges.TryGetValue(baseBadge.GetType(), out var generatedType))
        {
            TypeBuilder tb = ModuleBuilder.DefineType(
                baseBadge.GetType().FullName + ".Generated", 
                TypeAttributes.Public | 
                TypeAttributes.Class | 
                TypeAttributes.AutoClass | 
                TypeAttributes.AnsiClass | 
                TypeAttributes.BeforeFieldInit | 
                TypeAttributes.AutoLayout,
                typeof(DynamicBadge));

            var baseConstructor =
                typeof(DynamicBadge).GetConstructor([typeof(CustomBadge), typeof(SerializableRun), typeof(ulong)]);

            var ctrBuilder = tb.DefineConstructor(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, 
                CallingConventions.Standard | CallingConventions.HasThis,
                [typeof(SerializableRun), typeof(ulong), typeof(CustomBadge)]);

            ILGenerator generator = ctrBuilder.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_3);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldarg_2);
            generator.Emit(OpCodes.Call, baseConstructor);
            generator.Emit(OpCodes.Ret);
            
            generatedType = tb.CreateType();
            GeneratedBadges[baseBadge.GetType()] = generatedType;
            BaseLibMain.Logger.Info($"Generated beta branch badge type for {baseBadge.Id}");
        }
        return (Badge)Activator.CreateInstance(generatedType, run, playerId, baseBadge)!;
    }
}

/// <summary>
/// Not intended for direct use. If making a custom badge inherit from CustomBadge.
/// </summary>
public abstract class DynamicBadge : Badge
{
    private readonly CustomBadge _baseBadge;

    public DynamicBadge(CustomBadge baseBadge, SerializableRun run, ulong playerId) : base(run, playerId, baseBadge.Id, baseBadge.RequiresWin, baseBadge.MultiplayerOnly)
    {
        _baseBadge = baseBadge;
    }

    public override bool IsObtained()
    {
        return _baseBadge.IsObtained(_run, _localPlayer);
    }

    public override BadgeRarity Rarity => _baseBadge.Rarity(_run, _localPlayer);
}

[HarmonyPatch(typeof(BadgePool), nameof(BadgePool.CreateAll))]
class CustomBadgesPatch
{
    [HarmonyPostfix]
    static IReadOnlyCollection<Badge> AddCustomBadges(IReadOnlyCollection<Badge> __result, SerializableRun run, ulong playerId)
    {
        var list = __result.ToList();
        foreach (var type in CustomContentDictionary.CustomBadgeTypes)
        {
            var customBadge = (CustomBadge)Activator.CreateInstance(type)!;
            list.Add(customBadge.ToRealBadge(run, playerId));
        }
        
        return list;
    }
}

[HarmonyPatch(typeof(NBadge), nameof(NBadge.Create), typeof(string), typeof(BadgeRarity))]
class NBadgeCreateStringPatch
{
    [HarmonyTranspiler]
    static List<CodeInstruction> CreateCustomBadge(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                    .ldstr("ui/game_over_screen/badge_"),
                new CallMatcher(typeof(ImageHelper).Method(nameof(ImageHelper.GetImagePath)))
            )
            .Step(-1)
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.Call(typeof(NBadgeCreateStringPatch), nameof(UseCustomBadgeIconPath))
            ]);
    }

    static string UseCustomBadgeIconPath(string origPath, string id)
    {
        var type = CustomContentDictionary.CustomBadgeTypes
            .FirstOrDefault(t => (t.GetPrefix() + t.Name.ToSnakeCase().ToUpperInvariant())
                .Equals(id, StringComparison.OrdinalIgnoreCase));
        if (type == null) return origPath;
        
        var badge = (CustomBadge)RuntimeHelpers.GetUninitializedObject(type);
        if (string.IsNullOrEmpty(badge.CustomBadgeIconPath)) return origPath;

        return badge.CustomBadgeIconPath;
    }
}


[HarmonyPatch(typeof(Badge), nameof(Badge.IconPath), MethodType.Getter)]
class BadgeIconGetterPatch
{
    [HarmonyPrefix]
    static bool CustomPath(Badge __instance, ref string? __result)
    {
        __result = CustomBadge.CustomBadgeIconPathDict[__instance];
        BaseLibMain.Logger.Info($"Got custom badge path {__result} for badge {__instance}");
        return __result == null;
    }
}
