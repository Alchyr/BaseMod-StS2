using System.Collections.Concurrent;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Utils.NodeFactories;

/// <summary>
/// Factory for producing instances of scene scripts that are normally inaccessible in Godot editor when modding.
/// Will convert a given scene and nodes within the scene into valid types for target scene if it is possible to do so.
/// Should reference NodeFactory&lt;T&gt;.CreateFromScene.
/// </summary>
public abstract class NodeFactory
{
    public static void Init()
    {
        new ControlFactory();
        new NCreatureVisualsFactory();
        new NRestSiteCharacterFactory();
        new NMerchantCharacterFactory();
        new NEnergyCounterFactory();
        new NCustomTreasureRoomChestFactory();
    }
    
    // Both dictionaries are concurrent — _factories is written during Init on the main thread
    // and read from the Instantiate postfix which can fire from any thread during asset loading.
    private static readonly ConcurrentDictionary<Type, NodeFactory> _factories = new();
    private static readonly ConcurrentDictionary<string, (Type, Action<Node>?)> _registeredScenes = new();

    // Prevents recursion when the postfix triggers during a factory conversion.
    // ThreadStatic is correct here: Godot node creation is main-thread, but this also
    // makes us safe if background asset loading ever calls Instantiate.
    [ThreadStatic]
    private static HashSet<Node>? _convertingNodes;

    /// <summary>
    /// Register a scene path to be auto-converted to the specified node type on Instantiate.
    /// The node type must have a NodeFactory registered for it.
    /// </summary>
    public static void RegisterSceneType<TNode>(string scenePath, Action<TNode>? postConversionAction = null) where TNode : Node
    {
        RegisterSceneType(scenePath, (typeof(TNode), postConversionAction));
    }

    /// <summary>
    /// Register a scene path to be auto-converted to the specified node type on Instantiate.
    /// Logs a warning if the path was already registered for a different type (silent overwrite).
    /// </summary>
    public static void RegisterSceneType<TNode>(string scenePath, (Type, Action<TNode>?) nodeType) where TNode : Node
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            BaseLibMain.Logger.Warn($"Ignoring RegisterSceneType({nodeType.Item1.Name}) with null/empty path");
            return;
        }

        if (!scenePath.StartsWith("res://") && !scenePath.StartsWith("user://"))
        {
            BaseLibMain.Logger.Warn($"Registering non-res or user path '{scenePath}'; assuming res path");
            scenePath = "res://" + scenePath;
        }

        scenePath = scenePath.SimplifyPath();

        if (_registeredScenes.TryGetValue(scenePath, out var existing) && existing.Item1 != nodeType.Item1)
            BaseLibMain.Logger.Warn($"Overwriting scene registration for '{scenePath}': {existing.Item1.Name} → {nodeType.Item1.Name}");

        _registeredScenes[scenePath] = (nodeType.Item1, nodeType.Item2 as Action<Node>);
        BaseLibMain.Logger.Info($"Registered scene '{scenePath}' for auto-conversion to {nodeType.Item1.Name}");
    }

    internal static void RegisterFactory(Type nodeType, NodeFactory factory)
    {
        _factories[nodeType] = factory;
    }

    // Tracks which paths have already logged a conversion, to avoid log spam.
    // ConcurrentDictionary for thread safety — same contract as _factories/_sceneTypes.
    private static readonly ConcurrentDictionary<string, byte> _loggedConversions = new();

    /// <summary>
    /// Checks if the instantiated node needs auto-conversion based on registered scene types,
    /// and performs the conversion if a matching factory exists.
    /// Called from the PackedScene.Instantiate postfix.
    ///
    /// When CreateFromScene also calls Instantiate internally, the postfix handles the conversion
    /// and CreateFromScene's "if (n is T t) return t" short-circuits — both paths produce the same result.
    /// </summary>
    internal static bool TryAutoConvert(PackedScene scene, ref Node? result)
    {
        if (result == null || (_convertingNodes != null && _convertingNodes.Contains(result))) return false;

        var path = scene.ResourcePath.SimplifyPath();
        if (string.IsNullOrEmpty(path)) return false;
        if (!_registeredScenes.TryGetValue(path, out var sceneInfo)) return false; //No registered conversion
        if (sceneInfo.Item1.IsInstanceOfType(result)) return false; // already the right type

        if (!_factories.TryGetValue(sceneInfo.Item1, out var factory))
        {
            BaseLibMain.Logger.Warn($"Scene '{path}' registered for {sceneInfo.Item1.Name} but no factory exists for that type");
            return false;
        }

        _convertingNodes ??= [];
        var converting = result;
        _convertingNodes.Add(converting);
        
        try
        {
            var sourceTypeName = result.GetType().Name;
            var converted = factory.CreateFromNode(result);

            // Only log the first conversion per path to avoid spam
            if (_loggedConversions.TryAdd(path, 0))
                BaseLibMain.Logger.Info($"Auto-converted '{path}' from {sourceTypeName} to {converted.GetType().Name}");

            sceneInfo.Item2?.Invoke(converted);

            result = converted;
            return true;
        }
        catch (Exception e)
        {
            // CreateAndConvert is destructive — it reparents children from the source node
            // and may QueueFree it. If conversion fails midway, the original __result is
            // corrupted (children stripped, possibly queued for deletion). We MUST NOT return
            // false and let the caller use the mangled node. Re-throw so the failure is visible.
            BaseLibMain.Logger.Error($"Auto-conversion failed for '{path}': {e}");
            throw;
        }
        finally
        {
            _convertingNodes.Remove(converting);
        }
    }

    /// <summary>
    /// Create a new instance of the factory's target type and convert the source node into it.
    /// </summary>
    protected abstract Node CreateFromNode(Node source);

    /// <summary>
    /// Information about an element (node) contained in a scene, used to determine if conversion is possible/how to convert.
    /// </summary>
    protected interface INodeInfo
    {
        /// <summary>
        /// The node's expected path from the root of the scene.
        /// </summary>
        string Path { get; }
        /// <summary>
        /// Whether the node MUST be accessible through a unique name.
        /// </summary>
        bool UniqueName { get; }
        /// <summary>
        /// Whether the node should be made accessible through a unique name. Can be true even if node does not require
        /// a unique name.
        /// </summary>
        bool MakeNameUnique { get; }
        /// <summary>
        /// Returns true if the given node has the expected name.
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        bool IsValidName(Node n);
        /// <summary>
        /// Returns true if the node is a valid type to be used as this element of the scene.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        bool IsValidType(Node node);
        /// <summary>
        /// Returns true if the given node is a valid type, this node requires a unique name,
        /// and the node is named correctly.
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        bool IsValidUnique(Node n);
        /// <summary>
        /// The type that this node should have.
        /// </summary>
        /// <returns></returns>
        Type NodeType();
    }
    protected record NodeInfo<T>(string Path, bool MakeNameUnique = true) : INodeInfo
    {
        public bool UniqueName { get; init; } = Path.StartsWith('%');
        public StringName StringName { get; init; } = new(Path.StartsWith('%') ? Path[1..] : Path);

        public bool IsValidType(Node node)
        {
            return node is T;
        }

        public bool IsValidName(Node n)
        {
            return n.Name.Equals(StringName);
        }

        public bool IsValidUnique(Node n)
        {
            if (!UniqueName) return false;
            return n is T && n.Name.Equals(StringName);
        }

        public Type NodeType()
        {
            return typeof(T);
        }
    }
    
    /// <summary>
    /// Nodes that will be looked for in the generated type.
    /// Not all of these are necessarily required.
    /// </summary>
    protected readonly List<INodeInfo> NamedNodes;
    
    /// <summary>
    /// If true, then will simply add entire root node of a scene as child of a new instance of target scene type.
    /// Otherwise, will need to replace root node.
    /// </summary>
    protected readonly bool FlexibleStructure;
    
    protected NodeFactory(IEnumerable<INodeInfo> namedNodes)
    {
        NamedNodes = namedNodes.ToList();
        FlexibleStructure = NamedNodes.All(info => info.UniqueName);
    }
    
    
    /// <summary>
    /// Copies common positional/input properties of a control.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="source"></param>
    protected static void CopyControlProperties(Control target, Control source)
    {
        CopyCanvasItemProperties(target, source);
        target.LayoutMode = source.LayoutMode;
        target.AnchorLeft = source.AnchorLeft;
        target.AnchorTop = source.AnchorTop;
        target.AnchorRight = source.AnchorRight;
        target.AnchorBottom = source.AnchorBottom;
        target.OffsetLeft = source.OffsetLeft;
        target.OffsetTop = source.OffsetTop;
        target.OffsetRight = source.OffsetRight;
        target.OffsetBottom = source.OffsetBottom;
        target.GrowHorizontal = source.GrowHorizontal;
        target.GrowVertical = source.GrowVertical;
        target.Size = source.Size;
        target.CustomMinimumSize = source.CustomMinimumSize;
        target.PivotOffset = source.PivotOffset;
        target.MouseFilter = source.MouseFilter;
        target.FocusMode = source.FocusMode;
        target.ClipContents = source.ClipContents;
    }

    /// <summary>
    /// Copies common positional/input/visual properties of a CanvasItem.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="source"></param>
    protected static void CopyCanvasItemProperties(CanvasItem target, CanvasItem source)
    {
        target.Visible = source.Visible;
        target.Modulate = source.Modulate;
        target.SelfModulate = source.SelfModulate;
        target.ShowBehindParent = source.ShowBehindParent;
        target.TopLevel = source.TopLevel;
        target.ZIndex = source.ZIndex;
        target.ZAsRelative = source.ZAsRelative;
        target.YSortEnabled = source.YSortEnabled;
        target.TextureFilter = source.TextureFilter;
        target.TextureRepeat = source.TextureRepeat;
        target.Material = source.Material;
        target.UseParentMaterial = source.UseParentMaterial;

        if (target is Node2D targetNode2D && source is Node2D sourceNode2D)
        {
            targetNode2D.Position = sourceNode2D.Position;
            targetNode2D.Rotation = sourceNode2D.Rotation;
            targetNode2D.Scale = sourceNode2D.Scale;
            targetNode2D.Skew = sourceNode2D.Skew;
        }
    }

    /// <summary>
    /// Sets a child node and all its children to be owned by a target node for the purposes of unique name access.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="child"></param>
    protected static void SetChildrenOwner(Node target, Node child)
    {
        foreach (var grandchild in child.GetChildren())
        {
            grandchild.Owner = target;
            SetChildrenOwner(target, grandchild);
        }
    }
}

/// <summary>
/// Factory for producing instances of scene scripts that are normally inaccessible in Godot editor when modding.
/// Will convert a given scene and nodes within the scene into valid types for target scene if it is possible to do so.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class NodeFactory<T> : NodeFactory where T : Node, new()
{
    private static NodeFactory<T>? _instance;
    
    protected NodeFactory(IEnumerable<INodeInfo> namedNodes) : base(namedNodes)
    {
        _instance = this;
        RegisterFactory(typeof(T), this);
        BaseLibMain.Logger.Info($"Created node factory for {typeof(T).Name}.");
    }

    public static T CreateFromResource(object resource)
    {
        if (_instance == null) throw new Exception($"No node factory found for type '{typeof(T).FullName}'");
        if (!BaseLibMain.IsMainThread)
        {
            BaseLibMain.Logger.Warn($"NodeFactory<{typeof(T)}>.CreateFromResource called while not on main thread");
            throw new Exception($"NodeFactory<{typeof(T)}>.CreateFromResource called while not on main thread");
        }

        if (resource is string s && ResourceLoader.Exists(s))
        {
            resource = ResourceLoader.Load(s);
        }
        
        BaseLibMain.Logger.Info($"Creating {typeof(T).Name} from resource {resource.GetType().Name}");
        var n = _instance.CreateBareFromResource(resource);
        _instance.ConvertScene(n, null);
        return n;
    }

    /// <summary>
    /// Create a root node, using resource in node creation.
    /// The root node's name is recommended to be set based on the given resource.
    /// This root node will them be passed to ConvertScene.
    /// </summary>
    /// <param name="resource"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    protected virtual T CreateBareFromResource(object resource)
    {
        throw new Exception($"Node factory for {typeof(T).Name} does not support generation from resource type {resource.GetType().Name}");
    }

    public static T CreateFromScene(string scenePath)
    {
        if (!scenePath.EndsWith(".tscn")) BaseLibMain.Logger.Warn($"Attempting to create {typeof(T).Name} from scene {scenePath} with unusual file extension");
        return CreateFromScene(PreloadManager.Cache.GetScene(scenePath));
    }
    public static T CreateFromScene(PackedScene scene)
    {
        if (_instance == null) throw new Exception($"No node factory found for type '{typeof(T).FullName}'");
        if (!BaseLibMain.IsMainThread)
        {
            BaseLibMain.Logger.Error($"NodeFactory<{typeof(T)}>.CreateFromScene called while not on main thread");
            throw new Exception($"NodeFactory<{typeof(T)}>.CreateFromScene called while not on main thread");
        }
        
        BaseLibMain.Logger.Info($"Creating {typeof(T).Name} from scene {scene.ResourcePath}");
        return _instance.CreateFromNode(scene.Instantiate());
    }

    protected override T CreateFromNode(Node n)
    {
        if (n is T t) return t;
        
        //Attempt conversion.
        var node = new T();

        ConvertScene(node, n);
        
        return node;
    }

    /// <summary>
    /// Convert the root node. If there are additional properties to copy from the root node, that should be done here.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="source"></param>
    protected virtual void ConvertScene(T target, Node? source)
    {
        if (source != null)
        {
            //Copy (some) root node properties. Ideally nothing would be missed, but that would require too many specific checks.
            //This method can be overriden if necessary.
            target.Name = source.Name;
            
            switch (target)
            {
                case Control targetControl when source is Control sourceControl:
                    CopyControlProperties(targetControl, sourceControl);
                    break;
                case CanvasItem targetItem when source is CanvasItem sourceItem:
                    CopyCanvasItemProperties(targetItem, sourceItem);
                    break;
            }
        }
        TransferAndCreateNodes(target, source);
    }

    protected virtual void TransferAndCreateNodes(T target, Node? source)
    {
        if (source != null)
        {
            if (FlexibleStructure)
            {
                //All named nodes use unique names, therefore exact paths are not important to match.
                target.AddChild(source);
                source.Owner = target;
                SetChildrenOwner(target, source);
            }
            else
            {
                //Transfer all nodes and set their owners.
                foreach (var child in source.GetChildren())
                {
                    source.RemoveChild(child);
                    target.AddChild(child);
                    child.Owner = target;
                    SetChildrenOwner(target, child);
                }
            
                source.QueueFree();
            }
        }
            
        //Verify existence of/create named nodes
        List<INodeInfo> uniqueNames = [];
        Node placeholder = new();
        foreach (var named in NamedNodes)
        {
            if (named.UniqueName) uniqueNames.Add(named);
            else
            {
                var node = target.GetNodeOrNull(named.Path);
                if (node != null)
                {
                    if (!named.IsValidType(node))
                    {
                        node.ReplaceBy(placeholder);
                        node = ConvertNodeType(node, named.NodeType());
                        placeholder.ReplaceBy(node);
                    }

                    if (named.MakeNameUnique)
                    {
                        node.UniqueNameInOwner = true;
                        node.Owner = target;
                    }
                }
                else
                {
                    GenerateNode(target, named);
                }
            }
        }

        //Check all children for possible valid unique names
        Dictionary<INodeInfo, Node> backupUniqueNodes = []; //Nodes with matching name but incorrect type.
        foreach (var child in target.GetChildrenRecursive<Node>())
        {
            for (var index = 0; index < uniqueNames.Count; index++)
            {
                var unique = uniqueNames[index];

                if (unique.IsValidName(child))
                {
                    backupUniqueNodes[unique] = child;
                }
                if (!unique.IsValidUnique(child)) continue;
                
                child.UniqueNameInOwner = true;
                child.Owner = target;
                uniqueNames.Remove(unique);
                break;
            }
        }

        foreach (var missing in uniqueNames)
        {
            //Attempt conversion if no node with correct name and type was found, but matching name was found.
            if (backupUniqueNodes.TryGetValue(missing, out var node))
            {
                if (!missing.IsValidType(node))
                {
                    node.ReplaceBy(placeholder);
                    node = ConvertNodeType(node, missing.NodeType());
                    placeholder.ReplaceBy(node);
                }

                node.UniqueNameInOwner = true;
                node.Owner = target;
            }
            else
            {
                GenerateNode(target, missing);
            }
        }
        
        placeholder.QueueFree();
    }

    /// <summary>
    /// This method should convert the given node into the target type, or call the base method if unsupported.
    /// The given node should either be freed or incorporated as a child of the generated node.
    /// </summary>
    /// <param name="node"></param>
    /// <param name="targetType"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    protected virtual Node ConvertNodeType(Node node, Type targetType)
    {
        throw new InvalidOperationException(
            $"Node factory for {typeof(T).Name} does not support conversion of {node.GetType().Name} '{node.Name}' to {targetType.Name}");
    }

    /// <summary>
    /// Generate a new instance of the specified node type as a child of target based on the INodeInfo given.
    /// This method is used called when a named node is not found in the provided scene,
    /// which will be most named nodes if a scene is built from a resource.
    /// Optional nodes may be ignored.
    /// Required nodes that are unsupported should throw an exception.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="required"></param>
    protected abstract void GenerateNode(Node target, INodeInfo required);
}