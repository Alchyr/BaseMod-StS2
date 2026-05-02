using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Common.Rewards;

/// <summary>
/// A reward class similar to the card removal one created by <see cref="ForbiddenGrimoire"/>,
/// only for transforming instead of removing cards
/// </summary>
/// <example>
/// In a relic or power's <c>AfterCombatEnd</c> override
/// <code>
/// room.AddExtraReward(Owner.Player, new CardTransformReward(Owner.Player) {Amount = Amount, Upgrade = true});
/// </code> </example>
public sealed class CardTransformReward(Player player) : CustomReward(player)
{
    /// <summary>
    /// A new <see cref="RewardType"/> defined with the <see cref="CustomEnumAttribute"/> attribute
    /// </summary>
    [CustomEnum] public static RewardType CardTransform;
    /// <summary>
    /// Reference to the <see cref="RewardType"/> <see cref="CardTransform"/> defined earlier
    /// </summary>
    protected override RewardType RewardType => CardTransform;

    /// <summary>
    /// Whether the card rewards should be upgraded or not
    /// </summary>
    public required bool Upgrade;
    /// <summary>
    /// How many cards can be selected in this reward screen
    /// </summary>
    public required int Amount;

    /// <summary>
    /// The description to show in the reward screen,
    /// switches based on whether the reward will upgrade the transformed cards
    /// </summary>
    public override LocString Description
    {
        get
        {
            LocString locString = new LocString("gameplay_ui", "COMBAT_REWARD_CARD_TRANSFORM");
            locString.Add("cards", Amount);
            locString.Add("Upgrade", Upgrade);
            return locString;
        }
    }
    /// <inheritdoc/>
    public override bool IsPopulated => true;
    public static string RewardIcon => ImageHelperExtensions.GetModImagePath("ui/reward_screen/reward_icon_card_transform.png");
    /// <inheritdoc/>
    protected override string IconPath => RewardIcon;


    /// <summary>
    /// Serializing the reward, saving whether to upgrade and how many cards to transform in the vanilla fields
    /// </summary>
    public override SerializableReward ToSerializable()
    {
        return new SerializableReward()
        {
            RewardType = CardTransform,
            GoldAmount = Amount,
            WasGoldStolenBack = Upgrade
        };
    }

    /// <summary>
    /// Recreates the reward from the saved <see cref="SerializableReward"/>
    /// </summary>
    /// <param name="save">The <see cref="SerializableReward"/> that was created and saved from
    /// <see cref="ToSerializable"/></param>
    /// <param name="player">The <see cref="Player"/> the reward belongs to</param>
    public CardTransformReward CreateFromSerializable(SerializableReward save, Player player)
    {
        return new CardTransformReward(player) {
            // hijacking the gold amounts as a temp hack before worrying about extending the serialized values
            Amount = save.GoldAmount,
            Upgrade = save.WasGoldStolenBack
        };
    }

    /// <inheritdoc/>
    public override SerializableCustomReward<CustomReward> SerializeMethod => CreateFromSerializable;

    /// <inheritdoc/>
    public override void MarkContentAsSeen() { }

    /// <inheritdoc />
    public override Task Populate() { return Task.CompletedTask; }

    /// <inheritdoc/>
    protected override async Task<bool> OnSelect()
    {
        BaseLibMain.Logger.Info("Obtained card transformation from reward");
        return await RunManager.Instance.RewardSynchronizer.DoLocalCardTransform(Amount, true);
    }
}
