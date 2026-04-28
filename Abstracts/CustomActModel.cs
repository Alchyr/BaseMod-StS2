using System.Reflection.Emit;
using BaseLib.Extensions;
using BaseLib.Patches.Content;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Achievements;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NCustomTreasureRoomChest = BaseLib.BaseLibScenes.Acts.NCustomTreasureRoomChest;

namespace BaseLib.Abstracts;

public abstract class CustomActModel : ActModel, ICustomModel
{
    public int ActNumber { get; }

    /// <param name="actNumber">Set to -1 to prevent your act from spawning naturally.</param>
    /// <param name="autoAdd">If false, will not be added to CustomContentDictionary.</param>
    protected CustomActModel(int actNumber, bool autoAdd = true)
    {
        ActNumber = actNumber;
        if (autoAdd)
        {
            CustomContentDictionary.AddAct(this);
        }
    }

    #region default values

    public override Color MapTraveledColor => new("27221C");
    public override Color MapUntraveledColor => new("6E7750");
    public override Color MapBgColor => new("9B9562");

    public override string[] BgMusicOptions => [
        "event:/music/act3_a1_v1",
        "event:/music/act3_a2_v1"
    ];
    
    public override string[] MusicBankPaths => ["res://banks/desktop/act3_a1.bank", "res://banks/desktop/act3_a2.bank"];
    public override string AmbientSfx => "event:/sfx/ambience/act3_ambience";

    public override string ChestSpineResourcePath =>
        "res://animations/backgrounds/treasure_room/chest_room_act_3_skel_data.tres";

    public override string ChestSpineSkinNameNormal => "act3";
    public override string ChestSpineSkinNameStroke => "act3_stroke";
    public override string ChestOpenSfx => "event:/sfx/ui/treasure/treasure_act3";

    /// <summary>
    /// By default, all ancients preset in the act are considered unlocked.
    /// </summary>
    public override IEnumerable<AncientEventModel> GetUnlockedAncients(UnlockState state)
    {
        return AllAncients.ToList();
    }

    /// <summary>
    /// Default override is provided that returns ancients based on act number. If you don't want the default ancients
    /// or have a non-basegame act number, override this. If making custom ancients for your specific act,
    /// add them to the act using their IsValidForAct method, rather than by adding them to this method.
    /// </summary>
    /// <exception cref="Exception"></exception>
    public override IEnumerable<AncientEventModel> AllAncients
    {
        get
        {
            return ActNumber switch
            {
                1 => Act1Ancients,
                2 => Act2Ancients,
                3 => Act3Ancients,
                _ => throw new Exception("Override AllAncients for acts with a non-basegame act number.")
            };
        }
    }

    /// <summary>
    /// Fixed order in which bosses will first appear. Sets boss to first unseen encounter in provided set.
    /// Default override of empty list is provided.
    /// </summary>
    public override IEnumerable<EncounterModel> BossDiscoveryOrder => [];

    /// <summary>
    /// Required abstract method.
    /// Only used in Overgrowth specifically for the very first run, so default override is provided.
    /// </summary>
    protected override void ApplyActDiscoveryOrderModifications(UnlockState unlockState)
    {
    }

    /// <summary>
    /// By default, Act 1 has 15 rooms, Act 2 has 14, and Act 3 has 13.
    /// </summary>
    protected override int BaseNumberOfRooms => ActNumber switch
    {
        1 => 15,
        2 => 14,
        3 => 13,
        _ => 15
    };

    /// <summary>
    /// Specifically sets rest and unknown room count.
    /// Set up to provide values matching basegame acts based on act number.
    /// (Lower numbers on higher acts due to lower room count)
    /// </summary>
    public override MapPointTypeCounts GetMapPointTypes(Rng mapRng)
    {
        int restCount = 6;
        int unknownCount = MapPointTypeCounts.StandardRandomUnknownCount(mapRng);
        switch (ActNumber)
        {
            case 1:
                restCount = mapRng.NextGaussianInt(7, 1, 6, 7);
                break;
            case 2:
                restCount = mapRng.NextGaussianInt(6, 1, 6, 7);
                unknownCount--;
                break;
            case 3:
                restCount = mapRng.NextInt(5, 7);
                unknownCount--;
                break;
        }

        return new MapPointTypeCounts(unknownCount, restCount);
    }

    #endregion default values

    /// <summary>
    /// Override this if you want to provide your own BackgroundScene
    /// </summary>
    protected virtual string CustomBackgroundScenePath => "res://BaseLib/scenes/dynamic_background.tscn";

    protected abstract string CustomMapTopBgPath { get; }
    protected abstract string CustomMapMidBgPath { get; }
    protected abstract string CustomMapBotBgPath { get; }
    protected abstract string CustomRestSiteBackgroundPath { get; }

    /// <summary>
    /// Override this if you want to replace the chest-visuals in Treasure Rooms.<br></br>
    /// The scenes root node <b>must</b> have a script attached that derives from <see cref="NCustomTreasureRoomChest"/> <br></br>
    /// </summary>
    public virtual string? CustomChestScene => null;

    /// <summary>
    /// Defaults to generating a copy of Glory's background assets;
    /// see constructors of CustomBackgroundAssets for more options.
    /// <seealso cref="CustomBackgroundAssets"/>
    /// </summary>
    protected virtual BackgroundAssets CustomGenerateBackgroundAssets(Rng rng)
    {
        return new BackgroundAssets("glory", rng);
    }

    #region Patches

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.BackgroundScenePath), MethodType.Getter)]
    class CustomActBackgroundScenePath
    {
        [HarmonyPrefix]
        static bool UseAltTexture(ActModel __instance, ref string? __result)
        {
            if (__instance is not CustomActModel customAct) return true;
            __result = customAct.CustomBackgroundScenePath;
            return false;
        }
    }

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.MapTopBgPath), MethodType.Getter)]
    class CustomActMapTopBgPath
    {
        [HarmonyPrefix]
        static bool UseAltTexture(ActModel __instance, ref string? __result)
        {
            if (__instance is not CustomActModel customAct) return true;
            __result = customAct.CustomMapTopBgPath;
            return false;
        }
    }

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.MapMidBgPath), MethodType.Getter)]
    class CustomActMapMidBgPath
    {
        [HarmonyPrefix]
        static bool UseAltTexture(ActModel __instance, ref string? __result)
        {
            if (__instance is not CustomActModel customAct) return true;
            __result = customAct.CustomMapMidBgPath;
            return false;
        }
    }

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.MapBotBgPath), MethodType.Getter)]
    class CustomActMapBotBgPath
    {
        [HarmonyPrefix]
        static bool UseAltTexture(ActModel __instance, ref string? __result)
        {
            if (__instance is not CustomActModel customAct) return true;
            __result = customAct.CustomMapBotBgPath;
            return false;
        }
    }

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.RestSiteBackgroundPath), MethodType.Getter)]
    class CustomActRestSiteBackgroundPath
    {
        [HarmonyPrefix]
        static bool UseAltTexture(ActModel __instance, ref string? __result)
        {
            if (__instance is not CustomActModel customAct) return true;
            __result = customAct.CustomRestSiteBackgroundPath;
            return false;
        }
    }

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateBackgroundAssets))]
    public class CustomActGenerateBackgroundAssets
    {
        [HarmonyPrefix]
        public static bool UseCustomBackgroundAssets(ActModel __instance, Rng rng, ref BackgroundAssets __result)
        {
            if (__instance is not CustomActModel customAct) return true;
            __result = customAct.CustomGenerateBackgroundAssets(rng);
            return false;
        }
    }

    [HarmonyPatch(typeof(NTreasureRoom), nameof(NTreasureRoom._Ready))]
    public static class CustomActTreasureChest
    {
        private static readonly AccessTools.FieldRef<NTreasureRoom, IRunState?> RunStateRef =
            AccessTools.FieldRefAccess<NTreasureRoom, IRunState?>("_runState");

        private static readonly AccessTools.FieldRef<NTreasureRoom, Node2D?> ChestNodeRef =
            AccessTools.FieldRefAccess<NTreasureRoom, Node2D?>("_chestNode");

        private static readonly AccessTools.FieldRef<NTreasureRoom, NButton?> ChestButtonRef =
            AccessTools.FieldRefAccess<NTreasureRoom, NButton?>("_chestButton");

        [HarmonyPostfix]
        public static void InsertCustomChestVisualNode(NTreasureRoom __instance)
        {
            // validation
            IRunState? runState = RunStateRef(__instance);
            if (runState?.Act is not CustomActModel customActModel) return;
            if (customActModel.CustomChestScene is null) return;
            Node2D? chestNode = ChestNodeRef(__instance);
            NButton? chestButton = ChestButtonRef(__instance);
            if (chestNode is null || chestButton is null) // should in theory never be the case
            {
                BaseLibMain.Logger.Warn("References not found. Using normal Chest Visuals instead");
                return;
            }

            // node insertion
            chestNode.Visible =
                false; // Not removed so the game can still access the node whenever it wants, to prevent errors/crashing.
            Node parent = chestNode.GetParent();
            NCustomTreasureRoomChest? customTreasureRoom =
                NCustomTreasureRoomChest.Create(__instance, runState, chestButton, customActModel.CustomChestScene);
            if (customTreasureRoom is null)
            {
                BaseLibMain.Logger.Error(
                    $"Tried to instantiate custom treasure chest node but failed. Scene path: {customActModel.CustomChestScene}");
                return;
            }

            parent.AddChildSafely(customTreasureRoom);
        }
    }

    #endregion Patches

    /// <summary>
    /// Basegame set of act 1 ancients.
    /// </summary>
    protected static List<AncientEventModel> Act1Ancients =>
    [
        ModelDb.AncientEvent<Neow>()
    ];
    /// <summary>
    /// Basegame set of act 2 ancients.
    /// </summary>
    protected static List<AncientEventModel> Act2Ancients =>
    [
        ModelDb.AncientEvent<Orobas>(),
        ModelDb.AncientEvent<Pael>(),
        ModelDb.AncientEvent<Tezcatara>()
    ];
    /// <summary>
    /// Basegame set of act 3 ancients.
    /// </summary>
    protected static List<AncientEventModel> Act3Ancients =>
    [
        ModelDb.AncientEvent<Nonupeipe>(),
        ModelDb.AncientEvent<Tanx>(),
        ModelDb.AncientEvent<Vakuu>()
    ];
}

// Currently that method has no body so this patch is preemptive to when they add something
[HarmonyPatch(typeof(AchievementsHelper), nameof(AchievementsHelper.CheckForDefeatedAllEnemiesAchievement))]
public class SkipModdedActAchievementPatch
{
    [HarmonyPrefix]
    public static bool SkipCustomActs(ActModel act)
    {
        return act is not CustomActModel;
    }
}


// For some reason this method checks for specifically 4 Acts, this Transpiler removes that
// I'm still not entirely sure why they even do that
[HarmonyPatch(typeof(NRelicCollectionCategory), nameof(NRelicCollectionCategory.LoadRelics))]
static class RelicCollectionTranspiler
{
    [HarmonyTranspiler]
    static List<CodeInstruction> GenerateFullActList(IEnumerable<CodeInstruction> instructions)
    {
        var actsGetter = typeof(RelicCollectionTranspiler).Method(nameof(RelicCollectionTranspiler.SortedActs));
        
        return new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .stloc_s()
                .call(typeof(ModelDb).PropertyGetter(nameof(ModelDb.Acts)))
                .ldloc_s()
                .call(null)
                .call(null)
                .brfalse_s()
                .ldstr().PredicateMatch(operand => operand is string s && s.Contains("act list"))
            )
            .InsertBeforeMatch([
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Call, actsGetter)
            ]);
    }

    static List<ActModel> SortedActs()
    {
        var acts = ModelDb.Acts.ToList();
        acts.Sort((act1, act2) =>
        {
            int act1Num = act1.ActNumber(),
                act2Num = act2.ActNumber();

            if (act1Num == act2Num) return 0;

            if (act1Num <= 0 || act2Num <= 0)
            {
                return act2Num.CompareTo(act1Num);
            }

            return act1Num.CompareTo(act2Num);
        });
        return acts;
    }
}