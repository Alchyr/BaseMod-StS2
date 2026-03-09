using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.Combat;
using System.Collections.Generic;

namespace BaseLib.Utils;

public static class GodotUtils
{
    public static NCreatureVisuals CreatureVisualsFromScene(string path)
    {
        var visualsNode = new NCreatureVisuals();

        TransferNodes(visualsNode, PreloadManager.Cache.GetScene(path).Instantiate(), "Visuals", "Bounds", "IntentPos", "CenterPos", "OrbPos", "TalkPos");

        return visualsNode;
    }

    public static T TransferAllNodes<T>(this T obj, string sourceScene, params string[] uniqueNames) where T : Node
    {
        TransferNodes(obj, PreloadManager.Cache.GetScene(sourceScene).Instantiate(), uniqueNames);
        return obj;
    }
    

    private static void TransferNodes(Node target, Node source, params string[] names)
    {
        TransferNodes(target, source, true, names);
    }
    private static void TransferNodes(Node target, Node source, bool uniqueNames, params string[] names)
    {
        target.Name = source.Name;

        List<string> requiredNames = [.. names];
        foreach (var child in source.GetChildren())
        {
            source.RemoveChild(child);
            if (requiredNames.Remove(child.Name) && uniqueNames) child.UniqueNameInOwner = true;
            target.AddChild(child);
            child.Owner = target;
        }

        if (requiredNames.Count > 0)
        {
            MainFile.Logger.Warn($"Created {target.GetType().FullName} missing required children {string.Join(" ", requiredNames)}");
        }

        source.QueueFree();
    }
}
