using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

namespace BaseLib.HotReload;

/// <summary>
/// Tracks per-mod hot reload state across successive reloads.
///
/// Each mod gets one session that persists for the entire game session.
/// When a mod is reloaded, we need to know:
/// - Which ALC was used (so we can unload it if it was collectible)
/// - Which Harmony instance was used (so we can unpatch the old patches)
/// - Which assembly was loaded (so we can identify stale types)
/// </summary>
internal sealed class HotReloadSession
{
    /// <summary>The canonical mod name with _hr suffix stripped.</summary>
    public string ModKey { get; }

    /// <summary>The ALC used for the most recent reload (null for default ALC).</summary>
    public AssemblyLoadContext? LoadContext { get; set; }

    /// <summary>The assembly from the most recent reload.</summary>
    public Assembly? LastLoadedAssembly { get; set; }

    /// <summary>The Harmony instance from the most recent reload (unique ID per reload).</summary>
    public Harmony? HotReloadHarmony { get; set; }

    public HotReloadSession(string modKey)
    {
        ModKey = modKey;
    }
}
