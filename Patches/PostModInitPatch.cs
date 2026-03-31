using System.Reflection;
using BaseLib.Extensions;
using BaseLib.Patches.Features;
using BaseLib.Patches.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Patches;

//Simplest patch that occurs after mod initialization, before anything else is done
[HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))]
class PostModInitPatch
{
    private static Harmony? _postModInitHarmony;

    [HarmonyPrefix]
    private static void ProcessModdedTypes()
    {
        _postModInitHarmony = new Harmony("PostModInit");
        ProcessTypes(ReflectionHelper.ModTypes);
        SavedSpireFieldPatch.AddFieldsSorted();
    }

    /// <summary>
    /// Process a set of types for ModInterop, SavedProperty, and SavedSpireField registration.
    /// Called at startup for all mod types, and again during hot reload for new assembly types.
    /// </summary>
    internal static void ProcessTypes(IEnumerable<Type> types)
    {
        _postModInitHarmony ??= new Harmony("PostModInit");
        ModInterop interop = new();

        foreach (var type in types)
        {
            interop.ProcessType(_postModInitHarmony, type);

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
                SavedSpireFieldPatch.CheckSavedSpireField(field);
            }

            if (hasSavedProperty)
            {
                SavedPropertiesTypeCache.InjectTypeIntoCache(type);
            }
        }
    }

}