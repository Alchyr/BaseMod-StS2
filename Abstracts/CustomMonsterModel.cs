using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Abstracts;

public abstract class CustomMonsterModel : MonsterModel, ICustomModel
{
    /// <summary>
    /// Override this or place your scene at res://scenes/creature_visuals/class_name.tscn
    /// </summary>
    public virtual string? CustomVisualPath => null;
    
    
    /// <summary>
    /// By default, will convert a scene containing the necessary nodes into a NCreatureVisuals even if it is not one.
    /// </summary>
    /// <returns></returns>
    public virtual NCreatureVisuals? CreateCustomVisuals() {
        string? path = (CustomVisualPath ?? VisualsPath);
        if (path == null) return null;
        return GodotUtils.CreatureVisualsFromScene(path);
    }
    
    
    /// <summary>
    /// Override and return a CreatureAnimator if you need to set up states that differ from the default for your character.
    /// Using <seealso cref="SetupAnimationState"/> is suggested.
    /// </summary>
    /// <returns></returns>
    public virtual CreatureAnimator? SetupCustomAnimationStates(MegaSprite controller)
    {
        return null;
    }
}