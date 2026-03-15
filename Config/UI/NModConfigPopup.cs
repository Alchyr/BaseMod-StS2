using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace BaseLib.Config.UI;

public partial class NModConfigPopup : NClickableControl
{
    public static readonly SpireField<NModdingScreen, NModConfigPopup> ConfigPopup = new(Create);

    [HarmonyPatch(typeof(NModdingScreen), nameof(NModdingScreen._Ready))]
    static class NModConfigPatch
    {
        [HarmonyPostfix]
        static void PrepPopup(NModdingScreen __instance)
        {
            ConfigPopup.Get(__instance);
        }
    }

    public static void ShowModConfig(NModdingScreen screen, ModConfig config, NConfigButton opener)
    {
        var popup = ConfigPopup.Get(screen);
        if (popup == null)
        {
            opener.IsConfigOpen = false;
            return;
        }
        
        popup?.ShowMod(config, opener);
    }

    private ModConfig? _currentConfig;
    private NScrollableContainer _optionScrollContainer;
    private VBoxContainer _optionContainer;
    private NConfigButton? _opener;
    private double _saveTimer; //When any config option is changed, starts a timer. If enough time passes with no change, saves.
    private const double AutosaveDelay = 5;
    
    public static NModConfigPopup Create(NModdingScreen screen)
    {
        NModConfigPopup popup = new(screen);
        /*popup.Size = screen.Size;
        popup.MouseFilter = MouseFilterEnum.Ignore;
        popup.Hide();

        screen.AddChild(popup);
        popup.Owner = screen;

        popup._optionContainer.Size = new(, 1);
        popup.Position = */
        return popup;
    }

    private NModConfigPopup(Control futureParent)
    {
        _saveTimer = -1;

        Size = futureParent.Size;
        MouseFilter = MouseFilterEnum.Ignore;

        _optionScrollContainer = new();
        _optionScrollContainer.MouseFilter = MouseFilterEnum.Stop;
        _optionScrollContainer.Size = new(Math.Max(480, Size.X * 0.5f), Size.Y * 0.81f);
        Color back = new Color(0.1f, 0.1f, 0.1f); //Allow mods to change the color of their panel?
        Color border = new Color(239/255f, 198/255f, 93/255f); //Allow mods to change the color of their panel?
        _optionScrollContainer.Draw += () =>
        {
            _optionScrollContainer
                .DrawRect(new Rect2(0, 0, _optionScrollContainer.Size), back);
            _optionScrollContainer
                .DrawRect(new Rect2(0, 0, _optionScrollContainer.Size), border, false, 2);
        };

        AddChild(_optionScrollContainer);
        _optionScrollContainer.Owner = this;
        _optionScrollContainer.Position = Size * 0.5f - _optionScrollContainer.Size * 0.5f;

        NScrollbar scrollbar = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/scrollbar")).Instantiate<NScrollbar>();
        scrollbar.Name = "Scrollbar";
        _optionScrollContainer.AddChild(scrollbar);
        scrollbar.Owner = _optionScrollContainer;

        scrollbar.SetAnchorsPreset(LayoutPreset.RightWide);
        scrollbar.OffsetLeft = 0;
        scrollbar.OffsetRight = 48;
        scrollbar.OffsetTop = 32;
        scrollbar.OffsetBottom = -32;

        Control mask = new();
        mask.Name = "Mask";
        mask.Size = _optionScrollContainer.Size;
        mask.MouseFilter = MouseFilterEnum.Ignore;
        mask.ClipContents = true;

        _optionScrollContainer.AddChild(mask);
        mask.Owner = _optionScrollContainer;

        _optionContainer = new VBoxContainer();
        _optionContainer.Name = "Content";
        _optionContainer.CustomMinimumSize = new Vector2(mask.Size.X, 0);
        mask.MouseFilter = MouseFilterEnum.Ignore;

        _optionContainer.MinimumSizeChanged += () =>
        {
            _optionContainer.Size = new Vector2(mask.Size.X, _optionContainer.GetMinimumSize().Y);
        };

        mask.AddChild(_optionContainer);
        _optionContainer.Owner = mask;

        Hide();
        futureParent.AddChildSafely(this);
    }

    public override void _Ready()
    {
        ConnectSignals();
    }

    private void ShowMod(ModConfig config, NConfigButton opener)
    {
        _opener = opener;
        NHotkeyManager.Instance?.AddBlockingScreen(this);
        MouseFilter = MouseFilterEnum.Stop;

        try
        {
            config.SetupConfigUI(_optionContainer);
            _optionScrollContainer.DisableScrollingIfContentFits();
            _optionScrollContainer.InstantlyScrollToTop();
            _currentConfig = config;
            config.ConfigChanged += OnConfigChanged;
            Show();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error(e.ToString());
            ClosePopup();
        }
    }

    private void ClosePopup()
    {
        if (_opener != null) _opener.IsConfigOpen = false;
        NHotkeyManager.Instance?.RemoveBlockingScreen(this);
        MouseFilter = MouseFilterEnum.Ignore;
        if (_currentConfig != null) _currentConfig.ConfigChanged -= OnConfigChanged;
        Hide();
        _optionContainer.FreeChildren();
        foreach (var child in _optionContainer.GetParent().GetChildren())
            if (child != _optionContainer)
                child.QueueFreeSafely();
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        _saveTimer = AutosaveDelay;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_saveTimer > 0)
        {
            _saveTimer -= delta;
            if (_saveTimer <= 0)
            {
                SaveCurrentConfig();
            }
        }
    }

    protected override void OnRelease()
    {
        base.OnRelease();

        SaveCurrentConfig();
        ClosePopup();
    }

    private void SaveCurrentConfig()
    {
        _currentConfig?.Save();
        _saveTimer = -1;
    }
}