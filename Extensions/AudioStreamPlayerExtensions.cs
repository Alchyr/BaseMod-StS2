using BaseLib.Utils;
using Godot;

namespace BaseLib.Extensions;

public static class AudioStreamPlayerExtensions
{
    private static readonly NodePath VolumeDb = "volume_db";
    public static readonly SpireField<AudioStreamPlayer, Tween> CurrentTween = new(() => null);
    
    /// <summary>
    /// Intended for use with AudioStreamPlayers returned by ModAudio play methods.
    /// Fades in audio over specified duration.
    /// </summary>
    public static void FadeIn(this AudioStreamPlayer player, float duration)
    {
        CurrentTween[player]?.Kill();
        
        float targetVol = player.VolumeDb;
        player.VolumeDb = -80;
        
        var tween = player.CreateTween();
        tween.TweenProperty(player, VolumeDb, targetVol, duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        CurrentTween[player] = tween;
    }
    
    /// <summary>
    /// Intended for use with AudioStreamPlayers returned by ModAudio play methods.
    /// Fades out audio over specified duration and removes it from tree.
    /// </summary>
    public static void FadeOut(this AudioStreamPlayer player, float duration)
    {
        CurrentTween[player]?.Kill();
        
        var tween = player.CreateTween();
        tween.TweenProperty(player, VolumeDb, -80, duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() => player.GetParent()?.RemoveChild(player)));
        CurrentTween[player] = null; //Cannot be cancelled.
    }
}