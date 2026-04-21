using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace BaseLib.Config.UI;

public partial class NConfigColorPicker : CenterContainer, ISelectionReticle
{
    public static readonly Type[] SupportedTypes = [typeof(Color), typeof(string)];

    public NSelectionReticle? Reticle { get; set; }
    private ColorPickerButton _button;
    private ColorPicker _picker;
    private Popup _popup;
    private Tween? _tween;

    private ModConfig? _config;
    private PropertyInfo? _property;

    public NConfigColorPicker()
    {
        CustomMinimumSize = new Vector2(324, 64);
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;

        _button = new ColorPickerButton
        {
            CustomMinimumSize =  new Vector2(44, 44),
            PivotOffset = new Vector2(22, 22),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop,
            EditAlpha = true,
            EditIntensity = false
        };

        AddChild(_button);
        _picker = _button.GetPicker();
        _popup = _button.GetPopup();

        ((ISelectionReticle)this).SetupSelectionReticle(_button);
        FocusEntered += OnFocus;
        FocusExited += OnUnfocus;

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = new Color(0.3f, 0.3f, 0.3f),
            BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderWidthTop = 2, BorderWidthBottom = 2,

            ContentMarginLeft = 2, ContentMarginRight = 2,
            ContentMarginTop = 2, ContentMarginBottom = 2
        };

        _button.AddThemeStyleboxOverride("normal", style);
        _button.AddThemeStyleboxOverride("pressed", style);
        _button.AddThemeStyleboxOverride("hover", style);
        _button.AddThemeStyleboxOverride("focus", style);
    }
    
    public override void _Ready()
    {
        base._Ready();
        _picker.DeferredMode = true;

        if (_property == null) throw new Exception("NConfigColorPicker added to tree without an assigned property");
        SetFromProperty();

        _picker.ColorChanged += OnColorChanged;
        _button.MouseEntered += OnHover;
        _button.MouseExited += OnUnhover;

        var attr = _property.GetCustomAttribute<ConfigColorPickerAttribute>();
        if (attr == null) return;
        _picker.EditAlpha = attr.EditAlpha;
        _picker.EditIntensity = attr.EditIntensity && _property.PropertyType == typeof(Color);
    }

    public void Initialize(ModConfig modConfig, PropertyInfo property)
    {
        if (!SupportedTypes.Contains(property.PropertyType))
            throw new ArgumentException("Attempted to initialize NConfigColorPicker with an unsupported property type. " +
                                        $"Supported types: {string.Join<Type>(", ", SupportedTypes)}");

        _config = modConfig;
        _property = property;

        _config.OnConfigReloaded += SetFromProperty;
    }

    private void OnColorChanged(Color color)
    {
        object value = _property!.PropertyType == typeof(Color) ? color : $"#{color.ToHtml(true)}";
        _property?.SetValue(null, value);
        _config?.Changed();
    }

    private void SetFromProperty()
    {
        object? propValue = null;
        try
        {
            propValue = _property!.GetValue(null)!;
            _button.Color = _property.PropertyType == typeof(Color)
                ? (Color)propValue
                : Color.FromHtml(propValue as string);
        }
        catch (Exception)
        {
            BaseLibMain.Logger.Warn($"Failed to set value '{propValue}' for Color Picker {_property!.Name} in {GetType().FullName}");
        }
    }

    private void OnHover()
    {
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(_button, "scale", Vector2.One * 1.12f, 0.05);
    }

    private void OnUnhover()
    {
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(_button, "scale", Vector2.One, 0.5).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
    }

    private void OnFocus()
    {
        if (NControllerManager.Instance?.IsUsingController != true) return;
        OnHover();
    }

    private void OnUnfocus()
    {
        OnUnhover();
    }

    public override void _ExitTree()
    {
        if (_config != null) _config.OnConfigReloaded -= SetFromProperty;
        _picker.ColorChanged -= OnColorChanged;
        _button.MouseEntered -= OnHover;
        _button.MouseExited -= OnUnhover;
        base._ExitTree();
    }
}