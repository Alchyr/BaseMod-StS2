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

    internal static void ApplyHostWindowDefaults(NLogWindow logWindow, Window host)
    {
        logWindow.CurrentScreen = host.CurrentScreen;
        if (host.ContentScaleFactor > 0f)
            logWindow.ContentScaleFactor = host.ContentScaleFactor;

        if (BaseLibConfig.LogLastSizeX > 0 && BaseLibConfig.LogLastSizeY > 0)
            logWindow.Size = new Vector2I(BaseLibConfig.LogLastSizeX, BaseLibConfig.LogLastSizeY);
        else
            logWindow.Size = ComputeDefaultSize(host.Size);

        // No save here: if the window is left as-is, it will return to its old position if available next time
        if (!TryRestorePosition(logWindow))
            logWindow.MoveToCenter();
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
        if (x == 0 && y == 0)
            return false;

        var center = new Vector2I(x + logWindow.Size.X / 2, y + logWindow.Size.Y / 2);

        for (var i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            if (!DisplayServer.ScreenGetUsableRect(i).HasPoint(center)) continue;

            logWindow.Position = new Vector2I(x, y);
            return true;
        }

        return false;
    }
}
