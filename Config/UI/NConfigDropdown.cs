using System.Reflection;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Config.UI;

[HarmonyPatch(typeof(NDropdownPositioner), nameof(NDropdownPositioner.OnVisibilityChange))]
public static class NDropdownPositioner_OnVisibilityChange_Patch
{
    // This method doesn't do anything useful for us; in fact, it simply ruins our focus neighbor setup.
    public static bool Prefix(NDropdownPositioner __instance) => __instance._dropdownNode is not NConfigDropdown;
}

public partial class NConfigDropdown : NSettingsDropdown
{
    private List<NConfigDropdownItem.ItemData>? _items;
    private ModConfig? _config;
    private PropertyInfo? _property;

    private int _currentDisplayIndex = -1;
    private float _lastGlobalY;
    private NodePath _selfNodePath = new(".");

    public NConfigDropdown()
    {
        SetCustomMinimumSize(new(324, 64));
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        FocusMode = FocusModeEnum.All;

        this.TransferAllNodes(SceneHelper.GetScenePath("screens/settings_dropdown"));
    }

    public void Initialize(ModConfig config, PropertyInfo property, string modPrefix, Action? onChanged)
    {
        _config = config;
        _property = property;
        _items = [];

        var type = property.PropertyType;
        if (!type.IsEnum) throw new NotSupportedException("Dropdown only supports enum types currently");

        foreach (var value in type.GetEnumValues())
        {
            var loc = LocString.GetIfExists("settings_ui", $"{modPrefix}{StringHelper.Slugify(property.Name)}.{value}");
            var label = loc?.GetRawText() ?? value?.ToString() ?? "UNKNOWN";

            _items.Add(new NConfigDropdownItem.ItemData(label, value, () =>
            {
                _property.SetValue(null, value);
                onChanged?.Invoke();
            }));
        }

        _config.OnConfigReloaded += SetFromProperty;
    }

    public void SetFromProperty()
    {
        if (_property == null || _items == null) return;

        var currentValue = _property.GetValue(null);

        var newIndex = _items.FindIndex(item => item.Value?.Equals(currentValue) == true);
        if (newIndex < 0) newIndex = 0;

        _currentDisplayIndex = newIndex;

        if (!IsNodeReady()) return;
        _currentOptionLabel.SetTextAutoSize(_items[newIndex].Text);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Hacky, but this is overwritten and causes issues. Setting it in _Ready and on row creation isn't enough.
        if (FocusNeighborLeft != _selfNodePath || FocusNeighborRight != _selfNodePath)
        {
            FocusNeighborLeft = _selfNodePath;
            FocusNeighborRight = _selfNodePath;
        }

        if (IsNodeReady() && _dropdownContainer.Visible)
        {
            // Ensure the list of items follows the dropdown itself when the parent container is scrolled.
            // GlobalPosition/TopLevel is used to override the clipping from NModConfigPopup's edges.
            _dropdownContainer.GlobalPosition = GlobalPosition + new Vector2(0, Size.Y);
        }

        _lastGlobalY = GlobalPosition.Y;
    }

    public override void _Ready()
    {
        ConnectSignals();
        ClearDropdownItems();

        if (_items == null) throw new Exception("Created config dropdown without calling Initialize");

        for (var i = 0; i < _items.Count; i++)
        {
            NConfigDropdownItem child = NConfigDropdownItem.Create(_items[i]);
            _dropdownItems.AddChildSafely(child);
            child.Connect(NDropdownItem.SignalName.Selected,
                Callable.From(new Action<NDropdownItem>(OnDropdownItemSelected)));
            child.Init(i);

            if (i == _currentDisplayIndex)
            {
                _currentOptionLabel.SetTextAutoSize(child.Data.Text);
            }
        }
        

        _dropdownItems.GetParent<NDropdownContainer>().RefreshLayout();

        _dropdownContainer.VisibilityChanged += () => {
            _dropdownContainer.TopLevel = _dropdownContainer.Visible;
            _dropdownContainer.GlobalPosition = GlobalPosition + new Vector2(0, Size.Y);

            // Focus the last selected entry (base class always selects the first)
            if (_currentDisplayIndex < 0 || _currentDisplayIndex >= _items.Count) return;
            var entry = _dropdownItems.GetChildOrNull<NConfigDropdownItem>(_currentDisplayIndex);
            entry?.TryGrabFocus();
        };
    }
    
    private void OnDropdownItemSelected(NDropdownItem nDropdownItem)
    {
        if (nDropdownItem is not NConfigDropdownItem configDropdownItem)
            return;
        
        CloseDropdown();
        _currentOptionLabel.SetTextAutoSize(configDropdownItem.Data.Text);
        _currentDisplayIndex = configDropdownItem.DisplayIndex; 
        configDropdownItem.Data.OnSet();
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (_config != null) _config.OnConfigReloaded -= SetFromProperty;
    }
}