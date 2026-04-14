using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Patches.Fixes;

internal sealed class RewardExtData
{
    [JsonPropertyName("flags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Flags { get; set; }

    [JsonPropertyName("custom_card_ids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? CustomCardIds { get; set; }

    [JsonPropertyName("is_custom_pool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCustomPool { get; set; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Source { get; set; }

    [JsonPropertyName("rarity_odds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RarityOdds { get; set; }
}

internal static class RewardSerializationExt
{
    internal const string KeyPrefix = "__mod_reward_ext_";

    private static readonly ConditionalWeakTable<SerializableReward, RewardExtData> ExtTable = new();

    internal static void SetExtData(SerializableReward reward, RewardExtData data)
        => ExtTable.AddOrUpdate(reward, data);

    internal static bool TryGetExtData(SerializableReward reward, out RewardExtData? data)
    {
        if (ExtTable.TryGetValue(reward, out data!))
            return true;
        data = null;
        return false;
    }

    internal static string MakeKey(ulong netId, int index)
        => $"{KeyPrefix}{netId}_{index}";

    internal static bool TryParseKey(string key, out ulong netId, out int index)
    {
        netId = 0;
        index = 0;
        if (!key.StartsWith(KeyPrefix)) return false;

        var rest = key.AsSpan(KeyPrefix.Length);
        var sep = rest.IndexOf('_');
        if (sep < 0) return false;

        return ulong.TryParse(rest[..sep], out netId)
               && int.TryParse(rest[(sep + 1)..], out index);
    }

    internal static string ToJson(RewardExtData data) => JsonSerializer.Serialize(data);

    internal static RewardExtData? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RewardExtData>(json);
        }
        catch
        {
            return null;
        }
    }
}

[HarmonyPatch(typeof(CardReward), nameof(CardReward.ToSerializable))]
public static class CardRewardToSerializablePatch
{
    private static readonly Func<CardReward, CardCreationOptions> GetOptions =
        AccessTools.MethodDelegate<Func<CardReward, CardCreationOptions>>(
            AccessTools.DeclaredPropertyGetter(typeof(CardReward), "Options"));

    private static readonly Func<CardReward, int> GetOptionCount =
        AccessTools.MethodDelegate<Func<CardReward, int>>(
            AccessTools.DeclaredPropertyGetter(typeof(CardReward), "OptionCount"));

    static bool Prefix(CardReward __instance, ref SerializableReward __result)
    {
        var options = GetOptions(__instance);
        var hasFlags = options.Flags != 0;
        var hasFilter = options.CardPoolFilter != null;
        var hasNoPools = options.CardPools.Count <= 0;

        if (!hasFlags && !hasFilter && !hasNoPools)
            return true;

        var result = new SerializableReward { RewardType = RewardType.Card };
        RewardExtData? ext = null;

        if (hasNoPools && options.CustomCardPool != null)
        {
            ext = BuildCustomPoolExt(options);
            result.Source = options.Source;
            result.RarityOdds = options.RarityOdds;
            result.OptionCount = GetOptionCount(__instance);
        }
        else if (hasFilter && options.CardPools.Count > 0)
        {
            ext = BuildFilterSnapshotExt(options);
            result.Source = options.Source;
            result.RarityOdds = options.RarityOdds;
            result.OptionCount = GetOptionCount(__instance);
        }
        else
        {
            result.Source = options.Source;
            result.RarityOdds = options.RarityOdds;
            result.CardPoolIds = options.CardPools.Select(p => p.Id).ToList();
            result.OptionCount = GetOptionCount(__instance);
        }

        if (hasFlags)
        {
            ext ??= new RewardExtData();
            ext.Flags = (int)options.Flags;
        }

        if (ext != null)
            RewardSerializationExt.SetExtData(result, ext);

        __result = result;
        return false;
    }

    private static RewardExtData BuildCustomPoolExt(CardCreationOptions options)
    {
        return new RewardExtData
        {
            IsCustomPool = true,
            CustomCardIds = options.CustomCardPool!.Select(c => c.Id.ToString()).ToList(),
            Source = (int)options.Source,
            RarityOdds = (int)options.RarityOdds
        };
    }

    private static RewardExtData BuildFilterSnapshotExt(CardCreationOptions options)
    {
        var cards = options.CardPools
            .SelectMany(p => p.AllCards)
            .Where(options.CardPoolFilter!)
            .ToList();

        return new RewardExtData
        {
            IsCustomPool = true,
            CustomCardIds = cards.Select(c => c.Id.ToString()).ToList(),
            Source = (int)options.Source,
            RarityOdds = (int)options.RarityOdds
        };
    }
}

[HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.ToSerializable))]
public static class CombatRoomToSerializableRewardExtPatch
{
    static void Postfix(ref SerializableRoom __result)
    {
        foreach (var (netId, rewards) in __result.ExtraRewards)
        {
            for (var i = 0; i < rewards.Count; i++)
            {
                if (!RewardSerializationExt.TryGetExtData(rewards[i], out var ext) || ext == null)
                    continue;

                var key = RewardSerializationExt.MakeKey(netId, i);
                __result.EncounterState ??= new Dictionary<string, string>();
                __result.EncounterState[key] = RewardSerializationExt.ToJson(ext);
            }
        }
    }
}

[HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.FromSerializable))]
public static class CombatRoomFromSerializableRewardExtPatch
{
    static void Prefix(SerializableRoom serializableRoom)
    {
        if (serializableRoom.EncounterState != null)
        {
            foreach (var (key, json) in serializableRoom.EncounterState)
            {
                if (!RewardSerializationExt.TryParseKey(key, out var netId, out var index))
                    continue;

                if (!serializableRoom.ExtraRewards.TryGetValue(netId, out var rewards))
                    continue;

                if (index < 0 || index >= rewards.Count)
                    continue;

                var ext = RewardSerializationExt.FromJson(json);
                if (ext != null)
                    RewardSerializationExt.SetExtData(rewards[index], ext);
            }
        }

        foreach (var (_, rewards) in serializableRoom.ExtraRewards)
        {
            var removed = rewards.RemoveAll(r => r.RewardType == RewardType.None);
            if (removed > 0)
            {
                BaseLibMain.Logger.Warn(
                    $"[BaseLib] Stripped {removed} RewardType.None entry(s) from ExtraRewards " +
                    "(e.g. LinkedRewardSet) — serialization for this type is not supported.");
            }
        }
    }
}

[HarmonyPatch(typeof(Reward), nameof(Reward.FromSerializable))]
public static class RewardFromSerializableExtPatch
{
    static bool Prefix(SerializableReward save, Player player, ref Reward __result)
    {
        if (save.RewardType != RewardType.Card
            || !RewardSerializationExt.TryGetExtData(save, out var ext)
            || ext == null) return true;
        __result = RebuildCardReward(save, ext, player);
        return false;
    }

    private static CardReward RebuildCardReward(
        SerializableReward save, RewardExtData ext, Player player)
    {
        var flags = (CardCreationFlags)ext.Flags;

        if (ext is { IsCustomPool: true, CustomCardIds: not null })
        {
            var source = (CardCreationSource)ext.Source;
            var rarityOdds = (CardRarityOddsType)ext.RarityOdds;
            var cards = ext.CustomCardIds
                .Select(id => ModelDb.GetByIdOrNull<CardModel>(ModelId.Deserialize(id)))
                .Where(c => c != null)
                .Select(c => c!)
                .ToList();

            if (cards.Count > 0)
            {
                var options = new CardCreationOptions(cards, source, rarityOdds);
                if (flags != 0) options.WithFlags(flags);
                return new CardReward(options, save.OptionCount, player);
            }

            BaseLibMain.Logger.Warn(
                "[BaseLib] Reward.FromSerializable: CustomCardPool had no resolvable cards, falling back.");
        }

        var pools = save.CardPoolIds
            .Select(ModelDb.GetById<CardPoolModel>)
            .ToList();
        var poolOptions = new CardCreationOptions(pools, save.Source, save.RarityOdds);
        if (flags != 0)
            poolOptions.WithFlags(flags);

        return new CardReward(poolOptions, save.OptionCount, player);
    }
}
