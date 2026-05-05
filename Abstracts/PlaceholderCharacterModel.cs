using MegaCrit.Sts2.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BaseLib.Abstracts;

public abstract class PlaceholderCharacterModel : CustomCharacterModel
{
    public virtual string PlaceholderID => "ironclad";

    public override string CustomVisualPath => SceneHelper.GetScenePath("creature_visuals/" + PlaceholderID);

    public override string CustomTrailPath => SceneHelper.GetScenePath("vfx/card_trail_" + PlaceholderID);
    
    public override string? CustomMapMarkerPath
        => ImageHelper.GetImagePath("packed/map/icons/map_marker_" + PlaceholderID + ".png");

    public override string CustomIconPath => SceneHelper.GetScenePath("ui/character_icons/" + PlaceholderID + "_icon");

    public override string? CustomIconTexturePath
        => ImageHelper.GetImagePath("ui/top_panel/character_icon_" + PlaceholderID + ".png");

    public override string CustomEnergyCounterPath =>
        SceneHelper.GetScenePath("combat/energy_counters/" + PlaceholderID + "_energy_counter");

    public override string CustomRestSiteAnimPath =>
        SceneHelper.GetScenePath("rest_site/characters/" + PlaceholderID + "_rest_site");

    public override string CustomMerchantAnimPath =>
        SceneHelper.GetScenePath("merchant/characters/" + PlaceholderID + "_merchant");

    public override string CustomArmPointingTexturePath =>
        ImageHelper.GetImagePath("ui/hands/multiplayer_hand_" + PlaceholderID + "_point.png");

    public override string CustomArmRockTexturePath =>
        ImageHelper.GetImagePath("ui/hands/multiplayer_hand_" + PlaceholderID + "_rock.png");

    public override string CustomArmPaperTexturePath =>
        ImageHelper.GetImagePath("ui/hands/multiplayer_hand_" + PlaceholderID + "_paper.png");

    public override string CustomArmScissorsTexturePath =>
        ImageHelper.GetImagePath("ui/hands/multiplayer_hand_" + PlaceholderID + "_scissors.png");

    public override string CustomCharacterSelectBg =>
        SceneHelper.GetScenePath("screens/char_select/char_select_bg_" + PlaceholderID);

    public override string CustomCharacterSelectTransitionPath =>
        "res://materials/transitions/" + PlaceholderID + "_transition_mat.tres";

    public override string? CustomCharacterSelectIconPath
        => ImageHelper.GetImagePath("packed/character_select/char_select_" + PlaceholderID + ".png");

    public override string? CustomCharacterSelectLockedIconPath
        => ImageHelper.GetImagePath("packed/character_select/char_select_" + PlaceholderID + "_locked.png");

    public override string CharacterSelectSfx => $"event:/sfx/characters/{PlaceholderID}/{PlaceholderID}_select";

    public override string CustomAttackSfx => $"event:/sfx/characters/{PlaceholderID}/{PlaceholderID}_attack";

    public override string CustomCastSfx => $"event:/sfx/characters/{PlaceholderID}/{PlaceholderID}_cast";

    public override string CustomDeathSfx => $"event:/sfx/characters/{PlaceholderID}/{PlaceholderID}_die";

    public override List<string> GetArchitectAttackVfx()
    {
        int num = 5;
        List<string> list = new(num);
        CollectionsMarshal.SetCount(list, num);
        Span<string> span = CollectionsMarshal.AsSpan(list);
        int num2 = 0;
        span[num2] = "vfx/vfx_attack_blunt";
        num2++;
        span[num2] = "vfx/vfx_heavy_blunt";
        num2++;
        span[num2] = "vfx/vfx_attack_slash";
        num2++;
        span[num2] = "vfx/vfx_bloody_impact";
        num2++;
        span[num2] = "vfx/vfx_rock_shatter";
        return list; //is this just decompiler optimized code or was it coded this way?
    }
}
