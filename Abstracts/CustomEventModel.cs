using BaseLib.Patches.Content;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Abstracts;

public abstract class CustomEventModel : EventModel, ICustomModel, ILocalizationProvider
{
    public CustomEventModel(bool autoAdd = true)
    {
        if (autoAdd)
            CustomContentDictionary.AddEvent(this);
    }

    //Note - most shared events define an IsAllowed condition that check runState.CurrentActIndex
    //Until all possible events in an act are seen, events already seen in a run will be skipped
    /// <summary>
    /// The acts this event can spawn in.
    /// If none are set, the event will be considered a "shared" event and can spawn in any act.
    /// If you don't want the event to spawn, override IsAllowed.
    /// If making a custom act, set this rather than adding to the act model's event list.
    /// </summary>
    public virtual ActModel[] Acts => [];

    /*
     Additional relevant overrides:
     LayoutType
     CanonicalEncounter (fight event)
     IsAllowed - Spawn condition
     IsShared - events where all players must take same options. Required for combat events.
     CanonicalVars/CalculateVars
    */

    /*----- Custom Asset Paths -----*/
    public virtual string? CustomInitialPortraitPath => null;
    public virtual string? CustomBackgroundScenePath => null;
    /// <summary>
    /// Path to a VFX .tscn
    /// </summary>
    public virtual string? CustomVfxPath => null;
    

    public virtual List<(string, string)>? Localization => null;
    
    /*----- Utility Methods -----*/

    protected EventOption Option(Func<Task>? onChosen, LocString title, LocString description,
        params IHoverTip[] tips)
    {
        return new EventOption(this, onChosen, title, description, Id.Entry, tips);
    }
    
    /// <summary>
    /// Generate an EventOption with localization based on the passed delegate's method name.
    /// </summary>
    /// <param name="onChosen"></param>
    /// <param name="pageKey"></param>
    /// <param name="tips"></param>
    /// <returns></returns>
    protected EventOption Option(Func<Task>? onChosen, string pageKey = _initialPageKey, params IHoverTip[] tips)
    {
        var clickMethod = onChosen?.Method;
        string name = "UNKNOWN";
        if (clickMethod == null)
        {
            BaseLibMain.Logger.Error("Unable to get delegate method for CustomEventModel.Option; " +
                                     "provide an explicit title and description LocString if not passing a method directly.");
        }
        else 
        {
            if (clickMethod.IsSpecialName)
                BaseLibMain.Logger.Warn("Method passed as delegate to CustomEventModel.Option has special name; " +
                                    "recommended to directly pass declared method or provide an explicit title and description LocString.");
            name = clickMethod.Name;
        }
        return new EventOption(this, onChosen, $"{Id.Entry}.pages.{pageKey}.options.{StringHelper.Slugify(name)}", tips);
    }
    
    protected EventOption Option(Func<Task>? onChosen, IEnumerable<IHoverTip> tips, string pageKey = _initialPageKey)
    {
        var clickMethod = onChosen?.Method;
        string name = "UNKNOWN";
        if (clickMethod == null)
        {
            BaseLibMain.Logger.Error("Unable to get delegate method for CustomEventModel.Option; " +
                                     "provide an explicit title and description LocString if not passing a method directly.");
        }
        else 
        {
            if (clickMethod.IsSpecialName)
                BaseLibMain.Logger.Warn("Method passed as delegate to CustomEventModel.Option has special name; " +
                                        "recommended to directly pass declared method or provide an explicit title and description LocString.");
            name = clickMethod.Name;
        }
        return new EventOption(this, onChosen, $"{Id.Entry}.pages.{pageKey}.options.{StringHelper.Slugify(name)}", tips);
    }

    /// <summary>
    /// Get the LocString for an event page's description text.
    /// </summary>
    protected LocString PageDescription(string pageKey)
    {
        return L10NLookup($"{Id.Entry}.pages.{pageKey}.description");
    }
}

[HarmonyPatch(typeof(EventModel), nameof(EventModel.InitialPortraitPath), MethodType.Getter)]
class InitialPortraitPath
{
    [HarmonyPrefix]
    static bool CustomInitialPortraitPath(EventModel __instance, ref string? __result)
    {
        if (__instance is not CustomEventModel customEvent) return true;

        __result = customEvent.CustomInitialPortraitPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(EventModel), nameof(EventModel.BackgroundScenePath), MethodType.Getter)]
class EventBackgroundScenePath
{
    [HarmonyPrefix]
    static bool CustomInitialPortraitPath(EventModel __instance, ref string? __result)
    {
        if (__instance is not CustomEventModel customEvent) return true;

        __result = customEvent.CustomBackgroundScenePath;
        return __result == null;
    }
}