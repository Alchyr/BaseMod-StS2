using BaseLib.HotReload;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace BaseLib.Commands;

/// <summary>
/// Console command to hot reload a mod assembly at runtime.
///
/// Usage:
///   hotreload MyMod                    — reload by mod ID (auto-detects tier from folder contents)
///   hotreload E:/mods/MyMod/MyMod.dll  — reload a specific DLL path
///   hotreload MyMod 3                  — reload with explicit tier 3 (includes PCK remount)
///   hotreload MyMod 1                  — reload patches only (no entity refresh)
/// </summary>
public class HotReloadCommand : AbstractConsoleCmd
{
    public override string CmdName => "hotreload";
    public override string Args => "<dll_path_or_mod_id> [tier]";
    public override string Description => "Hot reload a mod assembly (tier: 1=patches, 2=entities, 3=+PCK, 0=auto)";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        // tier=0 means auto-detect (has .pck → 3, otherwise → 2)
        int tier = 0;
        string? target = null;

        // Parse arguments — numbers 1-3 are tier, everything else is the target
        foreach (var arg in args)
        {
            if (int.TryParse(arg, out var t) && t is >= 1 and <= 3)
                tier = t;
            else if (!string.IsNullOrWhiteSpace(arg))
                target = arg;
        }

        if (target == null)
            return new CmdResult(false, "Usage: hotreload <dll_path_or_mod_id> [tier]\nUse hotreload_list to see available mods.");

        HotReloadResult result;

        if (target.Contains('/') || target.Contains('\\') || target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            // Looks like a file path — use tier 2 as default if not specified
            result = HotReloadEngine.Reload(target, tier == 0 ? 2 : tier);
        }
        else
        {
            // Looks like a mod ID — auto-detect tier from directory contents
            result = HotReloadEngine.ReloadByModId(target, tier);
        }

        return new CmdResult(result.Success, result.Summary);
    }
}

/// <summary>
/// Console command to list mods available for hot reload.
///
/// Usage:
///   hotreload_list — show all mod folders with their latest DLL timestamps
/// </summary>
public class HotReloadListCommand : AbstractConsoleCmd
{
    public override string CmdName => "hotreload_list";
    public override string Args => "";
    public override string Description => "List mods available for hot reload";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var modsDir = FindModsDirectory();
        if (modsDir == null)
            return new CmdResult(false, "Could not find game mods directory.");

        var lines = new List<string> { $"Mods in {modsDir}:" };

        foreach (var modDir in Directory.GetDirectories(modsDir).OrderBy(d => d))
        {
            var modName = Path.GetFileName(modDir);
            var dlls = Directory.GetFiles(modDir, "*.dll");
            if (dlls.Length == 0) continue;

            // Find the most recently modified DLL and how long ago it was written
            var latest = dlls.OrderByDescending(File.GetLastWriteTimeUtc).First();
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(latest);
            var ageStr = age.TotalMinutes < 1 ? "just now"
                       : age.TotalMinutes < 60 ? $"{age.TotalMinutes:0}m ago"
                       : age.TotalHours < 24 ? $"{age.TotalHours:0}h ago"
                       : $"{age.TotalDays:0}d ago";

            var hasPck = Directory.GetFiles(modDir, "*.pck").Length > 0;
            var pckTag = hasPck ? " [PCK]" : "";

            lines.Add($"  {modName} — {Path.GetFileName(latest)} ({ageStr}){pckTag}");
        }

        if (lines.Count == 1)
            lines.Add("  (no mods with DLLs found)");

        return new CmdResult(true, string.Join("\n", lines));
    }

    private static string? FindModsDirectory()
    {
        var executable = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (executable == null) return null;
        var gameDir = Path.GetDirectoryName(executable);
        if (gameDir == null) return null;
        var modsDir = Path.Combine(gameDir, "mods");
        return Directory.Exists(modsDir) ? modsDir : null;
    }
}

/// <summary>
/// Console command to show hot reload status and history.
///
/// Usage:
///   hotreload_status — show last reload result and watcher state
/// </summary>
public class HotReloadStatusCommand : AbstractConsoleCmd
{
    public override string CmdName => "hotreload_status";
    public override string Args => "";
    public override string Description => "Show hot reload status and recent history";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var progress = HotReloadEngine.CurrentProgress;
        var history = HotReloadEngine.ReloadHistory;
        var watcher = HotReloadEngine.FileWatcher;

        var lines = new List<string>();

        if (!string.IsNullOrEmpty(progress))
            lines.Add($"In progress: {progress}");
        else
            lines.Add("No reload in progress");

        lines.Add($"Watcher: {(watcher?.IsWatching == true ? "active" : "inactive")}");
        lines.Add($"Reload history: {history.Count} entries");

        if (history.Count > 0)
        {
            var last = history[^1];
            lines.Add($"Last: {last.Summary}");
        }

        return new CmdResult(true, string.Join("\n", lines));
    }
}

/// <summary>
/// Console command to run the hot reload integration test suite.
/// These tests exercise the full pipeline with the live game state.
///
/// Usage:
///   hotreload_test — run all integration tests and report results
/// </summary>
public class HotReloadTestCommand : AbstractConsoleCmd
{
    public override string CmdName => "hotreload_test";
    public override string Args => "";
    public override string Description => "Run hot reload integration tests against live game state";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var (passed, failed, failures) = HotReloadSelfTests.RunIntegrationTests();

        var lines = new List<string>();
        if (failed == 0)
        {
            lines.Add($"All {passed} integration tests passed!");
        }
        else
        {
            lines.Add($"{passed} passed, {failed} FAILED:");
            foreach (var f in failures)
                lines.Add($"  FAIL: {f}");
        }

        return new CmdResult(failed == 0, string.Join("\n", lines));
    }
}
