using System.Collections.Concurrent;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Utils.NodeFactories;

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
        return CreateFromScene(PreloadManager.Cache.GetScene(scenePath));
    }
    public static T CreateFromScene(PackedScene scene)
    {
        if (_instance == null) throw new Exception($"No node factory found for type '{typeof(T).FullName}'");
        
        BaseLibMain.Logger.Info($"Creating {typeof(T).Name} from scene {scene.ResourcePath}");
        var n = scene.Instantiate();
        if (n is T t) return t;
        
        //Attempt conversion.
        var node = new T();

        _instance.ConvertScene(node, n);
        
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
        foreach (var named in _namedNodes)
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
        placeholder.QueueFree();

        //Check all children for possible valid unique names
        foreach (var child in target.GetChildrenRecursive<Node>())
        {
            for (var index = 0; index < uniqueNames.Count; index++)
            {
                var unique = uniqueNames[index];
                if (!unique.IsValidUnique(child)) continue;
                
                child.UniqueNameInOwner = true;
                child.Owner = target;
                uniqueNames.Remove(unique);
                break;
            }
        }

        foreach (var missing in uniqueNames)
        {
            GenerateNode(target, missing);
        }
    }

    internal override Node CreateAndConvert(Node source)
    {
        var target = new T();
        ConvertScene(target, source);
        return target;
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

public abstract class NodeFactory
{
    // Both dictionaries are concurrent — _factories is written during Init on the main thread
    // and read from the Instantiate postfix which can fire from any thread during asset loading.
    private static readonly ConcurrentDictionary<Type, NodeFactory> _factories = new();
    private static readonly ConcurrentDictionary<string, Type> _sceneTypes = new();

    // Prevents recursion when the postfix triggers during a factory conversion.
    // ThreadStatic is correct here: Godot node creation is main-thread, but this also
    // makes us safe if background asset loading ever calls Instantiate.
    [ThreadStatic]
    private static bool _isConverting;

    public static void Init()
    {
        new ControlFactory();
        new NCreatureVisualsFactory();
        new NEnergyCounterFactory();

        RunSelfTests();
    }

    /// <summary>
    /// Register a scene path to be auto-converted to the specified node type on Instantiate.
    /// The node type must have a NodeFactory registered for it.
    /// </summary>
    public static void RegisterSceneType<TNode>(string scenePath) where TNode : Node
    {
        RegisterSceneType(scenePath, typeof(TNode));
    }

    /// <summary>
    /// Register a scene path to be auto-converted to the specified node type on Instantiate.
    /// Logs a warning if the path was already registered for a different type (silent overwrite).
    /// </summary>
    public static void RegisterSceneType(string scenePath, Type nodeType)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            BaseLibMain.Logger.Warn($"Ignoring RegisterSceneType({nodeType.Name}) with null/empty path");
            return;
        }

        if (_sceneTypes.TryGetValue(scenePath, out var existing) && existing != nodeType)
            BaseLibMain.Logger.Warn($"Overwriting scene registration for '{scenePath}': {existing.Name} → {nodeType.Name}");

        _sceneTypes[scenePath] = nodeType;
        BaseLibMain.Logger.Info($"Registered scene '{scenePath}' for auto-conversion to {nodeType.Name}");
    }

    /// <summary>
    /// Remove a previously registered scene path. Safe to call even if the path was never registered.
    /// </summary>
    public static void UnregisterSceneType(string scenePath)
    {
        _sceneTypes.TryRemove(scenePath, out _);
    }

    /// <summary>
    /// Check whether a factory is registered for the given node type.
    /// </summary>
    public static bool HasFactory<TNode>() where TNode : Node => _factories.ContainsKey(typeof(TNode));

    /// <summary>
    /// Check whether a scene path is registered for auto-conversion.
    /// </summary>
    public static bool IsRegistered(string scenePath) => !string.IsNullOrEmpty(scenePath) && _sceneTypes.ContainsKey(scenePath);

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
    internal static bool TryAutoConvert(PackedScene scene, ref Node result)
    {
        if (_isConverting || result == null) return false;

        var path = scene.ResourcePath;
        if (string.IsNullOrEmpty(path)) return false;
        if (!_sceneTypes.TryGetValue(path, out var expectedType)) return false;
        if (expectedType.IsAssignableFrom(result.GetType())) return false; // already the right type

        if (!_factories.TryGetValue(expectedType, out var factory))
        {
            BaseLibMain.Logger.Warn($"Scene '{path}' registered for {expectedType.Name} but no factory exists for that type");
            return false;
        }

        _isConverting = true;
        try
        {
            var sourceTypeName = result.GetType().Name;
            var converted = factory.CreateAndConvert(result);

            // Only log the first conversion per path to avoid spam (monsters get instantiated a lot)
            if (_loggedConversions.TryAdd(path, 0))
                BaseLibMain.Logger.Info($"Auto-converted '{path}' from {sourceTypeName} to {converted.GetType().Name}");

            result = converted;
            return true;
        }
        catch (Exception e)
        {
            // CreateAndConvert is destructive — it reparents children from the source node
            // and may QueueFree it. If conversion fails midway, the original __result is
            // corrupted (children stripped, possibly queued for deletion). We MUST NOT return
            // false and let the caller use the mangled node. Re-throw so the failure is visible.
            BaseLibMain.Logger.Error($"Auto-conversion failed for '{path}' — the instantiated node is likely corrupt: {e}");
            throw;
        }
        finally
        {
            _isConverting = false;
        }
    }

    /// <summary>
    /// Create a new instance of the factory's target type and convert the source node into it.
    /// Used by auto-conversion to avoid calling Instantiate again.
    /// </summary>
    internal abstract Node CreateAndConvert(Node source);

    //-- Self-tests: run once at init to verify the whole postfix → factory pipeline works --

    private static int _testsPassed;
    private static int _testsFailed;

    private static void RunSelfTests()
    {
        _testsPassed = 0;
        _testsFailed = 0;

        TestCreatureVisualsConversion();
        TestGenericInstantiateChain();
        TestControlConversion();
        TestAlreadyCorrectTypePassthrough();
        TestUnregisteredScenePassthrough();
        TestNullAndEmptyPathValidation();
        TestOverwriteAndUnregister();
        TestQueryApis();

        if (_testsFailed == 0)
            BaseLibMain.Logger.Info($"All {_testsPassed} auto-conversion self-tests passed");
        else
            BaseLibMain.Logger.Error($"Auto-conversion self-tests: {_testsPassed} passed, {_testsFailed} FAILED");

        _testsPassed = 0;
        _testsFailed = 0;
    }

    private static void Assert(bool condition, string testName)
    {
        if (condition)
            _testsPassed++;
        else
        {
            _testsFailed++;
            BaseLibMain.Logger.Error($"FAIL: {testName}");
        }
    }

    /// <summary>
    /// Pack a Node2D scene with creature-like children, register it, Instantiate, expect NCreatureVisuals.
    /// </summary>
    private static void TestCreatureVisualsConversion()
    {
        const string path = "res://baselib_test/creature.tscn";
        try
        {
            var root = new Node2D { Name = "TestCreature" };
            AddOwnedChild(root, new Sprite2D { Name = "Visuals", UniqueNameInOwner = true });
            AddOwnedChild(root, new Control { Name = "Bounds", Size = new Vector2(200, 240), Position = new Vector2(-100, -240) });
            AddOwnedChild(root, new Marker2D { Name = "IntentPos" });
            AddOwnedChild(root, new Marker2D { Name = "CenterPos", UniqueNameInOwner = true });

            var scene = new PackedScene();
            scene.Pack(root);
            root.QueueFree();
            scene.ResourcePath = path;

            _sceneTypes[path] = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals);

            var result = scene.Instantiate(PackedScene.GenEditState.Disabled);
            Assert(result is MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals, "NCreatureVisuals conversion");
            result.QueueFree();
        }
        catch (Exception e) { Assert(false, $"NCreatureVisuals conversion (threw: {e.Message})"); }
        finally { _sceneTypes.TryRemove(path, out _); _loggedConversions.TryRemove(path, out _); }
    }

    /// <summary>
    /// Pack a Node2D, register for Control, Instantiate, expect Control.
    /// </summary>
    private static void TestControlConversion()
    {
        const string path = "res://baselib_test/control.tscn";
        try
        {
            var root = new Node2D { Name = "TestControl" };

            var scene = new PackedScene();
            scene.Pack(root);
            root.QueueFree();
            scene.ResourcePath = path;

            _sceneTypes[path] = typeof(Control);

            var result = scene.Instantiate(PackedScene.GenEditState.Disabled);
            Assert(result is Control, "Control conversion");
            result.QueueFree();
        }
        catch (Exception e) { Assert(false, $"Control conversion (threw: {e.Message})"); }
        finally { _sceneTypes.TryRemove(path, out _); _loggedConversions.TryRemove(path, out _); }
    }

    /// <summary>
    /// Register a scene whose root is already the expected type — should pass through with no conversion.
    /// </summary>
    private static void TestAlreadyCorrectTypePassthrough()
    {
        const string path = "res://baselib_test/passthrough.tscn";
        try
        {
            var root = new Control { Name = "AlreadyControl" };

            var scene = new PackedScene();
            scene.Pack(root);
            root.QueueFree();
            scene.ResourcePath = path;

            _sceneTypes[path] = typeof(Control);

            var result = scene.Instantiate(PackedScene.GenEditState.Disabled);
            // Should still be Control (no conversion needed), and specifically should NOT
            // have gone through CreateAndConvert (it would still be Control either way,
            // but IsAssignableFrom should short-circuit).
            Assert(result is Control, "Already-correct-type passthrough");
            result.QueueFree();
        }
        catch (Exception e) { Assert(false, $"Already-correct-type passthrough (threw: {e.Message})"); }
        finally { _sceneTypes.TryRemove(path, out _); }
    }

    /// <summary>
    /// Instantiate a scene that is NOT registered — should return the raw node unchanged.
    /// </summary>
    private static void TestUnregisteredScenePassthrough()
    {
        const string path = "res://baselib_test/unregistered.tscn";
        try
        {
            var root = new Node2D { Name = "Unregistered" };

            var scene = new PackedScene();
            scene.Pack(root);
            root.QueueFree();
            scene.ResourcePath = path;
            // Deliberately NOT registering this path

            var result = scene.Instantiate(PackedScene.GenEditState.Disabled);
            Assert(result is Node2D, "Unregistered scene passthrough");
            Assert(result.GetType() == typeof(Node2D), "Unregistered scene is exactly Node2D");
            result.QueueFree();
        }
        catch (Exception e) { Assert(false, $"Unregistered scene passthrough (threw: {e.Message})"); }
    }

    /// <summary>
    /// THE critical test: call the generic Instantiate&lt;NCreatureVisuals&gt;() which is what game
    /// code actually uses. Verifies the postfix on the non-generic Instantiate fires and converts
    /// the node BEFORE the (T)(object) cast in the generic method.
    /// If this test passes, the entire approach works end-to-end.
    /// </summary>
    private static void TestGenericInstantiateChain()
    {
        const string path = "res://baselib_test/generic_chain.tscn";
        try
        {
            var root = new Node2D { Name = "GenericChainTest" };
            AddOwnedChild(root, new Sprite2D { Name = "Visuals", UniqueNameInOwner = true });
            AddOwnedChild(root, new Control { Name = "Bounds", Size = new Vector2(200, 240), Position = new Vector2(-100, -240) });
            AddOwnedChild(root, new Marker2D { Name = "IntentPos" });
            AddOwnedChild(root, new Marker2D { Name = "CenterPos", UniqueNameInOwner = true });

            var scene = new PackedScene();
            scene.Pack(root);
            root.QueueFree();
            scene.ResourcePath = path;

            _sceneTypes[path] = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals);

            // This is the real deal — Instantiate<T> calls Instantiate() then casts to T.
            // Our postfix on Instantiate() must convert BEFORE the cast, or this throws InvalidCastException.
            var result = scene.Instantiate<MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals>(PackedScene.GenEditState.Disabled);
            Assert(result is MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals, "Generic Instantiate<NCreatureVisuals> chain");
            result.QueueFree();
        }
        catch (InvalidCastException)
        {
            Assert(false, "Generic Instantiate<NCreatureVisuals> chain (InvalidCastException — postfix didn't fire before cast!)");
        }
        catch (Exception e)
        {
            Assert(false, $"Generic Instantiate<NCreatureVisuals> chain (threw: {e.Message})");
        }
        finally { _sceneTypes.TryRemove(path, out _); _loggedConversions.TryRemove(path, out _); }
    }

    /// <summary>
    /// Verify that null/empty paths are rejected by RegisterSceneType.
    /// </summary>
    private static void TestNullAndEmptyPathValidation()
    {
        var countBefore = _sceneTypes.Count;
        RegisterSceneType<Control>(null!);
        RegisterSceneType<Control>("");
        RegisterSceneType<Control>("   ");
        Assert(_sceneTypes.Count == countBefore, "Null/empty/whitespace paths rejected");
    }

    /// <summary>
    /// Verify overwrite and unregister work correctly.
    /// </summary>
    private static void TestOverwriteAndUnregister()
    {
        const string path = "res://baselib_test/overwrite.tscn";
        try
        {
            // Register for Control, then overwrite to NCreatureVisuals
            _sceneTypes[path] = typeof(Control);
            RegisterSceneType<MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals>(path);
            Assert(_sceneTypes.TryGetValue(path, out var t) && t == typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals),
                "Overwrite registration");

            // Unregister
            UnregisterSceneType(path);
            Assert(!_sceneTypes.ContainsKey(path), "Unregister removes path");

            // Unregister again (should not throw)
            UnregisterSceneType(path);
            Assert(true, "Double unregister is safe");
        }
        catch (Exception e) { Assert(false, $"Overwrite/unregister (threw: {e.Message})"); }
        finally { _sceneTypes.TryRemove(path, out _); }
    }

    /// <summary>
    /// Verify the public query APIs work.
    /// </summary>
    private static void TestQueryApis()
    {
        Assert(HasFactory<MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals>(), "HasFactory<NCreatureVisuals>");
        Assert(HasFactory<Control>(), "HasFactory<Control>");
        Assert(!HasFactory<Sprite2D>(), "!HasFactory<Sprite2D> (no factory registered)");

        const string path = "res://baselib_test/query_test.tscn";
        _sceneTypes[path] = typeof(Control);
        Assert(IsRegistered(path), "IsRegistered for known path");
        Assert(!IsRegistered("res://nonexistent/path.tscn"), "!IsRegistered for unknown path");
        Assert(!IsRegistered(""), "!IsRegistered for empty path");
        Assert(!IsRegistered(null!), "!IsRegistered for null path");
        _sceneTypes.TryRemove(path, out _);
    }

    private static void AddOwnedChild(Node parent, Node child)
    {
        parent.AddChild(child);
        child.Owner = parent;
    }

    protected interface INodeInfo
    {
        string Path { get; }
        bool UniqueName { get; }
        bool MakeNameUnique { get; }
        bool IsValidType(Node node);
        bool IsValidUnique(Node n);
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
    protected readonly List<INodeInfo> _namedNodes;
    
    /// <summary>
    /// If true, then will simply add entire root node of a scene as child of a new instance of target scene type.
    /// Otherwise, will need to replace root node.
    /// </summary>
    protected readonly bool FlexibleStructure;
    
    protected NodeFactory(IEnumerable<INodeInfo> namedNodes)
    {
        _namedNodes = namedNodes.ToList();
        FlexibleStructure = _namedNodes.All(info => info.UniqueName);
    }
    
    
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

    protected static void SetChildrenOwner(Node target, Node child)
    {
        foreach (var grandchild in child.GetChildren())
        {
            grandchild.Owner = target;
            SetChildrenOwner(target, grandchild);
        }
    }
}