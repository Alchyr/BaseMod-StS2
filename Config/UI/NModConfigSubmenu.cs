using BaseLib.Extensions;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace BaseLib.Config.UI;

[HarmonyPatch(typeof(NMainMenuSubmenuStack), nameof(NMainMenuSubmenuStack.GetSubmenuType), typeof(Type))]
public static class InjectModConfigSubmenuPatch
{
    private static readonly SpireField<NMainMenuSubmenuStack, NModConfigSubmenu> SubmenuField = new(CreateSubmenu);

    private static NModConfigSubmenu CreateSubmenu(NMainMenuSubmenuStack stack)
    {
        var menu = new NModConfigSubmenu();
        menu.Visible = false;
        stack.AddChildSafely(menu);
        return menu;
    }

    public static bool Prefix(NMainMenuSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        if (type != typeof(NModConfigSubmenu)) return true;

        __result = SubmenuField.Get(__instance)!;
        return false;
    }
}

public partial class NModConfigSubmenu : NSubmenu
{
    private VBoxContainer? _optionContainer;
    private NScrollableContainer _scrollContainer;
    private Control _contentPanel;
    private MegaRichTextLabel _modTitle;
    private NConfigButton? _opener;
    private Tween? _fadeInTween;

    private ModConfig? _currentConfig;
    private double _saveTimer = -1;
    private const double AutosaveDelay = 5;

    private const float ContentWidth = 1012f; // Same as the base game
    private const float ClipperTopOffset = 187f;
    private const float ModTitleHeight = 90f;
    private const float MinPadding = 30f;

    protected override Control InitialFocusedControl => FindFirstFocusable(_optionContainer) ?? _contentPanel;

    public NModConfigSubmenu()
    {
        // Basic node structure:
        // NModConfigSubmenu > _scrollContainer > mask > clipper > _contentPanel > _optionContainer
        // ... where _optionContainer is passed to the mod's ModConfig
        AnchorRight = 1f;
        AnchorBottom = 1f;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;

        _scrollContainer = new NScrollableContainer
        {
            Name = "ScrollContainer",
            ClipChildren = ClipChildrenMode.Only,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
        };

        // Verbose, but basically the same as the fading gradient in the original settings scene
        var mask = new TextureRect
        {
            Name = "Mask",
            ClipChildren = ClipChildrenMode.Only,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
            MouseFilter = MouseFilterEnum.Ignore,
            Texture = new GradientTexture2D
            {
                FillFrom = new Vector2(0f, 1f),
                FillTo = Vector2.Zero,
                Gradient = new Gradient
                {
                    // Note: these are ordered bottom-to-top!
                    Offsets = [0f, 0.08f, 0.805f, 0.827f],
                    Colors =
                    [
                        new Color(1f, 1f, 1f, 0f),
                        new Color(1f, 1f, 1f),
                        new Color(1f, 1f, 1f),
                        new Color(1f, 1f, 1f, 0f)
                    ],
                },
            },
        };

        var clipper = new Control
        {
            Name = "Clipper",
            ClipContents = true,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetTop = ClipperTopOffset,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        mask.AddChild(clipper);

        _contentPanel = new Control
        {
            Name = "ModConfigContent",
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -ContentWidth / 2,
            OffsetTop = 24f,
            OffsetRight = ContentWidth / 2,
            GrowHorizontal = GrowDirection.Both,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        clipper.AddChild(_contentPanel);

        var scrollbar = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/scrollbar"))
            .Instantiate<NScrollbar>();
        scrollbar.Name = "Scrollbar";
        const float gap = 54f;
        const float width = 48f;
        scrollbar.AnchorLeft = 0.5f;
        scrollbar.AnchorRight = 0.5f;
        scrollbar.AnchorTop = 0f;
        scrollbar.AnchorBottom = 1f;
        scrollbar.OffsetLeft = ContentWidth / 2 + gap;
        scrollbar.OffsetRight = ContentWidth / 2 + gap + width;

        scrollbar.OffsetTop = ClipperTopOffset + 32f;
        scrollbar.OffsetBottom = -64f;

        _scrollContainer.AddChild(scrollbar);
        _scrollContainer.AddChild(mask);

        // Autosize is on, but we need a value here
        _modTitle = ModConfig.CreateRawLabelControl("[center]Unknown mod name[/center]", 36);
        _modTitle.Name = "ModTitle";
        _modTitle.AutoSizeEnabled = true;
        _modTitle.MaxFontSize = 64;
        _modTitle.CustomMinimumSize = new Vector2(ContentWidth, ModTitleHeight);

        _modTitle.SetAnchorsPreset(LayoutPreset.TopWide);
        _modTitle.OffsetBottom = ClipperTopOffset - 10;
        _modTitle.OffsetTop = _modTitle.OffsetBottom - ModTitleHeight;
    }

    public override void _Ready()
    {
        AddChild(_scrollContainer);
        AddChild(_modTitle);
        _scrollContainer.SetContent(_contentPanel);
        _scrollContainer.DisableScrollingIfContentFits();

        var backButton = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/back_button")).Instantiate<NBackButton>();
        backButton.Name = "BackButton";
        AddChild(backButton);

        ConnectSignals();
        GetViewport().Connect(Viewport.SignalName.SizeChanged, Callable.From(RefreshSize));
    }

    public void LoadModConfig(ModConfig config, NConfigButton opener)
    {
        if (_currentConfig != null) _currentConfig.ConfigChanged -= OnConfigChanged;
        _currentConfig = config;
        _opener = opener;

        // Recreate the container to ensure the previous mod can't change something persistent by mistake
        _optionContainer = CreateOptionContainer();
        _contentPanel.AddChild(_optionContainer);

        try
        {
            config.ConfigChanged += OnConfigChanged;
            config.SetupConfigUI(_optionContainer);
        }
        catch (Exception e)
        {
            ModConfig.ModConfigLogger.Error($"Failed setting up config for mod {GetType().Assembly.GetName().Name}.\n" +
                                            "This is either because the mod set something up incorrectly, or a " +
                                            "compatibility issue.\n" +
                                            "Try updating BaseLib and the mod in question, if newer versions exist.");
            MainFile.Logger.Error(e.ToString());
            _stack.Pop();
            return;
        }

        try
        {
            SetModTitle(config);
            RefreshSize();
            _scrollContainer.DisableScrollingIfContentFits();
            _scrollContainer.InstantlyScrollToTop();

            ModConfig.ShowAndClearPendingErrors();

            // Note: TryGrabFocus returns if a controller isn't being used
            Callable.From(
                () => FindFirstFocusable(_optionContainer)?.TryGrabFocus()
            ).CallDeferred();
        }
        catch (Exception e)
        {
            ModConfig.ModConfigLogger.Error("An error occurred while loading the mod config screen.\n" +
                                            "Please report a bug at:\nhttps://github.com/Alchyr/BaseLib-StS2");
            MainFile.Logger.Error(e.ToString());
            _stack.Pop();
        }
    }

    private VBoxContainer CreateOptionContainer()
    {
        var container = new VBoxContainer {
            Name = "VBoxContainer",
            CustomMinimumSize = new Vector2(ContentWidth, 0f),
            AnchorRight = 1f,
            GrowHorizontal = GrowDirection.Both,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        container.AddThemeConstantOverride("separation", 8);
        container.MinimumSizeChanged += RefreshSize;
        return container;
    }

    private void SetModTitle(ModConfig config)
    {
        var fallbackTitle = config.GetType().GetRootNamespace();
        if (string.IsNullOrWhiteSpace(fallbackTitle)) fallbackTitle = "Unknown mod name";

        var locKey = $"{config.ModPrefix[..^1]}.mod_title";
        var locStr = LocString.GetIfExists("settings_ui", locKey);
        if (locStr == null)
        {
            ModConfig.ModConfigLogger.Warn(
                $"No {locKey} found in localization table, using mod namespace {fallbackTitle} as title");
        }

        var titleText = locStr?.GetFormattedText() ?? fallbackTitle;
        _modTitle.SetTextAutoSize($"[center]{titleText}[/center]");
    }

    private void RefreshSize()
    {
        if (_optionContainer == null) return;
        var clipperSize = _contentPanel.GetParent<Control>().Size;
        var requiredHeight = _optionContainer.GetMinimumSize().Y;
        var paddedHeight = requiredHeight + MinPadding;

        // Emulate the game's menus: add padding below if scrolling is (almost) needed
        if (paddedHeight >= clipperSize.Y)
        {
            paddedHeight += clipperSize.Y * 0.3f;
        }

        _contentPanel.CustomMinimumSize = new Vector2(ContentWidth, paddedHeight);
        _contentPanel.Size = new Vector2(_contentPanel.Size.X, 0); // Force reset to new minimum height
        _optionContainer.Size = new Vector2(ContentWidth, requiredHeight);
        _scrollContainer.DisableScrollingIfContentFits();
    }

    protected override void OnSubmenuShown()
    {
        base.OnSubmenuShown();

        _saveTimer = -1;

        _fadeInTween?.Kill();
        _fadeInTween = CreateTween().SetParallel();
        _fadeInTween.TweenProperty(_contentPanel, "modulate", Colors.White, 0.5f)
            .From(new Color(0, 0, 0, 0))
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
    }

    protected override void OnSubmenuHidden()
    {
        if (_currentConfig != null) _currentConfig.ConfigChanged -= OnConfigChanged;
        if (_opener != null) _opener.IsConfigOpen = false;
        SaveCurrentConfig();

        if (_optionContainer != null)
        {
            _optionContainer.MinimumSizeChanged -= RefreshSize;
            _optionContainer.QueueFreeSafely();
            _optionContainer = null;
        }

        if (ModConfig.ModConfigLogger.PendingUserMessages.Count > 0)
        {
            // The main menu will only show this when recreated; if a player goes from settings to play a game,
            // that is AFTER finishing the game. We need to show the error now, so let's check here, too.
            Callable.From(ModConfig.ShowAndClearPendingErrors).CallDeferred();
        }

        base.OnSubmenuHidden();
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        _saveTimer = AutosaveDelay;
    }

    private static Control? FindFirstFocusable(Node? parent)
    {
        if (parent == null) return null;
        foreach (var child in parent.GetChildren())
        {
            if (child is Control { FocusMode: FocusModeEnum.All or FocusModeEnum.Click } control)
                return control;

            var nestedFocus = FindFirstFocusable(child);
            if (nestedFocus != null)
                return nestedFocus;
        }

        return null;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_saveTimer <= 0) return;
        _saveTimer -= delta;
        if (_saveTimer <= 0)
        {
            SaveCurrentConfig();
        }
    }

    private void SaveCurrentConfig()
    {
        _currentConfig?.Save();
        _saveTimer = -1;
    }
}