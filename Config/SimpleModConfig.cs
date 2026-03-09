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
        Control? current = null;
        try
        {
            foreach (var property in ConfigProperties)
            {
                t = property.PropertyType;
                var previous = current;
                current = Generators[t](this, options, property);
                
                if (previous == null) continue;
                
                current.FocusNeighborLeft = current.FocusNeighborTop = current.GetPathTo(previous);
                previous.FocusNeighborRight = previous.FocusNeighborBottom = previous.GetPathTo(current);
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
        }
    };
}