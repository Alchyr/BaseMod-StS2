using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.HotReload;

/// <summary>
/// Self-tests for the hot reload system, following the same pattern as NodeFactory's self-tests.
///
/// Two phases:
///   1. RunStartupTests() — called from HotReloadEngine.Init() at mod load time.
///      Tests pure helper logic and verifies game reflection targets exist.
///      These run early, before ModelDb is populated, so they can't test the full pipeline.
///
///   2. RunIntegrationTests() — called from the "hotreload_test" console command.
///      Tests the full pipeline with a real assembly build, deploy, and reload.
///      Requires the game to be fully loaded (ModelDb populated, LocManager ready).
/// </summary>
internal static class HotReloadSelfTests
{
    private static int _passed;
    private static int _failed;

    private static void Assert(bool condition, string testName)
    {
        if (condition)
            _passed++;
        else
        {
            _failed++;
            BaseLibMain.Logger.Error($"[HotReload] FAIL: {testName}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 1: Startup tests — pure logic + reflection target existence
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Run at mod init time. Tests helper functions and verifies that every private
    /// field/property we access via reflection actually exists in the current game version.
    /// If these fail, the hot reload pipeline WILL break at runtime.
    /// </summary>
    public static void RunStartupTests()
    {
        _passed = 0;
        _failed = 0;

        TestAssemblyStamperNormalize();
        TestAssemblyStamperIsStamped();
        TestComputeBitSize();
        TestInheritsFromByName();
        TestGetInjectionPriority();
        TestGetLoadableTypes();
        TestTypeSignatureHashDeterminism();
        TestTypeSignatureHashChangesOnModification();
        TestHotReloadResultSummary();
        TestSessionCreation();

        // These are the critical ones — if any fail, the pipeline will crash at runtime
        TestReflectionTargets_ModelDb();
        TestReflectionTargets_ModManager();
        TestReflectionTargets_ReflectionHelper();
        TestReflectionTargets_ModHelper();
        TestReflectionTargets_SerializationCache();
        TestReflectionTargets_LocTable();
        TestReflectionTargets_PoolModels();

        if (_failed == 0)
            BaseLibMain.Logger.Info($"[HotReload] All {_passed} startup self-tests passed");
        else
            BaseLibMain.Logger.Error($"[HotReload] Startup self-tests: {_passed} passed, {_failed} FAILED");

        _passed = 0;
        _failed = 0;
    }

    // ── AssemblyStamper tests ────────────────────────────────────────

    private static void TestAssemblyStamperNormalize()
    {
        // Normal mod name — no change
        Assert(AssemblyStamper.NormalizeModKey("MyMod") == "MyMod",
            "NormalizeModKey: plain name unchanged");

        // Hot-reload stamped name — strip suffix
        Assert(AssemblyStamper.NormalizeModKey("MyMod_hr143052789") == "MyMod",
            "NormalizeModKey: strips _hr9 suffix");

        // 6-digit stamp
        Assert(AssemblyStamper.NormalizeModKey("MyMod_hr143052") == "MyMod",
            "NormalizeModKey: strips _hr6 suffix");

        // Full DLL path
        Assert(AssemblyStamper.NormalizeModKey("E:/mods/MyMod/MyMod_hr143052789.dll") == "MyMod",
            "NormalizeModKey: handles full DLL path");

        // Null and empty
        Assert(AssemblyStamper.NormalizeModKey(null) == "",
            "NormalizeModKey: null returns empty");
        Assert(AssemblyStamper.NormalizeModKey("") == "",
            "NormalizeModKey: empty returns empty");
        Assert(AssemblyStamper.NormalizeModKey("  ") == "",
            "NormalizeModKey: whitespace returns empty");

        // Name with underscores that aren't _hr stamps
        Assert(AssemblyStamper.NormalizeModKey("My_Mod_Name") == "My_Mod_Name",
            "NormalizeModKey: preserves non-stamp underscores");

        // Short _hr suffix (too few digits — shouldn't strip)
        Assert(AssemblyStamper.NormalizeModKey("MyMod_hr12345") == "MyMod_hr12345",
            "NormalizeModKey: doesn't strip <6 digit suffix");
    }

    private static void TestAssemblyStamperIsStamped()
    {
        Assert(AssemblyStamper.IsHotReloadStamped("MyMod_hr143052789.dll"),
            "IsHotReloadStamped: 9-digit stamp recognized");
        Assert(AssemblyStamper.IsHotReloadStamped("MyMod_hr143052.dll"),
            "IsHotReloadStamped: 6-digit stamp recognized");
        Assert(!AssemblyStamper.IsHotReloadStamped("MyMod.dll"),
            "IsHotReloadStamped: unstamped rejected");
        Assert(!AssemblyStamper.IsHotReloadStamped("0Harmony.dll"),
            "IsHotReloadStamped: framework DLL rejected");
        Assert(AssemblyStamper.IsHotReloadStamped("E:/mods/MyMod/MyMod_hr143052789.dll"),
            "IsHotReloadStamped: full path with stamp recognized");
    }

    // ── TypeSignatureHasher tests ────────────────────────────────────

    private static void TestComputeBitSize()
    {
        Assert(TypeSignatureHasher.ComputeBitSize(0) == 0, "ComputeBitSize: 0 → 0");
        Assert(TypeSignatureHasher.ComputeBitSize(1) == 0, "ComputeBitSize: 1 → 0");
        Assert(TypeSignatureHasher.ComputeBitSize(2) == 1, "ComputeBitSize: 2 → 1");
        Assert(TypeSignatureHasher.ComputeBitSize(3) == 2, "ComputeBitSize: 3 → 2");
        Assert(TypeSignatureHasher.ComputeBitSize(4) == 2, "ComputeBitSize: 4 → 2");
        Assert(TypeSignatureHasher.ComputeBitSize(256) == 8, "ComputeBitSize: 256 → 8");
    }

    private static void TestInheritsFromByName()
    {
        // CardModel inherits from AbstractModel (via the game's type hierarchy)
        Assert(TypeSignatureHasher.InheritsFromByName(typeof(CardModel), nameof(AbstractModel)),
            "InheritsFromByName: CardModel → AbstractModel");

        Assert(TypeSignatureHasher.InheritsFromByName(typeof(RelicModel), nameof(AbstractModel)),
            "InheritsFromByName: RelicModel → AbstractModel");

        // CardModel does NOT inherit from RelicModel
        Assert(!TypeSignatureHasher.InheritsFromByName(typeof(CardModel), nameof(RelicModel)),
            "InheritsFromByName: CardModel !→ RelicModel");

        // object does not inherit from anything by this check
        Assert(!TypeSignatureHasher.InheritsFromByName(typeof(object), nameof(AbstractModel)),
            "InheritsFromByName: object !→ AbstractModel");
    }

    private static void TestGetInjectionPriority()
    {
        // Powers should come before cards (cards reference powers via PowerVar<T>)
        int powerPri = TypeSignatureHasher.GetInjectionPriority(typeof(PowerModel));
        int cardPri = TypeSignatureHasher.GetInjectionPriority(typeof(CardModel));
        Assert(powerPri < cardPri,
            $"InjectionPriority: PowerModel ({powerPri}) < CardModel ({cardPri})");

        // Monsters should come before encounters
        int monsterPri = TypeSignatureHasher.GetInjectionPriority(typeof(MonsterModel));
        int encounterPri = TypeSignatureHasher.GetInjectionPriority(typeof(EncounterModel));
        Assert(monsterPri < encounterPri,
            $"InjectionPriority: MonsterModel ({monsterPri}) < EncounterModel ({encounterPri})");

        // Unknown type gets high priority (injected last, least likely to break things)
        int unknownPri = TypeSignatureHasher.GetInjectionPriority(typeof(string));
        Assert(unknownPri > cardPri,
            $"InjectionPriority: unknown ({unknownPri}) > CardModel ({cardPri})");
    }

    private static void TestGetLoadableTypes()
    {
        // Should successfully return types from our own assembly
        var types = TypeSignatureHasher.GetLoadableTypes(typeof(HotReloadSelfTests).Assembly).ToList();
        Assert(types.Count > 0,
            "GetLoadableTypes: returns types from BaseLib assembly");
        Assert(types.Contains(typeof(HotReloadSelfTests)),
            "GetLoadableTypes: contains our own type");
    }

    private static void TestTypeSignatureHashDeterminism()
    {
        // Same type should always produce the same hash
        int hash1 = TypeSignatureHasher.ComputeHash(typeof(CardModel));
        int hash2 = TypeSignatureHasher.ComputeHash(typeof(CardModel));
        Assert(hash1 == hash2,
            "TypeSignatureHash: deterministic for same type");

        // Different types should produce different hashes
        int cardHash = TypeSignatureHasher.ComputeHash(typeof(CardModel));
        int relicHash = TypeSignatureHasher.ComputeHash(typeof(RelicModel));
        Assert(cardHash != relicHash,
            "TypeSignatureHash: different types produce different hashes");
    }

    private static void TestTypeSignatureHashChangesOnModification()
    {
        // This tests that types from different assemblies with the same name would
        // produce different hashes (simulated by comparing two different game types).
        // In real hot reload, the old and new assembly would have the same type names
        // but different IL, which is what we're detecting.
        int hash1 = TypeSignatureHasher.ComputeHash(typeof(PowerModel));
        int hash2 = TypeSignatureHasher.ComputeHash(typeof(PotionModel));
        Assert(hash1 != hash2,
            "TypeSignatureHash: structurally different types have different hashes");
    }

    // ── HotReloadResult tests ────────────────────────────────────────

    private static void TestHotReloadResultSummary()
    {
        var success = new HotReloadResult
        {
            Success = true, Tier = 2, EntitiesInjected = 5, PatchCount = 3,
            LiveInstancesRefreshed = 10, TotalMs = 150,
            Errors = [], Actions = [],
        };
        Assert(success.Summary.Contains("OK"), "HotReloadResult.Summary: success contains 'OK'");
        Assert(success.Summary.Contains("5 entities"), "HotReloadResult.Summary: success shows entity count");

        var failure = new HotReloadResult
        {
            Success = false, Errors = ["assembly_load: file not found"],
        };
        Assert(failure.Summary.Contains("FAILED"), "HotReloadResult.Summary: failure contains 'FAILED'");
        Assert(failure.Summary.Contains("file not found"), "HotReloadResult.Summary: failure shows error");

        var emptyError = new HotReloadResult { Success = false, Errors = [] };
        Assert(emptyError.Summary.Contains("unknown"), "HotReloadResult.Summary: no errors shows 'unknown'");
    }

    // ── Session tests ────────────────────────────────────────────────

    private static void TestSessionCreation()
    {
        var session = new HotReloadSession("TestMod");
        Assert(session.ModKey == "TestMod", "HotReloadSession: ModKey set correctly");
        Assert(session.LoadContext == null, "HotReloadSession: LoadContext initially null");
        Assert(session.LastLoadedAssembly == null, "HotReloadSession: LastLoadedAssembly initially null");
        Assert(session.HotReloadHarmony == null, "HotReloadSession: HotReloadHarmony initially null");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Reflection target existence tests
    // ═══════════════════════════════════════════════════════════════════
    // If any of these fail, the hot reload pipeline will crash when it tries
    // to access that field/property. These catch game updates that rename or
    // remove internal fields before the pipeline hits them at runtime.

    private const BindingFlags StaticNonPublic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags InstanceNonPublic = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void TestReflectionTargets_ModelDb()
    {
        // Step 6: we access _contentById to inject entities
        Assert(typeof(ModelDb).GetField("_contentById", StaticNonPublic) != null,
            "Reflection: ModelDb._contentById exists");

        // Step 7: we null these 14 cached fields
        string[] cacheFields =
        [
            "_allCards", "_allCardPools", "_allCharacterCardPools",
            "_allSharedEvents", "_allEvents", "_allEncounters", "_allPotions",
            "_allPotionPools", "_allCharacterPotionPools", "_allSharedPotionPools",
            "_allPowers", "_allRelics", "_allCharacterRelicPools", "_achievements"
        ];
        foreach (var fieldName in cacheFields)
        {
            Assert(typeof(ModelDb).GetField(fieldName, StaticNonPublic) != null,
                $"Reflection: ModelDb.{fieldName} exists");
        }
    }

    private static void TestReflectionTargets_ModManager()
    {
        // Step 3: we access _mods to update Mod.assembly
        Assert(typeof(ModManager).GetField("_mods", StaticNonPublic) != null,
            "Reflection: ModManager._mods exists");
    }

    private static void TestReflectionTargets_ReflectionHelper()
    {
        // Step 4: we null _modTypes to force cache rebuild
        Assert(typeof(ReflectionHelper).GetField("_modTypes", StaticNonPublic) != null,
            "Reflection: ReflectionHelper._modTypes exists");
    }

    private static void TestReflectionTargets_ModHelper()
    {
        // Step 8: we access _moddedContentForPools to unfreeze and clean pools
        Assert(typeof(ModHelper).GetField("_moddedContentForPools", StaticNonPublic) != null,
            "Reflection: ModHelper._moddedContentForPools exists");
    }

    private static void TestReflectionTargets_SerializationCache()
    {
        // Step 5: we register new entity IDs in ModelIdSerializationCache
        var cacheType = typeof(ModelId).Assembly.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache");
        Assert(cacheType != null, "Reflection: ModelIdSerializationCache type exists");

        if (cacheType != null)
        {
            Assert(cacheType.GetField("_categoryNameToNetIdMap", StaticNonPublic) != null,
                "Reflection: SerializationCache._categoryNameToNetIdMap exists");
            Assert(cacheType.GetField("_netIdToCategoryNameMap", StaticNonPublic) != null,
                "Reflection: SerializationCache._netIdToCategoryNameMap exists");
            Assert(cacheType.GetField("_entryNameToNetIdMap", StaticNonPublic) != null,
                "Reflection: SerializationCache._entryNameToNetIdMap exists");
            Assert(cacheType.GetField("_netIdToEntryNameMap", StaticNonPublic) != null,
                "Reflection: SerializationCache._netIdToEntryNameMap exists");
            Assert(cacheType.GetProperty("CategoryIdBitSize", StaticPublic) != null,
                "Reflection: SerializationCache.CategoryIdBitSize exists");
            Assert(cacheType.GetProperty("EntryIdBitSize", StaticPublic) != null,
                "Reflection: SerializationCache.EntryIdBitSize exists");
        }
    }

    private static void TestReflectionTargets_LocTable()
    {
        // Step 9 (via ModelLocPatch): we access LocTable._translations
        Assert(typeof(LocTable).GetField("_translations", InstanceNonPublic) != null,
            "Reflection: LocTable._translations exists");
    }

    private static void TestReflectionTargets_PoolModels()
    {
        // Step 8: we null lazy caches on pool model instances
        Assert(typeof(CardPoolModel).GetField("_allCards", InstanceNonPublic) != null,
            "Reflection: CardPoolModel._allCards exists");
        Assert(typeof(CardPoolModel).GetField("_allCardIds", InstanceNonPublic) != null,
            "Reflection: CardPoolModel._allCardIds exists");
        Assert(typeof(RelicPoolModel).GetField("_relics", InstanceNonPublic) != null,
            "Reflection: RelicPoolModel._relics exists");
        Assert(typeof(RelicPoolModel).GetField("_allRelicIds", InstanceNonPublic) != null,
            "Reflection: RelicPoolModel._allRelicIds exists");
        Assert(typeof(PotionPoolModel).GetField("_allPotions", InstanceNonPublic) != null,
            "Reflection: PotionPoolModel._allPotions exists");
        Assert(typeof(PotionPoolModel).GetField("_allPotionIds", InstanceNonPublic) != null,
            "Reflection: PotionPoolModel._allPotionIds exists");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 2: Integration tests — full pipeline, run on-demand
    // ═══════════════════════════════════════════════════════════════════
    // These require the game to be fully loaded (ModelDb populated, etc.)
    // and a test mod DLL to be available. Run via: hotreload_test console command.

    /// <summary>
    /// Run the full suite of integration tests. Requires the game to be fully loaded.
    /// Returns a structured result for the console command.
    /// </summary>
    public static (int passed, int failed, List<string> failures) RunIntegrationTests()
    {
        _passed = 0;
        _failed = 0;
        var failures = new List<string>();

        // Override Assert to capture failure messages
        void AssertIntegration(bool condition, string testName)
        {
            if (condition)
                _passed++;
            else
            {
                _failed++;
                failures.Add(testName);
                BaseLibMain.Logger.Error($"[HotReload] FAIL: {testName}");
            }
        }

        // ── Test: ModelDb._contentById is populated and accessible ──
        {
            var field = typeof(ModelDb).GetField("_contentById", StaticNonPublic);
            var dict = field?.GetValue(null) as Dictionary<ModelId, AbstractModel>;
            AssertIntegration(dict != null, "Integration: ModelDb._contentById accessible");
            AssertIntegration(dict != null && dict.Count > 0, $"Integration: ModelDb has entities ({dict?.Count ?? 0})");
        }

        // ── Test: SerializationCacheSnapshot round-trip ──
        // Capture the current state, restore it, and verify nothing changed
        {
            var cacheType = typeof(ModelId).Assembly.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache");
            if (cacheType != null)
            {
                var snapshot = SerializationCacheSnapshot.Capture(cacheType);
                AssertIntegration(snapshot != null, "Integration: SerializationCacheSnapshot captured");

                if (snapshot != null)
                {
                    // Record pre-restore state
                    var entryListField = cacheType.GetField("_netIdToEntryNameMap", StaticNonPublic);
                    var entryListBefore = entryListField?.GetValue(null) as List<string>;
                    int countBefore = entryListBefore?.Count ?? 0;

                    // Restore should be a no-op (same data)
                    snapshot.Restore();

                    var entryListAfter = entryListField?.GetValue(null) as List<string>;
                    int countAfter = entryListAfter?.Count ?? 0;

                    AssertIntegration(countBefore == countAfter,
                        $"Integration: SerializationCache round-trip preserves count ({countBefore} → {countAfter})");
                }
            }
            else
            {
                AssertIntegration(false, "Integration: SerializationCache type not found");
            }
        }

        // ── Test: Pool unfreezing and refreezing doesn't break pool access ──
        {
            var poolsField = typeof(ModHelper).GetField("_moddedContentForPools", StaticNonPublic);
            var pools = poolsField?.GetValue(null) as IDictionary;
            AssertIntegration(pools != null, "Integration: ModHelper._moddedContentForPools accessible");

            if (pools != null)
            {
                // Count entries — should be > 0 if any mods registered pool content
                AssertIntegration(pools.Count >= 0,
                    $"Integration: _moddedContentForPools has {pools.Count} entries");

                // Verify isFrozen and modelsToAdd fields exist on the content entries
                bool foundStructure = false;
                foreach (var key in pools.Keys)
                {
                    var content = pools[key];
                    if (content == null) continue;
                    var contentType = content.GetType();
                    var frozenField = contentType.GetField("isFrozen");
                    var modelsField = contentType.GetField("modelsToAdd");
                    foundStructure = frozenField != null && modelsField != null;
                    break; // just check the first one
                }
                if (pools.Count > 0)
                {
                    AssertIntegration(foundStructure,
                        "Integration: pool content entries have isFrozen and modelsToAdd fields");
                }
            }
        }

        // ── Test: ModManager._loadedMods is populated and has assembly field ──
        {
            var loadedModsField = typeof(ModManager).GetField("_mods", StaticNonPublic);
            var loadedMods = loadedModsField?.GetValue(null) as IList;
            AssertIntegration(loadedMods != null, "Integration: ModManager._loadedMods accessible");
            AssertIntegration(loadedMods != null && loadedMods.Count > 0,
                $"Integration: ModManager has {loadedMods?.Count ?? 0} loaded mods");

            if (loadedMods is { Count: > 0 })
            {
                var firstMod = loadedMods[0]!;
                var asmField = firstMod.GetType().GetField("assembly", BindingFlags.Public | BindingFlags.Instance);
                AssertIntegration(asmField != null, "Integration: Mod.assembly field exists");
                if (asmField != null)
                {
                    var asm = asmField.GetValue(firstMod) as Assembly;
                    AssertIntegration(asm != null, "Integration: first mod has a loaded assembly");
                }
            }
        }

        // ── Test: ReflectionHelper._modTypes invalidation and rebuild ──
        {
            var modTypesField = typeof(ReflectionHelper).GetField("_modTypes", StaticNonPublic);
            var originalTypes = modTypesField?.GetValue(null);

            // Null it (this is what step 4 does)
            modTypesField?.SetValue(null, null);
            AssertIntegration(modTypesField?.GetValue(null) == null,
                "Integration: ReflectionHelper._modTypes can be nulled");

            // Access ModTypes to trigger rebuild
            var rebuiltTypes = ReflectionHelper.ModTypes?.ToList();
            AssertIntegration(rebuiltTypes != null && rebuiltTypes.Count > 0,
                $"Integration: ReflectionHelper.ModTypes rebuilds after null ({rebuiltTypes?.Count ?? 0} types)");
        }

        // ── Test: Live instance refresh doesn't crash on empty scene ──
        // (This tests that the BFS walk handles the current scene tree gracefully)
        {
            try
            {
                int refreshed = LiveInstanceRefresher.RefreshSceneTree();
                // We don't know how many nodes exist, but it shouldn't throw
                AssertIntegration(true, $"Integration: RefreshSceneTree completed ({refreshed} refreshed)");
            }
            catch (Exception ex)
            {
                AssertIntegration(false, $"Integration: RefreshSceneTree threw: {ex.Message}");
            }
        }

        // ── Test: BuildModelId produces valid IDs for known types ──
        {
            try
            {
                var (category, entry) = TypeSignatureHasher.GetCategoryAndEntry(typeof(CardModel));
                AssertIntegration(!string.IsNullOrEmpty(category),
                    $"Integration: GetCategoryAndEntry produces category for CardModel: '{category}'");
                AssertIntegration(!string.IsNullOrEmpty(entry),
                    $"Integration: GetCategoryAndEntry produces entry for CardModel: '{entry}'");
            }
            catch (Exception ex)
            {
                AssertIntegration(false, $"Integration: GetCategoryAndEntry threw: {ex.Message}");
            }
        }

        // ── Test: DefaultAlcResolving finds loaded assemblies ──
        {
            // Try resolving BaseLib itself — it should find the loaded assembly
            var baseLibName = typeof(BaseLibMain).Assembly.GetName();
            var resolved = TypeSignatureHasher.DefaultAlcResolving(
                AssemblyLoadContext.Default,
                new AssemblyName(baseLibName.Name!));
            AssertIntegration(resolved != null,
                "Integration: DefaultAlcResolving resolves BaseLib by name");

            // Try resolving a nonexistent assembly — should return null
            var notFound = TypeSignatureHasher.DefaultAlcResolving(
                AssemblyLoadContext.Default,
                new AssemblyName("NonExistentMod_Definitely_Not_Loaded"));
            AssertIntegration(notFound == null,
                "Integration: DefaultAlcResolving returns null for unknown assembly");
        }

        // ── Test: GetAssembliesForMod finds the right assemblies ──
        {
            // BaseLib should find itself
            string baseLibKey = AssemblyStamper.NormalizeModKey(typeof(BaseLibMain).Assembly.GetName().Name);
            var found = TypeSignatureHasher.GetAssembliesForMod(baseLibKey).ToList();
            AssertIntegration(found.Count > 0,
                $"Integration: GetAssembliesForMod finds BaseLib (key='{baseLibKey}', found={found.Count})");

            // Exclude parameter should work
            var foundExcluding = TypeSignatureHasher.GetAssembliesForMod(baseLibKey, typeof(BaseLibMain).Assembly).ToList();
            AssertIntegration(foundExcluding.Count == found.Count - 1,
                "Integration: GetAssembliesForMod exclude parameter works");
        }

        var result = (_passed, _failed, failures);
        _passed = 0;
        _failed = 0;
        return result;
    }
}
