using BaseLib.Config.UI;

namespace BaseLib.Config;

/// <summary>
/// Creates a new section in the ModConfig UI, where the next property will be the first entry after the section header.
/// </summary>
/// <param name="name">The LocString name. Will be transformed just like property names, so for "FirstSection", you need to
/// specify a value for YOURMOD-FIRST_SECTION.title in the localization file(s).</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
public class ConfigSectionAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Specifies settings for a slider: range, step and label format string. Negative numbers are supported, as are
/// noninteger numbers.<br/>
/// Supported property types: <see cref="int"/>, <see cref="float"/>, <see cref="double"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ConfigSliderAttribute(double min = 0.0, double max = 100.0, double step = 1.0) : Attribute
{
    /// <summary>The minimum value the user can select.</summary>
    public double Min { get; } = min;

    /// <summary>The maximum value the user can select.</summary>
    public double Max { get; } = max;

    /// <summary>The smallest step between two values, and the amount moved by a quick controller input.</summary>
    public double Step { get; } = step;

    /// <summary>
    /// The string format to use for the slider's label.
    /// Uses standard C# format, see <see cref="String.Format(string, object?)"/>.
    /// </summary>
    public string? Format { get; set; }
}

[Obsolete("Use [ConfigSlider] instead. This will be removed in future versions.")]
[AttributeUsage(AttributeTargets.Property)]
public class SliderRangeAttribute : ConfigSliderAttribute
{
    public SliderRangeAttribute(double min, double max, double step = 1.0) : base(min, max, step) { }
}

[Obsolete("Use the Format property on [ConfigSlider] instead. This will be removed in future versions.")]
[AttributeUsage(AttributeTargets.Property)]
public class SliderLabelFormatAttribute(string format) : Attribute
{
    public string Format { get; } = format;
}

/// <summary>
/// <para>Show a tooltip for this setting on hover. Requires a settings_ui.json localization file.
/// The names used are typically:</para>
/// YOURMOD-PROPERTY_NAME.hover.title (optional, if missing, no title will be shown)<br/>
/// YOURMOD-PROPERTY_NAME.hover.desc (required)
/// </summary>
/// <param name="enabled">Enable hover tip for this property. Can be set to false in order to add exceptions for <see cref="ConfigHoverTipsByDefaultAttribute"/></param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
public class ConfigHoverTipAttribute(bool enabled = true) : Attribute
{
    public bool Enabled { get; } = enabled;
}

/// <summary>
/// Attempt to add hover tips to ALL config properties in this class. Use <see cref="ConfigHoverTipAttribute"/> with
/// enabled=false to add exceptions. Still requires .hover.desc entries in your localization file, see <see cref="ConfigHoverTipAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ConfigHoverTipsByDefaultAttribute : Attribute;

[Obsolete("Use [ConfigHoverTipsByDefault] instead. This will be removed in future versions.")]
[AttributeUsage(AttributeTargets.Class)]
public class HoverTipsByDefaultAttribute : ConfigHoverTipsByDefaultAttribute;

/// <summary>
/// Completely ignores this property. It will not be loaded or saved, and will not be shown for auto-generated UI.<br/>
/// Intended for properties that aren't configuration options at all.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ConfigIgnoreAttribute : Attribute;

/// <summary>
/// Saves and loads this property to the config file normally, but generates no UI for it in SimpleModConfig.<br/>
/// Intended for when you want to create the UI manually, or easily persist things that aren't user-configurable (e.g.
/// total gold gained / number of runs played with the mod active).
/// </summary>
// Should be ConfigHideInUIAttribute, but AFAIK we can't rename it without breaking: a rename breaks ABI compatibility,
// and creating ConfigHideInUIAttribute + ConfigHideInUI that inherits from it causes compilation errors due to ambiguity.
[AttributeUsage(AttributeTargets.Property)]
public class ConfigHideInUI : Attribute;

/// <summary>
/// Defines a set of allowed characters for a LineEdit.
/// </summary>
public enum TextInputPreset
{
    /// <summary>The default. No validation at all.</summary>
    Anything,
    /// <summary>English letters and numbers (a-z, A-Z, 0-9).</summary>
    Alphanumeric,
    /// <summary>English letters, numbers (a-z, A-Z, 0-9) and spaces.</summary>
    AlphanumericWithSpaces,
    /// <summary>Allows international letters, numbers, spaces, underscores, and hyphens.</summary>
    SafeDisplayName,
}

/// <summary>
/// <para>Defines the allowed characters and max length for the LineEdit generated by a string property.<br/>
/// You can specify a <see cref="TextInputPreset"/> or a custom Regex string for the allowed characters. By default,
/// any string is allowed, at any length.</para>
/// If a placeholder LocString exists (YOURMOD-PROPERTY_NAME.placeholder), it will be used; if you don't want a
/// placeholder, simply skip creating such a string.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ConfigTextInputAttribute : Attribute
{
    public string AllowedCharactersRegex { get; }
    public int MaxLength { get; set; } = 0;

    public ConfigTextInputAttribute() : this(TextInputPreset.Anything) { }

    public ConfigTextInputAttribute(TextInputPreset preset)
    {
        AllowedCharactersRegex = preset switch
        {
            TextInputPreset.Alphanumeric => "[a-zA-Z0-9]+",
            TextInputPreset.AlphanumericWithSpaces => "[a-zA-Z0-9 ]+",
            TextInputPreset.SafeDisplayName => @"[\p{L}\d_\- ]+",
            TextInputPreset.Anything or _ => ".*",
        };
    }

    public ConfigTextInputAttribute(string customRegex)
    {
        AllowedCharactersRegex = customRegex;
    }
}

/// <summary>
/// <para>Creates a clickable button from a method. Source code order is respected, so if the method is defined in between
/// two properties, the button will be in between their respective controls.</para>
/// <para>The method supports having any of these different arguments injected with any name, in any order (only the type is used):</para>
/// <br/>
///   <b>ModConfig</b>: Injects the instance of your ModConfig (may be useful if the method is static).<br />
///   <b>NConfigButton</b>: Injects the instance of the button that was clicked.<br />
///   <b>NConfigOptionRow</b>: Injects the instance of the row the button belongs to (the node that handles its layout, hover tip, etc.).<br />
/// </summary>
/// <param name="buttonLabelKey">LocString key for the text on the button.</param>
[AttributeUsage(AttributeTargets.Method)]
public class ConfigButtonAttribute(string buttonLabelKey) : Attribute
{
    public string ButtonLabelKey { get; } = buttonLabelKey;

    /// <summary>The color to use for the button, as an HTML color code.</summary>
    public string Color { get; set; } = NConfigButton.DefaultColor;
}

/// <summary>
/// <para>Sets up conditional visibility for the marked property (or [ConfigButton] method). You can specify either a
/// property or a method as a target, and optionally specify Invert = true to hide the row if the condition is true.</para>
/// <para>If a property is specified, its value will be compared to the arguments passed to this attribute; if the
/// property value is equal to ANY argument, the row will be visible.<br/>
/// For a boolean property, you can leave the arguments out, in which case the row will be visible
/// if the property is equal to true.</para>
/// <para>If you instead specify a method returning bool, the method will be executed, and its return value used to
/// decide the row's visibility.<br/>
/// All arguments specified for the attribute will be injected to the method; in addition, you can specify arguments of
/// types <see cref="ModConfig"/>, <see cref="System.Reflection.PropertyInfo"/> (when annotating a property),
/// <see cref="System.Reflection.MethodInfo"/> (when annotating a method) and <see cref="System.Reflection.MemberInfo"/>
/// (to support both with a single method) to inject information about the current row.<br/>
/// You can combine these as you wish, e.g. bool MyMethod(PropertyInfo targetProp, int min, int max), and then specify
/// two integer values in the attribute.
/// </para>
/// </summary>
/// <param name="targetName">The property to compare against, or method to call. Use <c>nameof(PropOrMethodName)</c>.</param>
/// <param name="args">Optional arguments: see summary and examples.</param>
/// <example>
/// <b>Targeting a boolean property (argument optional, defaults to true):</b>
/// <code>
/// public static bool EnableCustomMusic { get; set; } = false;
/// &#160;
/// [ConfigVisibleIf(nameof(EnableCustomMusic))]
/// [ConfigSlider(0, 100)]
/// public static float MusicVolume { get; set; } = 50;
/// </code>
///
/// <b>Targeting a property with multiple allowed arguments:</b>
/// <code>
/// public enum StartingBonusType { None, Tiny, Standard, Huge }
/// public static StartingBonusType StartingBonus { get; set; } = StartingBonusType.Standard;
/// &#160;
/// // Visible if StartingBonus is Standard or Huge
/// [ConfigVisibleIf(nameof(StartingBonus), StartingBonusType.Standard, StartingBonusType.Huge)]
/// [ConfigSlider(0, 500)]
/// public static int BonusStartingGold { get; set; } = 0;
/// </code>
///
/// <b>Targeting a method with PropertyInfo auto-injection:</b>
/// <code>
/// [ConfigSlider(1, 5)]
/// public static int NumCustomColors { get; set; } = 1;
/// &#160;
/// private static bool ShouldShowColorRow(PropertyInfo prop)
/// {
///     // Dynamically extract the slot number from property names like "CustomColor2"
///     if (int.TryParse(prop.Name.Replace("CustomColor", ""), out int index))
///         return NumCustomColors &gt;= index;
///     return false;
/// }
///
/// [ConfigVisibleIf(nameof(ShouldShowColorRow))]
/// public static Color CustomColor1 { get; set; }
///
/// [ConfigVisibleIf(nameof(ShouldShowColorRow))]
/// public static Color CustomColor2 { get; set; }
///
/// // ... etc.
/// </code>
///
/// <b>Targeting a method with explicitly injected arguments:</b>
/// <code>
/// [ConfigSlider(1, 5)]
/// public static int EliteCardRewardCount { get; set; } = 1;
/// &#160;
/// private static bool IsRewardCountBetween(int min, int max)
/// {
///     return EliteCardRewardCount &gt;= min &amp;&amp; EliteCardRewardCount &lt;= max;
/// }
/// &#160;
/// [ConfigVisibleIf(nameof(IsRewardCountBetween), 2, 5)]
/// public static bool WeightRewardsTowardsRare { get; set; } = false;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
public class ConfigVisibleIfAttribute(string targetName, params object?[] args) : Attribute
{
    public string TargetName { get; } = targetName;
    public object?[] Args { get; } = args;

    /// <summary>
    /// If true, the visibility condition is inverted.
    /// (e.g., Hidden when the method returns true, or hidden when the property matches).
    /// </summary>
    public bool Invert { get; set; } = false;
}

/// <summary>
/// No longer functional. Use ConfigVisibleIfAttribute instead.
/// </summary>
/// <param name="watchedPropertyName">The name of the property to watch (must be in the same ModConfig class).</param>
/// <param name="expectedValue">The value the watched property must have for this row to be visible.</param>
/// <param name="invert">If true, the row is visible when the value does NOT match.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
[Obsolete("No longer functional. Use ConfigVisibleIfAttribute instead.", error: true)]
public class ConfigVisibleWhenAttribute(string watchedPropertyName, object expectedValue, bool invert = false) : Attribute
{
    public string WatchedPropertyName { get; } = watchedPropertyName;
    public object ExpectedValue { get; } = expectedValue;
    public bool Invert { get; } = invert;
}

/// <summary>
/// <para>Displays this property as a color picker element, and sets some properties on the color picker.<br/>
/// Not required for <see cref="Godot.Color"/> properties, if you want the default values.</para>
/// <para>Supported property types: <see cref="Godot.Color"/> and <see cref="string"/> (HTML color code).<br/>
/// However, note that string is limited to standard 8-bit values, while Color supports a wider range if
/// <see cref="EditIntensity"/> is true.</para>
/// <para>Beware: the default value for <see cref="Godot.Color"/> has alpha 0, so you likely want to set a default
/// value, especially if EditAlpha is false.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ConfigColorPickerAttribute : Attribute
{
    /// <summary>
    /// If true, allow the user to adjust the alpha (transparency) channel in the color picker popup.
    /// Defaults to true.
    /// </summary>
    public bool EditAlpha { get; set; } = true;

    /// <summary>
    /// <para>If true, allow the user to adjust the intensity slider, to support "overbright" or HDR-ish colors.<br/>
    /// Disabled by default, since these colors are not supported for all use cases.</para>
    /// <para>Note: This is ignored unless the property type is <see cref="Godot.Color"/> as these above-maximum values
    /// cannot be stored as hex strings.</para>
    /// </summary>
    public bool EditIntensity { get; set; } = false;
}