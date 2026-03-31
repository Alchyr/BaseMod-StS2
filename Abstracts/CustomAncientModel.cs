using System.Text;
using BaseLib.Patches.Content;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public abstract class CustomAncientModel : AncientEventModel, ICustomModel, ILocalizationProvider
{
    //Suggested overrides: ButtonColor, DialogueColor
    private readonly bool _logDialogueLoad;
    
    public CustomAncientModel(bool autoAdd = true, bool logDialogueLoad = false)
    {
        if (autoAdd) CustomContentDictionary.AddAncient(this);
        _logDialogueLoad = logDialogueLoad;
    }
    
    public virtual List<(string, string)>? Localization => null;

    /// <summary>
    /// Suggested to check act.ActNumber == 2 or 3.
    ///
    /// If you are overriding ShouldForceSpawn, you should override this and return false.
    /// </summary>
    /// <param name="act"></param>
    /// <returns></returns>
    public virtual bool IsValidForAct(ActModel act) => true;
    
    /// <summary>
    /// Suggested to leave this set to false unless you want to force a specific ancient to spawn during map generation. Messing with this can cause mod conflicts, please only use if it is 100% necessary for your mod to function.
    /// </summary>
    /// <param name="act"></param>
    /// <param name="rngChosenAncient">The ancient that will have been chosen by the games rng.</param>
    /// <returns></returns>
    public virtual bool ShouldForceSpawn(ActModel act, AncientEventModel? rngChosenAncient) => false;
    
    /// <summary>
    /// Set up a new OptionPools with 1, 2, or 3 pools using MakePool for each pool.
    /// If there is 1 pool, all ancient options will be chosen randomly from this pool.
    /// With 2 pools, the first two options will use the first pool and the last option will use the second pool.
    /// With 3 pools, each option will use its own pool.
    /// </summary>
    protected abstract OptionPools MakeOptionPools { get; }

    private OptionPools? _optionPools;
    public OptionPools OptionPools
    {
        get
        {
            if (_optionPools == null) _optionPools = MakeOptionPools;
            return _optionPools;
        }
    }
    
    public override IEnumerable<EventOption> AllPossibleOptions => 
        OptionPools.AllOptions.SelectMany(option => option.AllVariants.Select(relic => RelicOption(relic)));
    
    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        var options = OptionPools.Roll(Rng);
        return options.Select(option => RelicOption(option.ModelForOption)).ToList();
    }
    
    public static WeightedList<AncientOption> MakePool(params RelicModel[] options)
    {
        WeightedList<AncientOption> pool = [..options.Select(model => (AncientOption) model)];
        return pool;
    }
    public static WeightedList<AncientOption> MakePool(params AncientOption[] options)
    {
        WeightedList<AncientOption> pool = [..options];
        return pool;
    }

    public static AncientOption AncientOption<T>(int weight = 1, Func<T, RelicModel>? relicPrep = null, Func<T, IEnumerable<RelicModel>>? makeAllVariants = null) where T : RelicModel
    {
        return new AncientOption<T>(weight)
        {
            ModelPrep = relicPrep,
            Variants = makeAllVariants
        };
    }
    
    /******************    Assets    ******************/

    /// <summary>
    /// Overridden to load custom event scene.
    /// </summary>
    /// <param name="runState"></param>
    /// <returns></returns>
    public override IEnumerable<string> GetAssetPaths(IRunState runState)
    {
        var customScene = CustomScenePath;
        return customScene != null ? [customScene] : base.GetAssetPaths(runState);
    }

    /// <summary>
    /// Path to a custom event scene which will be the background of the event.
    /// </summary>
    public virtual string? CustomScenePath => null;

    public virtual string? CustomMapIconPath => null;
    public virtual string? CustomMapIconOutlinePath => null;

    public virtual string? CustomRunHistoryIconPath => null;
    public virtual string? CustomRunHistoryIconOutlinePath => null;


    /****************** Localization ******************/
    private string FirstVisit => $"{Id.Entry}.talk.firstvisitEver.0-0.ancient";
    
    protected override AncientDialogueSet DefineDialogues()
    {
        StringBuilder? log = _logDialogueLoad ? new($"Prepping dialogue for ancient '{Id.Entry}'") : null;
        string sfxPath;
        AncientDialogue firstVisit = new(sfxPath = AncientDialogueUtil.SfxPath(FirstVisit));
        log?.AppendLine($"First visit with sfx '{sfxPath}'");

        Dictionary<string, IReadOnlyList<AncientDialogue>> characterDialogues = [];
        
        foreach (var character in ModelDb.AllCharacters)
        {
            var baseKey = AncientDialogueUtil.BaseLocKey(Id.Entry, character.Id.Entry);
            characterDialogues[character.Id.Entry] = AncientDialogueUtil.GetDialoguesForKey("ancients", baseKey, log);
        }
        
        var dialogueSet = new AncientDialogueSet
        {
            FirstVisitEverDialogue = firstVisit,
            CharacterDialogues = characterDialogues,
            AgnosticDialogues = AncientDialogueUtil.GetDialoguesForKey("ancients", AncientDialogueUtil.BaseLocKey(Id.Entry, "ANY"), log)
        };
        if (log != null) BaseLibMain.Logger.Info(log.ToString());
        return dialogueSet;
    }
}

[HarmonyPatch(typeof(EventModel), "BackgroundScenePath", MethodType.Getter)]
class BackgroundScenePath
{
    [HarmonyPrefix]
    static bool Custom(AncientEventModel __instance, ref string? __result)
    {
        if (__instance is not CustomAncientModel custom)
            return true;

        __result = custom.CustomScenePath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(AncientEventModel), "MapIconPath", MethodType.Getter)]
class MapIconPath
{
    [HarmonyPrefix]
    static bool Custom(AncientEventModel __instance, ref string? __result)
    {
        if (__instance is not CustomAncientModel custom)
            return true;

        __result = custom.CustomMapIconPath;
        return __result == null;
    }
}
[HarmonyPatch(typeof(AncientEventModel), "MapIconOutlinePath", MethodType.Getter)]
class MapIconOutlinePath
{
    [HarmonyPrefix]
    static bool Custom(AncientEventModel __instance, ref string? __result)
    {
        if (__instance is not CustomAncientModel custom)
            return true;

        __result = custom.CustomMapIconOutlinePath;
        return __result == null;
    }
}