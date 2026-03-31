namespace BaseLib.Abstracts;

//TODO - Add additional information to each class on anything that should be noted when writing its localization.

/// <summary>
/// <para>A model that implements this interface can define localization that will be added to its relevant localization table.
/// Recommended implementation is to return one of the provided Loc classes such as CardLoc.
/// To support translation, a switch statement like this is recommended:</para>
/// => LocManager.Instance.Language switch
/// { "aaa" => new CardLoc("translated"), _ => new CardLoc("default") };
/// <seealso cref="BaseLib.Patches.Utils.ModelLocPatch"/>
/// </summary>
public interface ILocalizationProvider
{
    string? LocTable => null;
    List<(string, string)>? Localization { get; }
}


/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for an act.
/// </summary>
public record ActLoc(string Title, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(ActLoc loc) =>
    [
        ("title", loc.Title),
        ..loc.ExtraLoc
    ];
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for an affliction or enchantment.
/// </summary>
public record CardModifierLoc(string Title, string Description, string? ExtraCardText = null, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(CardModifierLoc loc)
    {
        List<(string, string)> result = 
        [
            ("title", loc.Title),
            ("description", loc.Description),
            ..loc.ExtraLoc
        ];
        if (loc.ExtraCardText != null) result.Add(("extraCardText", loc.ExtraCardText));
        return result;
    }
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for a card.
/// </summary>
public record CardLoc(string Title, string Description, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(CardLoc loc) =>
    [
        ("title", loc.Title),
        ("description", loc.Description),
        ..loc.ExtraLoc
    ];
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for a character.
/// </summary>
public record CharacterLoc(string Title, string TitleObject, string Description, 
    string PronounObject, string PronounSubject, string PronounPossessive, string PossessiveAdjective,
    string AromaPrinciple, 
    string EndTurnPingAlive, string EndTurnPingDead, string EventDeathPrevention, string GoldMonologue,
    string CardsModifierTitle, string CardsModifierDescription,
    params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(CharacterLoc loc) =>
    [
        ("title", loc.Title),
        ("titleObject", loc.TitleObject),
        ("description", loc.Description),
        ("pronounObject", loc.PronounObject),
        ("pronounSubject", loc.PronounSubject),
        ("pronounPossessive", loc.PronounPossessive),
        ("possessiveAdjective", loc.PossessiveAdjective),
        ("aromaPrinciple", loc.AromaPrinciple),
        ("banter.alive.endTurnPing", loc.EndTurnPingAlive),
        ("banter.dead.endTurnPing", loc.EndTurnPingDead),
        ("eventDeathPrevention", loc.EventDeathPrevention),
        ("goldMonologue", loc.GoldMonologue),
        ("cardsModifierTitle", loc.CardsModifierTitle),
        ("cardsModifierDescription", loc.CardsModifierDescription),
        ..loc.ExtraLoc
    ];
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for an encounter.
/// </summary>
public record EncounterLoc(string Title, string LossText, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(EncounterLoc loc) =>
    [
        ("title", loc.Title),
        ("loss", loc.LossText),
        ..loc.ExtraLoc
    ];
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for a run modifier.
/// </summary>
public record ModifierLoc(string Title, string Description, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(ModifierLoc loc) =>
    [
        ("title", loc.Title),
        ("description", loc.Description),
        ..loc.ExtraLoc
    ];
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for a monster.
/// </summary>
/// <param name="Name">The monster's name.</param>
/// <param name="MoveTitles">Sets of move keys and names. Keys should be ALL_CAPS. The name will be displayed when the monster acts.</param>
/// <param name="ExtraLoc">Any additional desired localization.</param>
public record MonsterLoc(string Name, IEnumerable<(string, string)> MoveTitles, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(MonsterLoc loc)
    {
        List<(string, string)> result = 
        [
            ("name", loc.Name),
            ..loc.ExtraLoc
        ];

        foreach (var entry in loc.MoveTitles)
        {
            result.Add(($"moves.{entry.Item1}.title", entry.Item2));
        }

        return result;
    }
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for an orb.
/// </summary>
public record OrbLoc(string Title, string Description, string SmartDescription, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(OrbLoc loc) =>
    [
        ("title", loc.Title),
        ("description", loc.Description),
        ("smartDescription", loc.SmartDescription),
        ..loc.ExtraLoc
    ];
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for a potion.
/// </summary>
public record PotionLoc(string Title, string Description, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(PotionLoc loc) =>
    [
        ("title", loc.Title),
        ("description", loc.Description),
        ..loc.ExtraLoc
    ];
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for a power.
/// </summary>
public record PowerLoc(string Title, string Description, string SmartDescription, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(PowerLoc loc) =>
    [
        ("title", loc.Title),
        ("description", loc.Description),
        ("smartDescription", loc.SmartDescription),
        ..loc.ExtraLoc
    ];
}

/// <summary>
/// For use with ILocalizationProvider.<seealso cref="ILocalizationProvider"/>
/// Localization for a relic.
/// </summary>
public record RelicLoc(string Title, string Description, string Flavor, params (string, string)[] ExtraLoc)
{
    public static implicit operator List<(string, string)>(RelicLoc loc) =>
    [
        ("title", loc.Title),
        ("description", loc.Description),
        ("flavor", loc.Flavor),
        ..loc.ExtraLoc
    ];
}