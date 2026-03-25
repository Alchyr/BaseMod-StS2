using System.Diagnostics;
using BaseLib.BaseLibScenes;
using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace BaseLib.Patches.Features;

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

public class OpenLogWindow : AbstractConsoleCmd
{
    public override string CmdName => "showlog";
    public override string Args => "";
    public override string Description => "Open log display window";
    public override bool IsNetworked => false;
    
    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        OpenWindow();
        return new CmdResult(true, "Opened log window.");
    }

    public static void OpenWindow()
    {
        var instance = NGame.Instance;
        if (instance == null) return;
        
        Window window = instance.GetWindow();
        window.GuiEmbedSubwindows = false;
        
        var scene = PreloadManager.Cache.GetScene("res://BaseLib/scenes/LogWindow.tscn").Instantiate<NLogWindow>();
        scene.Size = DisplayServer.ScreenGetSize() * 2 / 3;
        window.AddChildSafely(scene);
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
        OpenLogWindow.OpenWindow();
    }
}
