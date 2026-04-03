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

    /// <summary>
    /// Allows a custom texture to be used as the card's back frame.
    /// A new texture loaded through ResourceLoader.Load&lt;Texture2D> should be returned.
    /// </summary>
    public virtual Texture2D? CustomFrame => null;

    private bool _initializedFrameMaterial = false;
    private Material? _frameMaterial = null;

    /// <summary>
    /// Returns a custom ShaderMaterial defined by CreateCustomFrameMaterial.
    /// </summary>
    public Material? CustomFrameMaterial
    {
        get
        {
            if (!_initializedFrameMaterial)
            {
                _frameMaterial = CreateCustomFrameMaterial;
                _initializedFrameMaterial = true;
            }
            return _frameMaterial;
        }
    }
    
    /// <summary>
    /// Override this to use a custom ShaderMaterial only for this card.<seealso cref="BaseLib.Utils.ShaderUtils.GenerateHsv" />
    /// </summary>
    public virtual Material? CreateCustomFrameMaterial => null;
    
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
        if (__instance is not CustomCardModel customCard) return true;
        
        __result = customCard.CustomFrame;
        if (__result != null) return false;

        if (__instance.Pool is not CustomCardPoolModel customCardPool) return true;
        
        __result = customCardPool.CustomFrame(customCard);
        return __result == null;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.FrameMaterial), MethodType.Getter)]
class CustomCardFrameMaterial
{
    [HarmonyPrefix]
    static bool UseAltMaterial(CardModel __instance, ref Material? __result)
    {
        if (__instance is not CustomCardModel customCard) return true;
        
        __result = customCard.CustomFrameMaterial;
        return __result == null;
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
        } else {
            return true;
        }
        return false;
    }
}