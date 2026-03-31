namespace BaseLib.HotReload;

/// <summary>
/// Describes a single entity that was affected by a hot reload.
/// For example, a card that was re-injected with new stats, or a relic
/// that was unchanged and skipped.
/// </summary>
public sealed class ChangedEntity
{
    public string Name { get; init; } = "";

    /// <summary>
    /// What happened to this entity: "injected" (new/changed), "removed" (gone from new assembly),
    /// or "unchanged" (signature hash matched, skipped for efficiency).
    /// </summary>
    public string Action { get; init; } = "";

    /// <summary>
    /// The ModelId string (e.g. "card/my-strike"), or null for unchanged/removed entities.
    /// </summary>
    public string? Id { get; init; }
}

/// <summary>
/// Everything that happened during a hot reload, good or bad.
/// Check Success first — if false, look at Errors for what went wrong.
/// StepTimings tells you where the time was spent.
/// ChangedEntities tells you exactly which entities were affected.
/// </summary>
public sealed class HotReloadResult
{
    public bool Success { get; init; }

    /// <summary>1 = patches only, 2 = entities + patches + loc, 3 = full + PCK resources.</summary>
    public int Tier { get; init; }

    public string? AssemblyName { get; init; }
    public int PatchCount { get; init; }
    public int EntitiesRemoved { get; init; }
    public int EntitiesInjected { get; init; }

    /// <summary>How many entities were unchanged (same signature hash) and skipped.</summary>
    public int EntitiesSkipped { get; init; }

    public int PoolsUnfrozen { get; init; }
    public int PoolRegistrations { get; init; }
    public bool LocalizationReloaded { get; init; }
    public bool PckReloaded { get; init; }
    public int LiveInstancesRefreshed { get; init; }

    /// <summary>How many cards passed the ToMutable() sanity check.</summary>
    public int MutableCheckPassed { get; init; }

    /// <summary>How many cards failed ToMutable() — usually a PowerVar resolution issue.</summary>
    public int MutableCheckFailed { get; init; }

    /// <summary>Whether the assembly was loaded into a collectible ALC (tier 1 only).</summary>
    public bool AlcCollectible { get; init; }

    public long TotalMs { get; init; }

    /// <summary>UTC timestamp of when the reload completed (ISO 8601).</summary>
    public string Timestamp { get; init; } = "";

    /// <summary>Time spent on each step, keyed like "step1_assembly_load", "step6_entity_reload", etc.</summary>
    public Dictionary<string, long> StepTimings { get; init; } = [];

    /// <summary>What the pipeline did, in order. Useful for debugging reload issues.</summary>
    public List<string> Actions { get; init; } = [];

    /// <summary>Things that went wrong and caused (or would cause) failure.</summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>Things that were slightly off but didn't prevent the reload from succeeding.</summary>
    public List<string> Warnings { get; init; } = [];

    public List<ChangedEntity> ChangedEntities { get; init; } = [];

    /// <summary>
    /// One-line summary suitable for console output.
    /// </summary>
    public string Summary
    {
        get
        {
            if (!Success)
            {
                var firstError = Errors.Count > 0 ? Errors[0] : "unknown error";
                return $"Hot reload FAILED: {firstError}";
            }
            return $"Hot reload OK — tier {Tier}, {EntitiesInjected} entities, {PatchCount} patches, {LiveInstancesRefreshed} live refreshed ({TotalMs}ms)";
        }
    }
}
