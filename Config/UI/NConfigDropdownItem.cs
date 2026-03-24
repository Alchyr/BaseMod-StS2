using BaseLib.Utils;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace BaseLib.Config.UI;

public partial class NConfigDropdownItem : NDropdownItem
{
    private static readonly string BaseScenePath = SceneHelper.GetScenePath("ui/dropdown_item");

    public static NConfigDropdownItem Create(ItemData data)
    {
        var dropdownItem = new NConfigDropdownItem
        {
            Data = data
        };
        dropdownItem.SetCustomMinimumSize(new(288, 44));
        dropdownItem.MouseFilter = MouseFilterEnum.Pass;
        dropdownItem.TransferAllNodes(BaseScenePath);
        
        return dropdownItem;
    }

    public required ItemData Data;
    public int DisplayIndex;
    private NConfigDropdownItem()
    {
        
    }

    public void Init(int setIndex)
    {
        DisplayIndex = setIndex;
        _label.SetTextAutoSize(Data.Text);
    }

    public class ItemData(string text, object? value, Action onSet)
    {
        public string Text { get; } = text;
        public object? Value { get; } = value;
        public Action OnSet { get; } = onSet;
    }
}