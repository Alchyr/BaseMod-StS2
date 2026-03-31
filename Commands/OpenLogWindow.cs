using BaseLib.BaseLibScenes;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;

namespace BaseLib.Commands;

public class OpenLogWindow : AbstractConsoleCmd
{
    public override string CmdName => "showlog";
    public override string Args => "";
    public override string Description => "Open log display window";
    public override bool IsNetworked => false;
    
    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        OpenWindow(stealFocus: true);
        return new CmdResult(true, "Opened log window.");
    }

    public static void OpenWindow(bool stealFocus)
    {
        var instance = NGame.Instance;
        if (instance == null) return;
        
        Window window = instance.GetWindow();
        window.GuiEmbedSubwindows = false;
        
        var scene = PreloadManager.Cache.GetScene("res://BaseLib/scenes/LogWindow.tscn").Instantiate<NLogWindow>();

        // Prevent flicker on open (open in the final position)
        scene.Visible = false;
        window.AddChildSafely(scene);
        LogWindowPlacement.ApplyHostWindowDefaults(scene, window);
        scene.Visible = true;

        if (!stealFocus)
            window.GrabFocus();
    }
}