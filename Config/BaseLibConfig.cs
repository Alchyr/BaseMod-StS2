using MegaCrit.Sts2.Core.Entities.Cards;

namespace BaseLib.Config;

internal class BaseLibConfig : SimpleModConfig
{
    public enum StartingActEnum
    {
        Overgrowth, Underdocks
    }

    [ConfigSection("These are just example settings")]
    public static bool EnableDebugLogging { get; set; } = false;
    public static bool SkipIntroCinematic { get; set; } = true;
    public static bool AllowDuplicateRelics { get; set; } = false;

    [ConfigSection("Nothing here does anything at all")]
    public static CardKeyword CreatedCardKeyword { get; set; } = CardKeyword.None;
    public static StartingActEnum StartingAct { get; set; } = StartingActEnum.Overgrowth;

    [ConfigSection("Various example sliders")]
    [SliderRange(0.1, 4.0, 0.05)]
    [SliderLabelFormat("{0:0.00}x")]
    public static double EnemyDamageMultiplier { get; set; } = 1.25;

    [SliderRange(-50, 50, 5)]
    [SliderLabelFormat("{0:+0;-0;0} HP")]
    public static double StartingHealthOffset { get; set; } = -10;

    [SliderRange(0, 1000)]
    [SliderLabelFormat("{0} Gold")]
    public static double StartingGold { get; set; } = 99;

    [SliderRange(0, 10)]
    public static double MinimumElitesPerAct { get; set; } = 6;
}