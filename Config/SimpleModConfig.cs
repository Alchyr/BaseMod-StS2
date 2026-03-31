using System.Diagnostics;
using System.Reflection;
using BaseLib.Config.UI;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

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
        BaseLibMain.Logger.Info($"Setting up SimpleModConfig {GetType().FullName}");
        GenerateOptionsForAllProperties(optionContainer);
        AddRestoreDefaultsButton(optionContainer);
    }

    protected void AddRestoreDefaultsButton(Control optionContainer)
    {
        var resetButton = CreateRawButtonControl(GetBaseLibLabelText("RestoreDefaultsButton"), async void () =>
        {
            try
            {
                await ConfirmRestoreDefaults();
            }
            catch (Exception e)
            {
                // Seems exceedingly unlikely, but still
                ModConfigLogger.Error($"Unable to show restore confirmation dialog: {e.Message}");
            }
        });
        resetButton.CustomMinimumSize = new Vector2(360, resetButton.CustomMinimumSize.Y);
        resetButton.SetColor(0.45f, 1.5f, 0.8f);

        var centerContainer = new CenterContainer();
        centerContainer.CustomMinimumSize = new Vector2(0, 128);
        centerContainer.AddChild(resetButton);

        optionContainer.AddChild(centerContainer);
    }

    public async Task ConfirmRestoreDefaults()
    {
        var confirmationModal = NGenericPopup.Create();
        if (confirmationModal == null || NModalContainer.Instance == null) return;
        NModalContainer.Instance.Add(confirmationModal);

        var confirmed = await confirmationModal.WaitForConfirmation(
            body: new LocString("settings_ui", "BASELIB-RESTORE_MODCONFIG_CONFIRMATION.body"),
            header: new LocString("settings_ui", "BASELIB-RESTORE_MODCONFIG_CONFIRMATION.header"),
            noButton: new LocString("main_menu_ui", "GENERIC_POPUP.cancel"),
            yesButton: new LocString("main_menu_ui", "GENERIC_POPUP.confirm")
        );

        if (confirmed)
            RestoreDefaultsNoConfirm();
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

        AddHoverTipToOptionRowIfEnabled(optionRow, property);

        return optionRow;
    }

    /// <summary>
    /// Auto-generates a button row from a method marked with [ConfigButton], including a hover tip if [ConfigHoverTip]
    /// is specified.<br/>
    /// Methods with [ConfigHideinUI] will NOT be ignored, so you can use this to manually create buttons for them
    /// if you wish.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown if [ConfigButton] is missing.</exception>
    protected NConfigOptionRow GenerateButtonRowFromMethod(MethodInfo method)
    {
        var attr = method.GetCustomAttribute<ConfigButtonAttribute>() ?? throw new ArgumentException(
            $"GenerateOptionFromMethod called on {method.Name} but it lacks a [ConfigButton] attribute.");

        // Validate the arguments early, or the exception won't occur until you click the button.
        // Will throw for invalid arguments.
        foreach (var param in method.GetParameters())
            ResolveButtonArgument(param, null!);

        // optionRow must be declared before buttonAction since it's captured by the closure,
        // but it won't be read until the button is clicked, so this is null safe
        NConfigOptionRow optionRow = null!;

        // ReSharper disable once ConvertToLocalFunction
        var onButtonClicked = () =>
        {
            try
            {
                // Like Harmony, we figure out the arguments to pass based on the method's parameter types
                var args = method.GetParameters()
                    .Select(param => ResolveButtonArgument(param, optionRow))
                    .ToArray();

                method.Invoke(method.IsStatic ? null : this, args);
            }
            catch (Exception e)
            {
                BaseLibMain.Logger.Error($"Error executing [ConfigButton] method {method.Name}: {e.Message}");

                // no return; we still need to call ConfigReloaded in case the method changed something
            }

            // Ensure controls are up-to-date in case the button modified some property values
            ConfigReloaded();
            ShowAndClearPendingErrors();
        };

        optionRow = CreateButton(method.Name, attr.ButtonLabelKey, onButtonClicked, true);
        AddHoverTipToOptionRowIfEnabled(optionRow, method);
        return optionRow;
    }

    // Figure out which arguments to pass the method based on its argument types
    protected object ResolveButtonArgument(ParameterInfo param, NConfigOptionRow? optionRow)
    {
        var t = param.ParameterType;

        if (typeof(ModConfig).IsAssignableFrom(t)) return this;
        if (t == typeof(NConfigOptionRow)) return optionRow!;

        // Nullable for the validation loop in GenerateButtonRowFromMethod to work (we don't want a NullPointerException
        // in the validation loop)
        if (t == typeof(NConfigButton)) return optionRow?.SettingControl!;

        throw new ArgumentException(
            $"Unsupported parameter type '{t.Name}' for method {param.Member.Name}.");
    }

    protected void AddHoverTipToOptionRowIfEnabled(NConfigOptionRow row, MemberInfo member)
    {
        // Create a HoverTip for this option row if appropriate
        var propertyHoverAttr = member.GetCustomAttribute<ConfigHoverTipAttribute>();
        var classHoverAttr = GetType().GetCustomAttribute<HoverTipsByDefaultAttribute>();

        var hoverTipsByDefault = classHoverAttr != null;
        var explicitHoverAttrEnabled = propertyHoverAttr?.Enabled;

        if (explicitHoverAttrEnabled ?? hoverTipsByDefault)
        {
            row.AddHoverTip();
        }
    }

    /// <summary>
    /// <para>Auto-generate option rows for all properties in this SimpleModConfig. Runs by default, so that a subclass
    /// only needs to add its config properties, and nothing more, to get a reasonable UI.</para>
    /// Properties marked with [ConfigHideInUI] will be ignored. Properties marked with [ConfigIgnore] won't even make
    /// it to this method.<br/>
    /// Methods marked with [ConfigButton] will generate buttons.
    /// </summary>
    /// <param name="targetContainer">Container where the generated options are inserted.</param>
    protected void GenerateOptionsForAllProperties(Control targetContainer)
    {
        Control? currentSetting = null;
        string? currentSection = null;

        // Fetch properties AND methods in the order they appear in the source file.
        // Instance is supported for methods, but not properties (which must be static); they are filtered in
        // ModConfig.CheckConfigProperties, and will be filtered out by IsVisibleMember().
        var filteredMembers = GetType()
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Where(IsVisibleMember)
            .OrderBy(GetSourceOrder)
            .ToList();

        for (var i = 0; i < filteredMembers.Count; i++)
        {
            var member = filteredMembers[i];
            var nextMember = i < filteredMembers.Count - 1 ? filteredMembers[i + 1] : null;

            // Create a section header if this property starts a new section
            var sectionName = member.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
            if (sectionName != null && sectionName != currentSection)
            {
                currentSection = sectionName;
                var isFirstChild = targetContainer.GetChildCount() == 0;
                targetContainer.AddChild(CreateSectionHeader(currentSection, alignToTop: isFirstChild));
            }

            // Generate the option row and set up focus handling
            try
            {
                var newRow = member switch
                {
                    PropertyInfo p => GenerateOptionFromProperty(p),
                    MethodInfo m => GenerateButtonRowFromMethod(m),
                    _ => throw new UnreachableException("Invalid type that should have been filtered out")
                };
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
                BaseLibMain.Logger.Error($"Not creating UI for unsupported property '{member.Name}': {ex.Message}");
                continue;
            }

            // Add a divider unless the next property starts a new section (or there is no next)
            var nextSectionName = nextMember?.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
            var nextIsSameSection = nextSectionName == null || nextSectionName == currentSection;
            if (nextMember != null && nextIsSameSection)
            {
                targetContainer.AddChild(CreateDividerControl());
            }
        }

        return;

        bool IsVisibleMember(MemberInfo member) => member switch
        {
            PropertyInfo p => ConfigProperties.Contains(p) && p.GetCustomAttribute<ConfigHideInUI>() == null,
            MethodInfo m => m.GetCustomAttribute<ConfigButtonAttribute>() != null,
            _ => false
        };

        int GetSourceOrder(MemberInfo member) => member switch
        {
            // Properties and methods are in different tables in the assembly, but since getters and setters are methods,
            // we can sort by looking at those instead.
            MethodInfo m => m.MetadataToken,
            PropertyInfo p => p.GetMethod?.MetadataToken ?? p.SetMethod?.MetadataToken ?? 0,
            _ => 0
        };
    }
}