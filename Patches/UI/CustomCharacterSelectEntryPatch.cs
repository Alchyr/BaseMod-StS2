using System;
using System.Collections.Generic;
using System.Linq;
using BaseLib.Abstracts;
using BaseLib.BaseLibScenes;
using BaseLib.Extensions;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Saves;

namespace BaseLib.Patches.UI;

[HarmonyPatch]
internal static class CustomCharacterSelectEntryPatch
{
    private static readonly SpireField<NCharacterSelectScreen, CustomCharacterSelectScreenState> ScreenStates = new(() => new());

    [HarmonyPatch(typeof(NCharacterSelectScreen), "InitCharacterButtons")]
    [HarmonyPostfix]
    private static void AddCustomEntryButtons(NCharacterSelectScreen __instance)
    {
        if (CustomCharacterSelectEntryRegistry.Entries.Count == 0) return;

        var state = ScreenStates.Get(__instance)!;
        if (state.Initialized) return;
        state.Initialized = true;

        var randomButton = __instance._randomCharacterButton;
        if (randomButton?.GetParent() == __instance._charButtonContainer)
        {
            __instance._charButtonContainer.RemoveChildSafely(randomButton);
        }

        foreach (var entry in CustomCharacterSelectEntryRegistry.Entries)
        {
            if (!entry.VisibleInCharacterSelect) continue;

            var button = new NCustomCharacterSelectEntryButton(entry, __instance, selected => SelectCustomEntry(__instance, selected));
            __instance._charButtonContainer.AddChildSafely(button.Button);
            state.Buttons.Add(button);
        }

        if (randomButton != null)
        {
            __instance._charButtonContainer.AddChildSafely(randomButton);
        }

        EnsureForegroundContainer(__instance, state);
        RebuildFocusNeighbors(__instance);
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
    [HarmonyPostfix]
    private static void OnSubmenuOpenedPostfix(NCharacterSelectScreen __instance)
    {
        var state = ScreenStates.Get(__instance);
        if (state == null) return;

        foreach (var button in state.Buttons)
        {
            button.Enable();
            button.Deselect();
        }

        ClearActiveEntry(__instance, clearScene: true);
        __instance._infoPanel.Visible = true;
        RebuildFocusNeighbors(__instance);
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
    [HarmonyPrefix]
    private static void OnSubmenuClosedPrefix(NCharacterSelectScreen __instance)
    {
        ClearActiveEntry(__instance, clearScene: true);
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
    [HarmonyPostfix]
    private static void SelectCharacterPostfix(NCharacterSelectScreen __instance)
    {
        ClearActiveEntry(__instance, clearScene: false);
        __instance._infoPanel.Visible = true;
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
    [HarmonyPrefix]
    private static bool OnEmbarkPressedPrefix(NCharacterSelectScreen __instance)
    {
        var state = ScreenStates.Get(__instance);
        if (state?.ActiveButton == null) return true;
        if (state.Context?.SelectedCharacter is { } character && !IsCharacterLocked(character)) return true;

        __instance._embarkButton.Disable();
        return false;
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
    [HarmonyPostfix]
    private static void OnEmbarkPressedPostfix(NCharacterSelectScreen __instance)
    {
        var state = ScreenStates.Get(__instance);
        if (state == null || !__instance.Lobby.LocalPlayer.isReady) return;

        foreach (var button in state.Buttons)
        {
            button.Disable();
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), "OnUnreadyPressed")]
    [HarmonyPostfix]
    private static void OnUnreadyPressedPostfix(NCharacterSelectScreen __instance)
    {
        var state = ScreenStates.Get(__instance);
        if (state == null) return;

        foreach (var button in state.Buttons)
        {
            button.Enable();
        }

        state.ActiveButton?.TryGrabFocus();
        RefreshEmbarkAvailability(__instance);
    }

    private static void SelectCustomEntry(NCharacterSelectScreen screen, NCustomCharacterSelectEntryButton button)
    {
        var state = ScreenStates.Get(screen)!;
        var customButtons = state.Buttons.Select(static customButton => customButton.Button).ToHashSet();

        foreach (var vanillaButton in screen._charButtonContainer.GetChildren().OfType<NCharacterSelectButton>())
        {
            if (!customButtons.Contains(vanillaButton))
            {
                vanillaButton.Deselect();
            }
        }

        foreach (var customButton in state.Buttons)
        {
            if (customButton != button)
            {
                customButton.Deselect();
            }
        }

        ClearBackground(screen);
        ClearActiveEntry(screen, clearScene: true);

        if (button.IsLocked)
        {
            state.ActiveButton = button;
            ApplyLockedEntryPanel(screen, button);
            return;
        }

        Control entryScene;
        try
        {
            entryScene = button.Entry.CreateCharacterSelectScene();
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Error($"Failed to create custom character select scene for {button.Entry.EntryId}: {e}");
            button.Deselect();
            return;
        }

        Control? foregroundScene = null;
        try
        {
            foregroundScene = button.Entry.CreateCharacterSelectForegroundScene();
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Error($"Failed to create custom character select foreground scene for {button.Entry.EntryId}: {e}");
        }

        entryScene.Name = $"{button.Entry.EntryId}_entry_bg";
        screen._bgContainer.AddChildSafely(entryScene);

        if (foregroundScene != null)
        {
            foregroundScene.Name = $"{button.Entry.EntryId}_entry_fg";
            EnsureForegroundContainer(screen, state).AddChildSafely(foregroundScene);
        }

        CustomCharacterSelectContext? context = null;
        context = new CustomCharacterSelectContext(
            button.Entry,
            screen,
            entryScene,
            foregroundScene,
            character => OnResolvedCharacterChanged(screen, context!, character));

        state.ActiveButton = button;
        state.ActiveScene = entryScene;
        state.ActiveForegroundScene = foregroundScene;
        state.Context = context;

        ApplyEntryPanel(screen, button.Entry);

        try
        {
            button.Entry.RegisterScene(entryScene, context);
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Error($"Failed to register custom character select scene for {button.Entry.EntryId}: {e}");
        }

        if (foregroundScene != null)
        {
            try
            {
                button.Entry.RegisterForegroundScene(foregroundScene, context);
            }
            catch (Exception e)
            {
                BaseLibMain.Logger.Error($"Failed to register custom character select foreground scene for {button.Entry.EntryId}: {e}");
            }
        }

        if (context.SelectedCharacter == null && button.Entry.InitialCharacter is { } initialCharacter)
        {
            context.SetCharacter(initialCharacter);
        }
        else
        {
            RefreshEmbarkAvailability(screen);
        }
    }

    private static void OnResolvedCharacterChanged(
        NCharacterSelectScreen screen,
        CustomCharacterSelectContext context,
        CharacterModel? character)
    {
        var state = ScreenStates.Get(screen);
        if (state?.Context != context) return;

        if (character == null)
        {
            ApplyEntryPanel(screen, context.Entry);
            RefreshEmbarkAvailability(screen);
            return;
        }

        ApplyCharacterPanel(screen, character, context.Entry);
    }

    private static void RefreshEmbarkAvailability(NCharacterSelectScreen screen)
    {
        var state = ScreenStates.Get(screen);
        if (state?.ActiveButton == null) return;

        if (state.Context?.SelectedCharacter == null)
        {
            screen._embarkButton.Disable();
        }
        else if (IsCharacterLocked(state.Context.SelectedCharacter))
        {
            screen._embarkButton.Disable();
        }
        else
        {
            screen._embarkButton.Enable();
        }
    }

    private static void ClearActiveEntry(NCharacterSelectScreen screen, bool clearScene)
    {
        var state = ScreenStates.Get(screen);
        if (state == null) return;

        state.ActiveButton?.Deselect();

        if (clearScene && state.ActiveScene != null && GodotObject.IsInstanceValid(state.ActiveScene))
        {
            if (state.ActiveScene.GetParent() != null)
            {
                state.ActiveScene.GetParent().RemoveChildSafely(state.ActiveScene);
            }

            state.ActiveScene.QueueFreeSafely();
        }

        if (clearScene && state.ActiveForegroundScene != null && GodotObject.IsInstanceValid(state.ActiveForegroundScene))
        {
            if (state.ActiveForegroundScene.GetParent() != null)
            {
                state.ActiveForegroundScene.GetParent().RemoveChildSafely(state.ActiveForegroundScene);
            }

            state.ActiveForegroundScene.QueueFreeSafely();
        }

        state.ActiveButton = null;
        state.ActiveScene = null;
        state.ActiveForegroundScene = null;
        state.Context = null;
    }

    private static void ClearBackground(NCharacterSelectScreen screen)
    {
        foreach (Node child in screen._bgContainer.GetChildren())
        {
            screen._bgContainer.RemoveChildSafely(child);
            child.QueueFreeSafely();
        }
    }

    private static Control EnsureForegroundContainer(
        NCharacterSelectScreen screen,
        CustomCharacterSelectScreenState state)
    {
        if (state.ForegroundContainer != null && GodotObject.IsInstanceValid(state.ForegroundContainer))
        {
            screen.MoveChild(state.ForegroundContainer, screen.GetChildCount() - 1);
            return state.ForegroundContainer;
        }

        var container = new Control
        {
            Name = "BaseLibCharacterSelectForeground",
            LayoutMode = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.AddChildSafely(container);
        screen.MoveChild(container, screen.GetChildCount() - 1);
        state.ForegroundContainer = container;
        return container;
    }

    private static void RebuildFocusNeighbors(NCharacterSelectScreen screen)
    {
        var buttons = screen._charButtonContainer.GetChildren()
            .OfType<Control>()
            .Where(static c => c.Visible)
            .ToList();

        if (buttons.Count == 0) return;

        for (var i = 0; i < buttons.Count; i++)
        {
            var current = buttons[i];
            current.FocusNeighborTop = current.GetPath();
            current.FocusNeighborBottom = current.GetPath();
            current.FocusNeighborLeft = buttons[(i - 1 + buttons.Count) % buttons.Count].GetPath();
            current.FocusNeighborRight = buttons[(i + 1) % buttons.Count].GetPath();
        }
    }

    private static void AnimateInfoPanel(NCharacterSelectScreen screen)
    {
        if (screen._infoPanelTween != null)
        {
            screen._infoPanel.Position = screen._infoPanelPosFinalVal;
        }

        screen._infoPanelPosFinalVal = screen._infoPanel.Position;
        screen._infoPanelTween?.Kill();
        screen._infoPanelTween = screen.CreateTween().SetParallel();
        screen._infoPanelTween.TweenProperty(screen._infoPanel, "position", screen._infoPanel.Position, 0.5)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo)
            .From(screen._infoPanel.Position - new Vector2(300f, 0f));
    }

    private static void ApplyEntryPanel(NCharacterSelectScreen screen, CustomCharacterSelectEntry entry)
    {
        screen._selectedButton = null;
        screen._embarkButton.Disable();
        screen._name.SetTextAutoSize(entry.EntryTitle);
        screen._description.Text = entry.EntryDescription;
        screen._hp.SetTextAutoSize("??/??");
        screen._gold.SetTextAutoSize("???");
        screen._relicIcon.SelfModulate = StsColors.transparentBlack;
        screen._relicIconOutline.SelfModulate = StsColors.transparentBlack;
        screen._relicTitle.Text = string.Empty;
        screen._relicDescription.Text = string.Empty;
        screen._ascensionPanel.Visible = false;
        ApplyInfoPanelVisibility(screen, entry.ShowVanillaInfoPanelWhenUnresolved);
    }

    private static void ApplyLockedEntryPanel(NCharacterSelectScreen screen, NCustomCharacterSelectEntryButton button)
    {
        if (button.LockSourceCharacter != null)
        {
            ApplyLockedCharacterPanel(
                screen,
                button.LockSourceCharacter,
                button.Entry.ShowVanillaInfoPanelWhenUnresolved);
            return;
        }

        screen._selectedButton = null;
        screen._embarkButton.Disable();
        screen._name.SetTextAutoSize(button.Entry.LockedTitle);
        screen._description.Text = button.Entry.LockedDescription;
        screen._hp.SetTextAutoSize("??/??");
        screen._gold.SetTextAutoSize("???");
        screen._relicIcon.SelfModulate = StsColors.transparentBlack;
        screen._relicIconOutline.SelfModulate = StsColors.transparentBlack;
        screen._relicTitle.Text = string.Empty;
        screen._relicDescription.Text = string.Empty;
        screen._ascensionPanel.Visible = false;
        ApplyInfoPanelVisibility(screen, button.Entry.ShowVanillaInfoPanelWhenUnresolved);
    }

    private static void ApplyCharacterPanel(
        NCharacterSelectScreen screen,
        CharacterModel character,
        CustomCharacterSelectEntry entry)
    {
        screen._selectedButton = null;

        if (IsCharacterLocked(character))
        {
            ApplyLockedCharacterPanel(screen, character, entry.ShowVanillaInfoPanelWhenResolved);
            return;
        }

        var formattedTitle = new LocString("characters", character.CharacterSelectTitle).GetFormattedText();
        screen._name.SetTextAutoSize(formattedTitle);
        screen._description.Text = new LocString("characters", character.CharacterSelectDesc).GetFormattedText();

        if (character is not RandomCharacter)
        {
            screen._hp.SetTextAutoSize($"{character.StartingHp}/{character.StartingHp}");
            screen._gold.SetTextAutoSize($"{character.StartingGold}");
            var relic = character.StartingRelics[0];
            screen._relicTitle.Text = relic.Title.GetFormattedText();
            screen._relicDescription.Text = relic.DynamicDescription.GetFormattedText();
            screen._relicIcon.Texture = relic.Icon;
            screen._relicIconOutline.Texture = relic.IconOutline;
            screen._relicIcon.SelfModulate = Colors.White;
            screen._relicIconOutline.SelfModulate = StsColors.halfTransparentBlack;
        }
        else
        {
            screen._hp.SetTextAutoSize("??/??");
            screen._gold.SetTextAutoSize("???");
            screen._relicIcon.SelfModulate = StsColors.transparentBlack;
            screen._relicIconOutline.SelfModulate = StsColors.transparentBlack;
            screen._relicTitle.Text = string.Empty;
            screen._relicDescription.Text = string.Empty;
        }

        screen._embarkButton.Enable();
        screen._lobby.SetLocalCharacter(character);
        if (!screen._lobby.NetService.Type.IsMultiplayer())
        {
            screen._ascensionPanel.AnimIn();
        }

        ApplyInfoPanelVisibility(screen, entry.ShowVanillaInfoPanelWhenResolved);
    }

    private static void ApplyLockedCharacterPanel(
        NCharacterSelectScreen screen,
        CharacterModel character,
        bool showInfoPanel)
    {
        screen._embarkButton.Disable();
        screen._name.SetTextAutoSize(new LocString("main_menu_ui", "CHARACTER_SELECT.locked.title").GetFormattedText());
        screen._description.Text = character.GetUnlockText().GetFormattedText();
        screen._hp.SetTextAutoSize("??/??");
        screen._gold.SetTextAutoSize("???");

        if (character is not RandomCharacter)
        {
            var relic = character.StartingRelics[0];
            screen._relicTitle.Text = new LocString("main_menu_ui", "CHARACTER_SELECT.lockedRelic.title").GetFormattedText();
            screen._relicDescription.Text =
                new LocString("main_menu_ui", "CHARACTER_SELECT.lockedRelic.description").GetFormattedText();
            screen._relicIcon.Texture = relic.Icon;
            screen._relicIconOutline.Texture = relic.IconOutline;
            screen._relicIcon.SelfModulate = StsColors.ninetyPercentBlack;
            screen._relicIconOutline.SelfModulate = StsColors.halfTransparentWhite;
        }
        else
        {
            screen._relicIcon.SelfModulate = StsColors.transparentBlack;
            screen._relicIconOutline.SelfModulate = StsColors.transparentBlack;
            screen._relicTitle.Text = string.Empty;
            screen._relicDescription.Text = string.Empty;
        }

        screen._ascensionPanel.Visible = false;
        ApplyInfoPanelVisibility(screen, showInfoPanel);
    }

    private static void ApplyInfoPanelVisibility(NCharacterSelectScreen screen, bool visible)
    {
        screen._infoPanel.Visible = visible;
        if (visible)
        {
            AnimateInfoPanel(screen);
        }
    }

    private static bool IsCharacterLocked(CharacterModel character)
    {
        var unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        if (character is RandomCharacter)
        {
            return ModelDb.AllCharacters
                .Where(static c => c is not CustomCharacterModel { AllowInVanillaRandomCharacterSelect: false })
                .Any(c => !unlockState.Characters.Contains(c));
        }

        return !unlockState.Characters.Contains(character);
    }

    private sealed class CustomCharacterSelectScreenState
    {
        public bool Initialized { get; set; }
        public List<NCustomCharacterSelectEntryButton> Buttons { get; } = [];
        public NCustomCharacterSelectEntryButton? ActiveButton { get; set; }
        public Control? ActiveScene { get; set; }
        public Control? ActiveForegroundScene { get; set; }
        public Control? ForegroundContainer { get; set; }
        public CustomCharacterSelectContext? Context { get; set; }
    }
}
