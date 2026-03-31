using System.Reflection;

namespace BaseLib.HotReload;

/// <summary>
/// Deep copy of ModelIdSerializationCache state for transactional rollback.
///
/// The serialization cache maps entity category/entry name strings to integer
/// net IDs for multiplayer serialization. It's built once at boot time, so
/// hot-reloaded entities aren't in it by default. We register new entries
/// during reload, but if something goes wrong we need to undo those additions
/// — otherwise the cache would reference types that don't exist in ModelDb.
/// </summary>
internal sealed class SerializationCacheSnapshot
{
    public Type? CacheType { get; init; }
    public Dictionary<string, int>? CategoryMap { get; init; }
    public List<string>? CategoryList { get; init; }
    public Dictionary<string, int>? EntryMap { get; init; }
    public List<string>? EntryList { get; init; }
    public int? CategoryBitSize { get; init; }
    public int? EntryBitSize { get; init; }

    private const BindingFlags StaticNonPublic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;

    /// <summary>Deep-copy all 6 fields from the live cache. Returns null if the type is null.</summary>
    public static SerializationCacheSnapshot? Capture(Type cacheType)
    {
        return new SerializationCacheSnapshot
        {
            CacheType = cacheType,

            CategoryMap = cacheType.GetField("_categoryNameToNetIdMap", StaticNonPublic)
                ?.GetValue(null) is Dictionary<string, int> catMap
                ? new Dictionary<string, int>(catMap)
                : null,

            CategoryList = cacheType.GetField("_netIdToCategoryNameMap", StaticNonPublic)
                ?.GetValue(null) is List<string> catList
                ? [.. catList]
                : null,

            EntryMap = cacheType.GetField("_entryNameToNetIdMap", StaticNonPublic)
                ?.GetValue(null) is Dictionary<string, int> entMap
                ? new Dictionary<string, int>(entMap)
                : null,

            EntryList = cacheType.GetField("_netIdToEntryNameMap", StaticNonPublic)
                ?.GetValue(null) is List<string> entList
                ? [.. entList]
                : null,

            CategoryBitSize = cacheType.GetProperty("CategoryIdBitSize", StaticPublic)
                ?.GetValue(null) as int?,

            EntryBitSize = cacheType.GetProperty("EntryIdBitSize", StaticPublic)
                ?.GetValue(null) as int?,
        };
    }

    /// <summary>Overwrite the live cache with this snapshot's data. Used on rollback.</summary>
    public void Restore()
    {
        if (CacheType == null) return;

        CacheType.GetField("_categoryNameToNetIdMap", StaticNonPublic)
            ?.SetValue(null, CategoryMap != null ? new Dictionary<string, int>(CategoryMap) : null);
        CacheType.GetField("_netIdToCategoryNameMap", StaticNonPublic)
            ?.SetValue(null, CategoryList != null ? new List<string>(CategoryList) : null);
        CacheType.GetField("_entryNameToNetIdMap", StaticNonPublic)
            ?.SetValue(null, EntryMap != null ? new Dictionary<string, int>(EntryMap) : null);
        CacheType.GetField("_netIdToEntryNameMap", StaticNonPublic)
            ?.SetValue(null, EntryList != null ? new List<string>(EntryList) : null);

        var catBitProp = CacheType.GetProperty("CategoryIdBitSize", StaticPublic);
        if (catBitProp?.SetMethod != null && CategoryBitSize.HasValue)
            catBitProp.SetValue(null, CategoryBitSize.Value);

        var entBitProp = CacheType.GetProperty("EntryIdBitSize", StaticPublic);
        if (entBitProp?.SetMethod != null && EntryBitSize.HasValue)
            entBitProp.SetValue(null, EntryBitSize.Value);
    }
}
