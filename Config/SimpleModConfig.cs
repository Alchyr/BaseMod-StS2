using System.Reflection;
using Godot;

namespace BaseLib.Config;

public class SimpleModConfig : ModConfig
{
    public override void SetupConfigUI(Control optionContainer)
    {
        VBoxContainer options = new();

        MainFile.Logger.Info($"Setting up SimpleModConfig {GetType().FullName}");
        
        options.Size = optionContainer.Size;
        options.AddThemeConstantOverride("separation", 8);
        optionContainer.AddChild(options);

        Type? t = null;
        Control? currentSetting = null;
        string? currentSection = null;

        try
        {
            var properties = ConfigProperties.ToArray();
            for (var i = 0; i < properties.Length; i++)
            {
                var property = properties[i];
                var nextProperty = i < properties.Length - 1 ? properties[i + 1] : null;

                // Create a section header if this property starts a new section
                var sectionName = property.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
                if (sectionName != null && sectionName != currentSection)
                {
                    currentSection = sectionName;
                    options.AddChild(CreateSectionLabel(currentSection));
                }

                // Create the option control
                t = property.PropertyType;
                var previousSetting = currentSetting;
                currentSetting = t.IsEnum ? Generators[typeof(Enum)](this, options, property) : Generators[t](this, options, property);

                // Set up focus handling
                if (previousSetting != null)
                {
                    if (currentSetting.FocusNeighborBottom == null) MainFile.Logger.Info("NEIGHBOR DEFAULT NULL");
                    // else MainFile.Logger.Info($"NEIGHBOR DEFAULT: {current.FocusNeighborBottom}");

                    NodePath path = currentSetting.GetPathTo(previousSetting);
                    currentSetting.FocusNeighborLeft ??= path;
                    currentSetting.FocusNeighborTop ??= path;
                    path = previousSetting.GetPathTo(currentSetting);
                    previousSetting.FocusNeighborRight ??= path;
                    previousSetting.FocusNeighborBottom ??= path;
                }

                // Add a divider unless the next property starts a new section (or there is no next)
                var nextSectionName = nextProperty?.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
                var nextIsSameSection = nextSectionName == null || nextSectionName == currentSection;
                if (nextProperty != null && nextIsSameSection)
                {
                    options.AddChild(CreateDivider());
                }
            }
        }
        catch (KeyNotFoundException)
        {
            MainFile.Logger.Error($"Attempted to construct SimpleModConfig with unsupported type {t?.FullName}");
        }
    }

    private static readonly Dictionary<Type, Func<ModConfig, Control, PropertyInfo, Control>> Generators = new()
    {
        { 
            typeof(bool),
            (cfg, control, property) => cfg.MakeToggleOption(control, property)
        },
        {
            typeof(double),
            (cfg, control, property) => cfg.MakeSliderOption(control, property)
        },
        {
            typeof(Enum),
            (cfg, control, property) => cfg.MakeDropdownOption(control, property)
        }
    };
}