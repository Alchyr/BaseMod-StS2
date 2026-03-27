using System.Reflection;
using BaseLib.Extensions;
using BaseLib.Patches.Features;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Patches;

//Simplest patch that occurs after mod initialization, before anything else is done
[HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))] 
class PostModInitPatch
{
    [HarmonyPrefix]
    private static void ProcessModdedTypes()
    {
        Harmony harmony = new("PostModInit");

        ModInterop interop = new();
        
        foreach (var t in ReflectionHelper.ModTypes)
        {
            interop.ProcessType(harmony, t);

            bool hasSavedProperty = false;
            foreach (var prop in t.GetProperties())
            {
                var savedPropertyAttr = prop.GetCustomAttribute<SavedPropertyAttribute>();
                if (savedPropertyAttr == null) continue;
                if (prop.DeclaringType == null) continue;

                if (prop.DeclaringType.GetRootNamespace() != "MegaCrit")
                {
                    var prefix = prop.DeclaringType.GetRootNamespace() + "_";
                    if (prop.Name.Length < 16 && !prop.Name.StartsWith(prefix))
                    {
                        MainFile.Logger.Warn($"Recommended to add a prefix such as \"{prefix}\" to SavedProperty {prop.Name} for compatibility.");
                    }
                }
                
                hasSavedProperty = true;
            }

            if (hasSavedProperty)
            {
                SavedPropertiesTypeCache.InjectTypeIntoCache(t);
            }
        }
    }
}