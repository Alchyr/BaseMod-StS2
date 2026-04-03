using BaseLib.Config;
using BaseLib.Config.UI;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Patches.Utils;

[HarmonyPatch(typeof(NMainMenuSubmenuStack), nameof(NMainMenuSubmenuStack.GetSubmenuType), typeof(Type))]
public static class InjectModConfigSubmenuTypePatch
{
    private static readonly SpireField<NMainMenuSubmenuStack, NModConfigSubmenu> SubmenuField = new(CreateSubmenu);

    private static NModConfigSubmenu CreateSubmenu(NMainMenuSubmenuStack stack)
    {
        var menu = new NModConfigSubmenu();
        menu.Visible = false;
        stack.AddChildSafely(menu);
        return menu;
    }

    public static bool Prefix(NMainMenuSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        if (type != typeof(NModConfigSubmenu)) return true;

        __result = SubmenuField.Get(__instance)!;
        return false;
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
public static class InjectMainMenuModConfigPatch
{
    public static void Prefix(NMainMenu __instance)
    {
        if (!BaseLibConfig.ShowModConfigInMainMenu) return;

        try
        {
            InjectMainMenuEntry(__instance);
        }
        catch (Exception)
        {
            ModConfig.ModConfigLogger.Error(
                "BaseLib was unable to add the Mod Configuration entry to the main menu." +
                "This is likely either due to a recent game update, or mod incompatibility.");
        }
    }

    public static void Postfix(NMainMenu __instance)
    {
        // Fix minor issue: left/right jumps immediately to the Mod Configuration entry because it's the widest
        // (in English, at least). Without our patch, left/right never does anything, so restore that behavior.
        var mainMenuButtonsProp = AccessTools.Property(typeof(NMainMenu), "MainMenuButtons");
        if (mainMenuButtonsProp == null) return;
        if (mainMenuButtonsProp.GetValue(__instance) is not NButton?[] nativeButtons) return;

        foreach (var button in nativeButtons)
        {
            if (button == null) continue;
            button.FocusNeighborLeft = new NodePath(".");
            button.FocusNeighborRight = new NodePath(".");
        }
    }

    private static void InjectMainMenuEntry(NMainMenu mainMenu)
    {
        var settingsButton = mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/SettingsButton");
        var modConfigButton = (NMainMenuTextButton)settingsButton.Duplicate();
        modConfigButton.Name = "ModConfigButton";

        modConfigButton.Connect(NClickableControl.SignalName.Released, Callable.From(
            new Action<NButton>(_ =>
            {
                var lastHitField = AccessTools.Field(typeof(NMainMenu), "_lastHitButton");
                lastHitField?.SetValue(mainMenu, modConfigButton);

                mainMenu.SubmenuStack.PushSubmenuType<NModConfigSubmenu>();
            })));

        settingsButton.AddSibling(modConfigButton);
        modConfigButton.SetLocalization("BASELIB-MOD_CONFIGURATION");

        // Increase area that responds to hover
        modConfigButton.CustomMinimumSize = new Vector2(300, modConfigButton.CustomMinimumSize.Y);

        var selfNodePath = new NodePath(".");
        modConfigButton.FocusNeighborRight = selfNodePath;
        modConfigButton.FocusNeighborLeft = selfNodePath;
    }
}

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
public static class InjectSettingsModConfigPatch
{
    public static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            InjectSettingsMenuEntry(__instance);
        }
        catch (Exception)
        {
            ModConfig.ModConfigLogger.Error(
                "BaseLib was unable to add the Mod Configuration entry to the Settings menu." +
                "This is likely either due to a recent game update, or mod incompatibility.");
        }
    }

    private static void InjectSettingsMenuEntry(NSettingsScreen settingsScreen)
    {
        var generalSettings = settingsScreen.GetNodeOrNull<Control>("ScrollContainer/Mask/Clipper/GeneralSettings");
        var origDivider = generalSettings.GetNodeOrNull<ColorRect>("VBoxContainer/SendFeedbackDivider");
        var feedbackContainer = generalSettings.GetNodeOrNull<MarginContainer>("VBoxContainer/SendFeedback");
        var modSettingsContainer = generalSettings.GetNodeOrNull<MarginContainer>("VBoxContainer/Modding");

        var modConfigDivider = origDivider.Duplicate();
        var modConfigContainer = (MarginContainer)modSettingsContainer.Duplicate();

        feedbackContainer.AddSibling(modConfigDivider);
        modConfigDivider.AddSibling(modConfigContainer);

        modConfigContainer.UniqueNameInOwner = false;
        modConfigContainer.Name = "BaseLibModConfig";
        modConfigContainer.Visible = true;

        var modConfigButton = modConfigContainer.GetNodeOrNull<Control>("ModdingButton");
        modConfigButton.UniqueNameInOwner = false;
        modConfigButton.Name = "ModConfigButton";

        var rowLabel = modConfigContainer.GetNodeOrNull<RichTextLabel>("Label");
        rowLabel.Text = LocString.GetIfExists("settings_ui", "BASELIB.MOD_CONFIG_SETTINGS_ROW.title")
            ?.GetFormattedText() ?? "Mod Configuration (BaseLib)";

        var buttonLabel = modConfigButton.GetNodeOrNull<Label>("Label");
        buttonLabel.Text = LocString.GetIfExists("settings_ui", "BASELIB.MOD_CONFIG_SETTINGS_ROW.button")
            ?.GetFormattedText() ?? "Open Config";

        modConfigButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
        {
            var stackField = AccessTools.Field(typeof(NSubmenu), "_stack");

            if (stackField.GetValue(settingsScreen) is NMainMenuSubmenuStack stackInstance)
                stackInstance.PushSubmenuType<NModConfigSubmenu>();
            else
                ModConfig.ModConfigLogger.Error("Unable to open BaseLib's Mod Configuration.");
        }));

        // TODO: dynamically figure these out as "above" and "below", to better support other mods injecting similar entries
        // Probably not worth doing until the game's base focus neighbors are fixed (they currently move from Feedback
        // straight to Credits, skipping Mod Settings)

        var feedbackButton = feedbackContainer.GetNodeOrNull<Control>("FeedbackButton");
        var modSettingsButton = modSettingsContainer.GetNodeOrNull<Control>("%ModdingButton");
        var creditsButton = generalSettings.GetNodeOrNull<Control>("VBoxContainer/Credits/CreditsButton");

        if (feedbackButton == null || modSettingsButton == null || creditsButton == null) return;

        // Patch base game issue (still relevant as of v0.101.0)
        creditsButton.FocusNeighborTop = creditsButton.GetPathTo(modSettingsButton);
        modSettingsButton.FocusNeighborBottom = modSettingsButton.GetPathTo(creditsButton);

        // Patch in our button
        modConfigButton.FocusNeighborTop = modConfigButton.GetPathTo(feedbackButton);
        modConfigButton.FocusNeighborBottom = modConfigButton.GetPathTo(modSettingsButton);
        feedbackButton.FocusNeighborBottom = feedbackButton.GetPathTo(modConfigButton);
        modSettingsButton.FocusNeighborTop = modSettingsButton.GetPathTo(modConfigButton);
    }
}