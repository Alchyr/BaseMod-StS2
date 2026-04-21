using BaseLib.Patches.Content;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

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
    /// Optional default resolved character when the entry is selected.
    /// </summary>
    public virtual CharacterModel? InitialCharacter => null;

    /// <summary>
    /// Override this or <seealso cref="CreateCharacterSelectScene"/> to provide a scene shown in the background container.
    /// </summary>
    public virtual string? CharacterSelectScenePath => null;

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
    /// Called after the entry scene has been instantiated and added to the background container.
    /// Use the provided context to wire any scene nodes to character selection logic.
    /// </summary>
    public virtual void RegisterScene(Control root, CustomCharacterSelectContext context)
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
        Action<CharacterModel?> setCharacter)
    {
        Entry = entry;
        Screen = screen;
        SceneRoot = sceneRoot;
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
    /// The character currently resolved by this custom entry, if any.
    /// </summary>
    public CharacterModel? SelectedCharacter { get; private set; }

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
