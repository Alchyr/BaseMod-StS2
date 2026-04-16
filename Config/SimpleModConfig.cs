using System.Diagnostics;
using System.Reflection;
using BaseLib.Config.UI;
using BaseLib.Extensions;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

// ReSharper disable MemberCanBePrivate.Global

namespace BaseLib.Config;

public class SimpleModConfig : ModConfig
{
    /// <summary>
    /// Lists to keep track of handlers created in order to properly to dispose.
    /// </summary>
    protected readonly List<EventHandler> _configChangedHandlers = new();
    protected readonly List<Action> _configReloadedHandlers = new();

    private static readonly NodePath selfNodePath = new(".");
    
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
        SetupFocusNeighbors(optionContainer);
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
        resetButton.SetColor(Color.FromHtml("#b03f3f"));

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

    /// <inheritdoc cref="CreateStandardOption"/>
    protected NConfigOptionRow CreateColorPickerOption(PropertyInfo property, bool addHoverTip = false) =>
    CreateStandardOption(CreateRawColorPickerControl, property, addHoverTip);

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
        control.ClearFocusNeighbors();
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
        control.ClearFocusNeighbors();
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
        var hasColorPickerAttribute = property.GetCustomAttribute<ConfigColorPickerAttribute>() != null;
        NConfigOptionRow optionRow;

        if (propertyType == typeof(bool))
            optionRow = CreateToggleOption(property);
        else if (propertyType == typeof(Color) || (propertyType == typeof(string) && hasColorPickerAttribute))
            optionRow = CreateColorPickerOption(property);
        else if (propertyType == typeof(string))
            optionRow = CreateLineEditOption(property);
        else if (NConfigSlider.SupportedTypes.Contains(propertyType))
            optionRow = CreateSliderOption(property);
        else if (propertyType.IsEnum)
            optionRow = CreateDropdownOption(property);
        else
            throw new NotSupportedException($"Type {propertyType.FullName} is not supported by SimpleModConfig.");

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
                BaseLibMain.Logger.Error($"Error executing [ConfigButton] method {method.Name}:\n{e}");

                // no return; we still need to call ConfigReloaded in case the method changed something
            }

            // Ensure controls are up-to-date in case the button modified some property values
            ConfigReloaded();
            ShowAndClearPendingErrors();
        };

        optionRow = CreateButton(method.Name, attr.ButtonLabelKey, onButtonClicked);
        if (optionRow.SettingControl is NConfigButton button) button.SetColor(Color.FromHtml(attr.Color));
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
        var classHoverAttr = GetType().GetCustomAttribute<ConfigHoverTipsByDefaultAttribute>();

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
        var sections = new SectionTracker();
        var dividers = new DividerTracker();

        var filteredMembers = GetFilteredMembers(GetType());

        for (var i = 0; i < filteredMembers.Count; i++)
        {
            var currentRowMember = filteredMembers[i];
            var nextRowMember = i < filteredMembers.Count - 1 ? filteredMembers[i + 1] : null;
            
            // Create a section header if this property starts a new section
            var sectionName = currentRowMember.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
            sections.MaybeStartNew(sectionName, CreateSectionHeader, targetContainer);

            // Set up the option row itself
            NConfigOptionRow? newRow;
            try
            {
                newRow = currentRowMember switch
                {
                    PropertyInfo p => GenerateOptionFromProperty(p),
                    MethodInfo m => GenerateButtonRowFromMethod(m),
                    _ => throw new UnreachableException("Invalid type that should have been filtered out")
                };
            }
            catch (NotSupportedException ex)
            {
                BaseLibMain.Logger.Error($"Not creating UI for unsupported property '{currentRowMember.Name}': {ex.Message}");
                continue;
            }

            targetContainer.AddChild(newRow);
            dividers.CompletePending(newRow); // Insert a divider between this row and the *previous* in the section, if any
            sections.RegisterRow(newRow);

            // Handle visibility for this row
            Action? rowVisibilityUpdater = BuildVisibilityUpdater(currentRowMember, newRow);
            Action? triggerVisibilityUpdate = null;

            if (rowVisibilityUpdater != null)
            {
                var headerForThisRow = sections.CurrentHeader;
                triggerVisibilityUpdate = () =>
                {
                    rowVisibilityUpdater();
                    dividers.UpdateAll();
                    if (headerForThisRow != null)
                        sections.UpdateHeaderVisibility(headerForThisRow);
                };

                EventHandler configChangedHandler = (_, _) => triggerVisibilityUpdate();
                ConfigChanged += configChangedHandler;
                OnConfigReloaded += triggerVisibilityUpdate;

                _configChangedHandlers.Add(configChangedHandler);
                _configReloadedHandlers.Add(triggerVisibilityUpdate);
            }

            // Add a divider if appropriate, and assign it to the visibility tracking
            var nextSectionName = nextRowMember?.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
            var nextIsSameSection = nextSectionName == null || nextSectionName == sections.CurrentSectionName;
            if (nextRowMember != null && nextIsSameSection)
            {
                var divider = CreateDividerControl();
                targetContainer.AddChild(divider);
                dividers.AddPending(divider, newRow);
            }

            triggerVisibilityUpdate?.Invoke();

        }

        // Ensure the final state is correct for visibility: the final visibility isn't known until the section
        // has been fully built.
        sections.UpdateAllHeaderVisibility();
        dividers.UpdateAll();
    }

    /// <summary>
    /// Connects the first focusable control on each row (each optionContainer child) to each other, for controller
    /// (and keyboard) navigation.<br/>
    /// You can run this if you've added or modified controls in some way, to ensure navigation doesn't skip over
    /// controls, which can happen when Godot tries to guess which control is "next" or "previous" when you navigate.
    /// </summary>
    /// <param name="optionContainer">The optionContainer passed to your SetupConfigUI method</param>
    public static void SetupFocusNeighbors(Control optionContainer)
    {
        var focusTargets = optionContainer
            .GetChildren()
            .OfType<Control>()
            .Select(c => c.FindFirstFocusable())
            .OfType<Control>() // Filter out nulls
            .ToList();

        if (focusTargets.Count == 0) return;

        // Connect each control to the one above/below, and wrap top->bottom and bottom->top
        for (var i = 0; i < focusTargets.Count; i++)
        {
            var current = focusTargets[i];

            // Wrap around
            var prevTarget = i == 0 ? focusTargets[^1] : focusTargets[i - 1];
            var nextTarget = i == focusTargets.Count - 1 ? focusTargets[0] : focusTargets[i + 1];

            // Only assign if the mod hasn't explicitly set something up manually, just in case
            if (current.FocusNeighborTop.IsEmpty) current.FocusNeighborTop = prevTarget.GetPath();
            if (current.FocusNeighborBottom.IsEmpty) current.FocusNeighborBottom = nextTarget.GetPath();

            // Lock horizontal navigation to the control itself by default
            if (current.FocusNeighborLeft.IsEmpty) current.FocusNeighborLeft = selfNodePath;
            if (current.FocusNeighborRight.IsEmpty) current.FocusNeighborRight = selfNodePath;
        }
    }

    private List<MemberInfo> GetFilteredMembers(Type type)
    {
        // Fetch properties AND methods in the order they appear in the source file.
        // Instance is supported for methods, but not properties (which must be static); they are filtered in
        // ModConfig.CheckConfigProperties, and will be filtered out by IsVisibleMember().
        return type
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Where(IsVisibleMember)
            .OrderBy(GetSourceOrder)
            .ToList();

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

    // Create a lambda that handles visibility updates from ConfigVisibleIf for this row
    private Action? BuildVisibilityUpdater(MemberInfo member, Control newRow)
    {
        var visibleIfAttr = member.GetCustomAttribute<ConfigVisibleIfAttribute>();
        if (visibleIfAttr == null) return null;

        var condition = BuildVisibilityCondition(visibleIfAttr, member);
        if (condition == null) return null;

        return () => { newRow.Visible = condition(); };
    }

    // A [ConfigVisibleIf] attribute can take either a property or a method as its first argument,
    // so this method simply checks which is used, then dispatches the actual creation to a helper.
    private Func<bool>? BuildVisibilityCondition(ConfigVisibleIfAttribute visibleIf, MemberInfo annotatedMember)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        var targetProp = GetType().GetProperty(visibleIf.TargetName, bindingFlags);
        if (targetProp != null)
        {
            if (visibleIf.Args.Length != 0 || targetProp.PropertyType == typeof(bool))
                return BuildPropertyCondition(targetProp, visibleIf, annotatedMember);

            BaseLibMain.Logger.Error(
                $"[ConfigVisibleIf] on '{annotatedMember.Name}': property '{visibleIf.TargetName}' is not a bool; " +
                $"at least one value to compare against is required.");
            return null;
        }

        // To clarify by example:
        // [ConfigVisibleIf(nameof(TargetMethod))]
        // public static bool AnnotatedMember { get; set; } = true; // Visible if TargetMethod() == true
        var targetMethod = GetType().GetMethod(visibleIf.TargetName, bindingFlags);
        if (targetMethod != null && targetMethod.ReturnType == typeof(bool))
            return BuildMethodCondition(targetMethod, visibleIf, annotatedMember);

        BaseLibMain.Logger.Error(
            $"[ConfigVisibleIf] on '{annotatedMember.Name}': no valid property or boolean method named " +
            $"'{visibleIf.TargetName}' found on {GetType().Name}.");

        return null;
    }

    private static Func<bool>? BuildPropertyCondition(PropertyInfo prop, ConfigVisibleIfAttribute visibleIf, MemberInfo annotatedMember)
    {
        // Pre-convert arguments at startup; for example, the property might be float/double, while the arguments
        // are specified as integers.
        object?[] convertedArgs = [];
        if (visibleIf.Args.Length > 0)
        {
            var propType = prop.PropertyType;
            try
            {
                convertedArgs = visibleIf.Args.Select(arg =>
                    arg == null ? null :
                    propType.IsEnum ? Enum.ToObject(propType, arg) :
                    Convert.ChangeType(arg, propType)
                ).ToArray();
            }
            catch (Exception e)
            {
                BaseLibMain.Logger.Error($"[ConfigVisibleIf] on '{annotatedMember.Name}': could not convert " +
                                         $"arguments to '{propType.Name}': {e.Message}");
                return null;
            }
        }

        return () =>
        {
            bool conditionMet;
            var currentVal = prop.GetValue(null);

            // If null, set as visible if that's actually an expected, allowed value.
            // If length is 0, treat as a boolean check (the caller ensures 0 args is only allowed for bool)
            // Otherwise, set as visible if ANY argument matches the current property value
            if (currentVal == null)
                conditionMet = convertedArgs.Any(a => a == null);
            else if (convertedArgs.Length == 0)
                conditionMet = currentVal is true;
            else
                conditionMet = convertedArgs.Any(currentVal.Equals);

            return visibleIf.Invert ? !conditionMet : conditionMet;
        };
    }

    private Func<bool> BuildMethodCondition(MethodInfo method, ConfigVisibleIfAttribute visibleIf, MemberInfo annotatedMember)
    {
        var argsQueue = new Queue<object?>(visibleIf.Args);
        var preResolvedArgs = method.GetParameters()
            .Select(param => ResolveVisibilityMethodArgument(param, annotatedMember, argsQueue))
            .ToArray();

        return () =>
        {
            try
            {
                var result = (bool)method.Invoke(method.IsStatic ? null : this, preResolvedArgs)!;
                return visibleIf.Invert ? !result : result;
            }
            catch (Exception e)
            {
                BaseLibMain.Logger.Error($"[ConfigVisibleIf] error invoking '{method.Name}':\n{e}");
                return true;
            }
        };
    }

    /// Resolves arguments for [ConfigVisibleIf] target methods. Auto-injects some types when requested, and injects
    /// all arguments given to the attribute.
    protected object? ResolveVisibilityMethodArgument(ParameterInfo param, MemberInfo memberInfo, Queue<object?> argsQueue)
    {
        var t = param.ParameterType;

        // I honestly don't see any use for MethodInfo (but have used PropertyInfo), but it's close enough to "free"
        // to support it, too, just in case.
        if (typeof(ModConfig).IsAssignableFrom(t)) return this;
        if (t == typeof(MemberInfo)) return memberInfo;
        if (t == typeof(PropertyInfo))
        {
            if (memberInfo is PropertyInfo propInfo) return propInfo;
            throw new ArgumentException($"Visibility method '{param.Member.Name}' asks for a PropertyInfo, but was " +
                                        $"applied to a Button ('{memberInfo.Name}'). Change the parameter to " +
                                        "MemberInfo to support both.");
        }
        if (t == typeof(MethodInfo))
        {
            if (memberInfo is MethodInfo methodInfo) return methodInfo;
            throw new ArgumentException($"Visibility method '{param.Member.Name}' asks for a MethodInfo, but was " +
                                        $"applied to a Property ('{memberInfo.Name}'). Change the parameter to " +
                                        "MemberInfo to support both.");
        }

        if (!argsQueue.TryDequeue(out var rawArg))
        {
            throw new ArgumentException(
                $"Method '{param.Member.Name}' requires more arguments than provided in the [ConfigVisibleIf] attribute, " +
                $"and parameter '{param.Name}' is not an auto-injectable type.");
        }

        try
        {
            // Map the type, in case the e.g. the method takes float/double but the attribute has an integer argument
            return rawArg != null ? Convert.ChangeType(rawArg, t) : null;
        }
        catch (Exception e)
        {
            throw new ArgumentException($"Cannot convert [ConfigVisibleIf] argument '{rawArg}' to expected type " +
                                        $"'{t.Name}' for method {param.Member.Name}.", e);
        }
    }

    public void ClearUIEventHandlers()
    {
        foreach (var handler in _configChangedHandlers)
            ConfigChanged -= handler;
        
        foreach (var handler in _configReloadedHandlers)
            OnConfigReloaded -= handler;
        
        _configChangedHandlers.Clear();
        _configReloadedHandlers.Clear();
    }

    private sealed class DividerTracker
    {
        private readonly List<(Control Divider, Control Upper, Control Lower)> _pairs = [];
        private Control? _pendingDivider;
        private Control? _pendingUpperRow;

        public void CompletePending(Control lowerRow)
        {
            if (_pendingDivider == null) return;
            _pairs.Add((_pendingDivider, _pendingUpperRow!, lowerRow));
            _pendingDivider = null;
        }

        public void AddPending(Control divider, Control upperRow)
        {
            _pendingDivider = divider;
            _pendingUpperRow = upperRow;
        }

        public void UpdateAll()
        {
            foreach (var (divider, upper, lower) in _pairs)
                divider.Visible = upper.Visible && lower.Visible;
        }
    }

    private sealed class SectionTracker
    {
        private readonly Dictionary<Control, List<Control>> _headerRows = [];
        public Control? CurrentHeader { get; private set; }
        public string? CurrentSectionName { get; private set; }

        public void MaybeStartNew(string? sectionName, Func<string, bool, Control> createHeader, Control targetContainer)
        {
            if (sectionName == null || sectionName == CurrentSectionName) return;

            CurrentSectionName = sectionName;
            CurrentHeader = createHeader(sectionName, targetContainer.GetChildCount() == 0);
            _headerRows[CurrentHeader] = [];
            targetContainer.AddChild(CurrentHeader);
        }

        public void RegisterRow(Control row)
        {
            if (CurrentHeader != null)
                _headerRows[CurrentHeader].Add(row);
        }

        public void UpdateHeaderVisibility(Control header)
        {
            header.Visible = _headerRows[header].Any(r => r.Visible);
        }

        public void UpdateAllHeaderVisibility()
        {
            foreach (var (header, _) in _headerRows)
                UpdateHeaderVisibility(header);
        }
    }
}