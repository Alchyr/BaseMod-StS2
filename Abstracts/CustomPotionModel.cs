using HarmonyLib;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Abstracts;

public abstract class CustomPotionModel : PotionModel, ICustomModel, ILocalizationProvider
{
    [Obsolete("Pass value in constructor instead. Field will be deleted.")]
    public virtual bool AutoAdd => true;
    
    public CustomPotionModel(bool autoAdd = true)
    {
        if (autoAdd) CustomContentDictionary.AddModel(GetType());
    }

    /// <summary>
    /// Override this or place your potion's image at
    /// "res://images/atlases/potion_atlas.sprites/modid-potion_name.tres"
    /// You may pass the path to a png or any other file that Godot can load as a Texture2D.
    /// </summary>
    public virtual string? CustomPackedImagePath => null;

    /// <summary>
    /// Override this or place your potion's outline image at
    /// "res://images/atlases/potion_outline_atlas.sprites/modid-potion_name.tres"
    /// You may pass the path to a png or any other file that Godot can load as a Texture2D.
    /// </summary>
    public virtual string? CustomPackedOutlinePath => null;
    
    /// <summary>
    /// Override this to define localization directly in your class.
    /// You are recommended to return a PotionLoc<seealso cref="PotionLoc"/>.
    /// </summary>
    public virtual List<(string, string)>? Localization => null;
    
    [HarmonyPatch(typeof(PotionModel), nameof(PackedImagePath), MethodType.Getter)]
    private static class ImagePatch {
        static bool Prefix(PotionModel __instance, ref string? __result) {
            if (__instance is not CustomPotionModel model)
                return true;
            __result = model.CustomPackedImagePath;
            return __result == null;
        }
    }
    [HarmonyPatch(typeof(PotionModel), nameof(PackedOutlinePath), MethodType.Getter)]
    private static class OutlinePatch {
        static bool Prefix(PotionModel __instance, ref string? __result) {
            if (__instance is not CustomPotionModel model)
                return true;
            __result = model.CustomPackedOutlinePath;
            return __result == null;
        }
    }
}
