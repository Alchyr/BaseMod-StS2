using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Config.UI;

public partial class NConfigButton : NSettingsButton
{
    private Action? _onPressedAction;
    private ShaderMaterial _colorShader;

    public NConfigButton()
    {
        CustomMinimumSize = new Vector2(324, 64);
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        FocusMode = FocusModeEnum.All;

        _colorShader = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("res://shaders/hsv.gdshader") };

        var image = new TextureRect
        {
            Name = "Image",
            Material = _colorShader,
            CustomMinimumSize = new Vector2(64, 64),
            Texture = PreloadManager.Cache.GetAsset<Texture2D>("res://images/ui/reward_screen/reward_skip_button.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale
        };
        image.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(image);

        var label = new Label
        {
            Name = "Label",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LabelSettings = new LabelSettings
            {
                Font = PreloadManager.Cache.GetAsset<FontVariation>("res://themes/kreon_bold_glyph_space_two.tres"),
                FontSize = 28,
                FontColor = new Color(0.91f, 0.86f, 0.74f),
                OutlineSize = 12,
                OutlineColor = new Color(0.29f, 0.14f, 0.14f)
            }
        };
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(label);

        var reticleScene = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/selection_reticle"));
        var reticle = reticleScene.Instantiate<NSelectionReticle>();
        reticle.Name = "SelectionReticle";
        reticle.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        AddChild(reticle);
    }

    /// <summary>
    /// Sets the color using an HSV shader.
    /// </summary>
    /// <param name="h">Hue, range 0-1</param>
    /// <param name="s">Saturation, 0-1 or higher for boosted saturation</param>
    /// <param name="v">Value, range 0-1</param>
    public void SetColor(float h, float s, float v)
    {
        _colorShader.SetShaderParameter("h", h);
        _colorShader.SetShaderParameter("s", s);
        _colorShader.SetShaderParameter("v", v);
    }

    public override void _Ready()
    {
        ConnectSignals();
    }

    public void Initialize(string buttonText, Action onPressed)
    {
        _onPressedAction = onPressed;

        var label = GetNodeOrNull<Label>("Label");
        if (label != null)
        {
            label.Text = buttonText;
        }

        _onPressedAction = onPressed;
        Connect(NClickableControl.SignalName.Released, Callable.From<NConfigButton>(OnReleased));
    }

    private void OnReleased(NConfigButton button)
    {
        _onPressedAction?.Invoke();
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        Disconnect(NClickableControl.SignalName.Released, Callable.From<NConfigButton>(OnReleased));
    }
}