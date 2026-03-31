using System.Collections;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace BaseLib.HotReload;

/// <summary>
/// Walks the Godot scene tree and the current run state to replace old model instances
/// with fresh ones from ModelDb. This makes hot-reloaded changes visible immediately
/// without needing a new encounter or game restart.
/// </summary>
internal static class LiveInstanceRefresher
{
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    // These are the mutable runtime state fields we copy from old → new instances.
    // Without this, a hot-reloaded card would lose its upgrade status, a power would
    // lose its stacks, etc.
    private static readonly string[] CardStateFields =
    [
        "CostForTurn", "CurrentCost", "TemporaryCost", "IsTemporaryCostModified",
        "FreeToPlay", "Retain", "Ethereal", "Exhaust", "Exhausts",
        "WasDiscarded", "WasDrawnThisTurn", "PlayedThisTurn", "Misc", "Counter", "TurnsInHand",
    ];

    private static readonly string[] RelicStateFields =
    [
        "Counter", "Charges", "UsesRemaining", "Cooldown",
        "TriggeredThisTurn", "TriggeredThisCombat", "PulseActive", "IsDisabled", "Grayscale",
    ];

    private static readonly string[] PowerStateFields =
    [
        "Stacks", "Amount", "Counter", "TurnsRemaining",
        "TriggeredThisTurn", "TriggeredThisCombat", "PulseActive", "JustApplied",
    ];

    private static readonly string[] PotionStateFields =
    [
        "Charges", "UsesRemaining", "Counter", "TriggeredThisCombat",
    ];

    // ─── Scene Tree Refresh ─────────────────────────────────────────────

    /// <summary>
    /// Walk the entire Godot scene tree and re-set Model properties on NCard, NRelic,
    /// NPower, NPotion, and NCreature nodes to fresh instances from ModelDb.
    /// Returns total number of nodes refreshed.
    /// </summary>
    public static int RefreshSceneTree()
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        int total = 0;

        total += WalkAndRefresh<NCard>(root, RefreshCard);
        total += WalkAndRefresh<NRelic>(root, RefreshRelic);
        total += WalkAndRefresh<NPower>(root, RefreshPower);
        total += WalkAndRefresh<NPotion>(root, RefreshPotion);
        total += WalkAndRefresh<NCreature>(root, RefreshCreature);

        return total;
    }

    /// <summary>
    /// BFS walk the scene tree, calling refreshFunc on each node of type T.
    /// Returns how many nodes were actually refreshed (model swapped).
    /// </summary>
    private static int WalkAndRefresh<T>(Node root, Func<T, bool> refreshFunc) where T : Node
    {
        int count = 0;
        var queue = new Queue<Node>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is T typed)
            {
                try
                {
                    if (refreshFunc(typed))
                        count++;
                }
                catch { /* best effort — don't crash the whole refresh for one node */ }
            }
            foreach (var child in node.GetChildren())
                queue.Enqueue(child);
        }

        return count;
    }

    // Each Refresh* method below does the same thing: check if the node's current model
    // was loaded from an old assembly, and if so, replace it with the fresh one from ModelDb.
    // They can't be a single generic because ModelDb.GetByIdOrNull<T> requires the specific
    // model type (CardModel, RelicModel, etc.) and the node types don't share a common
    // "get model" interface. NCreature is extra special — its Monster property is get-only
    // so we have to poke the compiler-generated backing field directly.

    private static bool RefreshCard(NCard node)
    {
        var modelProp = node.GetType().GetProperty("Model");
        if (modelProp?.GetValue(node) is not AbstractModel model) return false;
        var fresh = ModelDb.GetByIdOrNull<CardModel>(model.Id);
        if (fresh == null || ReferenceEquals(fresh, model)) return false;
        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;
        modelProp.SetValue(node, fresh);
        return true;
    }

    private static bool RefreshRelic(NRelic node)
    {
        var modelProp = node.GetType().GetProperty("Model");
        if (modelProp?.GetValue(node) is not AbstractModel model) return false;
        var fresh = ModelDb.GetByIdOrNull<RelicModel>(model.Id);
        if (fresh == null || ReferenceEquals(fresh, model)) return false;
        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;
        modelProp.SetValue(node, fresh);
        return true;
    }

    private static bool RefreshPower(NPower node)
    {
        var modelProp = node.GetType().GetProperty("Model");
        if (modelProp?.GetValue(node) is not AbstractModel model) return false;
        var fresh = ModelDb.GetByIdOrNull<PowerModel>(model.Id);
        if (fresh == null || ReferenceEquals(fresh, model)) return false;
        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;
        modelProp.SetValue(node, fresh);
        return true;
    }

    private static bool RefreshPotion(NPotion node)
    {
        var modelProp = node.GetType().GetProperty("Model");
        if (modelProp?.GetValue(node) is not AbstractModel model) return false;
        var fresh = ModelDb.GetByIdOrNull<PotionModel>(model.Id);
        if (fresh == null || ReferenceEquals(fresh, model)) return false;
        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;
        modelProp.SetValue(node, fresh);
        return true;
    }

    private static bool RefreshCreature(NCreature node)
    {
        // NCreature.Entity is a Creature, and Creature.Monster is the model.
        // Monster is a get-only auto-property so we have to set the compiler-generated
        // backing field directly.
        var creature = node.Entity;
        if (creature == null || !creature.IsMonster) return false;
        var model = creature.Monster;
        if (model == null) return false;
        var fresh = ModelDb.GetByIdOrNull<MonsterModel>(model.Id);
        if (fresh == null || ReferenceEquals(fresh, model)) return false;
        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;

        var backingField = creature.GetType().GetField("<Monster>k__BackingField", InstanceFlags);
        if (backingField == null) return false;
        backingField.SetValue(creature, fresh);
        fresh.Creature = creature;
        return true;
    }

    // ─── Run State Refresh ──────────────────────────────────────────────

    /// <summary>
    /// Refresh mutable card/relic/power/potion instances in the current run's state.
    /// This covers the master deck, all combat card piles, relics, potions, and active
    /// powers on each player. Returns total number of instances refreshed.
    /// </summary>
    public static int RefreshRunInstances(Assembly reloadedAssembly, string modKey)
    {
        int refreshed = 0;
        string assemblyKey = string.IsNullOrWhiteSpace(modKey)
            ? AssemblyStamper.NormalizeModKey(reloadedAssembly.GetName().Name)
            : modKey;

        // Build the set of type names from the new assembly so we know which
        // runtime instances belong to the reloaded mod
        var reloadedTypeNames = new HashSet<string>(
            TypeSignatureHasher.GetLoadableTypes(reloadedAssembly)
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Select(t => t.FullName ?? t.Name),
            StringComparer.Ordinal);

        try
        {
            // Find RunManager.CurrentRun via reflection — it's in game code
            var runManagerType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => TypeSignatureHasher.GetLoadableTypes(a))
                .FirstOrDefault(t => t.Name == "RunManager");
            if (runManagerType == null) return 0;

            var currentRun = runManagerType
                .GetProperty("CurrentRun", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            if (currentRun == null) return 0;

            // Walk each player's state: deck, relics, potions, combat piles, powers
            if (GetPropertyValue(currentRun, "Players") is not IEnumerable players)
                return 0;

            foreach (var player in players)
            {
                refreshed += RefreshModelList(
                    GetCollectionItems(GetPropertyValue(player, "MasterDeck", "Deck"), "Cards"),
                    assemblyKey, reloadedTypeNames);

                refreshed += RefreshModelList(
                    GetCollectionItems(GetPropertyValue(player, "Relics"), "Items"),
                    assemblyKey, reloadedTypeNames);

                refreshed += RefreshModelList(
                    GetCollectionItems(GetPropertyValue(player, "Potions"), "Items"),
                    assemblyKey, reloadedTypeNames);

                // Combat piles: hand, draw, discard, exhaust
                var combatState = GetPropertyValue(player, "PlayerCombatState", "CombatState");
                if (combatState == null) continue;

                foreach (var pileName in new[] { "Hand", "DrawPile", "DiscardPile", "ExhaustPile" })
                {
                    refreshed += RefreshModelList(
                        GetCollectionItems(GetPropertyValue(combatState, pileName), "Cards"),
                        assemblyKey, reloadedTypeNames);
                }

                // Active powers on the player's combat state
                refreshed += RefreshModelList(
                    GetCollectionItems(GetPropertyValue(combatState, "PlayerPowers", "Powers"), "Items"),
                    assemblyKey, reloadedTypeNames);
            }
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"[HotReload] Run instance refresh error: {ex}");
        }

        return refreshed;
    }

    // ─── Helpers for navigating run state via reflection ─────────────────

    private static object? GetPropertyValue(object? owner, params string[] names)
    {
        if (owner == null) return null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        foreach (var name in names)
        {
            var prop = owner.GetType().GetProperty(name, flags);
            if (prop != null) return prop.GetValue(owner);
        }
        return null;
    }

    private static IList? GetCollectionItems(object? container, params string[] itemPropertyNames)
    {
        if (container is IList list) return list;
        if (container == null) return null;
        foreach (var name in itemPropertyNames)
        {
            if (GetPropertyValue(container, name) is IList propList)
                return propList;
        }
        return null;
    }

    /// <summary>
    /// Walk a list of model instances, replacing any that belong to the reloaded mod
    /// with fresh ToMutable() copies from ModelDb. Preserves upgrade status and runtime state.
    /// </summary>
    private static int RefreshModelList(IList? items, string assemblyKey, HashSet<string> reloadedTypeNames)
    {
        if (items == null) return 0;
        int refreshed = 0;

        for (int i = 0; i < items.Count; i++)
        {
            try
            {
                if (items[i] is not AbstractModel current) continue;

                // Only touch instances from the reloaded mod's assembly
                if (!string.Equals(
                    AssemblyStamper.NormalizeModKey(current.GetType().Assembly.GetName().Name),
                    assemblyKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                var typeName = current.GetType().FullName ?? current.GetType().Name;
                var canonical = GetCanonicalModel(current);

                // If this type name isn't in the new assembly's type list, only
                // refresh if the canonical model (from ModelDb) belongs to the
                // reloaded mod. This handles renamed/moved types gracefully.
                if (!reloadedTypeNames.Contains(typeName))
                {
                    if (canonical == null
                        || !string.Equals(
                            AssemblyStamper.NormalizeModKey(canonical.GetType().Assembly.GetName().Name),
                            assemblyKey, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Create a fresh mutable copy from the canonical ModelDb entry
                var toMutable = canonical?.GetType().GetMethod("ToMutable");
                if (toMutable?.Invoke(canonical, null) is not AbstractModel mutable)
                    continue;

                // Preserve upgrade state (cards can be upgraded N times)
                if (current is CardModel)
                    ApplyCardUpgradeState(current, mutable);

                // Copy runtime state fields (counters, stacks, costs, etc.)
                var stateFields = current switch
                {
                    CardModel => CardStateFields,
                    RelicModel => RelicStateFields,
                    PowerModel => PowerStateFields,
                    PotionModel => PotionStateFields,
                    _ => Array.Empty<string>(),
                };
                CopyRuntimeState(current, mutable, stateFields);

                items[i] = mutable;
                refreshed++;
            }
            catch { /* best effort — leave existing instance if migration fails */ }
        }

        return refreshed;
    }

    private static AbstractModel? GetCanonicalModel(AbstractModel current)
    {
        return current switch
        {
            CardModel => ModelDb.GetByIdOrNull<CardModel>(current.Id),
            RelicModel => ModelDb.GetByIdOrNull<RelicModel>(current.Id),
            PowerModel => ModelDb.GetByIdOrNull<PowerModel>(current.Id),
            PotionModel => ModelDb.GetByIdOrNull<PotionModel>(current.Id),
            _ => null,
        };
    }

    /// <summary>
    /// Re-apply upgrade(s) to the fresh mutable card copy so upgraded cards
    /// don't revert to base stats after hot reload.
    /// </summary>
    private static void ApplyCardUpgradeState(object source, object target)
    {
        int upgrades = 0;
        if (TryReadMember(source, "TimesUpgraded", out var val) && val is int tu) upgrades = tu;
        else if (TryReadMember(source, "UpgradeCount", out val) && val is int uc) upgrades = uc;
        else if (TryReadMember(source, "IsUpgraded", out val) && val is true) upgrades = 1;

        if (upgrades <= 0) return;

        var upgradeMethod = target.GetType().GetMethod("Upgrade", InstanceFlags, null, Type.EmptyTypes, null);
        if (upgradeMethod == null) return;

        for (int i = 0; i < upgrades; i++)
        {
            try { upgradeMethod.Invoke(target, null); }
            catch { break; }
        }
    }

    private static void CopyRuntimeState(object source, object target, string[] memberNames)
    {
        foreach (var name in memberNames)
        {
            if (TryReadMember(source, name, out var value))
                TryWriteMember(target, name, value);
        }
    }

    private static bool TryReadMember(object source, string name, out object? value)
    {
        var prop = source.GetType().GetProperty(name, InstanceFlags);
        if (prop is { CanRead: true })
        {
            value = prop.GetValue(source);
            return true;
        }
        var field = source.GetType().GetField(name, InstanceFlags);
        if (field != null)
        {
            value = field.GetValue(source);
            return true;
        }
        value = null;
        return false;
    }

    private static void TryWriteMember(object target, string name, object? value)
    {
        var prop = target.GetType().GetProperty(name, InstanceFlags);
        if (prop is { CanWrite: true } && CanAssignValue(prop.PropertyType, value))
        {
            prop.SetValue(target, value);
            return;
        }
        var field = target.GetType().GetField(name, InstanceFlags);
        if (field is { IsInitOnly: false } && CanAssignValue(field.FieldType, value))
            field.SetValue(target, value);
    }

    /// <summary>
    /// Check whether a value can be assigned to a target type without throwing.
    /// Handles nullables and null values correctly.
    /// </summary>
    private static bool CanAssignValue(Type targetType, object? value)
    {
        if (value == null)
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
        var valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType))
            return true;
        var underlying = Nullable.GetUnderlyingType(targetType);
        return underlying != null && underlying.IsAssignableFrom(valueType);
    }
}
