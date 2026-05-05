using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace BaseLib.Patches.Localization;

[HarmonyPatch(typeof(LocManager), nameof(LocManager.LoadTable))]
public static partial class SimpleLoc
{
    private static readonly HashSet<string> SimpleLocEnabled = [];

    /// <summary>
    /// Call this in your mod's initializer to enable simple localization processing for all strings in your mod.
    /// </summary>
    /// <param name="modId"></param>
    public static void EnableSimpleLoc(string modId)
    {
        SimpleLocEnabled.Add(modId);
    }

    [HarmonyPostfix]
    static void ProcessSimpleLoc(string path, Dictionary<string, string>? __result)
    {
        if (__result == null) return;

        var pathElements = path.SimplifyPath().Split('/');
        int locIndex;
        for (locIndex = 0; locIndex < pathElements.Length; ++locIndex)
        {
            if (pathElements[locIndex] == "localization") break;
        }

        if (locIndex >= pathElements.Length || locIndex == 0) return;
        var modFolder = pathElements[locIndex - 1];

        bool modUseSimpleLoc = SimpleLocEnabled.Contains(modFolder);

        foreach (var key in __result.Keys.ToList())
        {
            var processed = __result[key];
            if (processed.StartsWith('#'))
            {
                __result[key] = Simplify(processed[1..]);
            }
            else if (modUseSimpleLoc)
            {
                __result[key] = Simplify(processed);
            }
        }
    }

    [GeneratedRegex(@"(^|[^/])\*(.+?)(?:\*|$|([\s\.|}]))")]
    private static partial Regex GoldHighlightRegex { get; }

    [GeneratedRegex(@"(^|[^/])\$(.+?)(?:\$|$|([\s\.|}]))")]
    static partial Regex BlueHighlightRegex { get; }

    [GeneratedRegex(@"({)([^:}.]+)([:}])")] private static partial Regex NormalVariableRegex { get; }
    [GeneratedRegex(@"!(.*?)!")] private static partial Regex DiffVariableRegex { get; }
    [GeneratedRegex(@"@(.*?)@")] private static partial Regex InverseVariableRegex { get; }
    
    [GeneratedRegex(@"(?:(?:-(.+?)-)|(?:\+(.+?)\+))(?:\+(.+?)\+)?")] 
    private static partial Regex UpgradeSwapRegex { get; }

    [GeneratedRegex(@"(.*{)([^{]+?)((?::.*)?}.*?)\(([^()]+?)\)")]
    private static partial Regex PluralizeRegex { get; }
    
    [GeneratedRegex(@"\[(?:(E\?)|(E+))\]")]
    private static partial Regex EnergyIconsRegex { get; }

    private static readonly Dictionary<string, string> SpecialVarDictionary = new()
    {
        { "D", "Damage" },
        { "CD", "CalculatedDamage" },
        { "B", "Block" },
        { "CB", "CalculatedBlock" },
        { "C", "Cards" },
        { "E", "Energy" },
        { "H", "Heal" }
    };

    /// <summary>
    /// Simplify the given string if it starts with `#`.
    /// Does not check if mod as a whole has simplified text enabled.
    /// </summary>
    public static string TrySimplify(string loc)
    {
        return !loc.StartsWith('#') ? loc : Simplify(loc[1..]);
    }
    private static string Simplify(string loc)
    {
        if (loc.StartsWith('#')) return loc; //if loc starts with '##' remove first one and cancel simplify
        loc = GoldHighlightRegex.Replace(loc, "$1[gold]$2[/gold]$3");
        loc = BlueHighlightRegex.Replace(loc, "$1[blue]$2[/blue]$3");
        loc = loc.Replace("/*", "*").Replace("/$", "$");
        loc = NormalVariableRegex.Replace(loc, match => $"{match.Groups[1].Value}{SpecialVarDictionary.GetValueOrDefault(match.Groups[2].Value, match.Groups[2].Value)}{match.Groups[3].Value}");
        loc = DiffVariableRegex.Replace(loc, match => ReplaceVarName(match, ":diff()"));
        loc = InverseVariableRegex.Replace(loc, match => ReplaceVarName(match, ":inverseDiff()"));
        loc = EnergyIconsRegex.Replace(loc, MakeEnergyIcons);
        loc = PluralizeRegex.Replace(loc, "$1$2$3{$2:plural:|$4}"); //Plural first so that upgrade var is not considered
        loc = UpgradeSwapRegex.Replace(loc, MakeUpgradeSwap);
        
        BaseLibMain.Logger.Info($"SimplifiedLoc: {loc}");
            
        return loc;
    }

    private static string MakeEnergyIcons(Match match)
    {
        if (match.Groups[1].Length > 0)
        {
            return "{Energy:energyIcons()}";
        }
        var count = match.Groups[2].Length;
        return count == 0 ? match.Value : $"{{energyPrefix:energyIcons({count})}}";
    }

    private static string MakeUpgradeSwap(Match match)
    {
        var removeText = match.Groups[1].Value;
        var addText = match.Groups[2].Value + match.Groups[3].Value;
        return $"{{IfUpgraded:show:{addText}|{removeText}}}";
    }

    private static string ReplaceVarName(Match match, string processor)
    {
        if (match.Groups.Count <= 1) return match.Value;

        var varText = match.Groups[1].Value;
        return $"{{{SpecialVarDictionary.GetValueOrDefault(varText, varText)}{processor}}}";
    }
}