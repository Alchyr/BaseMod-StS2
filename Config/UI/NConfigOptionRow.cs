using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace BaseLib.Config.UI;

// ReSharper disable once Godot.MissingParameterlessConstructor

// Wrapper class that takes a control (e.g. toggle, slider) and adds a label and layout with margins to it,
// while allowing for a HoverTip.
public partial class NConfigOptionRow : MarginContainer
{
    public Control SettingControl { get; private set; }
    private HoverTip? _hoverTip;
    private bool _hoverTipVisible;
    private readonly string _modPrefix;

    public NConfigOptionRow(string modPrefix, string name, Control label, Control settingControl)
    {
        _modPrefix = modPrefix;
        Name = name;
        SettingControl = settingControl;

        AddThemeConstantOverride("margin_left", 12);
        AddThemeConstantOverride("margin_right", 12);
        MouseFilter = MouseFilterEnum.Pass;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(0, 64);

        label.CustomMinimumSize = new Vector2(0, 64);

        AddChild(label);
        AddChild(settingControl);
    }

    [Obsolete("Use the constructor taking 'string name' instead.")]
    public NConfigOptionRow(string modPrefix, PropertyInfo property, Control label, Control settingControl)
        : this(modPrefix, property.Name, label, settingControl)
    {
    }

    /// <summary>
    /// Adds a HoverTip based on custom LocStrings. See <see cref="AddHoverTip"/> to use LocStrings based on the
    /// setting's property name.
    /// </summary>
    /// <param name="titleEntryKey">The LocString entry to use for the title (can be null)</param>
    /// <param name="descriptionEntryKey">The LocString entry to use for the description</param>
    public void AddCustomHoverTip(string? titleEntryKey, string descriptionEntryKey)
    {
        _hoverTip = titleEntryKey != null
            ? new HoverTip(title: new LocString("settings_ui", titleEntryKey), description: new LocString("settings_ui", descriptionEntryKey))
            : new HoverTip(new LocString("settings_ui", descriptionEntryKey));
    }

    /// <summary>
    /// <para>Adds a HoverTip to this row, using predefined entries in the localization file.<br/>
    /// Localization is required; the game's HoverTip class doesn't let you use fixed strings.
    /// The names used are typically:</para>
    /// YOURMOD-PROPERTY_NAME.hover.title (optional, if missing, no title will be shown)<br/>
    /// YOURMOD-PROPERTY_NAME.hover.desc (required)
    /// </summary>
    public void AddHoverTip()
    {
        var descriptionEntryKey = _modPrefix + StringHelper.Slugify(Name) + ".hover.desc";

        if (!LocString.Exists("settings_ui", descriptionEntryKey))
        {
            BaseLibMain.Logger.Warn($"{descriptionEntryKey} not found in settings_ui.json; skipping HoverTip.");
            return;
        }

        var explicitTitleKey = _modPrefix + StringHelper.Slugify(Name) + ".hover.title";
        var fallbackTitleKey = _modPrefix + StringHelper.Slugify(Name) + ".title";
        var titleKey = LocString.Exists("settings_ui", fallbackTitleKey) ? fallbackTitleKey : null;

        if (LocString.Exists("settings_ui", explicitTitleKey))
        {
            var hasText = LocString.GetIfExists("settings_ui", explicitTitleKey)?.GetFormattedText().Length > 0;
            titleKey = hasText ? explicitTitleKey : null;
        }

        AddCustomHoverTip(titleKey, descriptionEntryKey);
    }

    /// <summary>
    /// Removes a hover tip, if present.
    /// </summary>
    public void RemoveHoverTip()
    {
        _hoverTip = null;
    }

    public override void _Process(double delta)
    {
        if (_hoverTip == null || !IsVisibleInTree()) return;

        var hoveredControl = GetViewport().GuiGetHoveredControl();
        var shouldShowHoverTip = hoveredControl != null && (hoveredControl == this || IsAncestorOf(hoveredControl));

        if (shouldShowHoverTip)
        {
            // Exception: don't show if we're hovering a blocker/dismisser (used by e.g. NConfigDropdown when open),
            // unless the mouse pointer is also inside this row.
            var viewportSize = GetViewport().GetVisibleRect().Size;
            if (hoveredControl!.Size.X >= viewportSize.X * 0.8f && hoveredControl.Size.Y >= viewportSize.Y * 0.8f)
                shouldShowHoverTip = GetGlobalRect().HasPoint(GetGlobalMousePosition());
        }

        if (shouldShowHoverTip && !_hoverTipVisible) OnHovered();
        else if (!shouldShowHoverTip && _hoverTipVisible) OnUnhovered();

        _hoverTipVisible = shouldShowHoverTip;
    }

    private void OnHovered()
    {
        if (_hoverTip == null) return;

        var tipSet = NHoverTipSet.CreateAndShow(this, _hoverTip);

        var containerField = AccessTools.Field(typeof(NHoverTipSet), "_textHoverTipContainer");
        if (containerField?.GetValue(tipSet) is not VFlowContainer textContainer) return;

        var hoverTipWidth = 360f; // Fallback
        var hoverTipHeight = textContainer.Size.Y;

        // The container lies: wider hover tips will render outside the screen if we read its width (height is OK)
        foreach (var child in textContainer.GetChildren())
        {
            if (child is Control childControl)
                hoverTipWidth = Mathf.Max(hoverTipWidth, childControl.Size.X);
        }

        // Try to show the hover tip over the next row
        var screenSize = GetViewportRect().Size;
        tipSet.GlobalPosition = new Vector2(screenSize.X - hoverTipWidth, GlobalPosition.Y + 80f);

        // There isn't enough space below; show above instead
        if (tipSet.GlobalPosition.Y + hoverTipHeight > screenSize.Y)
            tipSet.GlobalPosition = new Vector2(screenSize.X - hoverTipWidth, GlobalPosition.Y - hoverTipHeight - 12f);
    }

    private void OnUnhovered()
    {
        if (_hoverTip == null) return;
        NHoverTipSet.Remove(this);
    }
}