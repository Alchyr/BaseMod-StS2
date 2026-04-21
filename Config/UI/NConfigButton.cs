using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Config.UI;

public partial class NConfigButton : NSettingsButton
{
    private Action? _onPressedAction;
    private new TextureRect _image;
    public static readonly string DefaultColor = "#3b7a83";

    public NConfigButton()
    {
        CustomMinimumSize = new Vector2(324, 64);
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        FocusMode = FocusModeEnum.All;

        _image = new TextureRect
        {
            Name = "Image",
            CustomMinimumSize = new Vector2(64, 64),
            Texture = PreloadManager.Cache.GetAsset<Texture2D>("res://BaseLib/images/config/configbutton.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale
        };
        _image.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_image);

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
    /// OBSOLETE: Sets the color using an HSV shader. Broken and will be removed soon; ONLY kept for binary compatibility.
    /// </summary>
    /// <param name="h">Hue, range 0-1</param>
    /// <param name="s">Saturation, 0-1 or higher for boosted saturation</param>
    /// <param name="v">Value, range 0-1</param>
    [Obsolete("BROKEN: Use SetColor(Color) instead")]
    public void SetColor(float h, float s, float v)
    {
        // TODO: remove this method, perhaps in May or June 2026. It likely has no external users and is kept for a while just in case.
        const float baseHue = 0.48f;
        var outputHue = (baseHue + (1f - h)) % 1f;
        SetColor(Color.FromHsv(outputHue, Math.Clamp(s * 0.4f, 0f, 1f), v));
    }

    /// <summary>
    /// Sets the button's color using Godot's SelfModulate property.<br/>
    /// The overall color will be slightly darker than the color specified, since it's a modulation of the existing
    /// colors, that aren't fully white.
    /// </summary>
    /// <param name="color">The color to use for SelfModulate.</param>
    public void SetColor(Color color)
    {
        _image.SelfModulate = color;
    }

    public override void _Ready()
    {
        ConnectSignals();
    }

    public void Initialize(string buttonText, Action onPressed)
    {
        SetColor(Color.FromHtml(DefaultColor));
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