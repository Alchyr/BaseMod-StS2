using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using BaseLib.Abstracts;
using BaseLib.Patches;
using BaseLib.Patches.Content;
using BaseLib.Patches.Utils;
// NodeFactory scene cleanup will be added when the auto-conversion branch merges
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.HotReload;

/// <summary>
/// The core hot reload pipeline. Executes a 12-step transactional process to reload
/// a mod assembly into the running game: load assembly, swap Harmony patches, replace
/// entities in ModelDb, refresh pools, re-inject localization, and update live instances.
///
/// This runs on the main thread — the game UI is effectively paused during the reload.
/// Thread safety is enforced by a lock in HotReloadEngine (only one reload at a time).
/// </summary>
internal static class HotReloadPipeline
{
    private const BindingFlags StaticNonPublic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;

    // We only want to register the assembly resolution handlers once per game session.
    // They stay active forever and handle all future hot-reloaded assemblies.
    // Two handlers are needed:
    //   1. AssemblyLoadContext.Default.Resolving — fires when default ALC probing fails
    //   2. AppDomain.CurrentDomain.AssemblyResolve — fires for version mismatches
    // Both redirect by assembly name (ignoring version) to already-loaded assemblies.
    private static bool _defaultAlcResolvingRegistered;

    /// <summary>
    /// Called by HotReloadEngine.Init() after registering the resolvers at startup.
    /// Prevents the pipeline from double-registering them.
    /// </summary>
    internal static void MarkResolversRegistered() => _defaultAlcResolvingRegistered = true;

    /// <summary>
    /// Run the full hot reload pipeline. Returns a structured result describing
    /// everything that happened (or failed).
    /// </summary>
    public static HotReloadResult Execute(
        string dllPath,
        int tier,
        string? pckPath,
        HotReloadSession session)
    {
        // ─── Bookkeeping ─────────────────────────────────────────────
        var actions = new List<string>();
        var errors = new List<string>();
        var warnings = new List<string>();
        var changedEntities = new List<ChangedEntity>();
        int entitiesRemoved = 0, entitiesInjected = 0, entitiesSkipped = 0;
        int poolsUnfrozen = 0, poolRegs = 0, patchCount = 0;
        int verified = 0, verifyFailed = 0, mutableOk = 0, mutableFailed = 0;
        int liveRefreshed = 0, depsLoaded = 0;
        bool locReloaded = false, pckReloaded = false;
        bool alcCollectible = false;
        var sw = Stopwatch.StartNew();
        var stepTimings = new Dictionary<string, long>();
        long lastLap = 0;

        string modKey = session.ModKey;
        Assembly? assembly = null;
        string? assemblyName = null;

        // These track staged state so we can roll back if something goes wrong
        // before we've committed to the new assembly.
        var priorLoadContext = session.LoadContext;
        var priorHarmony = session.HotReloadHarmony;
        AssemblyLoadContext? stagedLoadContext = null;
        Harmony? stagedHarmony = null;
        bool sessionCommitted = false;
        SerializationCacheSnapshot? serializationSnapshot = null;
        List<Type> newModelTypes = [];
        var previousModAssemblyRefs = new List<(object mod, FieldInfo field, Assembly prev)>();

        // If anything goes wrong before we commit, undo the staged Harmony patches
        // and unload the collectible ALC so we don't leak state.
        void CleanupStaged()
        {
            if (sessionCommitted) return;
            if (stagedHarmony != null)
            {
                try { stagedHarmony.UnpatchAll(stagedHarmony.Id); }
                catch (Exception ex) { warnings.Add($"staged_harmony_cleanup: {ex.Message}"); }
                stagedHarmony = null;
            }
            if (stagedLoadContext != null)
            {
                UnloadCollectibleAlc(stagedLoadContext, warnings);
                stagedLoadContext = null;
            }
        }

        // Shorthand to build the result at any point (success or failure)
        HotReloadResult Finish() => new()
        {
            Success = errors.Count == 0,
            Tier = tier,
            AssemblyName = assemblyName,
            PatchCount = patchCount,
            EntitiesRemoved = entitiesRemoved,
            EntitiesInjected = entitiesInjected,
            EntitiesSkipped = entitiesSkipped,
            PoolsUnfrozen = poolsUnfrozen,
            PoolRegistrations = poolRegs,
            LocalizationReloaded = locReloaded,
            PckReloaded = pckReloaded,
            LiveInstancesRefreshed = liveRefreshed,
            MutableCheckPassed = mutableOk,
            MutableCheckFailed = mutableFailed,
            AlcCollectible = alcCollectible,
            TotalMs = sw.ElapsedMilliseconds,
            Timestamp = DateTime.UtcNow.ToString("o"),
            StepTimings = stepTimings,
            Actions = actions,
            Errors = errors,
            Warnings = warnings,
            ChangedEntities = changedEntities,
        };

        // ─── Validate input ──────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
        {
            errors.Add($"dll_not_found: {dllPath}");
            return Finish();
        }
        tier = Math.Clamp(tier, 1, 3);

        BaseLibMain.Logger.Info($"[HotReload] Starting tier {tier} reload from {dllPath}");

        // ═════════════════════════════════════════════════════════════
        // STEP 1: Load the new assembly
        // ═════════════════════════════════════════════════════════════
        // Tier 1 (patch-only) uses a collectible ALC so the old assembly can be
        // garbage collected. Tier 2+ uses the default ALC because cross-ALC type
        // identity breaks ModelDb entity injection and runtime type casts.
        // The caller's build must stamp a unique assembly name (e.g., MyMod_hr143052789)
        // so the default ALC accepts it even if a previous version is loaded.
        HotReloadEngine.CurrentProgress = "loading_assembly";
        try
        {
            var modDir = Path.GetDirectoryName(dllPath)!;
            var mainDllName = Path.GetFileNameWithoutExtension(dllPath);
            string[] sharedDlls = ["GodotSharp", "0Harmony", "sts2"];

            // Load dependency DLLs (NuGet packages, other mod libs) into default ALC.
            // These must live in the default context for shared type identity.
            // IMPORTANT: Skip the main mod DLL, framework DLLs, old stamped versions
            // of this mod, and any DLL that's already loaded.
            foreach (var depDll in Directory.GetFiles(modDir, "*.dll"))
            {
                var depName = Path.GetFileNameWithoutExtension(depDll);
                var depNormalized = AssemblyStamper.NormalizeModKey(depName);

                // Skip framework DLLs, the main mod DLL, and any old hot-reload
                // versions of this mod (stamped or unstamped)
                if (sharedDlls.Any(s => string.Equals(s, depName, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(depName, mainDllName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(depNormalized, modKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Also skip DLLs that look like old MCP-stamped versions of this mod
                // (e.g., HotReloadTest_132829197112.dll — no _hr prefix, just digits)
                if (depName.StartsWith(modKey, StringComparison.OrdinalIgnoreCase)
                    && depName.Length > modKey.Length
                    && depName[modKey.Length] == '_')
                    continue;

                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == depName);

                if (existing == null)
                {
                    try
                    {
                        // Load deps into the same ALC as the game so type identity is preserved
                        var depAlc = AssemblyLoadContext.GetLoadContext(typeof(AbstractModel).Assembly)
                            ?? AssemblyLoadContext.Default;
                        depAlc.LoadFromAssemblyPath(Path.GetFullPath(depDll));
                        depsLoaded++;
                    }
                    catch (Exception ex) { warnings.Add($"dep_load_{depName}: {ex.Message}"); }
                }
                else
                {
                    // Warn if the on-disk version doesn't match what's loaded —
                    // dependency changes require a game restart.
                    try
                    {
                        var onDisk = AssemblyName.GetAssemblyName(Path.GetFullPath(depDll)).Version;
                        var loaded = existing.GetName().Version;
                        if (onDisk != null && loaded != null && onDisk != loaded)
                            warnings.Add($"dep_stale_{depName}: loaded={loaded}, on_disk={onDisk}. Restart required for dep changes.");
                    }
                    catch { /* version check is best-effort */ }
                }
            }

            if (tier <= 1)
            {
                // Collectible ALC: can be unloaded later to reclaim memory.
                // Patches don't need type identity with ModelDb.
                try
                {
                    var alc = new AssemblyLoadContext($"HotReload-{DateTime.Now.Ticks}", isCollectible: true);
                    alc.Resolving += (_, name) =>
                        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name);
                    assembly = alc.LoadFromAssemblyPath(dllPath);
                    stagedLoadContext = alc;
                    alcCollectible = true;
                }
                catch (Exception ex)
                {
                    // Fall back to default ALC if collectible fails
                    warnings.Add($"collectible_alc_fallback: {ex.Message}");
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
                }
            }
            else
            {
                // Tier 2+: load into the SAME ALC as the game's sts2.dll so type identity
                // works (IsSubclassOf, is checks, etc.). Godot may use a custom ALC for mods
                // rather than the default, so we can't assume Default is correct.
                if (!_defaultAlcResolvingRegistered)
                {
                    AssemblyLoadContext.Default.Resolving += TypeSignatureHasher.DefaultAlcResolving;
                    _defaultAlcResolvingRegistered = true;
                }

                var targetAlc = AssemblyLoadContext.GetLoadContext(typeof(AbstractModel).Assembly)
                    ?? AssemblyLoadContext.Default;
                assembly = targetAlc.LoadFromAssemblyPath(dllPath);
            }

            assemblyName = assembly.FullName;
            actions.Add(alcCollectible ? "assembly_loaded_collectible" : "assembly_loaded");
            if (depsLoaded > 0)
                actions.Add($"dependencies_loaded:{depsLoaded}");
            BaseLibMain.Logger.Info($"[HotReload] Assembly: {assembly.FullName} (+{depsLoaded} deps, collectible={alcCollectible})");
        }
        catch (Exception ex)
        {
            errors.Add($"assembly_load: {ex.Message}");
            CleanupStaged();
            return Finish();
        }
        stepTimings["step1_assembly_load"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 2: Stage new Harmony patches
        // ═════════════════════════════════════════════════════════════
        // We create a new Harmony instance with a unique ID per reload so patches
        // from different reloads don't collide. The staged patches are applied now
        // but can be unpatched during rollback if entity injection fails later.
        HotReloadEngine.CurrentProgress = "patching_harmony";
        try
        {
            var harmonyId = $"baselib.hotreload.{modKey}.{Guid.NewGuid():N}";
            stagedHarmony = new Harmony(harmonyId);
            stagedHarmony.PatchAll(assembly);

            patchCount = Harmony.GetAllPatchedMethods()
                .Select(m => Harmony.GetPatchInfo(m))
                .Where(info => info != null)
                .Select(info => info!.Prefixes.Count(p => p.owner == harmonyId)
                              + info.Postfixes.Count(p => p.owner == harmonyId)
                              + info.Transpilers.Count(p => p.owner == harmonyId))
                .Sum();

            actions.Add("harmony_staged");
        }
        catch (Exception ex)
        {
            errors.Add($"harmony: {ex.Message}");
            CleanupStaged();
        }
        stepTimings["step2_harmony"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Tier 1 stops here (patch-only reload) ────────────────────
        if (tier < 2)
        {
            if (stagedHarmony != null)
            {
                CommitSession(session, stagedHarmony, stagedLoadContext, assembly, ref sessionCommitted);
                UnpatchPrevious(priorHarmony, stagedHarmony, actions, warnings);
                RemoveStalePatchesForMod(modKey, assembly, actions);
                if (priorLoadContext != null && !ReferenceEquals(priorLoadContext, stagedLoadContext))
                    UnloadCollectibleAlc(priorLoadContext, warnings);
                actions.Add("harmony_repatched");
            }
            else
            {
                errors.Add("harmony: no staged instance was created");
            }
            return Finish();
        }

        // ═════════════════════════════════════════════════════════════
        // STEP 3: Update Mod.assembly reference in ModManager
        // ═════════════════════════════════════════════════════════════
        // The game's ModManager tracks each mod's assembly. We swap it to the new
        // one so that ReflectionHelper.ModTypes picks up the new types.
        HotReloadEngine.CurrentProgress = "updating_mod_reference";
        try
        {
            var loadedModsField = typeof(ModManager).GetField("_mods", StaticNonPublic);
            if (loadedModsField?.GetValue(null) is IList loadedMods)
            {
                int updated = 0;
                foreach (var mod in loadedMods)
                {
                    var asmField = mod.GetType().GetField("assembly", BindingFlags.Public | BindingFlags.Instance);
                    if (asmField?.GetValue(mod) is not Assembly currentAsm) continue;
                    if (!string.Equals(AssemblyStamper.NormalizeModKey(currentAsm.GetName().Name), modKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    previousModAssemblyRefs.Add((mod, asmField, currentAsm));
                    asmField.SetValue(mod, assembly);
                    updated++;
                }
                if (updated > 0) actions.Add("mod_reference_updated");
            }
        }
        catch (Exception ex) { errors.Add($"mod_ref: {ex.Message}"); }
        stepTimings["step3_mod_reference"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 4: Invalidate ReflectionHelper._modTypes cache
        // ═════════════════════════════════════════════════════════════
        // The game caches the list of mod types. Nulling it forces a rebuild
        // that includes types from the new assembly.
        HotReloadEngine.CurrentProgress = "invalidating_reflection_cache";
        try
        {
            typeof(ReflectionHelper).GetField("_modTypes", StaticNonPublic)?.SetValue(null, null);
            actions.Add("reflection_cache_invalidated");
        }
        catch (Exception ex) { errors.Add($"reflection_cache: {ex.Message}"); }
        stepTimings["step4_reflection_cache"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 5: Register new entity IDs in ModelIdSerializationCache
        // ═════════════════════════════════════════════════════════════
        // The serialization cache maps category/entry names to net IDs for multiplayer.
        // It's built at boot time, so our new entities aren't in it. We must register
        // them BEFORE constructing any ModelId objects. We also snapshot the cache so
        // we can roll back if entity injection fails.
        HotReloadEngine.CurrentProgress = "registering_entity_ids";

        // Collect AbstractModel subtypes sorted by injection priority:
        // Powers first (cards reference them via PowerVar<T>), events last.
        newModelTypes = TypeSignatureHasher.GetLoadableTypes(assembly, warnings, "new_assembly_types")
            .Where(t => !t.IsAbstract && !t.IsInterface && TypeSignatureHasher.InheritsFromByName(t, nameof(AbstractModel)))
            .OrderBy(TypeSignatureHasher.GetInjectionPriority)
            .ToList();

        try
        {
            var cacheType = typeof(ModelId).Assembly.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache");
            if (cacheType != null)
            {
                serializationSnapshot = SerializationCacheSnapshot.Capture(cacheType);
                var categoryMap = cacheType.GetField("_categoryNameToNetIdMap", StaticNonPublic)?.GetValue(null) as Dictionary<string, int>;
                var categoryList = cacheType.GetField("_netIdToCategoryNameMap", StaticNonPublic)?.GetValue(null) as List<string>;
                var entryMap = cacheType.GetField("_entryNameToNetIdMap", StaticNonPublic)?.GetValue(null) as Dictionary<string, int>;
                var entryList = cacheType.GetField("_netIdToEntryNameMap", StaticNonPublic)?.GetValue(null) as List<string>;

                int registered = 0;
                foreach (var newType in newModelTypes)
                {
                    var (category, entry) = TypeSignatureHasher.GetCategoryAndEntry(newType);
                    if (categoryMap != null && categoryList != null && !categoryMap.ContainsKey(category))
                    {
                        categoryMap[category] = categoryList.Count;
                        categoryList.Add(category);
                    }
                    if (entryMap != null && entryList != null && !entryMap.ContainsKey(entry))
                    {
                        entryMap[entry] = entryList.Count;
                        entryList.Add(entry);
                        registered++;
                    }
                }
                if (registered > 0)
                {
                    // Update bit sizes so network serialization doesn't truncate
                    var catBitProp = cacheType.GetProperty("CategoryIdBitSize", StaticPublic);
                    var entBitProp = cacheType.GetProperty("EntryIdBitSize", StaticPublic);
                    if (catBitProp?.SetMethod != null && categoryList != null)
                        catBitProp.SetValue(null, TypeSignatureHasher.ComputeBitSize(categoryList.Count));
                    if (entBitProp?.SetMethod != null && entryList != null)
                        entBitProp.SetValue(null, TypeSignatureHasher.ComputeBitSize(entryList.Count));
                    actions.Add($"serialization_cache_updated:{registered}");
                }
            }
        }
        catch (Exception ex) { warnings.Add($"serialization_cache: {ex.Message}"); }
        stepTimings["step5_serialization_cache"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 6: Transactionally replace entities in ModelDb
        // ═════════════════════════════════════════════════════════════
        // This is the heart of hot reload. We snapshot the current ModelDb state,
        // compute signature hashes to detect which types actually changed, create
        // new instances for changed types, and commit them atomically. If anything
        // fails during staging, we roll back the entire ModelDb to its pre-reload state.
        HotReloadEngine.CurrentProgress = "reloading_entities";
        var entitySnapshot = new Dictionary<ModelId, AbstractModel>();
        try
        {
            var contentByIdField = typeof(ModelDb).GetField("_contentById", StaticNonPublic);
            var typedDict = contentByIdField?.GetValue(null) as Dictionary<ModelId, AbstractModel>
                ?? throw new InvalidOperationException("ModelDb._contentById not found");

            // Snapshot everything we might touch
            var affectedIds = new HashSet<ModelId>();
            var oldTypeSignatures = new Dictionary<string, int>(StringComparer.Ordinal);
            var removedTypeNames = new Dictionary<ModelId, string>();

            // Find entities from previous versions of this mod's assembly
            foreach (var oldAssembly in TypeSignatureHasher.GetAssembliesForMod(modKey, assembly))
            {
                foreach (var oldType in TypeSignatureHasher.GetLoadableTypes(oldAssembly, warnings, $"old_assembly_types:{oldAssembly.FullName}")
                    .Where(t => !t.IsAbstract && !t.IsInterface && TypeSignatureHasher.InheritsFromByName(t, nameof(AbstractModel))))
                {
                    try
                    {
                        var id = TypeSignatureHasher.BuildModelId(oldType);
                        affectedIds.Add(id);
                        removedTypeNames[id] = oldType.Name;
                        if (typedDict.TryGetValue(id, out var existing))
                            entitySnapshot[id] = existing;
                        oldTypeSignatures[oldType.FullName ?? oldType.Name] = TypeSignatureHasher.ComputeHash(oldType);
                    }
                    catch (Exception ex) { warnings.Add($"snapshot_{oldType.Name}: {ex.Message}"); }
                }
            }

            // Also snapshot any new type IDs in case they're already there somehow
            foreach (var newType in newModelTypes)
            {
                try
                {
                    var id = TypeSignatureHasher.BuildModelId(newType);
                    affectedIds.Add(id);
                    if (typedDict.TryGetValue(id, out var existing))
                        entitySnapshot[id] = existing;
                }
                catch (Exception ex) { warnings.Add($"snapshot_{newType.Name}: {ex.Message}"); }
            }

            // ── Clean up old registrations BEFORE creating new instances ──
            // Two reasons this must happen first:
            //   1. The game's AbstractModel constructor throws DuplicateModelException if a
            //      canonical model of the same type already exists in ModelDb._contentById.
            //      We must remove old entries before Activator.CreateInstance().
            //   2. BaseLib entity constructors auto-register in pools via
            //      CustomContentDictionary.AddModel(). Old pool entries must be gone first.
            foreach (var oldAssembly in TypeSignatureHasher.GetAssembliesForMod(modKey, assembly))
            {
                CustomContentDictionary.RemoveByAssembly(oldAssembly);
                ModelDbSharedCardPoolsPatch.RemoveByAssembly(oldAssembly);
                ModelDbSharedRelicPoolsPatch.RemoveByAssembly(oldAssembly);
                ModelDbSharedPotionPoolsPatch.RemoveByAssembly(oldAssembly);
            }

            // Remove all affected entities from ModelDb BEFORE creating new instances
            int removed = 0;
            foreach (var id in affectedIds)
            {
                if (typedDict.Remove(id))
                    removed++;
            }
            entitiesRemoved = removed;

            // Log entities that won't be re-injected (type was deleted from new assembly)
            foreach (var (id, removedName) in removedTypeNames)
            {
                if (!newModelTypes.Any(t => TypeSignatureHasher.BuildModelId(t).Equals(id)))
                    changedEntities.Add(new ChangedEntity { Name = removedName, Action = "removed" });
            }

            // Now create new entity instances — safe because old ones are gone from ModelDb
            var stagedModels = new Dictionary<ModelId, AbstractModel>();
            foreach (var newType in newModelTypes)
            {
                try
                {
                    var id = TypeSignatureHasher.BuildModelId(newType);
                    var fullName = newType.FullName ?? newType.Name;

                    // Incremental: skip types whose signature hash hasn't changed
                    if (oldTypeSignatures.TryGetValue(fullName, out var oldHash)
                        && entitySnapshot.TryGetValue(id, out var existingModel)
                        && TypeSignatureHasher.ComputeHash(newType) == oldHash)
                    {
                        stagedModels[id] = existingModel;
                        entitiesSkipped++;
                        changedEntities.Add(new ChangedEntity { Name = newType.Name, Action = "unchanged" });
                        continue;
                    }

                    // Create a fresh instance — this triggers BaseLib constructor auto-registration
                    // (CustomContentDictionary.AddModel → ModHelper.AddModelToPool)
                    var instance = Activator.CreateInstance(newType);
                    if (instance is not AbstractModel model)
                        throw new InvalidOperationException($"{newType.FullName} is not assignable to AbstractModel at runtime");

                    model.InitId(id);
                    stagedModels[id] = model;
                    entitiesInjected++;
                    changedEntities.Add(new ChangedEntity { Name = newType.Name, Action = "injected", Id = id.ToString() });
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    errors.Add($"inject_{newType.Name}: {inner.GetType().Name}: {inner.Message}");
                    BaseLibMain.Logger.Error($"[HotReload] Failed to stage {newType.Name}: {inner}");
                }
            }

            // If any entity failed to stage, abort the entire commit
            if (errors.Any(e => e.StartsWith("inject_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Entity staging failed; ModelDb changes were not committed.");

            // ── Insert staged models into ModelDb ────────────────
            // Old entities were already removed above (before Activator.CreateInstance).
            foreach (var (id, model) in stagedModels)
                typedDict[id] = model;

            actions.Add("entities_reregistered");
            if (entitiesSkipped > 0) actions.Add($"entities_unchanged:{entitiesSkipped}");
        }
        catch (Exception ex)
        {
            // ── ROLLBACK: restore ModelDb to pre-reload state ────────
            errors.Add($"entity_reload: {ex.Message}");
            BaseLibMain.Logger.Error($"[HotReload] Entity reload error, rolling back: {ex}");

            try
            {
                var contentByIdField = typeof(ModelDb).GetField("_contentById", StaticNonPublic);
                if (contentByIdField?.GetValue(null) is Dictionary<ModelId, AbstractModel> rollbackDict)
                    RestoreEntitySnapshot(rollbackDict, entitySnapshot, modKey);
            }
            catch (Exception rbEx) { errors.Add($"rollback_entities: {rbEx.Message}"); }

            // Restore Mod.assembly references to previous values
            foreach (var (modRef, field, prev) in previousModAssemblyRefs)
            {
                try { field.SetValue(modRef, prev); }
                catch (Exception rbEx) { warnings.Add($"rollback_mod_ref: {rbEx.Message}"); }
            }

            // Re-invalidate type cache so it rebuilds without new assembly
            try { typeof(ReflectionHelper).GetField("_modTypes", StaticNonPublic)?.SetValue(null, null); }
            catch { /* best effort */ }

            serializationSnapshot?.Restore();
            CleanupStaged();
            return Finish();
        }
        stepTimings["step6_entity_reload"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 7: Null ModelDb cached enumerables
        // ═════════════════════════════════════════════════════════════
        // ModelDb lazily caches collections like AllCards, AllRelics, etc.
        // We null them so the next access re-enumerates from _contentById
        // and picks up our newly injected entities.
        HotReloadEngine.CurrentProgress = "clearing_modeldb_caches";
        try
        {
            string[] cacheFields =
            [
                "_allCards", "_allCardPools", "_allCharacterCardPools",
                "_allSharedEvents", "_allEvents", "_allEncounters", "_allPotions",
                "_allPotionPools", "_allCharacterPotionPools", "_allSharedPotionPools",
                "_allPowers", "_allRelics", "_allCharacterRelicPools", "_achievements"
            ];
            foreach (var fieldName in cacheFields)
                typeof(ModelDb).GetField(fieldName, StaticNonPublic)?.SetValue(null, null);
            actions.Add("modeldb_caches_cleared");
        }
        catch (Exception ex) { errors.Add($"modeldb_caches: {ex.Message}"); }
        stepTimings["step7_modeldb_caches"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 8: Unfreeze pools + null pool instance caches
        // ═════════════════════════════════════════════════════════════
        // ModHelper._moddedContentForPools entries have an isFrozen flag that blocks
        // new registrations. We unfreeze them, remove entries belonging to the old
        // assembly, and then null the lazy caches on pool model instances.
        //
        // NOTE: We don't need to explicitly re-register pool entries here!
        // Step 6's Activator.CreateInstance() already triggered BaseLib's
        // constructor auto-registration (CustomContentDictionary.AddModel →
        // ModHelper.AddModelToPool). This is the big simplification vs the bridge.
        HotReloadEngine.CurrentProgress = "refreshing_pools";
        try
        {
            var result = UnfreezeAndCleanPools(assembly, modKey, warnings);
            poolsUnfrozen = result.unfrozen;
            actions.Add("pools_refreshed");
        }
        catch (Exception ex) { errors.Add($"pool_refresh: {ex.Message}"); }
        stepTimings["step8_pools"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 8.5: Refresh BaseLib subsystems
        // ═════════════════════════════════════════════════════════════
        // This is NEW — the bridge mod couldn't do this because it doesn't have
        // access to BaseLib internals. We re-run PostModInit processing (ModInterop,
        // SavedProperty, SavedSpireField) and custom enum generation for the new types.
        HotReloadEngine.CurrentProgress = "refreshing_baselib_subsystems";
        try
        {
            var newTypes = TypeSignatureHasher.GetLoadableTypes(assembly).ToList();

            // Clean up old registrations from all previous versions of this mod
            foreach (var oldAsm in TypeSignatureHasher.GetAssembliesForMod(modKey, assembly))
            {
                SavedSpireFieldPatch.RemoveByAssembly(oldAsm);
                ModelDbCustomCharacters.RemoveByAssembly(oldAsm);
                CustomOrbModel.RemoveByAssembly(oldAsm);
            }

            // Re-run PostModInit logic: ModInterop, SavedProperty, SavedSpireField
            PostModInitPatch.ProcessTypes(newTypes);
            SavedSpireFieldPatch.AddFieldsSorted();

            // Re-generate custom enum values (skips already-generated fields via dedup)
            GenEnumValues.GenerateForTypes(newTypes);

            actions.Add("baselib_subsystems_refreshed");
        }
        catch (Exception ex) { warnings.Add($"baselib_subsystems: {ex.Message}"); }
        stepTimings["step8_5_baselib"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 9: Reload localization
        // ═════════════════════════════════════════════════════════════
        // Two-part: reload file-based loc tables (SetLanguage), then re-inject
        // ILocalizationProvider entries from our newly injected entities.
        // The bridge just calls SetLanguage and hopes for the best — we can do better
        // because we own ModelLocPatch and can call it directly.
        HotReloadEngine.CurrentProgress = "reloading_localization";
        try
        {
            var locManager = LocManager.Instance;
            if (locManager != null)
            {
                // Reload file-based localization tables
                locManager.SetLanguage(locManager.Language);

                // Re-inject ILocalizationProvider entries for ALL entities (not just
                // the reloaded mod) because SetLanguage wipes the loc tables clean.
                var contentByIdField = typeof(ModelDb).GetField("_contentById", StaticNonPublic);
                if (contentByIdField?.GetValue(null) is Dictionary<ModelId, AbstractModel> contentById)
                    ModelLocPatch.InjectLocalization(contentById);

                locReloaded = true;
                actions.Add("localization_reloaded");
            }
        }
        catch (Exception ex) { errors.Add($"localization: {ex.Message}"); }
        stepTimings["step9_localization"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 10: Remount PCK (tier 3 only)
        // ═════════════════════════════════════════════════════════════
        // Godot resources (scenes, textures, etc.) live in PCK files.
        // LoadResourcePack overlays new data onto the virtual filesystem.
        HotReloadEngine.CurrentProgress = "remounting_pck";
        if (tier >= 3 && !string.IsNullOrEmpty(pckPath) && File.Exists(pckPath))
        {
            try
            {
                if (ProjectSettings.LoadResourcePack(pckPath))
                {
                    pckReloaded = true;
                    actions.Add("pck_remounted");
                    // Re-trigger loc to pick up new PCK loc files
                    try { LocManager.Instance?.SetLanguage(LocManager.Instance.Language); } catch { }
                }
                else
                {
                    errors.Add($"pck_load_failed: Godot returned false for {pckPath}");
                }
            }
            catch (Exception ex) { errors.Add($"pck: {ex.Message}"); }
        }
        else if (tier >= 3 && string.IsNullOrEmpty(pckPath))
        {
            warnings.Add("Tier 3 requested but no pck_path provided");
        }

        if (!alcCollectible)
            warnings.Add("Old assembly loaded into default ALC (non-collectible); memory will accumulate");
        stepTimings["step10_pck"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 11: Verify injected entities exist in ModelDb
        // ═════════════════════════════════════════════════════════════
        // Sanity check: make sure everything we staged actually landed in ModelDb.
        // Also test ToMutable() on cards to catch PowerVar<T> resolution failures
        // early (e.g., when a card references a power that wasn't injected).
        HotReloadEngine.CurrentProgress = "verifying_entities";
        if (entitiesInjected > 0)
        {
            try
            {
                foreach (var type in newModelTypes)
                {
                    if (ModelDb.Contains(type))
                        verified++;
                    else
                        verifyFailed++;
                }
                if (verifyFailed > 0)
                    warnings.Add($"verify: {verifyFailed}/{newModelTypes.Count} injected types missing from ModelDb");
                else
                    actions.Add($"verified:{verified}_entities_in_modeldb");

                // ToMutable check on cards
                var contentByIdField = typeof(ModelDb).GetField("_contentById", StaticNonPublic);
                if (contentByIdField?.GetValue(null) is Dictionary<ModelId, AbstractModel> typedDict)
                {
                    foreach (var cardType in newModelTypes.Where(t => TypeSignatureHasher.InheritsFromByName(t, nameof(CardModel))))
                    {
                        try
                        {
                            var id = TypeSignatureHasher.BuildModelId(cardType);
                            if (typedDict.TryGetValue(id, out var cardModel) && cardModel is CardModel card)
                            {
                                card.ToMutable();
                                mutableOk++;
                            }
                        }
                        catch (Exception ex)
                        {
                            mutableFailed++;
                            var inner = ex.InnerException ?? ex;
                            warnings.Add($"ToMutable_{cardType.Name}: {inner.GetType().Name}: {inner.Message}");
                        }
                    }
                    if (mutableOk > 0) actions.Add($"mutable_check_passed:{mutableOk}");
                    if (mutableFailed > 0) actions.Add($"mutable_check_failed:{mutableFailed}");
                }
            }
            catch (Exception ex) { warnings.Add($"verify: {ex.Message}"); }
        }
        stepTimings["step11_verify"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ═════════════════════════════════════════════════════════════
        // STEP 12: Refresh live instances in scene tree and run state
        // ═════════════════════════════════════════════════════════════
        // Walk the Godot scene tree and swap Model properties on card/relic/power/
        // potion/creature nodes. Then walk the current run's player state and replace
        // mutable instances in the deck, combat piles, relics, potions, and powers.
        HotReloadEngine.CurrentProgress = "refreshing_live_instances";
        if (entitiesInjected > 0)
        {
            try
            {
                liveRefreshed = LiveInstanceRefresher.RefreshSceneTree();
                if (liveRefreshed > 0) actions.Add($"live_instances_refreshed:{liveRefreshed}");
            }
            catch (Exception ex) { warnings.Add($"live_refresh: {ex.Message}"); }

            try
            {
                int runRefreshed = LiveInstanceRefresher.RefreshRunInstances(assembly, modKey);
                if (runRefreshed > 0)
                {
                    liveRefreshed += runRefreshed;
                    actions.Add($"run_instances_refreshed:{runRefreshed}");
                }
            }
            catch (Exception ex) { warnings.Add($"run_refresh: {ex.Message}"); }
        }
        stepTimings["step12_live_refresh"] = sw.ElapsedMilliseconds - lastLap;

        // ═════════════════════════════════════════════════════════════
        // FINALIZE: Commit session, clean up old patches
        // ═════════════════════════════════════════════════════════════
        try
        {
            if (stagedHarmony != null)
            {
                CommitSession(session, stagedHarmony, stagedLoadContext, assembly, ref sessionCommitted);
                actions.Add("harmony_repatched");
            }
            UnpatchPrevious(priorHarmony, stagedHarmony, actions, warnings);
            RemoveStalePatchesForMod(modKey, assembly, actions);
            if (priorLoadContext != null && !ReferenceEquals(priorLoadContext, stagedLoadContext))
                UnloadCollectibleAlc(priorLoadContext, warnings);
        }
        catch (Exception ex) { warnings.Add($"session_commit: {ex.Message}"); }

        BaseLibMain.Logger.Info($"[HotReload] {(errors.Count == 0 ? "Complete" : "Failed")} — " +
            $"{entitiesInjected} entities, {patchCount} patches, {liveRefreshed} live ({sw.ElapsedMilliseconds}ms)");

        return Finish();
    }

    // ─── Helper methods ─────────────────────────────────────────────────

    private static void CommitSession(
        HotReloadSession session, Harmony harmony, AssemblyLoadContext? alc,
        Assembly? assembly, ref bool committed)
    {
        session.HotReloadHarmony = harmony;
        session.LoadContext = alc;
        session.LastLoadedAssembly = assembly;
        committed = true;
    }

    /// <summary>
    /// Unpatch the previous Harmony instance for this mod (the one from the last reload).
    /// </summary>
    private static void UnpatchPrevious(
        Harmony? prior, Harmony? staged, List<string> actions, List<string> warnings)
    {
        if (prior == null || staged == null || prior.Id == staged.Id) return;
        try
        {
            prior.UnpatchAll(prior.Id);
            actions.Add("previous_harmony_unpatched");
        }
        catch (Exception ex) { warnings.Add($"previous_harmony_unpatch: {ex.Message}"); }
    }

    /// <summary>
    /// Scan all patched methods and remove any patches that came from old versions
    /// of this mod's assembly (not the current one). This catches patches that were
    /// applied by the mod's Init() at game boot but are now stale.
    /// </summary>
    private static void RemoveStalePatchesForMod(string modKey, Assembly? currentAssembly, List<string> actions)
    {
        int staleRemoved = 0;
        foreach (var method in Harmony.GetAllPatchedMethods().ToList())
        {
            var info = Harmony.GetPatchInfo(method);
            if (info == null) continue;

            foreach (var patch in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers))
            {
                if (patch.PatchMethod?.DeclaringType?.Assembly is not Assembly patchAsm) continue;
                if (patchAsm == currentAssembly) continue;
                if (!string.Equals(AssemblyStamper.NormalizeModKey(patchAsm.GetName().Name), modKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    new Harmony(patch.owner).Unpatch(method, patch.PatchMethod);
                    staleRemoved++;
                }
                catch { /* best effort */ }
            }
        }
        if (staleRemoved > 0) actions.Add($"stale_patches_removed:{staleRemoved}");
    }

    /// <summary>
    /// Unfreeze ModHelper._moddedContentForPools and null the lazy caches on
    /// pool model instances (CardPoolModel._allCards, etc.). This is game code
    /// so we have to use reflection.
    /// </summary>
    private static (int unfrozen, int registered) UnfreezeAndCleanPools(
        Assembly newAssembly, string modKey, List<string> warnings)
    {
        int unfrozen = 0;

        // Build set of type names from old + new assembly for targeted cleanup
        var reloadedTypeNames = new HashSet<string>(
            TypeSignatureHasher.GetLoadableTypes(newAssembly, warnings, "pool_type_scan")
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Select(t => t.FullName ?? t.Name));
        foreach (var oldAsm in TypeSignatureHasher.GetAssembliesForMod(modKey, newAssembly))
        {
            foreach (var t in TypeSignatureHasher.GetLoadableTypes(oldAsm).Where(t => !t.IsAbstract && !t.IsInterface))
                reloadedTypeNames.Add(t.FullName ?? t.Name);
        }

        // Unfreeze and remove old entries from ModHelper._moddedContentForPools
        var poolsField = typeof(ModHelper).GetField("_moddedContentForPools", StaticNonPublic);
        if (poolsField?.GetValue(null) is IDictionary pools)
        {
            foreach (var key in pools.Keys.Cast<object>().ToList())
            {
                var content = pools[key];
                if (content == null) continue;
                var contentType = content.GetType();

                // Unfreeze so ModHelper.AddModelToPool works
                var frozenField = contentType.GetField("isFrozen");
                if (frozenField != null && (bool)frozenField.GetValue(content)!)
                {
                    frozenField.SetValue(content, false);
                    unfrozen++;
                }

                // Remove only entries belonging to the reloaded mod
                if (contentType.GetField("modelsToAdd")?.GetValue(content) is IList modelsList)
                {
                    for (int i = modelsList.Count - 1; i >= 0; i--)
                    {
                        var entry = modelsList[i];
                        if (entry != null)
                        {
                            var typeName = entry.GetType().FullName ?? entry.GetType().Name;
                            if (reloadedTypeNames.Contains(typeName))
                                modelsList.RemoveAt(i);
                        }
                    }
                }
            }
        }

        // Null lazy caches on pool model instances so they re-enumerate
        NullPoolInstanceCaches(typeof(CardPoolModel), "_allCards", "_allCardIds");
        NullPoolInstanceCaches(typeof(RelicPoolModel), "_relics", "_allRelicIds");
        NullPoolInstanceCaches(typeof(PotionPoolModel), "_allPotions", "_allPotionIds");

        return (unfrozen, 0); // registered count comes from constructor auto-registration
    }

    /// <summary>
    /// Null the lazy-computed fields on all instances of a pool base type.
    /// Pool instances are singletons stored in ModelDb._contentById.
    /// </summary>
    private static void NullPoolInstanceCaches(Type basePoolType, params string[] fieldNames)
    {
        var contentByIdField = typeof(ModelDb).GetField("_contentById", StaticNonPublic);
        if (contentByIdField?.GetValue(null) is not IDictionary contentById) return;

        foreach (var value in contentById.Values)
        {
            if (value == null || !basePoolType.IsAssignableFrom(value.GetType())) continue;
            foreach (var fieldName in fieldNames)
            {
                var field = value.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? basePoolType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(value, null);
            }
        }
    }

    private static void RestoreEntitySnapshot(
        Dictionary<ModelId, AbstractModel> target,
        Dictionary<ModelId, AbstractModel> snapshot,
        string modKey)
    {
        // Remove any entries that were injected from the new assembly
        var snapshotIds = snapshot.Keys.ToHashSet();
        foreach (var id in target.Keys.ToList())
        {
            if (snapshotIds.Contains(id)) continue;
            if (target[id] is AbstractModel model
                && string.Equals(AssemblyStamper.NormalizeModKey(model.GetType().Assembly.GetName().Name), modKey, StringComparison.OrdinalIgnoreCase))
                target.Remove(id);
        }

        // Put back the originals
        foreach (var (id, model) in snapshot)
            target[id] = model;
    }

    private static void UnloadCollectibleAlc(AssemblyLoadContext? alc, List<string> warnings)
    {
        if (alc == null) return;
        try
        {
            alc.Unload();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch (Exception ex) { warnings.Add($"alc_unload: {ex.Message}"); }
    }
}
