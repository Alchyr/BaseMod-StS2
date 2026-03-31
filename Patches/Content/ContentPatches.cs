using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.InitIds))]
public static class CustomContentDictionary
{
    private static readonly Dictionary<Type, int> CustomModelCounts = []; //May log, may just remove.
    private static readonly Dictionary<Type, Type> PoolTypes = [];

    public static readonly List<CustomAncientModel> CustomAncients = [];

    // Track which types were added per-assembly for hot reload cleanup
    private static readonly Dictionary<string, List<Type>> ModelTypesByAssembly = [];

    static CustomContentDictionary()
    {
        PoolTypes.Add(typeof(CardPoolModel), typeof(CardModel));
        PoolTypes.Add(typeof(RelicPoolModel), typeof(RelicModel));
        PoolTypes.Add(typeof(PotionPoolModel), typeof(PotionModel));
    }


    public static void AddModel(Type modelType)
    {
        var poolAttribute = modelType.GetCustomAttribute<PoolAttribute>()
            ?? throw new Exception($"Model {modelType.FullName} must be marked with a PoolAttribute to determine which pool to add it to.");

        if (!IsValidPool(modelType, poolAttribute.PoolType))
        {
            throw new Exception($"Model {modelType.FullName} is assigned to incorrect type of pool {poolAttribute.PoolType.FullName}.");
        }

        int count = CustomModelCounts.GetValueOrDefault(poolAttribute.PoolType, 0);
        CustomModelCounts[poolAttribute.PoolType] = count + 1;

        ModHelper.AddModelToPool(poolAttribute.PoolType, modelType);

        // Track for hot reload cleanup
        var asmName = modelType.Assembly.GetName().Name ?? "";
        if (!ModelTypesByAssembly.TryGetValue(asmName, out var list))
        {
            list = [];
            ModelTypesByAssembly[asmName] = list;
        }
        list.Add(modelType);
    }

    public static void AddAncient(CustomAncientModel ancient)
    {
        int count = CustomModelCounts.GetValueOrDefault(typeof(CustomAncientModel), 0);
        CustomModelCounts[typeof(CustomAncientModel)] = count + 1;
        CustomAncients.Add(ancient);
    }

    /// <summary>
    /// Remove all tracked content from a specific assembly. Used during hot reload
    /// to clean up before re-registration from the new assembly version.
    /// </summary>
    internal static void RemoveByAssembly(Assembly oldAssembly)
    {
        var asmName = oldAssembly.GetName().Name ?? "";

        // Remove tracked model types for this assembly
        if (ModelTypesByAssembly.Remove(asmName, out var oldTypes))
        {
            foreach (var modelType in oldTypes)
            {
                var poolAttr = modelType.GetCustomAttribute<PoolAttribute>();
                if (poolAttr != null && CustomModelCounts.ContainsKey(poolAttr.PoolType))
                {
                    CustomModelCounts[poolAttr.PoolType] = Math.Max(0, CustomModelCounts[poolAttr.PoolType] - 1);
                }
            }
        }

        // Remove ancients from old assembly
        CustomAncients.RemoveAll(a => a.GetType().Assembly == oldAssembly);
    }


    private static bool IsValidPool(Type modelType, Type poolType)
    {
        var basePoolType = poolType.BaseType;
        while (basePoolType != null)
        {
            if (PoolTypes.TryGetValue(basePoolType, out var poolValueType))
            {
                return modelType.IsAssignableTo(poolValueType);
            }
            basePoolType = basePoolType.BaseType;
        }
        throw new Exception($"Model {modelType.FullName} is assigned to {poolType.FullName} which is not a valid pool type.");
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllSharedAncients), MethodType.Getter)]
class CustomAncientExistence
{
    [HarmonyPostfix]
    static IEnumerable<AncientEventModel> AddCustomAncientForCompendium(IEnumerable<AncientEventModel> __result)
    {
        return [.. __result, .. CustomContentDictionary.CustomAncients];
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateRooms))]
public class CurrentGeneratingRunState
{
    public static RunState? State { get; private set; }
    private static readonly MethodInfo StateGetter = AccessTools.PropertyGetter(typeof(RunManager), "State");
    
    [HarmonyPrefix]
    static void GetState(RunManager __instance)
    {
        State = (RunState?) StateGetter.Invoke(__instance, []);
    }

    [HarmonyPostfix]
    static void ClearState()
    {
        State = null;
    }
}
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
class AddCustomAncientsToPool
{
    private static readonly FieldInfo RoomSet = AccessTools.Field(typeof(ActModel), "_rooms");
    
    [HarmonyPrefix]
    static void AddToModelPool(ActModel __instance, List<AncientEventModel>? ____sharedAncientSubset)
    {
        if (____sharedAncientSubset == null) return; //Act 1 or other act with no shared ancients

        //Not a fan of this, but having them in shared ancients rather than all ancients is the easiest way to have them
        //appear in compendium.
        ____sharedAncientSubset.RemoveAll(CustomContentDictionary.CustomAncients.Contains);
        
        List<CustomAncientModel> toAdd = [..CustomContentDictionary.CustomAncients];
        toAdd.Sort((a, b) =>  string.Compare(a.Id.Entry, b.Id.Entry, StringComparison.Ordinal));
        
        toAdd.RemoveAll(ancient => !ancient.IsValidForAct(__instance) || ____sharedAncientSubset.Contains(ancient));
        foreach (var act in CurrentGeneratingRunState.State?.Acts ?? [])
        {
            if (RoomSet.GetValue(act) is not RoomSet { HasAncient: true }) continue;
            if (act == __instance) continue;
            if (act.Ancient is CustomAncientModel customAncient) toAdd.Remove(customAncient);
        }
        ____sharedAncientSubset.AddRange(toAdd);
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllSharedCardPools), MethodType.Getter)]
class ModelDbSharedCardPoolsPatch
{
    private static readonly List<CardPoolModel> CustomSharedPools = [];

    [HarmonyPostfix]
    static IEnumerable<CardPoolModel> AddCustomPools(IEnumerable<CardPoolModel> __result)
    {
        return [.. __result, .. CustomSharedPools];
    }

    public static void Register(CustomCardPoolModel pool)
    {
        CustomSharedPools.Add(pool);
    }

    internal static void RemoveByAssembly(Assembly asm)
    {
        CustomSharedPools.RemoveAll(p => p.GetType().Assembly == asm);
    }
}

[HarmonyPatch(typeof(ModelDb), "AllSharedRelicPools", MethodType.Getter)]
class ModelDbSharedRelicPoolsPatch
{
    private static readonly List<RelicPoolModel> customSharedPools = [];

    [HarmonyPostfix]
    static IEnumerable<RelicPoolModel> AddCustomPools(IEnumerable<RelicPoolModel> __result)
    {
        return [.. __result, .. customSharedPools];
    }

    public static void Register(CustomRelicPoolModel pool)
    {
        customSharedPools.Add(pool);
    }

    internal static void RemoveByAssembly(Assembly asm)
    {
        customSharedPools.RemoveAll(p => p.GetType().Assembly == asm);
    }
}

[HarmonyPatch(typeof(ModelDb), "AllSharedPotionPools", MethodType.Getter)]
class ModelDbSharedPotionPoolsPatch
{
    private static readonly List<PotionPoolModel> customSharedPools = [];

    [HarmonyPostfix]
    static IEnumerable<PotionPoolModel> AddCustomPools(IEnumerable<PotionPoolModel> __result)
    {
        return [.. __result, .. customSharedPools];
    }

    public static void Register(CustomPotionPoolModel pool)
    {
        customSharedPools.Add(pool);
    }

    internal static void RemoveByAssembly(Assembly asm)
    {
        customSharedPools.RemoveAll(p => p.GetType().Assembly == asm);
    }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
class ActModelGenerateRoomsPatch
{
    [HarmonyPostfix]
    static void ForceAncientToSpawn(ActModel __instance)
    {
        var rooms = Traverse.Create(__instance).Field<RoomSet>("_rooms").Value;
        if (!rooms.HasAncient) return;
        
        var rngChosenAncient = rooms.Ancient;
        var ancientToSpawn = CustomContentDictionary.CustomAncients.Find(a => a.ShouldForceSpawn(__instance, rngChosenAncient));

        if (ancientToSpawn != null)
        {
            rooms.Ancient = ancientToSpawn;
        }
    }
}
/*
class CardPoolPatch
{
    internal static void Patch(Harmony harmony)
    {
        Type[] poolTypes = 
            [typeof(IroncladCardPool),
            typeof(SilentCardPool),
            typeof(DefectCardPool),
            typeof(RegentCardPool),
            typeof(NecrobinderCardPool),
            typeof(ColorlessCardPool),
            typeof(TokenCardPool),
            typeof(EventCardPool),
            typeof(QuestCardPool),
            typeof(StatusCardPool),
            typeof(CurseCardPool)
        ]; //CustomCardPoolModel generate method utilizes CustomContentDictionary

        foreach (var poolType in poolTypes)
        {
            var originalMethod = AccessTools.Method(poolType, "GenerateAllCards");
            var postfix = AccessTools.Method(typeof(CardPoolPatch), nameof(AdjustPool));
            harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfix));
        }
    }

    static CardModel[] AdjustPool(CardModel[] __result, CardPoolModel __instance)
    {
        if (CustomContentDictionary.Cards.TryGetValue(__instance.GetType(), out var cards))
        {
            return [.. __result, .. cards];
        }
        return __result;
    }
}
class RelicPoolPatch
{
    internal static void Patch(Harmony harmony)
    {
        Type[] poolTypes =
            [typeof(SharedRelicPool),
            typeof(IroncladRelicPool),
            typeof(SilentRelicPool),
            typeof(DefectRelicPool),
            typeof(RegentRelicPool),
            typeof(NecrobinderRelicPool),
            typeof(EventRelicPool)
        ]; //CustomRelicPoolModel generate method utilizes CustomContentDictionary

        foreach (var poolType in poolTypes)
        {
            var originalMethod = AccessTools.Method(poolType, "GenerateAllRelics");
            var postfix = AccessTools.Method(typeof(RelicPoolPatch), nameof(AdjustPool));
            harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfix));
        }
    }

    static IEnumerable<RelicModel> AdjustPool(IEnumerable<RelicModel> __result, RelicPoolModel __instance)
    {
        if (CustomContentDictionary.Relics.TryGetValue(__instance.GetType(), out var relics))
        {
            return [.. __result, .. relics];
        }
        return __result;
    }
}
class PotionPoolPatch
{
    internal static void Patch(Harmony harmony)
    {
        Type[] poolTypes =
            [typeof(SharedPotionPool),
            typeof(IroncladPotionPool),
            typeof(SilentPotionPool),
            typeof(DefectPotionPool),
            typeof(RegentPotionPool),
            typeof(NecrobinderPotionPool),
            typeof(EventPotionPool),
            typeof(TokenPotionPool)
        ]; //CustomPotionPoolModel generate method utilizes CustomContentDictionary

        foreach (var poolType in poolTypes)
        {
            var originalMethod = AccessTools.Method(poolType, "GenerateAllPotions");
            var postfix = AccessTools.Method(typeof(PotionPoolPatch), nameof(AdjustPool));
            harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfix));
        }
    }

    static IEnumerable<PotionModel> AdjustPool(IEnumerable<PotionModel> __result, PotionPoolModel __instance)
    {
        if (CustomContentDictionary.Potions.TryGetValue(__instance.GetType(), out var potions))
        {
            return [.. __result, .. potions];
        }
        return __result;
    }
}*/