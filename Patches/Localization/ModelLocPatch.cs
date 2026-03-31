using System.Reflection;
using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Timeline;

namespace BaseLib.Patches.Localization;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Init))]
class ModelLocPatch
{
    private static readonly Dictionary<string, string?> CategoryToLocTable = new()
    {
        { ModelId.SlugifyCategory(nameof(ActModel)), "acts" },
        { ModelId.SlugifyCategory(nameof(AfflictionModel)), "afflictions" },
        { ModelId.SlugifyCategory(nameof(CardModel)), "cards" },
        { ModelId.SlugifyCategory(nameof(CharacterModel)), "characters" },
        { ModelId.SlugifyCategory(nameof(EnchantmentModel)), "enchantments" },
        { ModelId.SlugifyCategory(nameof(EncounterModel)), "encounters" },
        { ModelId.SlugifyCategory(nameof(EpochModel)), "epochs" }, //Does not inherit AbstractModel and so currently will not function. Here for reference purposes.
        { ModelId.SlugifyCategory(nameof(ModifierModel)), "modifiers" },
        { ModelId.SlugifyCategory(nameof(MonsterModel)), "monsters" },
        { ModelId.SlugifyCategory(nameof(OrbModel)), "orbs" },
        { ModelId.SlugifyCategory(nameof(PotionModel)), "potions" },
        { ModelId.SlugifyCategory(nameof(PowerModel)), "powers" },
        { ModelId.SlugifyCategory(nameof(RelicModel)), "relics" },
        { ModelId.SlugifyCategory(nameof(DynamicVar)), "static_hover_tips" } //Does not inherit AbstractModel and so currently will not function. Here for reference purposes.
    };
    
    //TODO - also check for ILocalizationProviders from other classes?

    private static readonly FieldInfo LocDictionaryField = AccessTools.Field(typeof(LocTable), "_translations");
    
    [HarmonyPostfix]
    static void AddModelLoc(Dictionary<ModelId, AbstractModel> ____contentById)
    {
        foreach (KeyValuePair<ModelId, AbstractModel> content in ____contentById)
        {
            if (content.Value is ILocalizationProvider locProvider)
            {
                var loc = locProvider.Localization;
                if (loc == null) continue;
                
                var table = locProvider.LocTable
                               ?? CategoryToLocTable.GetValueOrDefault(content.Key.Category, null)
                               ?? throw new Exception("Override LocTable in your ILocalizationProvider.");
                var locTable = LocManager.Instance.GetTable(table);
                var dict = LocDictionaryField.GetValue(locTable) as Dictionary<string, string> 
                           ?? throw new Exception("Failed to get localization dictionary.");
                
                string key = content.Key.Entry;
                foreach (var locEntry in loc)
                {
                    dict[$"{key}.{locEntry.Item1}"] = locEntry.Item2;
                }
            }
        }
    }
}