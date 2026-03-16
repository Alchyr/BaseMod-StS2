using System.Reflection;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Config.UI;

// We don't inherit from NSettingsSlider because it's too rigid
public partial class NConfigSlider : Control
{
    private ModConfig? _config;
    private PropertyInfo? _property;
    private string _displayFormat = "{0}";

    // _realMin is a workaround to support negative numbers, without forcing
    // the underlying NSlider to understand that such things really exist
    private NSlider _slider = null!;
    private double _realMin;

    public NConfigSlider()
    {
        SetCustomMinimumSize(new Vector2(324, 64));
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
    }

    public override void _Ready()
    {
        _slider = GetNode<NSlider>("Slider");

        var label = GetNodeOrNull<MegaLabel>("SliderValue");
        if (label != null)
        {
            label.AutoSizeEnabled = false;
            label.AddThemeFontSizeOverride("font_size", 28);

            // Right-align the label and let it overflow, so users can use formats more than a few characters wide
            label.GrowHorizontal = GrowDirection.Begin;
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.ClipContents = false;
        }

        SetFromProperty();
        _slider.Connect(Godot.Range.SignalName.ValueChanged, Callable.From<double>(OnValueChanged));
    }

    public void Initialize(ModConfig modConfig, PropertyInfo property)
    {
        if (property.PropertyType != typeof(double)) throw new ArgumentException("Attempted to assign NConfigSlider a non-double property");

        _config = modConfig;
        _property = property;
    }

    private void SetFromProperty()
    {
        var rangeAttr = _property!.GetCustomAttribute<SliderRangeAttribute>();
        var formatAttr = _property!.GetCustomAttribute<SliderLabelFormatAttribute>();

        _realMin = rangeAttr?.Min ?? 0;
        var realMax = rangeAttr?.Max ?? 100;

        // Force the internal slider to run from 0 upwards
        _slider.MinValue = 0;
        _slider.MaxValue = realMax - _realMin;
        _slider.Step = rangeAttr?.Step ?? 1;

        _displayFormat = formatAttr?.Format ?? "{0}";

        var propValue = (double)_property!.GetValue(null)!;
        var clampedValue = Math.Clamp(propValue, _realMin, realMax);

        _slider.SetValueWithoutAnimation(clampedValue - _realMin);
        UpdateLabel(clampedValue);
    }

    private void OnValueChanged(double proxyValue)
    {
        var realValue = proxyValue + _realMin;

        _property?.SetValue(null, realValue);
        _config?.Changed();
        UpdateLabel(realValue);
    }

    private void UpdateLabel(double value)
    {
        var label = GetNodeOrNull<MegaLabel>("SliderValue");
        label?.SetText(string.Format(_displayFormat, value));
    }
}