using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Common.Rewards;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class CardTransformReward(Player player, bool upgrade = false) : CustomReward(player)
{
    [CustomEnum]
    public static RewardType CardTransform;
    protected override RewardType RewardType => CardTransform;

    public bool Upgrade = upgrade;

    public override LocString Description => new LocString("gameplay_ui", "COMBAT_REWARD_CARD_TRANSFORM");
    public override bool IsPopulated => true;
    public static string RewardIcon => ImageHelper.GetImagePath("ui/reward_screen/reward_icon_card_removal.png");
    protected override string IconPath => RewardIcon;

    public static CardTransformReward CreateFromSerializable(SerializableReward save, Player player)
    {
        return new CardTransformReward(player) { Upgrade = save.WasGoldStolenBack}; // temp hack before worrying about extending the serialized values
    }

    public override SerializableReward ToSerializable()
    {
        return new SerializableReward()
        {
            RewardType = CardTransform,
            WasGoldStolenBack = Upgrade
        };
    }

    public override SerializableCustomReward<CustomReward> SerializeMethod => CreateFromSerializable;

    public override void MarkContentAsSeen()
    {
    }

    public override Task Populate()
    {
        return Task.CompletedTask;
    }

    protected override async Task<bool> OnSelect()
    {
        BaseLibMain.Logger.Info("Obtained card transformation from reward");
        return await RunManager.Instance.RewardSynchronizer.DoLocalCardTransform(true);
    }
}
