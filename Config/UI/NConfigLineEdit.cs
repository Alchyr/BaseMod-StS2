using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Config.UI;

public partial class NConfigLineEdit : NMegaLineEdit
{
    private ModConfig? _config;
    private PropertyInfo? _property;

    private StyleBoxFlat? _focusInvalid;
    private Regex? _validationRegex;
    private string _lastValidText = "";

    public NConfigLineEdit()
    {
        var targetSize = new Vector2(324, 64);
        CustomMinimumSize = targetSize;
        Size = targetSize;
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        FocusMode = FocusModeEnum.All;
    }

    private bool ValidateString(string value)
    {
        return _validationRegex == null || _validationRegex.IsMatch(value);
    }

    public void Initialize(ModConfig modConfig, PropertyInfo property)
    {
        if (property.PropertyType != typeof(string))
            throw new ArgumentException("Attempted to assign NConfigLineEdit a non-string property");

        _config = modConfig;
        _property = property;

        var attr = property.GetCustomAttribute<ConfigTextInputAttribute>();
        if (attr?.MaxLength > 0) MaxLength = attr.MaxLength;

        var placeholderKey = $"{_config.ModPrefix}{StringHelper.Slugify(property.Name)}.placeholder";
        var locPlaceholder = LocString.GetIfExists("settings_ui", placeholderKey)?.GetFormattedText();
        PlaceholderText = locPlaceholder ?? "";

        try
        {
            var pattern = attr?.AllowedCharactersRegex ?? ".*";
            _validationRegex = new Regex($"^(?:{pattern})$", RegexOptions.Compiled);
        }
        catch (Exception e)
        {
            // Should only happen for a modder, so showing in the GUI should be helpful rather than invasive
            ModConfig.ModConfigLogger.Error($"Unable to compile validation regex for {property.Name}: {e.Message}");
        }
    }

    public override void _Ready()
    {
       SetFromProperty();

       // We can also duplicate the current and change BorderColor, but if the game changes from a StyleboxFlat in the
       // future, that won't work. This will always work, but will instead look bad if the base game style changes.
       _focusInvalid = new StyleBoxFlat();
       _focusInvalid.DrawCenter = false;
       _focusInvalid.BorderColor = new Color (1, 0, 0);
       _focusInvalid.SetBorderWidthAll(2);
       _focusInvalid.SetCornerRadiusAll(3);
       _focusInvalid.SetExpandMarginAll(2);

       Connect(Godot.LineEdit.SignalName.TextChanged, Callable.From<string>(OnTextChanged));
       Connect(Godot.LineEdit.SignalName.TextSubmitted, Callable.From<string>(OnTextSubmitted));
       Connect(Godot.Control.SignalName.FocusExited, Callable.From(OnUnfocus));
    }

    private void SetFromProperty()
    {
        var propValue = (string?)_property!.GetValue(null) ?? "";

        if (!ValidateString(propValue))
        {
            MainFile.Logger.Warn($"{_property.Name}: stored value '{propValue}' violates the validation regex; resetting value");
            propValue = "";
            _property.SetValue(null, propValue);
            _config?.Changed();
        }

        _lastValidText = propValue;
        Text = propValue;
    }

    private void OnTextChanged(string newText)
    {
        if (!ValidateString(newText))
        {
            AddThemeStyleboxOverride("focus", _focusInvalid);
            return;
        }

        RemoveThemeStyleboxOverride("focus");
        _lastValidText = newText;

        _property?.SetValue(null, newText);
        _config?.Changed();
    }

    private void OnTextSubmitted(string submittedText)
    {
        RevertIfInvalid();
        ReleaseFocus();
    }

    private void OnUnfocus()
    {
        RevertIfInvalid();
    }

    private void RevertIfInvalid()
    {
        RemoveThemeStyleboxOverride("focus");
        if (ValidateString(Text)) return;
        Text = _lastValidText;
    }

    public override void _GuiInput(InputEvent @event)
    {
        base._GuiInput(@event);

        // Default action for controllers is to place the caret at the start, even when there's text already, but
        // only the *first* time. We want the default behavior (remember the position) the rest of the time.
        if (@event.IsActionPressed(MegaInput.select) && CaretColumn == 0)
        {
            CaretColumn = Text.Length;
        }
    }
}