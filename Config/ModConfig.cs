using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BaseLib.Config.UI;
using BaseLib.Extensions;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using Environment = System.Environment;

namespace BaseLib.Config;

public abstract partial class ModConfig
{
    private const string SettingsTheme = "res://themes/settings_screen_line_header.tres";
    private static readonly Font KreonNormal = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_regular_shared.tres");
    private static readonly Font KreonBold = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_bold_shared.tres");
    
    public event EventHandler? ConfigChanged;
    public delegate void ValuesChanged();

    private readonly string _path;

    protected readonly List<PropertyInfo> ConfigProperties = [];

    public ModConfig(string? filename = null)
    {
        _path = GetType().GetRootNamespace();
        if (_path == "") _path = "Unknown";
        
        _path = SpecialCharRegex().Replace(_path, "");
        
        filename = filename == null ? _path : SpecialCharRegex().Replace(filename, "");
        if (!filename.Contains('.')) filename += ".cfg";

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (appData == "") appData = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        var libFolder = OperatingSystem.IsMacOS() ? "Library" : ".baselib";
        _path = Path.Combine(appData, libFolder, _path, filename);

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
            if (!property.CanRead || !property.CanWrite || property.GetMethod?.IsStatic != true) continue;
            ConfigProperties.Add(property);
        }
    }
    
    public abstract void SetupConfigUI(Control optionContainer);

    private void Init()
    {
        //Check for existing config file.
        //If it doesn't exist, save it. If it does, laod it.
        _ = File.Exists(_path) ? Load() : Save();
    }

    public void Changed()
    {
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }
    
    //Would be slightly more straightforward to directly serialize/deserialize the class,
    //But it would require slightly more setup on the user's part.
    private bool _fileActive = false;
    
    public async Task Save()
    {
        if (_fileActive) return;
        
        _fileActive = true;
        
        Dictionary<string, object> values = [];
        foreach (var property in ConfigProperties)
        {
            var value = property.GetValue(null);
            if (value != null) values.Add(property.Name, value);
        }

        try
        {
            new FileInfo(_path).Directory?.Create();
            await using var fileStream = File.Create(_path);
            await JsonSerializer.SerializeAsync(fileStream, values);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to save config {GetType().Name};");
            MainFile.Logger.Error(e.ToString());
        }

        _fileActive = false;
    }
    
    public async Task Load()
    {
        if (_fileActive ||!File.Exists(_path)) return;
        
        _fileActive = true;
        
        try
        {
            await using var fileStream = File.OpenRead(_path);
            var values = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(fileStream);

            if (values != null)
            {
                foreach (var property in ConfigProperties)
                {
                    if (values.TryGetValue(property.Name, out var value))
                    {
                        object? oldVal = property.GetValue(null);
                        if (oldVal != value)
                        {
                            property.SetValue(null, value);
                        }
                    }
                }
                
                MainFile.Logger.Info($"Loaded config {GetType().Name} successfully");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error("Failed to load config;");
            MainFile.Logger.Error(e.ToString());
        }
        
        _fileActive = false;
    }

    

    public NConfigTickbox MakeToggleOption(Control parent, PropertyInfo property)
    {
        //format varName. Or support localization keys. Check defining namespace.
        MarginContainer container = MakeOptionContainer(parent, "Toggle_" + property.Name, property.Name);
        
        var tickbox = new NConfigTickbox().TransferAllNodes(SceneHelper.GetScenePath("screens/settings_tickbox"));
        tickbox.SetCustomMinimumSize(new (320, 64));
        tickbox.Initialize(this, property);
        
        container.AddChild(tickbox);
        tickbox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        tickbox.SizeFlagsVertical = Control.SizeFlags.Fill;
        return tickbox;
    }

    protected static MarginContainer MakeOptionContainer(Control parent, string name, string labelText)
    {
        MarginContainer container = new();
        container.Name = name;
        container.AddThemeConstantOverride("margin_left", 12);
        container.AddThemeConstantOverride("margin_right", 12);
        container.MouseFilter = Control.MouseFilterEnum.Ignore;

        MegaRichTextLabel label = new();
        label.Name = "Label";
        label.Theme = PreloadManager.Cache.GetAsset<Theme>(SettingsTheme);
        label.SetCustomMinimumSize(new(0, 64));
        label.AddThemeFontSizeOverride("normal_font_size", 20);
        label.AddThemeFontSizeOverride("bold_font_size", 20);
        label.AddThemeFontSizeOverride("bold_italics_font_size", 20);
        label.AddThemeFontSizeOverride("italics_font_size", 20);
        label.AddThemeFontSizeOverride("mono_font_size", 20);
        label.AddThemeFontOverride("normal_font", KreonNormal);
        label.AddThemeFontOverride("bold_font", KreonBold);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;

        label.BbcodeEnabled = true;
        label.ScrollActive = false;
        label.VerticalAlignment = VerticalAlignment.Center;

        container.AddChild(label);
        label.Owner = container;
        label.Text = labelText;

        parent.AddChild(container);
        container.Owner = parent;
        
        return container;
    }

    [GeneratedRegex("[^a-zA-Z0-9_]")]
    private static partial Regex SpecialCharRegex();
}