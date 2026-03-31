using BaseLib.Patches;
using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BaseLib.Utils.NodeFactories;
using Godot;

namespace BaseLib.Extensions;

public static class StringExtensions
{
    public static string RemovePrefix(this string id)
    {
        return id[(id.IndexOf(TypePrefix.PrefixSplitChar) + 1)..];
    }

    /// <summary>
    /// Registers a scene to be automatically converted to the specified node type when instantiated.
    /// Requires a factory to exist in NodeFactory<seealso cref="NodeFactory"/> to perform the conversion to the specified type.
    /// </summary>
    /// <param name="scenePath"></param>
    /// <typeparam name="TNode"></typeparam>
    public static void RegisterSceneForConversion<TNode>(this string scenePath) where TNode : Node
    {
        NodeFactory.RegisterSceneType<TNode>(scenePath);
    }
}
