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
    public static readonly Type[] SupportedTypes = [typeof(int), typeof(float), typeof(double)];
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

    private static bool IsInteger(double value) => Math.Abs(value - Math.Round(value)) < 1e-6;

    /// <summary>
    /// Updates the slider's limits (and optionally step) atomically, ensuring no issues where e.g. min > max occur,
    /// even briefly.<br/>
    /// The current value is clamped to fit inside [min, max].<br/>
    /// Use <see cref="ConfigSliderAttribute"/> instead if your values are known at compile time.
    /// </summary>
    /// <exception cref="ArgumentException">If min is greater than or equal to max</exception>
    public void SetRange(double min, double max, double? step = null)
    {
        if (min >= max)
            throw new ArgumentException($"Invalid slider range: min ({min}) must be less than max ({max}).");
        if (step <= 0 || step > max - min)
            throw new ArgumentException($"Invalid slider step: step must be greater than zero, and no larger than than max-min.");

        if (_property?.PropertyType == typeof(int))
        {
            if (!IsInteger(min) || !IsInteger(max))
                throw new ArgumentException("Invalid slider values: min and max must be integers for property type int");

            min = Math.Round(min);
            max = Math.Round(max);

            if (step.HasValue)
            {
                if (!IsInteger(step.Value))
                    throw new ArgumentException("Invalid slider values: step must be integer for property type int");

                step = Math.Round(step.Value);
            }
        }

        // We use an offset to enable negative value support in NSlider
        var currentRealValue = _slider.Value + MinValue;

        MinValue = min;
        MaxValue = max;

        // The internal NSlider does not support negative numbers, so we force it to run from 0 and up, and handle the
        // "real" values manually
        _slider.MinValue = 0;
        _slider.MaxValue = MaxValue - MinValue;
        if (step != null) _slider.Step = step.Value;
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

        _config!.OnConfigReloaded += SetFromProperty;

        _fullyInitialized = true;
    }

    public void Initialize(ModConfig modConfig, PropertyInfo property)
    {
        if (!SupportedTypes.Contains(property.PropertyType))
            throw new ArgumentException("Attempted to initialize NConfigSlider with an unsupported property type. " +
                                        $"Supported types: {string.Join<Type>(", ", SupportedTypes)}");

        _config = modConfig;
        _property = property;

        var sliderAttr = property.GetCustomAttribute<ConfigSliderAttribute>();

#pragma warning disable CS0618 // Type or member is obsolete
        var legacyFormatAttr = property.GetCustomAttribute<SliderLabelFormatAttribute>();
#pragma warning restore CS0618 // Type or member is obsolete

        _displayFormat = sliderAttr?.Format ?? legacyFormatAttr?.Format ?? "{0}";
        var min = sliderAttr?.Min ?? 0;
        var max = sliderAttr?.Max ?? 100;
        var step = sliderAttr?.Step ?? 1;
        SetRange(min, max, step);
    }

    private void SetFromProperty()
    {
        var rawValue = _property!.GetValue(null)!;
        SetValue(Convert.ToDouble(rawValue));
    }

    private void SetValue(double value)
    {
        var clampedValue = Math.Clamp(value, MinValue, MaxValue);
        _slider.SetValueWithoutAnimation(clampedValue - MinValue);
        UpdateLabel(clampedValue);

        // Clamp always returns one of its parameters, so exactness isn't an issue here
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (value != clampedValue) _property?.SetValue(null, Convert.ChangeType(clampedValue, _property.PropertyType));
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

        _property?.SetValue(null, Convert.ChangeType(realValue, _property.PropertyType));
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

    public override void _ExitTree()
    {
        base._ExitTree();
        if (_config != null) _config.OnConfigReloaded -= SetFromProperty;
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