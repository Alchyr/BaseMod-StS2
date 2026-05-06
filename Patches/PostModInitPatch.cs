using System.Reflection;
using BaseLib.Extensions;
using BaseLib.Patches.Content;
using BaseLib.Patches.Features;
using BaseLib.Patches.Utils;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Patches;

//Simplest patch that occurs after mod initialization, before anything else is done.
//See OneTimeInitialization.ExecuteEssential

//TODO - If no mods that modify gameplay and use baselib as a dependency are enabled, exclude basemod models from database
//This would allow features like vitality to be merged.

[HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))] 
class PostModInitPatch
{
    private static bool _anyModModifiesGameplay = false;
    public static bool CanModifyGameplay => _anyModModifiesGameplay;
    
    [HarmonyPrefix]
    private static void ProcessModdedTypes()
    {
        BaseLibMain.Logger.Info("Performing post-mod init patch");

        foreach (var mod in ModManager.GetLoadedMods())
        {
            if (mod.manifest?.affectsGameplay == true)
            {
                _anyModModifiesGameplay = true;
                break;
            }
        }
        
        
        Harmony harmony = new("PostModInit");

        AddActContent.Patch(harmony);
        

        ModInterop interop = new();
        
        foreach (var type in ReflectionHelper.ModTypes)
        {
            interop.ProcessType(harmony, type);

            bool hasSavedProperty = false;
            foreach (var prop in type.GetProperties())
            {
                var savedPropertyAttr = prop.GetCustomAttribute<SavedPropertyAttribute>();
                if (savedPropertyAttr == null) continue;
                if (prop.DeclaringType == null) continue;

                if (prop.DeclaringType.GetRootNamespace() != "MegaCrit")
                {
                    var prefix = prop.DeclaringType.GetRootNamespace() + "_";
                    if (prop.Name.Length < 16 && !prop.Name.StartsWith(prefix))
                    {
                        BaseLibMain.Logger.Warn($"Recommended to add a prefix such as \"{prefix}\" to SavedProperty {prop.Name} for compatibility.");
                    }
                }
                
                hasSavedProperty = true;
            }

            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                CheckSpecialSpireField(field);
            }

            if (hasSavedProperty)
            {
                SavedPropertiesTypeCache.InjectTypeIntoCache(type);
            }
        }

        SavedSpireFieldPatch.AddFieldsSorted();
    }

    private static void CheckSpecialSpireField(FieldInfo field)
    {
        Type fType = field.FieldType;
                
        if (!fType.IsGenericType)
            return;
        
        var genericTypeDef = fType.GetGenericTypeDefinition();

        if (genericTypeDef != typeof(SavedSpireField<,>) &&
            genericTypeDef != typeof(AddedNode<,>) &&
            genericTypeDef != typeof(SpireMethod.SpireMethod<>))
            return;

        field.GetValue(null); //Trigger field initialization
    }
}