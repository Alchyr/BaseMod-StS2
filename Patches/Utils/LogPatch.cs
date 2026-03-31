using System.Diagnostics;
using BaseLib.BaseLibScenes;
using BaseLib.Commands;
using BaseLib.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace BaseLib.Patches.Utils;

[HarmonyPatch(typeof(ConsoleLogPrinter), nameof(ConsoleLogPrinter.Print))]
class LogPatch
{
    [HarmonyPrefix]
    static void Prefix(ConsoleLogPrinter __instance, LogLevel logLevel, string text, int skipFrames)
    {
        ++skipFrames;
        string upperInvariant = logLevel.ToString().ToUpperInvariant();
        switch (logLevel)
        {
            case LogLevel.Error:
                var stackTrace = new StackTrace(skipFrames, true);
                NLogWindow.AddLog($"[{upperInvariant}] {text}\n{stackTrace}");
                break;
            case LogLevel.VeryDebug:
            case LogLevel.Load:
            case LogLevel.Debug:
            case LogLevel.Info:
            case LogLevel.Warn:
            default:
                NLogWindow.AddLog($"[{upperInvariant}] {text}");
                break;
        }
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
class NMainMenuReadyOpenLogWindowPatch
{
    private static bool _hasOpenedOnStartup;

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_hasOpenedOnStartup || !BaseLibConfig.OpenLogWindowOnStartup) return;

        _hasOpenedOnStartup = true;
        OpenLogWindow.OpenWindow(stealFocus: false);
    }
}
