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

    private bool _fullyInitialized;
    private NSlider _slider;
    private MegaLabel _sliderLabel;
    private NSelectionReticle _selectionReticle;

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

    // These store the real min/max values used in the UI. NSlider has different ones, as it does not support
    // negative numbers.
    public double MinValue { get; private set; }
    public double MaxValue { get; private set; }
    public double Step => _slider.Step;

    /// <summary>
    /// Updates the slider's limits (and optionally step) atomically, ensuring no issues where e.g. min > max occur,
    /// even briefly.<br/>
    /// The current value is clamped to fit inside [min, max].<br/>
    /// Use <see cref="SliderRangeAttribute"/> instead if your values are known at compile time.
    /// </summary>
    /// <exception cref="ArgumentException">If min is greater than or equal to max</exception>
    public void SetRange(double min, double max, double? step = null)
    {
        if (min >= max)
            throw new ArgumentException($"Invalid slider range: min ({min}) must be less than max ({max}).");

        // We use an offset to enable negative value support in NSlider
        var currentRealValue = _slider.Value + MinValue;

        MinValue = min;
        MaxValue = max;
        if (step != null) _slider.Step = step.Value;

        // _slider.MinValue is always 0, so we don't touch it here even if _realMin changes
        _slider.MaxValue = MaxValue - MinValue;
        RecalculateMinRepeatDelay();

        // NSlider.SetValueWithoutAnimation crashes unless its _Ready has executed, but if SetRange is called prior
        // to AddChild, we don't need call SetValue here anyhow: it happens in *our* _Ready, so prior to display.
        if (_fullyInitialized) SetValue(currentRealValue);
    }

    public void RecalculateMinRepeatDelay()
    {
        var numSteps = (float)((_slider.MaxValue - _slider.MinValue) / _slider.Step);
        var dynamicFloor = 1.5f / numSteps;
        _minRepeatDelay = Mathf.Max(0.002f, dynamicFloor);
    }

    public override void _Ready()
    {
        _slider.FocusMode = FocusModeEnum.All;

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

        _fullyInitialized = true;
    }

    public void Initialize(ModConfig modConfig, PropertyInfo property)
    {
        if (property.PropertyType != typeof(double))
            throw new ArgumentException("Attempted to assign NConfigSlider a non-double property");

        _config = modConfig;
        _property = property;

        var rangeAttr = property.GetCustomAttribute<SliderRangeAttribute>();
        var formatAttr = property.GetCustomAttribute<SliderLabelFormatAttribute>();

        var min = rangeAttr?.Min ?? 0;
        var max = rangeAttr?.Max ?? 100;
        var step = rangeAttr?.Step ?? 1;
        _displayFormat = formatAttr?.Format ?? "{0}";

        if (min >= max)
            throw new ArgumentException($"Invalid slider range: min ({min}) must be less than max ({max}).");

        MinValue = min;
        MaxValue = max;

        // Force the internal Godot NSlider to run from 0 upwards, since it does not support negative numbers
        _slider.MinValue = 0;
        _slider.MaxValue = MaxValue - MinValue;
        _slider.Step = step;

        RecalculateMinRepeatDelay();
    }

    private void SetFromProperty()
    {
        var propValue = (double)_property!.GetValue(null)!;
        SetValue(propValue);
    }

    private void SetValue(double value)
    {
        var clampedValue = Math.Clamp(value, MinValue, MaxValue);
        _slider.SetValueWithoutAnimation(clampedValue - MinValue);
        UpdateLabel(clampedValue);

        // Clamp always returns one of its parameters, so exactness isn't an issue here
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (value != clampedValue) _property?.SetValue(null, clampedValue);
    }

    private void OnValueChanged(double proxyValue)
    {
        var realValue = proxyValue + MinValue;

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