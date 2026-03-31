using Godot;

namespace BaseLib.Utils;

/// <summary>
/// Utility class for handling playing animations using Godot's built-in nodes.
/// </summary>
public static class CustomAnimation
{
    private static SpireField<Node, Func<string[], bool>> _animHandler = new(() => null);
    
    public static bool PlayCustomAnimation(Node n, params string[] tryAnimNames)
    {
        if (_animHandler[n] == null)
        {
            _animHandler[n] = FindNode<AnimationPlayer>(n)?.UseAnimationPlayer() ??
                              FindNode<AnimatedSprite2D>(n)?.UseAnimatedSprite2D() ??
                              SearchRecursive<AnimationPlayer>(n)?.UseAnimationPlayer() ??
                              SearchRecursive<AnimatedSprite2D>(n)?.UseAnimatedSprite2D();

        }
        return _animHandler[n]?.Invoke(tryAnimNames) != null;
    }

    private static Func<string[], bool> UseAnimatedSprite2D(this AnimatedSprite2D animSprite)
    {
        return (animNames) =>
        {
            foreach (var name in animNames)
            {
                if (animSprite.SpriteFrames.HasAnimation(name))
                {
                    animSprite.Play(name);
                    return true;
                }
            }
            
            return false;
        };
    }

    private static Func<string[], bool> UseAnimationPlayer(this AnimationPlayer animPlayer)
    {
        return (animNames) =>
        {
            foreach (var name in animNames)
            {
                if (animPlayer.HasAnimation(name))
                {
                    if (animPlayer.CurrentAnimation.Equals(name))
                        animPlayer.Stop();
                
                    animPlayer.Play(name);
                    return true;
                }
            }

            return false;
        };
    }

    private static T? FindNode<T>(Node root, string? name = null) where T : Node?
    {
        name ??= nameof(T);
        var n = root.GetNodeOrNull(name)
                ?? root.GetNodeOrNull("Visuals/" + name)
                ?? root.GetNodeOrNull("Body/" + name);
        return n as T;
    }

    private static T? SearchRecursive<T>(Node parent) where T : Node?
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is T nodeToFind) return nodeToFind;
            var found = SearchRecursive<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}