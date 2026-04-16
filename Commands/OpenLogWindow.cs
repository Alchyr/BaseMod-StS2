using BaseLib.BaseLibScenes;
using Godot;
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
        if (!BaseLibMain.IsMainThread) throw new InvalidOperationException("OpenWindow called when not on main thread");
        var instance = NGame.Instance;
        if (instance == null) return;

        try
        {
            Window window = instance.GetWindow();
            window.GuiEmbedSubwindows = false;
            var scene = ResourceLoader.Load<PackedScene>("res://BaseLib/scenes/LogWindow.tscn").Instantiate<NLogWindow>();

            // Prevent flicker on open (open in the final position)
            scene.Visible = false;
            window.AddChildSafely(scene);
            LogWindowPlacement.SetupPosition(scene, window);
            scene.Visible = true;

            if (!stealFocus)
                window.GrabFocus();
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Info($"Failed to open log window: {e}");
        }
    }
}