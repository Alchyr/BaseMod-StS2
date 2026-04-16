using System.Reflection;
using System.Text;
using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.InitIds))]
public static class CustomContentDictionary
{
    public static readonly HashSet<Type> RegisteredTypes = [];
    private static readonly Dictionary<Type, Type> PoolTypes = [];
    public static readonly List<CustomEncounterModel> CustomEncounters = [];
    public static readonly List<CustomAncientModel> CustomAncients = [];
    /// <summary>
    /// Custom events tied to a specific act.
    /// </summary>
    public static readonly List<CustomEventModel> ActCustomEvents = [];
    /// <summary>
    /// Custom events not tied to a specific act.
    /// </summary>
    public static readonly List<CustomEventModel> SharedCustomEvents = [];
    
    static CustomContentDictionary()
    {
        PoolTypes.Add(typeof(CardPoolModel), typeof(CardModel));
        PoolTypes.Add(typeof(RelicPoolModel), typeof(RelicModel));
        PoolTypes.Add(typeof(PotionPoolModel), typeof(PotionModel));
    }

    public static bool RegisterType(Type t)
    {
        return RegisteredTypes.Add(t);
    }

    public static void AddModel(Type modelType)
    {
        if (!RegisterType(modelType)) return;
        
        var poolAttribute = modelType.GetCustomAttribute<PoolAttribute>()
            ?? throw new Exception($"Model {modelType.FullName} must be marked with a PoolAttribute to determine which pool to add it to.");

        if (!IsValidPool(modelType, poolAttribute.PoolType))
        {
            throw new Exception($"Model {modelType.FullName} is assigned to incorrect type of pool {poolAttribute.PoolType.FullName}.");
        }
        
        ModHelper.AddModelToPool(poolAttribute.PoolType, modelType);
    }

    public static void AddEncounter(CustomEncounterModel encounter)
    {
        if (!RegisterType(encounter.GetType())) return;

        CustomEncounters.Add(encounter);
    }

    public static void AddAncient(CustomAncientModel ancient)
    {
        if (!RegisterType(ancient.GetType())) return;
        
        CustomAncients.Add(ancient);
    }
    
    public static void AddEvent(CustomEventModel eventModel)
    {
        if (!RegisterType(eventModel.GetType())) return;

        if (eventModel.Acts.Length == 0)
        {
            SharedCustomEvents.Add(eventModel);
        }
        else
        {
            ActCustomEvents.Add(eventModel);
        }
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
        if (!CustomContentDictionary.RegisterType(pool.GetType())) return;
        
        CustomSharedPools.Add(pool);
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
        if (!CustomContentDictionary.RegisterType(pool.GetType())) return;
        
        customSharedPools.Add(pool);
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
        if (!CustomContentDictionary.RegisterType(pool.GetType())) return;
        
        customSharedPools.Add(pool);
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

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllSharedEvents), MethodType.Getter)]
class CustomSharedEvents
{
    [HarmonyTranspiler]
    static List<CodeInstruction> AddCustomShared(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                .dup()
                .stsfld(null))
            .Step(-2)
            .Insert(CodeInstruction.Call(typeof(CustomSharedEvents), nameof(ConcatCustom)));
    }

    static IEnumerable<EventModel> ConcatCustom(IEnumerable<EventModel> events)
    {
        var result = new List<EventModel>(events);
        result.AddRange(CustomContentDictionary.SharedCustomEvents);
        return result;
    }
}

/// <summary>
/// Called in PostModInitPatch to catch modded acts
/// </summary>
public static class AddActContent
{
    public static void Patch(Harmony harmony)
    {
        StringBuilder patchedTypes = new("Patching act types for custom encounters and events");
        
        foreach (var t in ReflectionHelper.GetSubtypes<ActModel>()
                     .Chain(ReflectionHelper.GetSubtypesInMods<ActModel>()))
        {
            bool patched = false;
            var method = AccessTools.DeclaredMethod(t, nameof(ActModel.GenerateAllEncounters));
            if (method != null)
            {
                patched = true;
                harmony.Patch(method, postfix: AccessTools.Method(typeof(AddActContent), nameof(AddCustomEncounters)));
            }

            method = AccessTools.DeclaredPropertyGetter(t, nameof(ActModel.AllEvents));
            if (method != null)
            {
                patched = true;
                harmony.Patch(method, postfix: AccessTools.Method(typeof(AddActContent), nameof(AddCustomEvents)));
            }

            if (patched)
            {
                patchedTypes.Append(" | ").Append(t.Name);
            }
        }

        BaseLibMain.Logger.Info(patchedTypes.ToString());
    }

    static IEnumerable<EncounterModel> AddCustomEncounters(IEnumerable<EncounterModel> result, ActModel __instance)
    {
        foreach (var value in result)
        {
            yield return value;
        }

        foreach (var encounter in CustomContentDictionary.CustomEncounters)
        {
            if (encounter.IsValidForAct(__instance)) yield return encounter;
        }
    }

    static IEnumerable<EventModel> AddCustomEvents(IEnumerable<EventModel> result, ActModel __instance)
    {
        foreach (var value in result)
        {
            yield return value;
        }

        foreach (var eventModel in CustomContentDictionary.ActCustomEvents)
        {
            if (eventModel.Acts.Any(act => act.Id.Equals(__instance.Id))) yield return eventModel;
        }
    }
}