using System.Reflection;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Config.UI;

// We don't inherit from NSettingsSlider because it's too rigid (forces % in the format, forces step of 5, fixed width)
public partial class NConfigSlider : Control
{
    private ModConfig? _config;
    private PropertyInfo? _property;
    private string _displayFormat = "{0}";

    private NSlider _slider;
    private MegaLabel _sliderLabel;
    private NSelectionReticle _selectionReticle;

    // _realMin is a workaround to support negative numbers, without forcing
    // the underlying NSlider to understand that such things really exist
    private double _realMin;
    private const int LabelFontSize = 28;

    // Controller hold-to-accelerate stuff
    private enum HoldDirection { None, Left, Right }
    private HoldDirection _holdDir = HoldDirection.None;
    private float _holdTimer;
    private float _stepTimer;
    private float _currentRepeatRate = 0.1f;
    private const float InitialDelay = 0.3f;
    private const float StartingRepeatRate = 0.1f;
    private float _minRepeatDelay = 0.002f;

    public NConfigSlider()
    {
        var targetSize = new Vector2(324, 64);
        CustomMinimumSize = targetSize;
        Size = targetSize;
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        FocusMode = FocusModeEnum.All;

        this.TransferAllNodes(SceneHelper.GetScenePath("screens/settings_slider"));

        _slider = GetNode<NSlider>("Slider");
        _sliderLabel = GetNode<MegaLabel>("SliderValue");
        _selectionReticle = GetNode<NSelectionReticle>((NodePath) "SelectionReticle");
    }

    public override void _Ready()
    {
        _slider.FocusMode = FocusModeEnum.None;
        var numSteps = (float)((_slider.MaxValue - _slider.MinValue) / _slider.Step);
        var dynamicFloor = 1.5f / numSteps;
        _minRepeatDelay = Mathf.Max(0.002f, dynamicFloor);

        _sliderLabel.AutoSizeEnabled = false;
        _sliderLabel.AddThemeFontSizeOverride("font_size", LabelFontSize);

        // Right-align the label and let it overflow, so users can use formats more than a few characters wide
        _sliderLabel.GrowHorizontal = GrowDirection.Begin;
        _sliderLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _sliderLabel.ClipContents = false;

        _selectionReticle.AnchorRight = 1f;
        _selectionReticle.OffsetRight = 10f;

        SetFromProperty();
        _slider.Connect(Godot.Range.SignalName.ValueChanged, Callable.From<double>(OnValueChanged));
        Connect(Godot.Control.SignalName.FocusEntered, Callable.From(OnFocus));
        Connect(Godot.Control.SignalName.FocusExited, Callable.From(OnUnfocus));
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

        // Round to avoid floating point precision issues
        var step = _slider.Step;
        if (step > 0)
        {
            var decimalPlaces = BitConverter.GetBytes(decimal.GetBits((decimal)step)[3])[2];
            realValue = Math.Round(realValue, decimalPlaces);
        }

        _property?.SetValue(null, realValue);
        _config?.Changed();
        UpdateLabel(realValue);
    }

    private void UpdateLabel(double value)
    {
        _sliderLabel.Text = string.Format(_displayFormat, value);

        // Update the reticle; the label size doesn't match the text size, so calculate the correct offset manually
        var textWidth = _sliderLabel.GetMinimumSize().X;
        var labelRightEdge = _sliderLabel.Position.X + _sliderLabel.Size.X;
        var labelLeftEdge = labelRightEdge - textWidth;
        _selectionReticle.OffsetLeft = labelLeftEdge - 10f;
    }

    private void OnFocus()
    {
        if (NControllerManager.Instance?.IsUsingController != true) return;
        _selectionReticle.OnSelect();
    }

    private void OnUnfocus()
    {
        _selectionReticle.OnDeselect();
    }

    // The remaining code below is all controller specific, to improve UX by not being forced to tap left/right
    // once for every step you want to move the slider. (Technically works with keyboard, too.)

    public override void _GuiInput(InputEvent @event)
    {
        base._GuiInput(@event);

        if (@event.IsActionPressed(MegaInput.left))
        {
            _slider.Value -= _slider.Step;
            StartHolding(HoldDirection.Left);
            AcceptEvent();
        }
        else if (@event.IsActionPressed(MegaInput.right))
        {
            _slider.Value += _slider.Step;
            StartHolding(HoldDirection.Right);
            AcceptEvent();
        }

        else if (@event.IsActionReleased(MegaInput.left) && _holdDir == HoldDirection.Left ||
                 @event.IsActionReleased(MegaInput.right) && _holdDir == HoldDirection.Right)
        {
            _holdDir = HoldDirection.None;
        }
    }

    private void StartHolding(HoldDirection dir)
    {
        _holdDir = dir;
        _holdTimer = 0f;
        _stepTimer = 0f;
        _currentRepeatRate = StartingRepeatRate;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (_holdDir == HoldDirection.None) return;
        if (!HasFocus())
        {
            _holdDir = HoldDirection.None;
            return;
        }

        _holdTimer += (float)delta;
        if (_holdTimer < InitialDelay) return;

        _stepTimer += (float)delta;
        if (_stepTimer < _currentRepeatRate) return;

        // Time to make a step; accelerate until we reach the limit
        _stepTimer = 0f;
        _currentRepeatRate = Mathf.Clamp(_currentRepeatRate - 0.01f, _minRepeatDelay, 0.15f);

        if (_holdDir == HoldDirection.Left)
            _slider.Value -= _slider.Step;
        else
            _slider.Value += _slider.Step;
    }
}