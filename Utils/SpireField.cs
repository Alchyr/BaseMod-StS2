using System.Runtime.CompilerServices;
using BaseLib.Patches.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Utils;

/// <summary>
/// A basic wrapper around <seealso cref="ConditionalWeakTable{TKey, TValue}"/> for convenience.
/// While this can be used to store value types, they will be boxed and thus is somewhat inefficient.
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TVal"></typeparam>
public class SpireField<TKey, TVal> where TKey : class
{
    private readonly ConditionalWeakTable<TKey, object?> _table = [];
    private readonly Func<TKey, TVal?> _defaultVal;

    public SpireField(Func<TVal?> defaultVal)
    {
        _defaultVal = _ => defaultVal();
    }

    public SpireField(Func<TKey, TVal?> defaultVal)
    {
        _defaultVal = defaultVal;
    }

    public TVal? this[TKey obj]
    {
        get => Get(obj);
        set => Set(obj, value);
    }

    public TVal? Get(TKey obj) {
        if (_table.TryGetValue(obj, out var result)) return (TVal?)result;

        _table.Add(obj, result = _defaultVal(obj));
        return (TVal?)result;
    }

    public void Set(TKey obj, TVal? val)
    {
        _table.AddOrUpdate(obj, val);
    }
}

internal interface IAddedNodes<TParentType> where TParentType : Node
{
    protected static List<IAddedNodes<TParentType>> _addedNodes = [];
    private static bool _patched = false;

    Node? GetNode(TParentType obj);
        
    protected static void PatchNodeReady()
    {
        if (_patched) return;
        _patched = true;
        
        BaseLibMain.Logger.Info($"Patching type {typeof(TParentType).Name} to add nodes.");
        
        var harmony = BaseLibMain.MainHarmony;
        var method = AccessTools.DeclaredMethod(typeof(TParentType), "_Ready", []);

        if (method != null)
        {
            var unconditionalMethod = typeof(IAddedNodes<TParentType>).DeclaredMethod(nameof(UnconditionalAdd));
            BaseLibMain.Logger.Info($"Adding postfix {unconditionalMethod.FullDescription()}");
            harmony.Patch(method, postfix: unconditionalMethod);
            return;
        }
        
        method = AccessTools.Method(typeof(TParentType), "_Ready", []);

        if (method == null)
        {
            BaseLibMain.Logger.Error($"Failed to patch _Ready method for type {typeof(TParentType).Name} to add nodes; _Ready method not found.");
            return;
        }

        var conditionalMethod = typeof(IAddedNodes<TParentType>).DeclaredMethod(nameof(ConditionalAdd));
        BaseLibMain.Logger.Info($"Adding postfix {conditionalMethod.FullDescription()}");
        harmony.Patch(method, postfix: conditionalMethod);
    }

    private static void UnconditionalAdd(TParentType __instance)
    {
        foreach (var add in _addedNodes)
        {
            var child = add.GetNode(__instance);
            if (__instance.IsAncestorOf(child)) return;
            __instance.AddChild(child);
        }
    }

    private static void ConditionalAdd(object __instance)
    {
        if (__instance is not TParentType parent) return;
        UnconditionalAdd(parent);
    }
}

/// <summary>
/// Adds a node as a child to all instances of the specified parent node type.
/// </summary>
public class AddedNode<TParentType, TNode> : SpireField<TParentType, TNode>, IAddedNodes<TParentType> where TParentType : Node where TNode : Node
{
    
    public AddedNode(Func<TParentType, TNode> defaultVal) : base(defaultVal)
    {
        IAddedNodes<TParentType>._addedNodes.Add(this);
        IAddedNodes<TParentType>.PatchNodeReady();
    }

    /// <summary>
    /// An AddedNode that adds a specific scene as a child.
    /// </summary>
    /// <param name="scenePath">.tscn resource file path of the scene to instantiate.</param>
    /// <param name="extraSetup">If additional properties of the scene need to be set up before it is added as a child,
    /// or to add it as a child in a specific way.</param>
    public AddedNode(string scenePath, Action<TParentType, TNode>? extraSetup = null) :
        this(parent =>
        {
            var scene = ResourceLoader.Load<PackedScene>(scenePath).Instantiate<TNode>();
            extraSetup?.Invoke(parent, scene);
            return scene;
        })
    { }
    
    public new void Set(TParentType obj, TNode? val)
    {
        throw new InvalidOperationException("The value of an AddedNode should not be set. Instead, modify the node already within the scene.");
    }

    public Node? GetNode(TParentType obj)
    {
        return Get(obj);
    }
}

internal interface ISavedSpireField
{
    protected static readonly HashSet<Type> SupportedTypes =
    [
        typeof(int),
        typeof(bool),
        typeof(string),
        typeof(ModelId),
        typeof(int[]),
        typeof(SerializableCard),
        typeof(SerializableCard[]),
        typeof(List<SerializableCard>),
    ];
    
    protected static bool IsTypeSupported(Type t) =>
        SupportedTypes.Contains(t) || t.IsEnum || (t.IsArray && t.GetElementType()!.IsEnum);
    
    string Name { get; }
    Type TargetType { get; }
    void Export(object model, SavedProperties props);
    void Import(object model, SavedProperties props);
}

/// <summary>
/// A SpireField whose value will automatically be saved and loaded.
/// Only functions on model types that support SavedProperty, so mainly just cards and relics.
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TVal"></typeparam>
public class SavedSpireField<TKey, TVal> : SpireField<TKey, TVal>, ISavedSpireField where TKey : class
{
    public SavedSpireField(Func<TVal?> defaultVal, string name) : this(_ => defaultVal(), name) { }

    public SavedSpireField(Func<TKey, TVal?> defaultVal, string name) : base(defaultVal)
    {
        string typeName = typeof(TKey).Name;
        Name = $"{typeName}_{name}";
        if (!ISavedSpireField.IsTypeSupported(typeof(TVal)))
        {
            throw new NotSupportedException(
                $"SavedSpireField {name} uses unsupported type {typeof(TVal).Name}."
            );
        }
        
        SavedSpireFieldPatch.Register(this);
    }
    
    public string Name { get; }
    public Type TargetType { get; } = typeof(TKey);

    public void Export(object model, SavedProperties props)
    {
        AddToProperties(props, Name, Get((TKey)model));
    } 

    public void Import(object model, SavedProperties props)
    {
        if (TryGetFromProperties<TVal>(props, Name, out var val))
            Set((TKey)model, val);
    }
    
    private static void AddToProperties(SavedProperties props, string name, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case int i:
                (props.ints ??= []).Add(new(name, i));
                break;
            case bool b:
                (props.bools ??= []).Add(new(name, b));
                break;
            case string s:
                (props.strings ??= []).Add(new(name, s));
                break;
            case Enum e:
                (props.ints ??= []).Add(new(name, Convert.ToInt32(e)));
                break;
            case ModelId mid:
                (props.modelIds ??= []).Add(new(name, mid));
                break;
            case SerializableCard card:
                (props.cards ??= []).Add(new(name, card));
                break;
            case int[] iArr:
                (props.intArrays ??= []).Add(new(name, iArr));
                break;
            case Enum[] eArr:
                (props.intArrays ??= []).Add(new(name, eArr.Select(Convert.ToInt32).ToArray()));
                break;
            case SerializableCard[] cArr:
                (props.cardArrays ??= []).Add(new(name, cArr));
                break;
            case List<SerializableCard> cList:
                (props.cardArrays ??= []).Add(new(name, cList.ToArray()));
                break;
        }
    }

    private static bool TryGetFromProperties<T>(SavedProperties props, string name, out T? value)
    {
        value = default;

        if (typeof(T) == typeof(int) || typeof(T).IsEnum)
        {
            var found = props.ints?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = typeof(T).IsEnum
                ? (T)Enum.ToObject(typeof(T), found.Value.value)
                : (T)(object)found.Value.value;
            return true;
        }
        else if (typeof(T) == typeof(bool))
        {
            var found = props.bools?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = (T)(object)found.Value.value;
            return true;
        }
        else if (typeof(T) == typeof(string))
        {
            var found = props.strings?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = (T)(object)found.Value.value;
            return true;
        }
        else if (typeof(T) == typeof(ModelId))
        {
            var found = props.modelIds?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = (T)(object)found.Value.value;
            return true;
        }
        else if (
            typeof(T) == typeof(int[])
            || (typeof(T).IsArray && typeof(T).GetElementType()!.IsEnum)
        )
        {
            var found = props.intArrays?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            if (typeof(T).IsArray && typeof(T).GetElementType()!.IsEnum)
            {
                Type enumType = typeof(T).GetElementType()!;
                Array enumArr = Array.CreateInstance(enumType, found.Value.value.Length);
                for (int i = 0; i < found.Value.value.Length; i++)
                    enumArr.SetValue(Enum.ToObject(enumType, found.Value.value[i]), i);
                value = (T)(object)enumArr;
            }
            else
            {
                value = (T)(object)found.Value.value;
            }
            return true;
        }
        else if (typeof(T) == typeof(SerializableCard))
        {
            var found = props.cards?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = (T)(object)found.Value.value;
            return true;
        }
        else if (
            typeof(T) == typeof(SerializableCard[])
            || typeof(T) == typeof(List<SerializableCard>)
        )
        {
            var found = props.cardArrays?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value =
                typeof(T) == typeof(List<SerializableCard>)
                    ? (T)(object)found.Value.value.ToList()
                    : (T)(object)found.Value.value;
            return true;
        }
        return false;
    }
}