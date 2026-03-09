using System.Reflection;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Config.UI;

public partial class NConfigTickbox : NSettingsTickbox
{
    private ModConfig? _config;
    private PropertyInfo? _property;
    
    public override void _Ready()
    {
        if (_property == null) throw new Exception("NConfigTickbox added to tree without an assigned property");
        ConnectSignals();
        SetFromProperty();
    }

    public void Initialize(ModConfig modConfig, PropertyInfo property)
    {
        if (property.PropertyType != typeof(bool)) throw new ArgumentException("Attempted to assign NConfigTickbox a non-bool property");
        _config = modConfig;
        _property = property;
    }

    private void SetFromProperty()
    {
        IsTicked = (bool?) _property!.GetValue(null) == true;
    }

    protected override void OnTick()
    {
        _property?.SetValue(null, true);
        _config?.Changed();
    }

    protected override void OnUntick()
    {
        _property?.SetValue(null, false);
        _config?.Changed();
    }
}