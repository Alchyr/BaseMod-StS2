using Godot;

namespace BaseLib.Extensions;

public static class ControlExtensions
{
    /// <summary>
    /// Draws the area of a Control.
    /// An easy way to use for debugging is by adding it to the Control's Draw event.
    /// eg. control.Draw += control.DrawDebug;
    /// </summary>
    /// <param name="item"></param>
    public static void DrawDebug(this Control item)
    {
        item.DrawRect(new Rect2(0, 0, item.Size), new Color(1, 1, 1, 0.5f));
    }
    public static void DrawDebug(this Control artist, Control child)
    {
        artist.DrawRect(new Rect2(child.Position, child.Size), new Color(1, 1, 1, 0.5f));
    }

    /// <summary>
    /// Calls AddThemeFontSizeOverride() for all font types: font_size, {normal,bold,italics,bold_italics,mono}_font_size.
    /// </summary>
    public static void AddThemeFontSizeOverrideAll(this Control control, int fontSize)
    {
        string[] fontTypes = [
            "font_size",
            "normal_font_size",
            "bold_font_size",
            "italics_font_size",
            "bold_italics_font_size",
            "mono_font_size"
        ];

        foreach (var fontType in fontTypes)
        {
            control.AddThemeFontSizeOverride(fontType, fontSize);
        }
    }

    private static readonly NodePath EmptyNodePath = new();
    public static void ClearFocusNeighbors(this Control control)
    {
        control.FocusNeighborTop = EmptyNodePath;
        control.FocusNeighborBottom = EmptyNodePath;
        control.FocusNeighborLeft = EmptyNodePath;
        control.FocusNeighborRight = EmptyNodePath;
    }
}