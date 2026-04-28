using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Modding;

namespace BaseLib.Utils;

/// <summary>
/// Mod save entry point.
/// </summary>
/// <example>
/// Set entry point for custom run data:
/// <code>
/// public static class MySaveManager
/// {
///     [ModSave] public static MyRunData RunData = new();
/// }
/// </code>
///
/// Root save container
/// <code>
/// public class MyRunData : ISaveSchema, IPacketSerializable
/// {
///     [JsonPropertyName("schema_version")]
///     public int SchemaVersion { get; set; } = 1;
///     &#10;
///     // Example save data
///     [JsonPropertyName("player_data")]
///     public List&lt;MyPlayerData&gt; PlayerData { get; set; } = [];
///     &#10;
///     public void Serialize(PacketWriter writer)
///     {
///         writer.WriteInt(SchemaVersion);
///         writer.WriteInt(PlayerData.Count);
///         foreach (var pData in PlayerData)
///         {
///             pData.Serialize(writer);
///         }
///     }
///     &#10;
///     public void Deserialize(PacketReader reader)
///     {
///         ...
///     }
/// }
/// </code>
///
/// Player save data:
/// <code>
/// public class MyPlayerData : IPacketSerializable
/// {
///     [JsonPropertyName("net_id")]
///     public ulong NetId { get; set; }
///     &#10;
///     [JsonPropertyName("collector_deck")]
///     public List&lt;SerializableCard> SavedCards { get; set; } = [];
///     &#10;
///     [JsonPropertyName("essence")]
///     public int Essence { get; set; }
///     &#10;
///     public void Serialize(PacketWriter writer)
///     {
///         writer.WriteULong(NetId);
///         writer.WriteInt(Essence);
///         writer.WriteList(SavedCards);
///     }
///     &#10;
///     public void Deserialize(PacketReader reader)
///     {
///         NetId = reader.ReadULong();
///         Essence = reader.ReadInt();
///         SavedCards = reader.ReadList&lt;SerializableCard>();
///     }
///     &#10;
///     public static MyPlayerData FromPlayer(Player player)
///     {
///         var netId = player.NetId;
///         var data = MyRunData.PlayerData.Find(p => p.NetId == netId);
///         if (data != null) return data;
///         data = new MyPlayerData { NetId = netId };
///         MyRunData.PlayerData.Add(data);
///         return data;
///     }
/// 
///     public static List&lt;CardModel> GetSavedCards(Player player) =>
///         MyPlayerData.FromPlayer(player).SavedCards.Select(CardModel.FromSerializable)
///         .ToList();
///     &#10;
///     public static void AddSavedCard(Player player, CardModel card) =>
///         MyPlayerData.FromPlayer(player).SavedCards.Add(card.ToSerializable());
///         &#10;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field)]
public class ModSaveAttribute : Attribute;



    
public static class ModSaveUtils
{
    /// <summary>
    /// Gets the unique identifier of a mod.
    /// </summary>
    public static string GetModId(Mod mod)
    {
        return mod.manifest?.id ?? "UnknownMod";
    }

    /// <summary>
    /// Builds the mod-specific save file path based on a vanilla save path.
    /// </summary>
    public static string GetModPath(string vanillaPath, string modId)
    {
        var directory = Path.GetDirectoryName(vanillaPath) ?? "";
        var fileName = Path.GetFileName(vanillaPath);
        return Path.Combine(directory, "mods", modId, fileName).Replace("\\", "/");
    }
    
    private static readonly Dictionary<string, FieldInfo> SaveFieldCache = new();
    
    private static FieldInfo? GetSaveField(Mod mod)
    {
        var modId = GetModId(mod);
        if (SaveFieldCache.TryGetValue(modId, out var cachedField)) return cachedField;
        var field = mod.assembly?.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .FirstOrDefault(f => f.GetCustomAttribute<ModSaveAttribute>() != null);

        if (field != null) SaveFieldCache[modId] = field;
        return field;
    }

    /// <summary>
    /// Serializes the mod save field to JSON.
    /// </summary>
    /// <param name="mod">Target mod.</param>
    /// <returns>JSON save data, or null if unavailable.</returns>
    public static string? GetModDataToSave(Mod mod)
    {
        var field = GetSaveField(mod);
        if (field == null) return null;
        var liveData = field.GetValue(null);
        if (liveData == null) return null;
        return JsonSerializer.Serialize(liveData, field.FieldType, new JsonSerializerOptions {
            WriteIndented = true,
            IncludeFields = true 
        });
    }
    
    /// <summary>
    /// Deserializes JSON save data and injects it into the mod's save field.
    /// </summary>
    /// <param name="mod">Target mod instance.</param>
    /// <param name="json">Serialized save data.</param>
    /// <remarks>
    /// Uses reflection to locate the <c>[ModSave]</c> field and replaces its value.
    /// If deserialization fails, the error is logged and the operation is safely ignored.
    /// </remarks>
    public static void LoadDataIntoMod(Mod mod, string json)
    {
        var field = GetSaveField(mod);
        if (field == null) return;

        try
        {
            var loadedData = JsonSerializer.Deserialize(json, field.FieldType, new JsonSerializerOptions {
                IncludeFields = true
            });

            if (loadedData == null) return;
            field.SetValue(null, loadedData);
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"Load error for {mod.manifest?.id}: {ex.Message}");
        }
    }
}