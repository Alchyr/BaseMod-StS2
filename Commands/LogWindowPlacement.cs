using BaseLib.BaseLibScenes;
using BaseLib.Config;
using Godot;

namespace BaseLib.Commands;

/// <summary>
/// Sizes and positions the log window relative to the game window so ultrawide / multi-monitor
/// setups do not inherit a full-screen-width two-thirds rectangle.
/// </summary>
internal static class LogWindowPlacement
{
    internal static void SetupPosition(NLogWindow logWindow, Window host)
    {
        var targetScreen = host.CurrentScreen;
        bool restoredPosition = TryRestorePosition(logWindow);
        
        if (!restoredPosition)
        {
            var screenCount = DisplayServer.GetScreenCount();
            if (screenCount > 1)
            {
                for (var i = 0; i < screenCount; ++i)
                {
                    if (i == targetScreen) continue;
                
                    targetScreen = i;
                    break;
                }
            }
            logWindow.CurrentScreen = targetScreen;
        }
        else
        {
            targetScreen = logWindow.CurrentScreen;
        }
        
        if (host.ContentScaleFactor > 0f)
            logWindow.ContentScaleFactor = host.ContentScaleFactor;
        
        var screenRect = DisplayServer.ScreenGetUsableRect(targetScreen);

        if (BaseLibConfig.LogLastSizeX > 0 && BaseLibConfig.LogLastSizeY > 0 &&
            BaseLibConfig.LogLastSizeX <= screenRect.Size.X && BaseLibConfig.LogLastSizeY <= screenRect.Size.Y)
        {
            logWindow.Size = new Vector2I(BaseLibConfig.LogLastSizeX, BaseLibConfig.LogLastSizeY);
        }
        else
        {
            logWindow.Size = ComputeDefaultSize(targetScreen == host.CurrentScreen ? host.Size : screenRect.Size);
        }
        
        // Restore failed: center the window. MoveToCenter crashes if Visible = false, which we want to prevent a flicker.
        if (!restoredPosition)
        {
            logWindow.Position = screenRect.Position + screenRect.Size / 2 - logWindow.Size / 2;
        }
    }

    /// <summary>
    /// Load a saved position (if any), ensure it is on a visible screen, and restore it.
    /// </summary>
    /// <returns>True if position was valid and restored, otherwise false.</returns>
    private static bool TryRestorePosition(Window logWindow)
    {
        var x = BaseLibConfig.LogLastPosX;
        var y = BaseLibConfig.LogLastPosY;

        // Position not saved; use default
        if (x == int.MinValue && y == int.MinValue)
            return false;

        var logXSize = BaseLibConfig.LogLastSizeX > 0 ? BaseLibConfig.LogLastSizeX : logWindow.Size.X;
        var logYSize = BaseLibConfig.LogLastSizeY  > 0 ? BaseLibConfig.LogLastSizeY : logWindow.Size.Y;

        var center = new Vector2I(x + logXSize / 2, y + logYSize / 2);

        for (var i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            if (!DisplayServer.ScreenGetUsableRect(i).HasPoint(center)) continue;

            logWindow.CurrentScreen = i;
            logWindow.Position = new Vector2I(x, y);
            return true;
        }

        return false;
    }
    
    internal static Vector2I ComputeDefaultSize(Vector2I hostSize)
    {
        if (hostSize.X <= 0 || hostSize.Y <= 0)
            return new Vector2I(800, 600);

        int tw = hostSize.X * 2 / 3;
        int th = hostSize.Y * 2 / 3;

        // Avoid an extremely wide panel on ultrawide / super-ultrawide fullscreen.
        int maxReadableW = Mathf.Clamp((int)(th * 2.35f), 960, 2048);
        tw = Mathf.Min(tw, maxReadableW);

        tw = Mathf.Min(tw, Mathf.Max(320, hostSize.X - 32));
        th = Mathf.Min(th, Mathf.Max(200, hostSize.Y - 32));

        return new Vector2I(tw, th);
    }
}
