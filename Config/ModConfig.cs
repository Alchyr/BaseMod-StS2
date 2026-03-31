using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BaseLib.Config.UI;
using BaseLib.Extensions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Config;

// NMainMenu is recreated every time you return from a run, etc., so this isn't a run-once as it seems.
// We also check for errors when exiting the mod config submenu, as that won't trigger this code.
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
public static class NMainMenu_Ready_Patch
{
    public static void Postfix()
    {
        if (ModConfig.ModConfigLogger.PendingUserMessages.Count == 0) return;
        Callable.From(ModConfig.ShowAndClearPendingErrors).CallDeferred();
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame.Quit))]
public static class NGameQuitModConfigSavePatch
{
    public static void Prefix()
    {
        BaseLibMain.Logger.Info("NGame.Quit(): saving all ModConfigs");
        foreach (var modConfig in ModConfigRegistry.GetAll())
            modConfig.Save();
    }
}

public abstract partial class ModConfig
{
    private const string SettingsTheme = "res://themes/settings_screen_line_header.tres";

    /// <summary>
    /// Event that fires when <see cref="Changed()"/> is called. Custom controls must call Changed() when mutating
    /// a property.
    /// </summary>
    public event EventHandler? ConfigChanged;
    public event Action? OnConfigReloaded;
    public void ConfigReloaded() => OnConfigReloaded?.Invoke();

    private readonly string _path;
    public string ModPrefix { get; private set; }

    private readonly string _modConfigName;
    private bool _savingDisabled;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private CancellationTokenSource? _saveDebounceToken;
    private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

    protected readonly List<PropertyInfo> ConfigProperties = [];
    private readonly Dictionary<string, object?> _defaultValues = new();

    public static class ModConfigLogger
    {
        public static List<string> PendingUserMessages { get; } = [];

        /// <summary>
        /// Show a message in the console, and optionally in the GUI. Only use showInGui=true if truly necessary;
        /// players won't enjoy having warnings/errors shoved in their faces unless it's something that truly impacts them.
        /// </summary>
        public static void Warn(string message, bool showInGui = false)
        {
            BaseLibMain.Logger.Warn(message);
            if (showInGui && !PendingUserMessages.Contains(message)) PendingUserMessages.Add(message);
        }

        /// <inheritdoc cref="Warn" />
        public static void Error(string message, bool showInGui = true)
        {
            BaseLibMain.Logger.Error(message);
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
            if (property.GetCustomAttribute<ConfigIgnoreAttribute>() != null) continue;
            if (!property.CanRead || !property.CanWrite) continue;
            if (property.GetMethod?.IsStatic != true)
            {
                ModConfigLogger.Warn($"Ignoring {_modConfigName} property {property.Name}: only static properties are supported");
                continue;
            }

            ConfigProperties.Add(property);
        }
    }

    public T? GetDefaultValue<T>(string propertyName)
    {
        if (_defaultValues.TryGetValue(propertyName, out var val) && val is T typedValue)
        {
            return typedValue;
        }

        return default;
    }

    protected void RestoreDefaultsNoConfirm()
    {
        foreach (var property in ConfigProperties)
        {
            var defaultValue = GetDefaultValue<object?>(property.Name);
            property.SetValue(null, defaultValue);
        }

        Save();
        OnConfigReloaded?.Invoke();
    }

    public abstract void SetupConfigUI(Control optionContainer);

    private void Init()
    {
        foreach (var property in ConfigProperties)
        {
            _defaultValues.TryAdd(property.Name, property.GetValue(null));
        }

        if (File.Exists(_path)) Load();
        else Save(); // Save default values
    }

    public void Changed()
    {
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Static helper to reload your mod's config, for when you don't have easy access to the instance.
    /// </summary>
    /// <typeparam name="T">Your ModConfig subclass</typeparam>
    /// <example><code>
    /// internal class MyConfig : SimpleModConfig { ... }
    /// ModConfig.Load&lt;MyConfig&gt;();
    /// </code></example>
    public static void Load<T>() where T : ModConfig
    {
        ModConfigRegistry.Get<T>()?.Load();
    }

    /// <summary>
    /// <para>Save after <paramref name="delayMs"/> ms, unless SaveDebounced was called again during the delay, in which
    /// case the old request is canceled and the new one replaces it.</para>
    /// <para>Note that you ONLY ever need to call this if you yourself modify the properties at runtime. BaseLib will
    /// always save when the user changes something in the settings screen.</para>
    /// </summary>
    /// <typeparam name="T">Your ModConfig subclass</typeparam>
    /// <example><code>
    /// internal class MyConfig : SimpleModConfig { ... }
    /// ModConfig.SaveDebounced&lt;MyConfig&gt;();
    /// </code></example>
    public static void SaveDebounced<T>(int delayMs = 1000) where T : ModConfig
    {
        ModConfigRegistry.Get<T>()?.SaveDebounced(delayMs);
    }

    /// <summary>
    /// <para>Save after <paramref name="delayMs"/> ms, unless SaveDebounced was called again during the delay, in which case
    /// the old request is canceled and the new one replaces it.</para>
    /// <para>Note that you ONLY ever need to call this if you yourself modify the properties at runtime. BaseLib will always
    /// save when the user changes something in the settings screen.</para>
    /// </summary>
    public void SaveDebounced(int delayMs = 1000) => SaveDebouncedInternal(delayMs);

    private async void SaveDebouncedInternal(int delayMs = 1000)
    {
        try
        {
            // Cancel the previous request, if any, prior to replacing it
            _saveDebounceToken?.Cancel();
            _saveDebounceToken?.Dispose();

            _saveDebounceToken = new CancellationTokenSource();
            var token = _saveDebounceToken.Token;
            await Task.Delay(delayMs, token);

            await _saveLock.WaitAsync(token);
            try
            {
                Save();
            }
            finally
            {
                _saveLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Another save request came in to replace this one; this is fully expected
        }
        catch (Exception ex)
        {
            ModConfigLogger.Error($"Failed to save config for {_modConfigName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Immediately save the current configuration to disk. Prefer using <see cref="SaveDebounced"/> (on the instance or
    /// its static variant) instead unless you absolutely must save *now*. Indeed, using SaveDebounced(0) is likely still
    /// better than calling this directly.<br/>
    /// Using both is not recommended and may cause issues with locking/hangs.
    /// </summary>
    public void Save()
    {
        if (_savingDisabled)
        {
            // No GUI error here, because that would've been shown already when _savingDisabled was set.
            ModConfigLogger.Warn($"Skipping save for {_modConfigName} because the config file is currently in a corrupted, read-only state.");
            return;
        }

        Dictionary<string, string> values = [];

        try
        {
            foreach (var property in ConfigProperties)
            {
                var value = property.GetValue(null);
                var converter = TypeDescriptor.GetConverter(property.PropertyType);
                var stringValue = converter.ConvertToInvariantString(value);

                if (stringValue != null)
                    values.Add(property.Name, stringValue);
                else
                {
                    ModConfigLogger.Warn(
                        $"Failed to convert {_modConfigName} property {property.Name} to string for saving; " +
                        "it will be omitted.");
                }
            }
        }
        catch (Exception)
        {
            // During testing, I have never seen an exception here, but let's avoid a game crash/menu hang, etc.
            ModConfigLogger.Error($"Failed to save config {_modConfigName}: unknown error during conversion.", false);
            return;
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
            Dictionary<string, string>? values;
            using (var fileStream = File.OpenRead(_path))
            {
                values = JsonSerializer.Deserialize<Dictionary<string, string>>(fileStream);
            }

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

                if (hasSoftErrors)
                    ModConfigLogger.Warn($"Loaded config {_modConfigName} with some missing or invalid fields.");
            }
        }
        catch (JsonException jsonEx)
        {
            // Unlikely to happen except for people who have modified the file manually, so let's be verbose and show in GUI.
            var locationText = jsonEx.LineNumber.HasValue
                ? $"Line {jsonEx.LineNumber + 1}, position {jsonEx.BytePositionInLine + 1}"
                : "unknown line";
            ModConfigLogger.Error($"Failed to parse config file for {_modConfigName}. The JSON is likely invalid.\n" +
                                  $"File path: {_path}\n" +
                                  $"Error location: {locationText}");
            ModConfigLogger.Warn("Config saving has been DISABLED for this mod to protect any manual edits.", true);
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
                                     "Converter returned null.");
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
                                 $"Error: {ex.Message}");
            return false;
        }
    }

    protected string GetLabelText(string labelName)
    {
        var loc = LocString.GetIfExists("settings_ui", $"{ModPrefix}{StringHelper.Slugify(labelName)}.title");
        return loc != null ? loc.GetFormattedText() : labelName;
    }

    protected static string GetBaseLibLabelText(string labelName)
    {
        var loc = LocString.GetIfExists("settings_ui", $"BASELIB-{StringHelper.Slugify(labelName)}.title");
        return loc != null ? loc.GetFormattedText() : labelName;
    }

    /// <summary>
    /// Creates a raw control, with no layout (label, margins), no automatic hover tip, etc.<br/>
    /// Use the Create*Option methods instead unless you need a custom layout (or use them, and customize them).
    /// </summary>
    /// <param name="property">The property this control is bound to. Fetch with e.g. GetType().GetProperty() in
    /// a ModConfig.</param>
    protected NConfigTickbox CreateRawTickboxControl(PropertyInfo property)
    {
        var tickbox = new NConfigTickbox();
        tickbox.Initialize(this, property);
        return tickbox;
    }

    /// <inheritdoc cref="CreateRawTickboxControl"/>
    protected NConfigSlider CreateRawSliderControl(PropertyInfo property)
    {
        var slider = new NConfigSlider();
        slider.Initialize(this, property);
        return slider;
    }

    /// <inheritdoc cref="CreateRawTickboxControl"/>
    protected NConfigLineEdit CreateRawLineEditControl(PropertyInfo property)
    {
        var lineEdit = new NConfigLineEdit();
        lineEdit.Initialize(this, property);
        return lineEdit;
    }

    /// <summary>
    /// Creates a raw button control. You may want <see cref="SimpleModConfig.CreateButton" /> instead.
    /// </summary>
    /// <param name="labelText">The text to place on the button</param>
    /// <param name="onPressed">Action to perform when the user clicks/presses the button.</param>
    protected NConfigButton CreateRawButtonControl(string labelText, Action onPressed)
    {
        var button = new NConfigButton();
        button.Initialize(labelText, onPressed);
        return button;
    }

    private static readonly FieldInfo DropdownNode = AccessTools.DeclaredField(typeof(NDropdownPositioner), "_dropdownNode");
    /// <inheritdoc cref="CreateRawTickboxControl"/>
    protected NDropdownPositioner CreateRawDropdownControl(PropertyInfo property)
    {
        var dropdown = new NConfigDropdown();
        dropdown.Initialize(this, property, ModPrefix, Changed);
        dropdown.SetFromProperty();

        var dropdownPositioner = new NDropdownPositioner();
        dropdownPositioner.SetCustomMinimumSize(new(324, 64));
        dropdownPositioner.FocusMode = Control.FocusModeEnum.All;
        dropdownPositioner.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        dropdownPositioner.SizeFlagsVertical = Control.SizeFlags.Fill;

        DropdownNode.SetValue(dropdownPositioner, dropdown);

        dropdownPositioner.AddChild(dropdown);
        dropdownPositioner.MouseFilter = Control.MouseFilterEnum.Ignore;

        return dropdownPositioner;
    }

    /// <inheritdoc cref="CreateRawTickboxControl"/>
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
            FocusMode = Control.FocusModeEnum.None,
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