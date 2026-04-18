using BaseLib;
using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Baselib.Patches.Content;

[HarmonyPatch(typeof(Reward))]
internal static class CustomRewardPatches
{
    internal static readonly Dictionary<RewardType, SerializableCustomReward<CustomReward>> _RewardTypeSerializers = [];

    public static void RegisterCustomReward(RewardType type, SerializableCustomReward<CustomReward> serializer)
    {
        if (_RewardTypeSerializers.ContainsKey(type))
        {
            BaseLibMain.Logger.Error($"Registering multiple rewards of the same type ({type}) is not supported");
            throw new NotSupportedException($"Registering multiple rewards of the same type ({type}) is not supported");
        }

        BaseLibMain.Logger.Info($"Registering RewardType {nameof(type)}");
        _RewardTypeSerializers.Add(type, serializer);
    }

    [HarmonyPatch(nameof(Reward.FromSerializable))]
    [HarmonyPrefix]
    public static bool FromSerializablePrefix(SerializableReward save, Player player, ref Reward __result)
    {
        if (_RewardTypeSerializers.Keys.Contains(save.RewardType))
        {
            BaseLibMain.Logger.Debug($"Found RewardType {save.RewardType} in registry from mod {_RewardTypeSerializers[save.RewardType].Method.GetType().Assembly}");

            var method = _RewardTypeSerializers[save.RewardType];
            __result = method.Invoke(save, player);
            return false;
        }

        BaseLibMain.Logger.Debug($"No CustomReward found for RewardType {save.RewardType}, proceeding to vanilla method");
        return true;
    }
}
