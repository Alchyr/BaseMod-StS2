using BaseLib.Patches.Content;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;

namespace BaseLib.Abstracts;

public abstract class CustomEncounterModel : EncounterModel, ICustomModel
{
    public override RoomType RoomType { get; }
    private BackgroundAssets? _customBackgroundAssets;
    
    protected CustomEncounterModel(RoomType roomType, bool autoAdd = true)
    {
        if (roomType is not (RoomType.Monster or RoomType.Elite or RoomType.Boss))
        {
            BaseLibMain.Logger.Warn($"Encounter {Id.Entry} sets unexpected room type {roomType}");
        }
        RoomType = roomType;

        if (autoAdd)
        {
            CustomContentDictionary.AddEncounter(this);
        }
    }

    public abstract bool IsValidForAct(ActModel act);
    
    //Todo - Support non-event:/ bgm? needs audio stuff
    //Automatically add to encounter pool
    
    /*
     Required:
     
     Override AllPossibleMonsters returning every monster that can spawn in this encounter.
     Override GenerateMonsters returning mutable instances of the actual monsters that will appear in the encounter.
        Use `this.Rng` if you need to choose randomly.
     
    
    
    Other overrides:
    GetCameraScaling
    Tags - The game will avoid generating two encounters that share a tag in a row.
    IsWeak - Weak encounters are the first 3 encounters in act 1, and the first 2 in the other acts.
    BossNodePath - May set something up for this. Uses skeleton data.
    MapNodeAssetPaths - returns boss node assets
    
    */

    /// <summary>
    /// The path to an encounter scene.
    /// An encounter scene is a 1920x1080 Control with Full Rect anchors, and Marker2D children for enemy positions.
    /// The names of these markers can be used with CreatureCmd.Add when spawning additional enemies.
    /// Initial enemies will be placed at these markers in the order they exist in the scene.
    /// </summary>
    public virtual string? CustomScenePath => null;

    /// <summary>
    /// Should not be necessary to override; will return true if CustomScenePath returns a valid resource path or
    /// a scene exists at the basegame expected path res://scenes/encounters/modname-encounter_name.tscn
    /// </summary>
    public override bool HasScene => (CustomScenePath != null && ResourceLoader.Exists(CustomScenePath)) ||
                                     ResourceLoader.Exists(ScenePath);

    /// <summary>
    /// Generates and stores a custom background.
    /// </summary>
    /// <param name="parentAct"></param>
    /// <param name="rng"></param>
    protected void PrepCustomBackground(ActModel parentAct, Rng rng)
    {
        _customBackgroundAssets = CustomEncounterBackground(parentAct, rng);
    }

    /// <summary>
    /// Works automatically if CustomEncounterBackground is overridden.
    /// If not using CustomEncounterBackground and instead placing files in basegame expected paths,
    /// override this to return true.
    /// </summary>
    protected override bool HasCustomBackground => _customBackgroundAssets != null;

    /// <summary>
    /// Override this method if you want to provide a custom encounter background for your scene using custom paths.
    /// To do so, return a new CustomBackgroundAssets object.
    /// Alternatively you can place your assets at res://scenes/backgrounds/modname-encounter_name/layers and
    /// res://scenes/backgrounds/modname-encounter_name/modname-encounter_name_background.tscn, then override 
    /// </summary>
    public virtual BackgroundAssets? CustomEncounterBackground(ActModel parentAct, Rng rng)
    {
        return null;
    }
    
    
    
    [HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.ScenePath), MethodType.Getter)]
    static class ScenePathPatch {
        [HarmonyPrefix]
        static bool Custom(EncounterModel __instance, ref string? __result) {
            if (__instance is not CustomEncounterModel model)
                return true;

            __result = model.CustomScenePath;
            return __result == null;
        }
    }
    
    [HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.GetBackgroundAssets))]
    static class GetCustomBackgroundAssets {
        [HarmonyPrefix]
        static void Custom(EncounterModel __instance, ActModel parentAct, Rng rng) {
            if (__instance is not CustomEncounterModel model)
                return;

            model.PrepCustomBackground(parentAct, rng);
        }
    }
    
    [HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.CreateBackgroundAssetsForCustom))]
    static class ScenePatch {
        [HarmonyPrefix]
        static bool Custom(EncounterModel __instance, ref BackgroundAssets? __result) {
            if (__instance is not CustomEncounterModel model)
                return true;
            
            __result = model._customBackgroundAssets;
            return __result == null;
        }
    }

}