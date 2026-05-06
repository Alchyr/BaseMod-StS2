using BaseLib.Patches.Content;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Saves;

namespace BaseLib.Abstracts;

/// <summary>
/// Registers a custom entry in the character select screen that can resolve into a playable character.
/// </summary>
public abstract class CustomCharacterSelectEntry : ICustomModel
{
    /// <summary>
    /// Creates and auto-registers the entry with BaseLib.
    /// </summary>
    protected CustomCharacterSelectEntry()
    {
        CustomCharacterSelectEntryRegistry.Register(this);
    }

    /// <summary>
    /// Stable identifier for this entry. Recommended to include a mod prefix.
    /// </summary>
    public virtual string EntryId => StringHelper.Slugify(GetType().FullName ?? GetType().Name);

    /// <summary>
    /// Icon shown on the custom character select button.
    /// </summary>
    public abstract string ButtonIconPath { get; }

    /// <summary>
    /// Title shown in the info panel while no concrete character is currently resolved.
    /// </summary>
    public virtual string EntryTitle => GetType().Name;

    /// <summary>
    /// Description shown in the info panel while no concrete character is currently resolved.
    /// </summary>
    public virtual string EntryDescription => string.Empty;

    /// <summary>
    /// Sort order among custom entries. Lower values appear first.
    /// </summary>
    public virtual int SortOrder => 0;

    /// <summary>
    /// Override and return false to hide this entry from the character select screen.
    /// </summary>
    public virtual bool VisibleInCharacterSelect => true;

    /// <summary>
    /// Optional character whose vanilla unlock state and lock tooltip semantics should be reused by this entry.
    /// </summary>
    public virtual CharacterModel? AvailabilitySourceCharacter => null;

    /// <summary>
    /// Controls whether this entry is currently unlocked and can be selected.
    /// Defaults to the unlock state of <seealso cref="AvailabilitySourceCharacter"/> when one is provided.
    /// </summary>
    public virtual bool UnlockedInCharacterSelect =>
        AvailabilitySourceCharacter == null || CustomCharacterSelectEntryAvailability.IsUnlocked(AvailabilitySourceCharacter);

    /// <summary>
    /// Optional default resolved character when the entry is selected.
    /// </summary>
    public virtual CharacterModel? InitialCharacter => null;

    /// <summary>
    /// Controls whether the vanilla info panel should be shown while this entry is active and no concrete character is resolved.
    /// </summary>
    public virtual bool ShowVanillaInfoPanelWhenUnresolved => true;

    /// <summary>
    /// Controls whether the vanilla info panel should be shown after this entry resolves to a concrete character.
    /// </summary>
    public virtual bool ShowVanillaInfoPanelWhenResolved => true;

    /// <summary>
    /// Title shown in the vanilla info panel when this entry is visible but locked and no source character lock panel is used.
    /// </summary>
    public virtual string LockedTitle =>
        new LocString("main_menu_ui", "CHARACTER_SELECT.locked.title").GetFormattedText();

    /// <summary>
    /// Description shown in the vanilla info panel when this entry is visible but locked and no source character lock panel is used.
    /// </summary>
    public virtual string LockedDescription => EntryDescription;

    /// <summary>
    /// Override this or <seealso cref="CreateCharacterSelectScene"/> to provide a scene shown in the background container.
    /// </summary>
    public virtual string? CharacterSelectScenePath => null;

    /// <summary>
    /// Override this or <seealso cref="CreateCharacterSelectForegroundScene"/> to provide a scene shown above the vanilla character select UI.
    /// </summary>
    public virtual string? CharacterSelectForegroundScenePath => null;

    /// <summary>
    /// Create the scene that will be added to the character select background container.
    /// Override if you want to instantiate or build the node manually.
    /// </summary>
    public virtual Control CreateCharacterSelectScene()
    {
        if (CharacterSelectScenePath == null)
        {
            throw new InvalidOperationException(
                $"{GetType().FullName} must override either {nameof(CharacterSelectScenePath)} or {nameof(CreateCharacterSelectScene)}.");
        }

        return ResourceLoader.Load<PackedScene>(CharacterSelectScenePath)
                   ?.Instantiate<Control>(PackedScene.GenEditState.Disabled)
               ?? throw new InvalidOperationException(
                   $"Failed to load character select scene at path '{CharacterSelectScenePath}' for {GetType().FullName}.");
    }

    /// <summary>
    /// Create the optional scene that will be added above the vanilla character select UI.
    /// Override if you want to instantiate or build the node manually.
    /// Return <see langword="null"/> to omit the foreground layer.
    /// </summary>
    public virtual Control? CreateCharacterSelectForegroundScene()
    {
        if (CharacterSelectForegroundScenePath == null)
        {
            return null;
        }

        return ResourceLoader.Load<PackedScene>(CharacterSelectForegroundScenePath)
                   ?.Instantiate<Control>(PackedScene.GenEditState.Disabled)
               ?? throw new InvalidOperationException(
                   $"Failed to load character select foreground scene at path '{CharacterSelectForegroundScenePath}' for {GetType().FullName}.");
    }

    /// <summary>
    /// Called after the entry scene has been instantiated and added to the background container.
    /// Use the provided context to wire any scene nodes to character selection logic.
    /// </summary>
    public virtual void RegisterScene(Control root, CustomCharacterSelectContext context)
    {
    }

    /// <summary>
    /// Called after the optional foreground scene has been instantiated and added above the vanilla character select UI.
    /// </summary>
    public virtual void RegisterForegroundScene(Control root, CustomCharacterSelectContext context)
    {
    }
}

/// <summary>
/// Runtime context passed to a custom character select entry scene.
/// Use it to publish the currently resolved character back to the character select screen.
/// </summary>
public sealed class CustomCharacterSelectContext
{
    private readonly Action<CharacterModel?> _setCharacter;

    internal CustomCharacterSelectContext(
        CustomCharacterSelectEntry entry,
        NCharacterSelectScreen screen,
        Control sceneRoot,
        Control? foregroundSceneRoot,
        Action<CharacterModel?> setCharacter)
    {
        Entry = entry;
        Screen = screen;
        SceneRoot = sceneRoot;
        ForegroundSceneRoot = foregroundSceneRoot;
        _setCharacter = setCharacter;
    }

    /// <summary>
    /// The entry that created this context.
    /// </summary>
    public CustomCharacterSelectEntry Entry { get; }

    /// <summary>
    /// The owning vanilla character select screen.
    /// </summary>
    public NCharacterSelectScreen Screen { get; }

    /// <summary>
    /// The active lobby backing the current character select screen.
    /// </summary>
    public StartRunLobby Lobby => Screen.Lobby;

    /// <summary>
    /// Root node of the instantiated custom entry scene.
    /// </summary>
    public Control SceneRoot { get; }

    /// <summary>
    /// Root node of the instantiated foreground scene, if this entry created one.
    /// </summary>
    public Control? ForegroundSceneRoot { get; }

    /// <summary>
    /// The character currently resolved by this custom entry, if any.
    /// </summary>
    public CharacterModel? SelectedCharacter { get; private set; }

    /// <summary>
    /// Current visibility of the vanilla character info panel.
    /// </summary>
    public bool VanillaInfoPanelVisible => Screen._infoPanel.Visible;

    /// <summary>
    /// Resolves the current selection to the given playable character.
    /// Pass <see langword="null"/> to clear the current resolution.
    /// </summary>
    public void SetCharacter(CharacterModel? character)
    {
        SelectedCharacter = character;
        _setCharacter(character);
    }

    /// <summary>
    /// Clears the currently resolved character and disables embark until a new one is set.
    /// </summary>
    public void ClearCharacter()
    {
        SetCharacter(null);
    }

    /// <summary>
    /// Shows or hides the vanilla character info panel.
    /// </summary>
    public void SetVanillaInfoPanelVisible(bool visible)
    {
        Screen._infoPanel.Visible = visible;
    }
}

internal static class CustomCharacterSelectEntryRegistry
{
    public static readonly List<CustomCharacterSelectEntry> Entries = [];

    public static void Register(CustomCharacterSelectEntry entry)
    {
        if (!CustomContentDictionary.RegisterType(entry.GetType())) return;

        Entries.Add(entry);
        Entries.Sort(static (a, b) =>
        {
            var result = a.SortOrder.CompareTo(b.SortOrder);
            return result != 0 ? result : string.CompareOrdinal(a.EntryId, b.EntryId);
        });
    }
}

internal static class CustomCharacterSelectEntryAvailability
{
    public static bool IsUnlocked(CharacterModel character)
    {
        var unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        return unlockState.Characters.Contains(character);
    }
}
