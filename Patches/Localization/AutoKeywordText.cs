using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace BaseLib.Patches.Localization;

[HarmonyPatch(typeof(CardKeywordOrder), MethodType.StaticConstructor)]
public class AutoKeywordText
{
    public static readonly List<CardKeyword> AdditionalBeforeKeywords = [];
    public static readonly List<CardKeyword> AdditionalAfterKeywords = [];
    
    [HarmonyPostfix]
    static void Postfix(ref CardKeyword[] ___beforeDescription, ref CardKeyword[] ___afterDescription)
    {
        //I think this shouldn't work but it does.
        ___beforeDescription = [.. ___beforeDescription, .. AdditionalBeforeKeywords];
        ___afterDescription = [.. ___afterDescription, .. AdditionalAfterKeywords];
    }
    
    /*[HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        
    }*/
}