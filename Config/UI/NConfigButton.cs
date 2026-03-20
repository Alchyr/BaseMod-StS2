using System.Reflection;
using BaseLib.Patches.UI;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using MegaCrit.Sts2.Core.Nodes.TopBar;

namespace BaseLib.Config.UI;

public partial class NConfigButton : NTopBarButton
{
	//private static readonly HoverTip _hoverTip = new HoverTip(new LocString("static_hover_tips", "SETTINGS.title"), new LocString("static_hover_tips", "SETTINGS.description"));
	
    public bool IsConfigOpen { get; set; }
    public static Control Create(string name, NModInfoContainer node)
    {
        NConfigButton button = new();
        button.Name = name;
        button.MouseFilter = MouseFilterEnum.Stop;

        Control control = new();
        control.Name = "Control";
        control.MouseFilter = MouseFilterEnum.Ignore;

        TextureRect icon = new();
        icon.Name = "Icon";
        icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        icon.Texture = PreloadManager.Cache.GetAsset<AtlasTexture>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_settings.tres");
        icon.Material = ShaderUtils.GenerateHsv(1, 1, 0.9f);
        icon.Size = icon.CustomMinimumSize = new(64, 64);
        icon.PivotOffset = icon.Size * 0.5f;

        control.AddChild(icon);
        icon.Owner = control;
        
        button.AddChild(control);
        control.Owner = button;
        
        control.Size = icon.Size;
        button.Size = control.Size + new Vector2(16, 16);
        control.Position = new(8, 8);
        
        node.AddChild(button);
        button.Owner = node;

        button.Position = new(node.Size.X - (button.Size.X + 8), 8);
        button.Hide();
        
        return button;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (IsConfigOpen)
        {
            _icon.Rotation += (float)delta;
        }
    }

    private static readonly FieldInfo ModdingScreenStack = AccessTools.Field(typeof(NModdingScreen), "_stack");
    protected override void OnRelease()
    {
        base.OnRelease();
        if (IsOpen())
        {
            IsConfigOpen = false;
        }
        else
        {
            var mod = ModConfigFillPatch.CurrentMod;
            if (mod == null) return;
        
            var modConfig = ModConfigRegistry.Get(mod.manifest?.id);
            if (modConfig != null)
            {
                OpenModConfigSubmenu(modConfig);
            }
        }
        
        UpdateScreenOpen(); //triggers animations
        _hsv?.SetShaderParameter("v", 0.9f);
    }

    /* TODO - generate hovertip
    public override void OnFocus()
    {
        base.OnFocus();
        NHoverTipSet nHoverTipSet = NHoverTipSet.CreateAndShow(this, _hoverTip);
        nHoverTipSet.GlobalPosition = base.GlobalPosition + new Vector2(base.Size.X - nHoverTipSet.Size.X, base.Size.Y + 20f);
    }

    public override void OnUnfocus()
    {
        base.OnUnfocus();
        NHoverTipSet.Remove(this);
    }*/

	protected override bool IsOpen()
    {
        return IsConfigOpen;
    }

    private void OpenModConfigSubmenu(ModConfig modConfig)
    {
        var stackField = AccessTools.Field(typeof(NSubmenu), "_stack");

        if (FindParent("ModdingScreen") is not NModdingScreen moddingScreen ||
            stackField.GetValue(moddingScreen) is not NMainMenuSubmenuStack stackInstance)
        {
            ModConfig.ModConfigLogger.Error("Unable to locate the game's modding screen!\n" +
                                            "Please report a bug at:\nhttps://github.com/Alchyr/BaseLib-StS2");
            return;
        }

        var modConfigSubmenu = stackInstance.PushSubmenuType<NModConfigSubmenu>();
        modConfigSubmenu.LoadModConfig(modConfig, this);

        IsConfigOpen = true;
    }
}