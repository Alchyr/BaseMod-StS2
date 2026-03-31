using System.Text.RegularExpressions;

namespace BaseLib.HotReload;

/// <summary>
/// Hot reload builds stamp each assembly with a unique timestamp suffix like _hr143052789.
/// These helpers strip that suffix to recover the canonical mod name, and detect whether
/// a given DLL was produced by a hot reload build.
/// </summary>
internal static class AssemblyStamper
{
    private static readonly Regex HotReloadSuffixRegex = new(@"_hr\d{6,9}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Strip the _hrNNNNNNNNN suffix from an assembly name or file path to get the canonical mod key.
    /// E.g. "MyMod_hr143052789" → "MyMod", "E:/mods/MyMod/MyMod_hr143052789.dll" → "MyMod"
    /// </summary>
    public static string NormalizeModKey(string? assemblyNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyNameOrPath))
            return "";
        var fileOrAssemblyName = Path.GetFileNameWithoutExtension(assemblyNameOrPath);
        return HotReloadSuffixRegex.Replace(fileOrAssemblyName, "");
    }

    /// <summary>
    /// Returns true if this DLL was produced by a hot reload build (has the _hrNNN suffix).
    /// Used by the file watcher to ignore dependency DLLs and manifests.
    /// </summary>
    public static bool IsHotReloadStamped(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return HotReloadSuffixRegex.IsMatch(name);
    }
}
