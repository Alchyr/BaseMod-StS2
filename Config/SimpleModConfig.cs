using System.Reflection;
using BaseLib.Config.UI;
using BaseLib.Extensions;
using Godot;

// ReSharper disable MemberCanBePrivate.Global

namespace BaseLib.Config;

public class SimpleModConfig : ModConfig
{
    /// <summary>
    /// Auto-generate a UI from the properties used. Should be enough for the vast majority of mods,
    /// but you can also subclass SimpleModConfig and override this to get access to helpers like
    /// <see cref="CreateToggleOption"/> (in addition to the raw Create*Control methods from ModConfig),
    /// without an auto-generated UI.
    /// </summary>
    public override void SetupConfigUI(Control optionContainer)
    {
        MainFile.Logger.Info($"Setting up SimpleModConfig {GetType().FullName}");
        GenerateOptionsForAllProperties(optionContainer);
    }

    /// <inheritdoc cref="CreateStandardOption"/>
    protected NConfigOptionRow CreateToggleOption(PropertyInfo property, bool addHoverTip = false) =>
        CreateStandardOption(CreateRawTickboxControl, property, addHoverTip);

    /// <inheritdoc cref="CreateStandardOption"/>
    protected NConfigOptionRow CreateSliderOption(PropertyInfo property, bool addHoverTip = false) =>
        CreateStandardOption(CreateRawSliderControl, property, addHoverTip);

    /// <inheritdoc cref="CreateStandardOption"/>
    protected NConfigOptionRow CreateDropdownOption(PropertyInfo property, bool addHoverTip = false) =>
        CreateStandardOption(CreateRawDropdownControl, property, addHoverTip);

    /// <inheritdoc cref="CreateStandardOption"/>
    protected NConfigOptionRow CreateLineEditOption(PropertyInfo property, bool addHoverTip = false) =>
        CreateStandardOption(CreateRawLineEditControl, property, addHoverTip);

    /// <summary>
    /// Creates a button that can be mapped to perform any action.
    /// </summary>
    /// <param name="rowLabelKey">LocString key for the row label (shown where setting names are shown).</param>
    /// <param name="buttonLabelKey">LocString key for the button's label text.</param>
    /// <param name="onPressed">Action to perform when clicked/pressed.</param>
    /// <param name="addHoverTip">If true, generates a localized hover tip; the localization key name is based on rowLabelKey.</param>
    protected NConfigOptionRow CreateButton(string rowLabelKey, string buttonLabelKey, Action onPressed, bool addHoverTip = false)
    {
        var control = CreateRawButtonControl(GetLabelText(buttonLabelKey), onPressed);
        var label = CreateRawLabelControl(GetLabelText(rowLabelKey), 28);
        var option = new NConfigOptionRow(ModPrefix, rowLabelKey, label, control);
        if (addHoverTip) option.AddHoverTip();
        return option;
    }

    /// <summary>
    /// Creates a layout-ready section header row.
    /// </summary>
    protected MarginContainer CreateSectionHeader(string labelName, bool alignToTop = false)
    {
        MarginContainer container = new();
        container.Name = "Container_" + labelName.Replace(" ", "");
        container.AddThemeConstantOverride("margin_left", 24);
        container.AddThemeConstantOverride("margin_right", 24);
        container.MouseFilter = Control.MouseFilterEnum.Ignore;
        container.FocusMode = Control.FocusModeEnum.None;

        var label = CreateRawLabelControl($"[center][b]{GetLabelText(labelName)}[/b][/center]", 40);
        label.Name = "SectionLabel_" + labelName.Replace(" ", "");
        label.CustomMinimumSize = new Vector2(0, 64);

        if (alignToTop) label.VerticalAlignment = VerticalAlignment.Top;

        container.AddChild(label);
        return container;
    }

    /// <summary>
    /// <para>Creates a standard configuration row containing a label and an option control. It has default margins
    /// and optionally a hover tip (see <see cref="NConfigOptionRow.AddHoverTip()"/> for requirements).</para>
    /// <para>You likely only need to call this if you create a custom control and want to use the default font/margin
    /// settings for it.</para>
    /// </summary>
    /// <param name="controlCreator"/>
    /// <param name="property">The property this option represents.</param>
    /// <param name="addHoverTip">If true, automatically attaches a localized tooltip.</param>
    /// <returns>A fully configured <see cref="NConfigOptionRow"/>, ready to insert with AddChild.</returns>
    protected NConfigOptionRow CreateStandardOption(Func<PropertyInfo, Control> controlCreator, PropertyInfo property, bool addHoverTip = false)
    {
        var control = controlCreator.Invoke(property);
        var label = CreateRawLabelControl(GetLabelText(property.Name), 28);
        var option = new NConfigOptionRow(ModPrefix, property.Name, label, control);
        if (addHoverTip) option.AddHoverTip();
        return option;
    }

    /// <summary>
    /// Auto-generates a UI row from a property, including a hover tip if [ConfigHoverTip] is specified.<br/>
    /// Properties with [ConfigHideinUI] will NOT be ignored, so you can use this to manually create them if you wish.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown for non-supported property types.</exception>
    protected NConfigOptionRow GenerateOptionFromProperty(PropertyInfo property)
    {
        var propertyType = property.PropertyType;

        NConfigOptionRow optionRow;
        if (propertyType == typeof(bool)) optionRow = CreateToggleOption(property);
        else if (propertyType == typeof(double)) optionRow = CreateSliderOption(property);
        else if (propertyType == typeof(string)) optionRow = CreateLineEditOption(property);
        else if (propertyType.IsEnum) optionRow = CreateDropdownOption(property);
        else throw new NotSupportedException($"Type {propertyType.FullName} is not supported by SimpleModConfig.");

        // Create a HoverTip for this option row if appropriate
        var propertyHoverAttr = property.GetCustomAttribute<ConfigHoverTipAttribute>();
        var classHoverAttr = GetType().GetCustomAttribute<HoverTipsByDefaultAttribute>();

        var hoverTipsByDefault = classHoverAttr != null;
        var explicitHoverAttrEnabled = propertyHoverAttr?.Enabled;

        if (explicitHoverAttrEnabled ?? hoverTipsByDefault)
        {
            optionRow.AddHoverTip();
        }

        return optionRow;
    }

    /// <summary>
    /// <para>Auto-generate option rows for all properties in this SimpleModConfig. Runs by default, so that a subclass
    /// only needs to add its config properties, and nothing more, to get a reasonable UI.</para>
    /// Properties marked with [ConfigHideInUI] will be ignored. Properties marked with [ConfigIgnore] won't even make
    /// it to this method.
    /// </summary>
    /// <param name="targetContainer">Container where the generated options are inserted.</param>
    protected void GenerateOptionsForAllProperties(Control targetContainer)
    {
        Control? currentSetting = null;
        string? currentSection = null;

        var properties = ConfigProperties.Where(prop =>
            prop.GetCustomAttribute<ConfigHideInUI>() == null).ToArray();

        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            var nextProperty = i < properties.Length - 1 ? properties[i + 1] : null;

            // Create a section header if this property starts a new section
            var sectionName = property.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
            if (sectionName != null && sectionName != currentSection)
            {
                currentSection = sectionName;
                var isFirstChild = targetContainer.GetChildCount() == 0;
                targetContainer.AddChild(CreateSectionHeader(currentSection, alignToTop: isFirstChild));
            }

            // Generate the option row and set up focus handling
            try
            {
                var newRow = GenerateOptionFromProperty(property);
                targetContainer.AddChild(newRow);

                var previousSetting = currentSetting;
                currentSetting = newRow.SettingControl;

                if (previousSetting != null)
                {
                    currentSetting.FocusNeighborTop = currentSetting.GetPathTo(previousSetting);
                    previousSetting.FocusNeighborBottom = previousSetting.GetPathTo(currentSetting);
                }

                currentSetting.FocusNeighborLeft = currentSetting.GetPath();
                currentSetting.FocusNeighborRight = currentSetting.GetPath();
            }
            catch (NotSupportedException ex)
            {
                MainFile.Logger.Error($"Not creating UI for unsupported property '{property.Name}': {ex.Message}");
                continue;
            }

            // Add a divider unless the next property starts a new section (or there is no next)
            var nextSectionName = nextProperty?.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
            var nextIsSameSection = nextSectionName == null || nextSectionName == currentSection;
            if (nextProperty != null && nextIsSameSection)
            {
                targetContainer.AddChild(CreateDividerControl());
            }
        }
    }
}