using BaseLib.Abstracts;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Localization;

[HarmonyPatch(typeof(AncientDialogueSet), nameof(AncientDialogueSet.PopulateLocKeys))]
class AddAncientDialogues
{
    [HarmonyPrefix]
    static void AddCharacterDefinedInteractions(AncientDialogueSet __instance, string ancientEntry)
    {
        BaseLibMain.Logger.Info($"Checking for additional interactions with {ancientEntry}");
        var characterDialogues = __instance.CharacterDialogues;
        
        foreach (var character in ModelDb.AllCharacters)
        {
            if (character is not CustomCharacterModel) continue;
            
            var baseKey = AncientDialogueUtil.BaseLocKey(ancientEntry, character.Id.Entry);
            var currentDialogues = characterDialogues.GetValueOrDefault(baseKey, []);
            var newDialogues = AncientDialogueUtil.GetDialoguesForKey("ancients", baseKey);
            
            if (newDialogues.Count > 0)
            {
                characterDialogues[character.Id.Entry] = [..currentDialogues, ..newDialogues];
                BaseLibMain.Logger.Info($"Found {newDialogues.Count} additional dialogues for {ancientEntry} with {character.Id.Entry}, total {characterDialogues[character.Id.Entry].Count}");
            }
        }
    }
}