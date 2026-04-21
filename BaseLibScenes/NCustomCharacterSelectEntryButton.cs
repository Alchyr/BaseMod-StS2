using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.BaseLibScenes;

/// <summary>
/// Internal UI node for rendering a custom character select entry button.
/// </summary>
internal partial class NCustomCharacterSelectEntryButton : NButton
{
    private const string OutlineTexturePath = "res://images/packed/character_select/char_select_outline.png";
    private const string ButtonMaskTexturePath = "res://images/packed/character_select/char_select_button_mask.png";
    private const string AdditiveMaterialPath = "res://themes/canvas_item_material_additive_shared.tres";

    private static readonly StringName S = new("s");
    private static readonly StringName V = new("v");
    private static readonly Vector2 HoverScale = Vector2.One * 1.1f;

    private TextureRect? _icon;
    private Control? _outline;
    private ShaderMaterial? _hsv;
    private Tween? _hoverTween;
    private Action<NCustomCharacterSelectEntryButton>? _onSelected;
    private bool _isSelected;

    /// <summary>
    /// Entry currently bound to this button.
    /// </summary>
    public CustomCharacterSelectEntry? Entry { get; private set; }

    public NCustomCharacterSelectEntryButton()
    {
        Name = nameof(NCustomCharacterSelectEntryButton);
        CustomMinimumSize = new Vector2(100, 148);
        LayoutMode = 3;
        PivotOffset = new Vector2(50, 74);
        FocusMode = FocusModeEnum.All;

        var maskTexture = GD.Load<Texture2D>(ButtonMaskTexturePath)
                          ?? throw new InvalidOperationException($"Failed to load {ButtonMaskTexturePath}.");
        var outlineTexture = GD.Load<Texture2D>(OutlineTexturePath)
                             ?? throw new InvalidOperationException($"Failed to load {OutlineTexturePath}.");
        var additiveMaterial = GD.Load<Material>(AdditiveMaterialPath)
                               ?? throw new InvalidOperationException($"Failed to load {AdditiveMaterialPath}.");

        var margin = new MarginContainer
        {
            Name = "MarginContainer",
            LayoutMode = 1,
            MouseFilter = MouseFilterEnum.Ignore
        };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 6);
        margin.AddThemeConstantOverride("margin_top", 9);
        margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddThemeConstantOverride("margin_bottom", 9);
        AddChild(margin);

        var control = new Control
        {
            Name = "Control",
            LayoutMode = 2
        };
        margin.AddChild(control);

        var shadow = new TextureRect
        {
            Name = "Shadow",
            Modulate = new Color(0f, 0f, 0f, 0.25f),
            ShowBehindParent = true,
            LayoutMode = 0,
            OffsetLeft = 8f,
            OffsetTop = 10f,
            OffsetRight = 96f,
            OffsetBottom = 140f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
            PivotOffset = new Vector2(44, 65),
            MouseFilter = MouseFilterEnum.Ignore,
            Texture = maskTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
        };
        control.AddChild(shadow);

        _outline = new TextureRect
        {
            Name = "Outline",
            UniqueNameInOwner = true,
            Visible = false,
            SelfModulate = new Color(0.94f, 0.706f, 0.16f, 0.85f),
            ShowBehindParent = true,
            Material = additiveMaterial,
            LayoutMode = 0,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
            Scale = new Vector2(1.16f, 1.06f),
            PivotOffset = new Vector2(44, 65),
            MouseFilter = MouseFilterEnum.Ignore,
            Texture = outlineTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
        };
        control.AddChild(_outline);

        var mask = new TextureRect
        {
            Name = "Mask",
            ClipChildren = ClipChildrenMode.Only,
            LayoutMode = 2,
            Texture = maskTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale
        };
        margin.AddChild(mask);

        _icon = new TextureRect
        {
            Name = "Icon",
            UniqueNameInOwner = true,
            Material = ShaderUtils.GenerateHsv(1f, 0.2f, 0.8f),
            LayoutMode = 0,
            OffsetTop = 3f,
            OffsetRight = 88f,
            OffsetBottom = 133f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
            Scale = new Vector2(1.01f, 1.01f),
            PivotOffset = new Vector2(44, 65),
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale
        };
        mask.AddChild(_icon);
    }

    public override void _Ready()
    {
        ConnectSignals();
        _hsv = (ShaderMaterial)_icon!.Material;
        _hsv.SetShaderParameter(S, 0.2f);
        _hsv.SetShaderParameter(V, 0.8f);
        Connect(Control.SignalName.FocusEntered, Callable.From(Select));
        if (Entry != null)
        {
            _icon.Texture = ResourceLoader.Load<Texture2D>(Entry.ButtonIconPath);
        }
        RefreshVisualState(immediate: true);
    }

    /// <summary>
    /// Binds the button to an entry and selection callback.
    /// </summary>
    public void Initialize(CustomCharacterSelectEntry entry, Action<NCustomCharacterSelectEntryButton> onSelected)
    {
        Entry = entry;
        _onSelected = onSelected;

        if (_icon != null)
        {
            _icon.Texture = ResourceLoader.Load<Texture2D>(entry.ButtonIconPath);
        }
    }

    /// <summary>
    /// Marks the button selected and notifies the owning screen.
    /// </summary>
    public void Select()
    {
        if (_isSelected) return;

        _hoverTween?.Kill();
        _isSelected = true;
        _onSelected?.Invoke(this);
        RefreshVisualState(immediate: false);
    }

    /// <summary>
    /// Clears the selected visual state.
    /// </summary>
    public void Deselect()
    {
        if (!_isSelected) return;

        _isSelected = false;
        RefreshVisualState(immediate: false);
    }

    /// <summary>
    /// Attempts to return keyboard/controller focus to this button.
    /// </summary>
    public void TryGrabFocus()
    {
        if (Visible && IsInsideTree())
        {
            GrabFocus();
        }
    }

    protected override void OnFocus()
    {
        if (_isSelected) return;

        _hoverTween?.Kill();
        Scale = HoverScale;
        _hsv?.SetShaderParameter(S, 1f);
        _hsv?.SetShaderParameter(V, 1.1f);
        SfxCmd.Play("event:/sfx/ui/clicks/ui_hover");
    }

    protected override void OnPress()
    {
    }

    protected override void OnUnfocus()
    {
        if (_isSelected) return;

        RefreshVisualState(immediate: false);
    }

    private void RefreshVisualState(bool immediate)
    {
        if (_outline == null || _hsv == null) return;

        _outline.Visible = _isSelected;

        var targetScale = _isSelected ? HoverScale : Vector2.One;
        var targetS = _isSelected ? 1f : 0.2f;
        var targetV = _isSelected ? 1.1f : 0.8f;

        if (immediate)
        {
            Scale = targetScale;
            _hsv.SetShaderParameter(S, targetS);
            _hsv.SetShaderParameter(V, targetV);
            return;
        }

        _hoverTween?.Kill();
        _hoverTween = CreateTween().SetParallel();
        _hoverTween.TweenProperty(this, "scale", targetScale, 0.5f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
        _hoverTween.TweenMethod(Callable.From<float>(UpdateShaderS), _hsv.GetShaderParameter(S), targetS, 0.5f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
        _hoverTween.TweenMethod(Callable.From<float>(UpdateShaderV), _hsv.GetShaderParameter(V), targetV, 0.5f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
    }

    private void UpdateShaderS(float value)
    {
        _hsv?.SetShaderParameter(S, value);
    }

    private void UpdateShaderV(float value)
    {
        _hsv?.SetShaderParameter(V, value);
    }
}
