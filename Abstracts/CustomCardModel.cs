using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using System.Linq;
using BaseLib.Patches;
using BaseLib.Patches.Content;

namespace BaseLib.Abstracts;

public abstract class CustomCardModel : CardModel, ICustomModel, ILocalizationProvider
{
    /// <summary>
    /// For convenience; can be manually overridden if necessary.
    /// </summary>
    public override bool GainsBlock => DynamicVars.Any((dynVar)=>dynVar.Value is BlockVar or CalculatedBlockVar);

    public CustomCardModel(int baseCost, CardType type, CardRarity rarity, TargetType target, bool showInCardLibrary = true, bool autoAdd = true) : base(baseCost, type, rarity, target, showInCardLibrary)
    {
        if (autoAdd) CustomContentDictionary.AddModel(GetType());
    }

    public virtual Texture2D? CustomFrame => null;
    public virtual string? CustomPortraitPath => null;
    public virtual Texture2D? CustomPortrait => null;
    public virtual List<(string, string)>? Localization => null;
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Frame), MethodType.Getter)]
class CustomCardFrame
{
    [HarmonyPrefix]
    static bool UseAltTexture(CardModel __instance, ref Texture2D? __result)
    {
        if (__instance is CustomCardModel customCard)
        {
            __result = customCard.CustomFrame;
            if (__result != null) return false;

            if (__instance.Pool is CustomCardPoolModel customCardPool)
            {
                __result = customCardPool.CustomFrame(customCard);
                if (__result != null) return false;
            }
        }
        return true;
    }
}

[HarmonyPatch(typeof(CardModel), "PortraitPngPath", MethodType.Getter)]
class CustomCardPortraitPngPath
{
    [HarmonyPrefix]
    static bool UseAltTexture(CardModel __instance, ref string? __result)
    {
        if (__instance is not CustomCardModel customCard) return true;
        
        if (customCard.CustomPortraitPath != null) {
            __result = customCard.CustomPortraitPath;
        } else {
            return true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Portrait), MethodType.Getter)]
class CustomCardPortrait
{
    [HarmonyPrefix]
    static bool UseAltTexture(CardModel __instance, ref Texture2D? __result)
    {
        if (__instance is not CustomCardModel customCard) return true;

        if (customCard.CustomPortrait != null) {
            __result = customCard.CustomPortrait;
        } else if (customCard.CustomPortraitPath != null) {
            __result = ResourceLoader.Load<Texture2D>(customCard.CustomPortraitPath);
        } else {
            return true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.PortraitPath), MethodType.Getter)]
class CustomCardPortraitPath
{
    [HarmonyPrefix]
    static bool UseAltTexture(CardModel __instance, ref string? __result)
    {
        if (__instance is not CustomCardModel customCard) return true;

        if (customCard.CustomPortrait != null) {
            __result = customCard.CustomPortrait.ResourcePath;
        } else if (customCard.CustomPortraitPath != null) {
            __result = ResourceLoader.Load<Texture2D>(customCard.CustomPortraitPath).ResourcePath;
            // Confusingly CustomPortraitPath is a png
        } else {
            return true;
        }
        return false;
    }
}