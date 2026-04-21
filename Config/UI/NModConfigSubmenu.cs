using BaseLib.Extensions;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace BaseLib.Config.UI;

public partial class NModConfigSubmenu : NSubmenu
{
    private new NBackButton? _backButton;
    private NNativeScrollableContainer _leftScrollArea;
    private VBoxContainer _modListVbox;
    private Control _modListPanel;
    private MegaRichTextLabel _modListTitle;

    private NNativeScrollableContainer _rightScrollArea;
    private VBoxContainer? _optionContainer;
    private Control _contentPanel;
    private MegaRichTextLabel _modTitle;
    private Tween? _fadeInTween;

    private ModConfig? _currentConfig;
    private double _saveTimer = -1;
    private bool _modLoadFailed;
    private bool _lastFocusOnModList = true;
    private const double AutosaveDelay = 5;

    private bool _isUsingController;
    private double _navRepeatTimer;
    private StringName? _heldNavAction;
    private const float InitialRepeatDelay = 0.4f;
    private const float RepeatRate = 0.1f;

    private const float ModTitleHeight = 90f;
    private const float TopOffset = ModTitleHeight + 30f;

    private const float ModListPosition = 180f;
    private const float ModListWidth = 360f;

    private const float MaxRightSideWidth = 1200f; // Dynamically sized, but not above this (hurts UW readability)
    private const int ModConfigPadding = 16; // Padding between the clipper and the mod config content

    // Read when the screen is shown *and* after a modal (e.g. confirm Restore Defaults). Ensure we return
    // to the same side that was active prior.
    protected override Control? InitialFocusedControl =>
        _lastFocusOnModList ? GetActiveModButton() : _optionContainer?.FindFirstFocusable();

    public NModConfigSubmenu()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;

        _leftScrollArea = new NNativeScrollableContainer(TopOffset);
        _modListPanel = new Control
        {
            Name = "ModListContent",
            MouseFilter = MouseFilterEnum.Ignore
        };
        _modListPanel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_leftScrollArea);

        _rightScrollArea = new NNativeScrollableContainer(TopOffset);
        _contentPanel = new Control
        {
            Name = "ModConfigContent",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _contentPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
        AddChild(_rightScrollArea);

        _modListTitle = CreateTitleControl("ModListTitle", "[center]Mods[/center]", 0f);
        _modListTitle.OffsetLeft = ModListPosition;
        _modListTitle.OffsetRight = ModListPosition + ModListWidth - NNativeScrollableContainer.ScrollbarGutterWidth;

        _modTitle = CreateTitleControl("ModTitle", "[center]Unknown mod name[/center]", 0f);
        _modListVbox = new VBoxContainer();
    }

    public override void _Ready()
    {
        AddChild(_modTitle);
        AddChild(_modListTitle);

        _modListPanel.AddChild(_modListVbox);
        _modListPanel.SetAnchorsPreset(LayoutPreset.TopLeft);

        InitializeModList();

        _modListVbox.MinimumSizeChanged += () => {
            _modListPanel.CustomMinimumSize = new Vector2(_leftScrollArea.AvailableContentWidth, _modListVbox.GetMinimumSize().Y);
        };

        _leftScrollArea.AttachContent(_modListPanel);
        _leftScrollArea.DisableScrollingIfContentFits();
        _rightScrollArea.AttachContent(_contentPanel);
        _rightScrollArea.DisableScrollingIfContentFits();

        _backButton = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/back_button"))
            .Instantiate<NBackButton>();
        _backButton.Name = "BackButton";
        AddChild(_backButton);

        _isUsingController = NControllerManager.Instance?.IsUsingController ?? false;

        ConnectSignals();
        GetViewport().Connect(Viewport.SignalName.SizeChanged, Callable.From(RefreshSize));
        GetViewport().Connect(Viewport.SignalName.GuiFocusChanged, Callable.From<Control>(OnGlobalFocusChanged));
        NControllerManager.Instance?.Connect(NControllerManager.SignalName.MouseDetected,
            Callable.From(InputTypeChanged));
        NControllerManager.Instance?.Connect(NControllerManager.SignalName.ControllerDetected,
            Callable.From(InputTypeChanged));
    }

    private void InitializeModList()
    {
        var selfNodePath = new NodePath(".");

        foreach (var modConfig in ModConfigRegistry.GetAll().Where(mod => mod.HasVisibleSettings()))
        {
            var modName = GetModTitle(modConfig);
            var modButton = new NModListButton(modName);
            _modListVbox.AddChild(modButton);

            modButton.Connect(NClickableControl.SignalName.Released, Callable.From<NModListButton>(button =>
                ModButtonClicked(button, modConfig)));

            modButton.Connect(NClickableControl.SignalName.Focused,
                Callable.From<NModListButton>(ModButtonFocused));

            modButton.FocusNeighborLeft = selfNodePath;
            modButton.FocusNeighborRight = selfNodePath;
        }

        // Set up dummies to test scrolling, etc.
        // for (var i = 1; i <= 15; i++) { var btn = new NModListButton($"Test mod {i}"); _modListVbox.AddChild(btn); }

        // Set up focus neighbors for controller navigation; connect top -> bottom and bottom -> top
        var mods = _modListVbox.GetChildren();
        var firstMod = mods.First() as NModListButton;
        var lastMod = mods.Last() as NModListButton;
        if (firstMod != null) firstMod.FocusNeighborTop   = firstMod.GetPathTo(lastMod);
        if (lastMod != null)  lastMod.FocusNeighborBottom = lastMod.GetPathTo(firstMod);

        // Add spacers due to the fade effect
        var topSpacer = new Control { CustomMinimumSize = new Vector2(0, 20) };
        _modListVbox.AddChild(topSpacer);
        _modListVbox.MoveChild(topSpacer, 0);
        _modListVbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });
    }

    private NModListButton? GetActiveModButton()
    {
        if (_currentConfig == null) return null;
        foreach (var button in _modListPanel.GetChild(0).GetChildren())
        {
            if (button is NModListButton listButton && listButton.ModName == GetModTitle(_currentConfig))
                return listButton;
        }

        return null;
    }

    private void ModButtonClicked(NModListButton button, ModConfig modConfig)
    {
        if (modConfig != _currentConfig)
            LoadModConfig(modConfig);

        if (!_isUsingController || _modLoadFailed) return;

        button.SetHotkeyIconVisible(true);
        Callable.From(() => { _optionContainer?.FindFirstFocusable()?.TryGrabFocus(); })
            .CallDeferred();
    }

    private void ModButtonFocused(NModListButton button)
    {
        SetBackButtonVisible(true);
    }

    private void FocusActiveModButton()
    {
        Callable.From(() => GetActiveModButton()?.TryGrabFocus()).CallDeferred();
    }

    private void SetBackButtonVisible(bool visible)
    {
        if (_backButton == null) return;

        if (!visible) _backButton.Disable();
        else
        {
            // An early return in NClickableControl.Enable() causes a desync issue where _isEnabled is true, but the
            // button isn't enabled/visible. Bypass the return and *actually* enable the button.
            _backButton._isEnabled = false;
            _backButton.Enable();
        }
    }

    private void SetHighlightedModButton(ModConfig config)
    {
        foreach (var button in _modListPanel.GetChild(0).GetChildren())
        {
            if (button is NModListButton listButton)
                listButton.SetActiveState(listButton.ModName == GetModTitle(config));
        }
    }

    private void InputTypeChanged()
    {
        _isUsingController = NControllerManager.Instance?.IsUsingController ?? false;
        SetBackButtonVisible(true);
        FocusActiveModButton();
    }

    public override void _Input(InputEvent @event)
    {
        // Handle moving from the mod config list back to the mod list on back (e.g. B on Xbox controllers)
        base._Input(@event);
        if (_backButton?.IsEnabled == true) return;

        if (!@event.IsActionReleased(MegaInput.cancel) &&
            !@event.IsActionReleased(MegaInput.pauseAndBack) &&
            !@event.IsActionReleased(MegaInput.back)) return;

        // Ensure we're not in a modal dialog (such as Restore Defaults), on-screen keyboard, etc. that should handle this
        var focusOwner = GetViewport().GuiGetFocusOwner();
        if (focusOwner == null || _optionContainer?.IsAncestorOf(focusOwner) != true)
        {
            return;
        }

        FocusActiveModButton();
        AcceptEvent();
    }

    private void LoadModConfig(ModConfig config)
    {
        if (config.ModId != null)
            BaseLibConfig.LastModConfigModId = config.ModId;

        if (_optionContainer != null || _currentConfig != null)
            SaveAndClearCurrentMod();

        _currentConfig = config;
        config.ConfigChanged += OnConfigChanged;
        SetHighlightedModButton(config);

        // Recreate the container to ensure the previous mod can't change something persistent by mistake
        _optionContainer = CreateOptionContainer();
        _contentPanel.AddChild(_optionContainer);

        try
        {
            config.SetupConfigUI(_optionContainer);
            _modLoadFailed = false;
        }
        catch (Exception e)
        {
            _modLoadFailed = true;
            SaveAndClearCurrentMod();
            _currentConfig = config; // Cleared by the above, but should remain in this case

            _optionContainer = CreateOptionContainer();
            _contentPanel.AddChild(_optionContainer);

            var modName = GetModTitle(config);
            var message = $"[center]BaseLib failed setting up the mod config for {modName}.\n" +
                          "This is either because the mod set something up incorrectly, or a " +
                          "compatibility issue.\n" +
                          $"Try updating BaseLib and {modName}, if newer versions exist.[/center]";
            var errorLabel = ModConfig.CreateRawLabelControl(message, 32);

            errorLabel.FitContent = true;
            errorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            _optionContainer.AddChild(errorLabel);

            BaseLibMain.Logger.Error($"SetupConfigUI failed for mod {modName}: {e}");
        }

        try
        {
            var title = $"[center]{GetModTitle(config)}[/center]";
            _modTitle.SetTextAutoSize(title);

            RefreshSize();
            _rightScrollArea.InstantlyScrollToTop();

            ModConfig.ShowAndClearPendingErrors();
        }
        catch (Exception e)
        {
            ModConfig.ModConfigLogger.Error("An error occurred while loading the mod config screen.\n" +
                                            "Please report a bug at:\nhttps://github.com/Alchyr/BaseLib-StS2");
            BaseLibMain.Logger.Error(e.ToString());
            _stack.Pop();
        }
    }

    private VBoxContainer CreateOptionContainer()
    {
        var container = new VBoxContainer {
            Name = "VBoxContainer",
            CustomMinimumSize = new Vector2(0f, 0f),
            AnchorRight = 1f,
            GrowHorizontal = GrowDirection.End,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        container.AddThemeConstantOverride("separation", 8);
        container.MinimumSizeChanged += RefreshSize;
        return container;
    }

    private static MegaRichTextLabel CreateTitleControl(string name, string defaultText, float minimumWidth)
    {
        var title = ModConfig.CreateRawLabelControl(defaultText, 36);
        title.Name = name;
        title.AutoSizeEnabled = true;
        title.MaxFontSize = 64;
        title.CustomMinimumSize = new Vector2(minimumWidth, ModTitleHeight);

        title.SetAnchorsPreset(LayoutPreset.TopLeft);
        title.OffsetBottom = TopOffset - 10;
        title.OffsetTop = title.OffsetBottom - ModTitleHeight;

        return title;
    }

    private static string GetModTitle(ModConfig config)
    {
        var locKey = $"{config.ModPrefix[..^1]}.mod_title";
        var locStr = LocString.GetIfExists("settings_ui", locKey);
        if (locStr != null)
            return locStr.GetFormattedText();

        ModConfig.ModConfigLogger.Warn($"No {locKey} found in localization table, using fallback title");

        var fallbackTitle = config.GetType().GetRootNamespace();
        if (string.IsNullOrWhiteSpace(fallbackTitle))
            fallbackTitle = LocString.GetIfExists("settings_ui", "BASELIB-UNKNOWN_MOD_NAME")!.GetFormattedText();

        return fallbackTitle;
    }

    private void OnGlobalFocusChanged(Control newFocus)
    {
        if (!IsVisibleInTree()) return;

        var focusOnModList = _leftScrollArea.IsAncestorOf(newFocus);

        var focusMovedToModList = focusOnModList && !_lastFocusOnModList;
        var focusMovedToContent = !focusOnModList && _lastFocusOnModList;
        _lastFocusOnModList = focusOnModList;

        if (focusMovedToModList)
        {
            SetBackButtonVisible(true);

            foreach (var modButton in _modListVbox.GetChildren())
            {
                if (modButton is NModListButton listButton)
                    listButton.SetHotkeyIconVisible(false);
            }

            if (newFocus != GetActiveModButton())
                FocusActiveModButton();
        }
        else if (focusMovedToContent && _isUsingController)
        {
            SetBackButtonVisible(false);
        }
    }

    private void RefreshSize()
    {
        if (_optionContainer == null) return;

        var (screenWidth, screenHeight) = GetViewportRect().Size;

        // Handle the left hand side (mod list)
        _leftScrollArea.Position = new Vector2(ModListPosition, 0);
        _leftScrollArea.Size = new Vector2(ModListWidth, screenHeight);

        var leftContentWidth = _leftScrollArea.AvailableContentWidth;

        _modListPanel.CustomMinimumSize = new Vector2(leftContentWidth, _modListPanel.CustomMinimumSize.Y);
        _modListVbox.CustomMinimumSize = new Vector2(leftContentWidth, _modListVbox.CustomMinimumSize.Y);
        _modListVbox.Size = new Vector2(leftContentWidth, _modListVbox.Size.Y);

        // The rest of this method handles the right side spacing. It's complex, but behaves well with any aspect ratio.

        const float minimumGap = 32f;
        const float modListEnd = ModListPosition + ModListWidth;
        const float scrollbarGutter = 60f; // Space reserved for the scrollbar
        const float TotalPaddingWidth = ModConfigPadding * 2f;
        const float maxSettingsWidth = MaxRightSideWidth - scrollbarGutter - TotalPaddingWidth;

        var totalAvailableSpace = screenWidth - modListEnd;
        var spaceForSettings = totalAvailableSpace - 2 * minimumGap - scrollbarGutter - TotalPaddingWidth;
        var actualSettingsWidth = Mathf.Min(spaceForSettings, maxSettingsWidth);

        var leftoverSpace = totalAvailableSpace - actualSettingsWidth - scrollbarGutter - TotalPaddingWidth;
        var unallocatedSpace = leftoverSpace - 2 * minimumGap;

        var extraScrollbarSpacing = 0f;
        var centeringOffset = 0f;

        if (unallocatedSpace > 0)
        {
            // First, give breathing room to the scrollbar
            extraScrollbarSpacing = Mathf.Min(unallocatedSpace, 64f);
            unallocatedSpace -= extraScrollbarSpacing;

            // Then use whatever is left to center the entire block
            centeringOffset = unallocatedSpace / 2f;
        }

        var contentPosition = modListEnd + minimumGap + centeringOffset;
        var containerWidth = actualSettingsWidth + TotalPaddingWidth + extraScrollbarSpacing + scrollbarGutter;
        var containerPosition = contentPosition - ModConfigPadding;

        // Position and size the scroll area
        _rightScrollArea.Position = new Vector2(containerPosition, 0);
        _rightScrollArea.Size = new Vector2(containerWidth, screenHeight);

        // Offset the VBox by the padding amount instead of using a MarginContainer, which complicates things with its
        // strict auto-layout rules
        _optionContainer.Position = new Vector2(ModConfigPadding, 0);
        _optionContainer.CustomMinimumSize = new Vector2(actualSettingsWidth, 0);
        _optionContainer.Size = new Vector2(actualSettingsWidth, 0);

        var requiredHeight = _optionContainer.GetMinimumSize().Y;

        var paddedHeight = requiredHeight + 30f;

        // Emulate the game and add extra space at the bottom
        var clipperSize = _contentPanel.GetParent<Control>().Size;
        if (paddedHeight >= clipperSize.Y)
            paddedHeight += clipperSize.Y * 0.3f;

        // Update the internal container sizes, accounting for the scrollbar area
        var rightContentWidth = _rightScrollArea.AvailableContentWidth;
        _contentPanel.CustomMinimumSize = new Vector2(rightContentWidth, paddedHeight);
        _contentPanel.Size = new Vector2(rightContentWidth, paddedHeight);

        _optionContainer.Size = new Vector2(actualSettingsWidth, requiredHeight);

        // Force center the mod title over the actual settings
        _modTitle.OffsetLeft = contentPosition;
        _modTitle.OffsetRight = contentPosition + actualSettingsWidth;
        _modTitle.CustomMinimumSize = new Vector2(actualSettingsWidth, ModTitleHeight);
    }

    protected override void OnSubmenuShown()
    {
        base.OnSubmenuShown();
        _contentPanel.Modulate = new Color(1f, 1f, 1f, 0f);

        _saveTimer = -1;

        // Load the most recent config, or default to BaseLib
        var baseLibConfig = ModConfigRegistry.Get<BaseLibConfig>()!;
        var lastModId = BaseLibConfig.LastModConfigModId;
        var lastMod = !string.IsNullOrWhiteSpace(lastModId) ? ModConfigRegistry.Get(lastModId) : baseLibConfig;
        LoadModConfig(lastMod ?? baseLibConfig); // lastMod could be null if the mod is no longer loaded

        // Ensure back button is visible when switching between controller/mouse, etc.
        Callable.From(InputTypeChanged).CallDeferred();

        WaitForLayoutAndFadeIn();
    }

    private async void WaitForLayoutAndFadeIn()
    {
        // Wait for the layout: one frame is USUALLY but not always enough to avoid jerking.
        // try/catch due to async void.
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            _leftScrollArea.ScrollToFocusedControl(skipAnimation: true);
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Error(e.ToString());
        }
        finally
        {
            if (IsInstanceValid(this) && IsInsideTree())
            {
                _fadeInTween?.Kill();
                _fadeInTween = CreateTween().SetParallel();
                _fadeInTween.TweenProperty(_contentPanel, "modulate", Colors.White, 0.5f)
                    .From(new Color(0, 0, 0, 0))
                    .SetEase(Tween.EaseType.Out)
                    .SetTrans(Tween.TransitionType.Cubic);
            }
        }
    }

    protected override void OnSubmenuHidden()
    {
        SaveAndClearCurrentMod();

        base.OnSubmenuHidden();
    }

    private void SaveAndClearCurrentMod()
    {
        if (_currentConfig != null) _currentConfig.ConfigChanged -= OnConfigChanged;
        SaveCurrentConfig();

        if (_optionContainer != null)
        {
            _optionContainer.MinimumSizeChanged -= RefreshSize;
            _optionContainer.QueueFreeSafely();
            _optionContainer = null;
        }
        
        if (_currentConfig is SimpleModConfig simpleModConfig)
            simpleModConfig.ClearUIEventHandlers();

        _currentConfig = null;

        if (ModConfig.ModConfigLogger.PendingUserMessages.Count > 0)
        {
            // The main menu will only show this when recreated; if a player goes from settings to play a game,
            // that is AFTER finishing the game. We need to show the error now, so let's check here, too.
            Callable.From(ModConfig.ShowAndClearPendingErrors).CallDeferred();
        }
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        _saveTimer = AutosaveDelay;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_isUsingController)
            CreateControllerNavEcho(delta);

        if (_saveTimer <= 0) return;
        _saveTimer -= delta;
        if (_saveTimer <= 0)
        {
            SaveCurrentConfig();
        }
    }

    // Send repeat events of up/down inputs to allow easier movement on controllers
    private void CreateControllerNavEcho(double delta)
    {
        var currentAction =
            Input.IsActionPressed(MegaInput.down) ? MegaInput.down :
            Input.IsActionPressed(MegaInput.up) ? MegaInput.up :
            null;

        if (currentAction != _heldNavAction)
        {
            _heldNavAction = currentAction;
            _navRepeatTimer = InitialRepeatDelay;
            return;
        }

        if (currentAction == null) return;

        _navRepeatTimer -= delta;
        if (_navRepeatTimer > 0) return;
        _navRepeatTimer = RepeatRate;

        Input.ParseInputEvent(new InputEventAction
        {
            Action = currentAction,
            Pressed = true
        });
    }

    private void SaveCurrentConfig()
    {
        _saveTimer = -1;
        if (_modLoadFailed)
            BaseLibMain.Logger.Warn($"Ignoring SaveCurrentConfig for {_currentConfig?.ModId}: UI setup failed");
        else
            _currentConfig?.Save();
    }

    public override void _ExitTree()
    {
        GetViewport().Disconnect(Viewport.SignalName.SizeChanged, Callable.From(RefreshSize));
        GetViewport().Disconnect(Viewport.SignalName.GuiFocusChanged, Callable.From<Control>(OnGlobalFocusChanged));
        NControllerManager.Instance?.Disconnect(NControllerManager.SignalName.MouseDetected,
            Callable.From(InputTypeChanged));
        NControllerManager.Instance?.Disconnect(NControllerManager.SignalName.ControllerDetected,
            Callable.From(InputTypeChanged));

        base._ExitTree();
    }
}