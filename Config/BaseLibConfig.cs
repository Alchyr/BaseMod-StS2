using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace BaseLib.Config;

// Try to generate hover tips for all properties.
// Use [ConfigHoverTip(false)] for exceptions; warnings will be printed for each property
// that is missing a corresponding settings_ui.json localization string (see below)
[HoverTipsByDefault]
internal class BaseLibConfig : SimpleModConfig
{
    // Actual BaseLib settings
    [ConfigSection("LogSection")]
    public static bool OpenLogWindowOnStartup { get; set; } = false;

    [SliderRange(128, 2048, 64)]
    [SliderLabelFormat("{0:0}")]
    public static double LimitedLogSize { get; set; } = 256;

    // Everything below is just examples. Will likely be removed very soon, when the Wiki has examples and explanations.

    public enum StartingActEnum
    {
        Overgrowth, Underdocks
    }

    // Note: In all the example localization strings below, BASELIB is used because this file is in BaseLib!
    // Your own mod name would be required there if you copied this class over to your mod.

    // BASELIB-FIRST_EXAMPLE_SECTION.title in settings_ui.json
    [ConfigSection("FirstExampleSection")]

    // BASELIB-ALLOW_DUPLICATE_RELICS.title in settings_ui.json
    public static bool AllowDuplicateRelics { get; set; } = false;

    // Would generate a hover tip if BASELIB-CREATED_CARD_KEYWORD.hover.desc exists
    // Doesn't do anything in this example because the class already has [HoverTipsByDefault]
    [ConfigHoverTip]
    public static CardKeyword CreatedCardKeyword { get; set; } = CardKeyword.None;

    [ConfigSection("SecondSection")]
    [SliderRange(0.1, 4.0, 0.05)]
    [SliderLabelFormat("{0:0.00}x")]
    public static double EnemyDamageMultiplier { get; set; } = 1.25;

    // The type is double, but doubles perfectly represent integers up to 2^53, so there are no floating point
    // accuracy/rounding issues to worry about here. Use an integer step and simply cast to int if required.
    [SliderRange(-50, 50, 5)]
    [SliderLabelFormat("{0:+0;-0;0} HP")] // Force a + sign in front of positive numbers
    [ConfigHoverTip(false)] // Don't try to generate a hover tip despite [HoverTipsByDefault] on the class
    public static double StartingHealthOffset { get; set; } = -10;

    [ConfigHoverTip(false)]
    public static StartingActEnum StartingAct { get; set; } = StartingActEnum.Overgrowth;

    [SliderRange(0, 10)] // Default step value is 1
    [ConfigHoverTip(false)]
    public static double MinimumElitesPerAct { get; set; } = 6;

    //[ConfigTextInput(@"[A-Za-z0-9 ]*")] // Custom regex example, with unlimited length, and empty value allowed
    [ConfigHoverTip(false)]
    [ConfigTextInput(TextInputPreset.SafeDisplayName, MaxLength = 16)]
    public static string PlayerName { get; set; } = "Player";

    [ConfigHideInUI] // Load and save automatically, but don't create a UI
    public static int TotalCardsPlayed { get; set; } = 0;

    [ConfigIgnore] // Don't load, save or create a UI for this property
    public static int NotAConfigProperty { get; set; } = 42;

    // An example on how to add a custom button at the end of the list, but before the restore defaults button
    public override void SetupConfigUI(Control optionContainer)
    {
        GenerateOptionsForAllProperties(optionContainer);

        optionContainer.AddChild(CreateDividerControl());

        var buttonRow = CreateButton("ExampleButton", "HelloWorld", () =>
        {
            var name = string.IsNullOrWhiteSpace(PlayerName) ? "Player" : PlayerName;
            var popup = NErrorPopup.Create("Hello World", $"Hi there, {name}!", false);
            if (popup != null && NModalContainer.Instance != null) NModalContainer.Instance.Add(popup);
        }, true);
        optionContainer.AddChild(buttonRow);

        AddRestoreDefaultsButton(optionContainer);
    }
}