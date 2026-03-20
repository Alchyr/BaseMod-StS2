using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BaseLib.Config.UI;
using BaseLib.Extensions;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Config;

public abstract partial class ModConfig
{
    private const string SettingsTheme = "res://themes/settings_screen_line_header.tres";

    /// <summary>
    /// Event that fires when <see cref="Changed()"/> is called. Custom controls must call Changed() when mutating
    /// a property.
    /// </summary>
    public event EventHandler? ConfigChanged;

    private readonly string _path;
    public string ModPrefix { get; private set; }

    private readonly string _modConfigName;
    private bool _savingDisabled;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    protected readonly List<PropertyInfo> ConfigProperties = [];

    public static class ModConfigLogger
    {
        public static List<string> PendingUserMessages { get; } = [];

        public static void Warn(string message, bool showInGui = false)
        {
            MainFile.Logger.Warn(message);
            if (showInGui && !PendingUserMessages.Contains(message)) PendingUserMessages.Add(message);
        }

        public static void Error(string message, bool showInGui = true)
        {
            MainFile.Logger.Error(message);
            if (showInGui && !PendingUserMessages.Contains(message)) PendingUserMessages.Add(message);
        }
    }

    public ModConfig(string? filename = null)
    {
        ModPrefix = GetType().GetPrefix();
        _modConfigName = GetType().FullName ?? "unknown";
        var rootNamespace = GetType().GetRootNamespace();

        if (string.IsNullOrEmpty(rootNamespace) && string.IsNullOrEmpty(filename))
        {
            var message = "Cannot determine a safe configuration file path for " +
                          $"{_modConfigName} (assembly {GetType().Assembly.GetName().Name}). " +
                          "You must either place your configuration class inside a namespace, " +
                          "or explicitly provide a filename in the constructor.";
            ModConfigLogger.Error(message); // Shows it in the GUI when opening ANY mod config menu
            throw new InvalidOperationException(message);
        }

        var defaultFilename = SpecialCharRegex().Replace(rootNamespace, "");

        filename = filename == null ? defaultFilename : SpecialCharRegex().Replace(filename, "");
        if (!filename.Contains('.')) filename += ".cfg";

        _path = Path.Combine(OS.GetUserDataDir(), "mod_configs", filename);

        CheckConfigProperties();
        Init();
    }

    public bool HasSettings() => ConfigProperties.Count > 0;

    private void CheckConfigProperties()
    {
        var configType = GetType();

        ConfigProperties.Clear();
        foreach (var property in configType.GetProperties())
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (property.GetMethod?.IsStatic != true)
            {
                ModConfigLogger.Warn($"Ignoring {_modConfigName} property {property.Name}: only static properties are supported", true);
                continue;
            }

            ConfigProperties.Add(property);
        }
    }
    
    public abstract void SetupConfigUI(Control optionContainer);

    private void Init()
    {
        if (File.Exists(_path)) Load();
        else Save(); // Save default values
    }

    public void Changed()
    {
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    //Would be slightly more straightforward to directly serialize/deserialize the class,
    //But it would require slightly more setup on the user's part.
    public void Save()
    {
        if (_savingDisabled)
        {
            ModConfigLogger.Error($"Skipping save for {_modConfigName} because the config file is currently in a corrupted, read-only state.");
            return;
        }

        Dictionary<string, string> values = [];
        foreach (var property in ConfigProperties)
        {
            var value = property.GetValue(null);

            var converter = TypeDescriptor.GetConverter(property.PropertyType);
            var stringValue = converter.ConvertToInvariantString(value);

            if (stringValue != null)
                values.Add(property.Name, stringValue);
            else
                ModConfigLogger.Warn($"Failed to convert {_modConfigName} property {property.Name} to string for saving; it will be omitted.", true);
        }

        try
        {
            new FileInfo(_path).Directory?.Create();
            using var fileStream = File.Create(_path);
            JsonSerializer.Serialize(fileStream, values, JsonOptions);
        }
        catch (Exception e)
        {
            ModConfigLogger.Error($"Failed to save config {_modConfigName}: {e.Message}");
        }
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            ModConfigLogger.Error($"Load for {_modConfigName} failed. File not found: {_path}");
            return;
        }

        // Missing fields or bad values (safe to overwrite the config if true)
        var hasSoftErrors = false;

        // Hard errors disable saving (until the next successful load)
        _savingDisabled = false;

        try
        {
            using var fileStream = File.OpenRead(_path);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(fileStream);

            if (values == null)
            {
                ModConfigLogger.Warn($"Config file {_modConfigName} was empty or null. Will re-save using default values.");
                hasSoftErrors = true;
            }
            else
            {
                foreach (var property in ConfigProperties)
                {
                    if (!values.TryGetValue(property.Name, out var value))
                    {
                        // Missing value; might be due to a new mod version, etc. Re-save later to fill it in.
                        ModConfigLogger.Warn($"Config {_modConfigName} has no value for {property.Name}; will re-save to fill in the default.");
                        hasSoftErrors = true;
                        continue;
                    }

                    if (!TryApplyPropertyValue(property, value)) hasSoftErrors = true;
                }

                MainFile.Logger.Info(!hasSoftErrors
                    ? $"Loaded config {_modConfigName} successfully"
                    : $"Loaded config {_modConfigName} with some missing or invalid fields.");
            }
        }
        catch (JsonException jsonEx)
        {
            var locationText = jsonEx.LineNumber.HasValue
                ? $"Line {jsonEx.LineNumber + 1}, position {jsonEx.BytePositionInLine + 1}"
                : "unknown line";
            ModConfigLogger.Error($"Failed to parse config file for {_modConfigName}. The JSON is likely invalid.\n" +
                                  $"File path: {_path}\n" +
                                  $"Error location: {locationText}");
            ModConfigLogger.Warn("Config saving has been DISABLED for this session to protect any manual edits. " +
                                 "Please fix the JSON formatting.", true);
            _savingDisabled = true;
            return;
        }
        catch (Exception e)
        {
            ModConfigLogger.Error($"Unexpected error loading config {_modConfigName}: {e.Message}");
            return;
        }

        if (hasSoftErrors && !_savingDisabled)
        {
            ModConfigLogger.Warn($"Saving fresh config for {_modConfigName} to correct soft errors (missing fields, invalid fields).");
            Save();
        }
    }

    // Convert a single value and update the property. Return true on success, false on failure.
    private static bool TryApplyPropertyValue(PropertyInfo property, string value)
    {
        try
        {
            var converter = TypeDescriptor.GetConverter(property.PropertyType);
            var configVal = converter.ConvertFromInvariantString(value);

            if (configVal == null)
            {
                ModConfigLogger.Warn($"Failed to load saved config value \"{value}\" for property {property.Name}:" +
                                     "Converter returned null.", true);
                return false;
            }

            var oldVal = property.GetValue(null);
            if (!configVal.Equals(oldVal))
            {
                property.SetValue(null, configVal);
            }

            return true;
        }
        catch (Exception ex)
        {
            ModConfigLogger.Warn($"Failed to load saved config value \"{value}\" for property {property.Name}. " +
                                 $"Error: {ex.Message}", true);
            return false;
        }
    }

    protected string GetLabelText(string labelName)
    {
        var loc = LocString.GetIfExists("settings_ui", ModPrefix + StringHelper.Slugify(labelName) + ".title");
        return loc != null ? loc.GetFormattedText() : labelName;
    }

    // Creates a raw toggle control, with no layout (see SimpleModConfig.CreateToggleOption unless you want custom layout)
    protected NConfigTickbox CreateRawTickboxControl(PropertyInfo property)
    {
        var tickbox = new NConfigTickbox().TransferAllNodes(SceneHelper.GetScenePath("screens/settings_tickbox"));
        tickbox.Initialize(this, property);
        return tickbox;
    }

    // Creates a raw slider control, with no layout (see SimpleModConfig.CreateSliderOption unless you want custom layout)
    protected NConfigSlider CreateRawSliderControl(PropertyInfo property)
    {
        var slider = new NConfigSlider().TransferAllNodes(SceneHelper.GetScenePath("screens/settings_slider"));
        slider.Initialize(this, property);
        return slider;
    }

    // Creates a raw dropdown control, with no layout (see SimpleModConfig.CreateDropdownOption unless you want custom layout)
    private static readonly FieldInfo DropdownNode = AccessTools.DeclaredField(typeof(NDropdownPositioner), "_dropdownNode");
    protected NDropdownPositioner CreateRawDropdownControl(PropertyInfo property)
    {
        var dropdown = new NConfigDropdown().TransferAllNodes(SceneHelper.GetScenePath("screens/settings_dropdown"));
        var items = CreateDropdownItems(property, out var currentIndex);
        dropdown.SetItems(items, currentIndex);
        
        var dropdownPositioner = new NDropdownPositioner();
        dropdownPositioner.SetCustomMinimumSize(new(320, 64));
        dropdownPositioner.FocusMode = Control.FocusModeEnum.All;
        dropdownPositioner.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        dropdownPositioner.SizeFlagsVertical = Control.SizeFlags.Fill;
        DropdownNode.SetValue(dropdownPositioner, dropdown);

        dropdownPositioner.AddChild(dropdown);
        dropdownPositioner.MouseFilter = Control.MouseFilterEnum.Ignore;

        return dropdownPositioner;
    }

    private List<NConfigDropdownItem.ConfigDropdownItem> CreateDropdownItems(PropertyInfo property, out int currentIndex)
    {
        List<NConfigDropdownItem.ConfigDropdownItem> items = [];
        var type = property.PropertyType;
        var currentValue = property.GetValue(null);
        int count = 0;
        currentIndex = 0;
        
        if (type.IsEnum)
        {
            foreach (var value in type.GetEnumValues())
            {
                if (currentValue != null && currentValue.Equals(value))
                {
                    currentIndex = count;
                }
                ++count;
                var loc = LocString.GetIfExists("settings_ui", $"{ModPrefix}{StringHelper.Slugify(property.Name)}.{value}");
                var label = loc?.GetRawText() ?? value?.ToString() ?? "UNKNOWN";
                items.Add(new (label, () =>
                {
                    property.SetValue(null, value);
                    Changed();
                }));
            }
        }
        else //Check for dropdown options attribute
        {
            throw new NotSupportedException("Dropdown only supports enum types currently");
        }

        return items;
    }

    // Creates a raw label control, with no layout (see SimpleModConfig.Create*Option and CreateSectionHeader for
    // layout-ready controls)
    public static MegaRichTextLabel CreateRawLabelControl(string labelText, int fontSize)
    {
        var kreonNormal = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_regular_shared.tres");
        var kreonBold = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_bold_shared.tres");

        MegaRichTextLabel label = new()
        {
            Name = "Label",
            Theme = PreloadManager.Cache.GetAsset<Theme>(SettingsTheme),
            AutoSizeEnabled = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            BbcodeEnabled = true,
            ScrollActive = false,
            VerticalAlignment = VerticalAlignment.Center,
            Text = labelText
        };

        label.AddThemeFontOverride("normal_font", kreonNormal);
        label.AddThemeFontOverride("bold_font", kreonBold);
        label.AddThemeFontSizeOverrideAll(fontSize);

        return label;
    }

    protected static ColorRect CreateDividerControl()
    {
        return new ColorRect
        {
            Name = "Divider",
            CustomMinimumSize = new Vector2(0, 2),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = new Color(0.909804f, 0.862745f, 0.745098f, 0.25098f)
        };
    }

    public static void ShowAndClearPendingErrors()
    {
        var pendingMessages = ModConfigLogger.PendingUserMessages;
        if (pendingMessages.Count <= 0) return;

        var errorPopup = NErrorPopup.Create("Mod configuration error",
            string.Join('\n', pendingMessages), false);
        if (errorPopup == null || NModalContainer.Instance == null) return;
        NModalContainer.Instance.Add(errorPopup);

        var vertPopup = errorPopup.GetNodeOrNull<NVerticalPopup>("VerticalPopup");
        if (vertPopup == null) return;
        vertPopup.BodyLabel.AddThemeFontSizeOverrideAll(22);

        pendingMessages.Clear();
    }

    [GeneratedRegex("[^a-zA-Z0-9_.]")]
    private static partial Regex SpecialCharRegex();
}