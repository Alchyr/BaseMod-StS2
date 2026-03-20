using BaseLib.Extensions;
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
    private static NModConfigSubmenu? _modConfigSubmenu;

    [HarmonyPrefix]
    public static bool Prefix(NMainMenuSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        if (type != typeof(NModConfigSubmenu)) return true;

        if (_modConfigSubmenu == null)
        {
            _modConfigSubmenu = new NModConfigSubmenu();
            _modConfigSubmenu.Visible = false;
            __instance.AddChildSafely(_modConfigSubmenu);
        }
        __result = _modConfigSubmenu;
        return false;
    }
}

public partial class NModConfigSubmenu : NSubmenu
{
    private VBoxContainer _optionContainer;
    private NScrollableContainer _scrollContainer;
    private Control _contentPanel;
    private MegaRichTextLabel _modTitle;
    private NConfigButton? _opener;
    private Tween? _fadeTween;

    private const float ContentWidth = 1012f; // Same as the base game
    private const float ClipperTopOffset = 187f;
    private const float ModTitleHeight = 90f;
    private const float MinPadding = 30f;

    protected override Control InitialFocusedControl => FindFirstFocusable(_optionContainer) ?? _optionContainer;

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

        // The container that we send to the ModConfig to populate
        _optionContainer = new VBoxContainer
        {
            Name = "VBoxContainer",
            CustomMinimumSize = new Vector2(ContentWidth, 0f),
            AnchorRight = 1f,
            GrowHorizontal = GrowDirection.Both,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        _optionContainer.MinimumSizeChanged += RefreshSize;

        _contentPanel.AddChild(_optionContainer);

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

        scrollbar.OffsetTop = 219f;
        scrollbar.OffsetBottom = -64f;

        _scrollContainer.AddChild(scrollbar);
        scrollbar.Owner = _scrollContainer;
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
        _opener = opener;
        _optionContainer.FreeChildren();
        _optionContainer.AddThemeConstantOverride("separation", 8);

        try
        {
            config.SetupConfigUI(_optionContainer);

            SetModTitle(config);
            _scrollContainer.DisableScrollingIfContentFits();
            RefreshSize();
            ModConfig.ShowAndClearPendingErrors();

            // Note: TryGrabFocus returns if a controller isn't being used
            Callable.From(
                () => FindFirstFocusable(_optionContainer)?.TryGrabFocus()
            ).CallDeferred();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error(e.ToString());
            _stack.Pop();
        }
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
                $"No {locKey} found in localization table, using mod namespace as title");
        }

        var titleText = locStr?.GetFormattedText() ?? fallbackTitle;
        _modTitle.SetTextAutoSize($"[center]{titleText}[/center]");
    }

    private void RefreshSize()
    {
        var clipperSize = _contentPanel.GetParent<Control>().Size;
        var requiredHeight = _optionContainer.GetMinimumSize().Y;
        var paddedHeight = requiredHeight + MinPadding;

        // Emulate the game's menus: add padding below if scrolling is (almost) needed
        if (paddedHeight >= clipperSize.Y)
        {
            paddedHeight += clipperSize.Y * 0.3f;
        }

        _contentPanel.CustomMinimumSize = new Vector2(ContentWidth, paddedHeight);
        _optionContainer.Size = new Vector2(ContentWidth, requiredHeight);
        _scrollContainer.DisableScrollingIfContentFits();
    }

    protected override void OnSubmenuShown()
    {
        base.OnSubmenuShown();
        _scrollContainer.InstantlyScrollToTop();

        _fadeTween?.Kill();
        _fadeTween = CreateTween().SetParallel();
        _fadeTween.TweenProperty(_contentPanel, "modulate", Colors.White, 0.5f)
            .From(new Color(0, 0, 0, 0))
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
    }

    protected override void OnSubmenuHidden()
    {
        if (_opener != null) _opener.IsConfigOpen = false;

        base.OnSubmenuHidden();
    }

    private static Control? FindFirstFocusable(Node parent)
    {
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
}