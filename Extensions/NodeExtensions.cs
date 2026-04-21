using Godot;

namespace BaseLib.Extensions;

public static class NodeExtensions
{
    public static void AddUnique(this Node n, Node child, string? name = null)
    {
        if (name != null) child.Name = name;
        child.UniqueNameInOwner = true;
        n.AddChild(child);
        child.Owner = n;
    }

    public static Control? FindFirstFocusable(this Node? node)
    {
        if (node == null) return null;
        if (node is Control { FocusMode: Control.FocusModeEnum.All } control)
            return control;

        return node
            .GetChildren()
            .Select(FindFirstFocusable)
            .OfType<Control>()
            .FirstOrDefault();
    }
}