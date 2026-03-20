using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using BaseLib.Abstracts;

namespace BaseLib.Utils;

/// <summary>
/// Central audio helper for mods depending on BaseLib. Resolves streams under each
/// character's <see cref="CustomCharacterModel.CustomAudioPath"/> (mod res root).
/// </summary>
public static class ModAudioHub
{
    private static readonly Dictionary<string, AudioStream> CachedStreams = new();
    private static readonly Dictionary<string, string> CharacterIdToRoot = new();
    private static bool _discovered;

    private static AudioStreamPlayer? _musicPlayer;
    private static string? _currentMusicPath;
    private static float _currentVolumeOffset;
    private static Tween? _fadeTween;
    private static AudioStreamPlayer? _outgoingPlayer;
    private static Tween? _outgoingFadeTween;
    private static AudioStreamPlayer? _ambiencePlayer;
    private static string? _currentAmbiencePath;
    private static Tween? _ambienceFadeTween;

    private const float MusicVolumeOffset = -6f;
    private const float AmbienceVolumeOffset = -6f;
    private const float SfxVolumeOffset = -3f;


    public static void Register(string characterId, string modAudioResRoot)
    {
        if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(modAudioResRoot))
            return;
        CharacterIdToRoot[characterId] = modAudioResRoot.TrimEnd('/');
    }

    /// <summary>
    /// Scans loaded assemblies for concrete <see cref="PlaceholderCharacterModel"/> types,
    /// instantiates them, and registers non-empty <see cref="CustomCharacterModel.CustomAudioPath"/>.
    /// </summary>
    public static void DiscoverPlaceholderCharacters()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
                continue;

            IEnumerable<Type> types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null)!;
            }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract || !typeof(PlaceholderCharacterModel).IsAssignableFrom(type))
                    continue;
                if (type.Assembly == typeof(PlaceholderCharacterModel).Assembly)
                    continue;

                CustomCharacterModel? instance;
                try
                {
                    instance = Activator.CreateInstance(type) as CustomCharacterModel;
                }
                catch
                {
                    continue;
                }

                if (instance == null)
                    continue;

                var root = instance.CustomAudioPath;
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                var id = instance is PlaceholderCharacterModel ph && !string.IsNullOrWhiteSpace(ph.PlaceholderID)
                    ? ph.PlaceholderID
                    : type.Name;

                Register(id, root);
            }
        }
    }

    private static void EnsureDiscovered()
    {
        if (_discovered)
            return;
        _discovered = true;
        DiscoverPlaceholderCharacters();
    }

    private static bool TryResolveRoot(string characterId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? root)
    {
        EnsureDiscovered();
        return CharacterIdToRoot.TryGetValue(characterId, out root);
    }

    private static bool TryResolveRoot(CustomCharacterModel? model, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? root)
    {
        root = model?.CustomAudioPath?.TrimEnd('/');
        if (!string.IsNullOrEmpty(root))
            return true;

        EnsureDiscovered();
        if (model is PlaceholderCharacterModel ph && !string.IsNullOrWhiteSpace(ph.PlaceholderID) &&
            CharacterIdToRoot.TryGetValue(ph.PlaceholderID, out root))
            return true;

        return false;
    }

    public static void Play(string characterId, string folder, string soundName, float volume = 0f, float pitchVariation = 0f,
        float basePitch = 1f)
    {
        if (!TryResolveRoot(characterId, out var modRoot))
            return;
        PlayFromRoot(modRoot, folder, soundName, volume, pitchVariation, basePitch);
    }

    public static void Play(CustomCharacterModel model, string folder, string soundName, float volume = 0f,
        float pitchVariation = 0f, float basePitch = 1f)
    {
        if (!TryResolveRoot(model, out var modRoot))
            return;
        PlayFromRoot(modRoot, folder, soundName, volume, pitchVariation, basePitch);
    }

    private static void PlayFromRoot(string modRoot, string folder, string soundName, float volume, float pitchVariation,
        float basePitch)
    {
        var stream = GetOrLoadStream(modRoot, folder, soundName);
        if (stream == null)
            return;

        var player = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = volume + SfxVolumeOffset,
            Bus = "SFX",
            PitchScale = pitchVariation > 0f
                ? basePitch + (float)Rng.Chaotic.NextDouble() * 2f * pitchVariation - pitchVariation
                : basePitch
        };

        var combatRoom = NCombatRoom.Instance;
        if (combatRoom != null)
        {
            combatRoom.AddChild(player);
            player.Play();
            player.Finished += () => player.QueueFree();
        }
    }

    /// <summary>Legacy-style call: creature parameter ignored (same as per-mod ModAudio).</summary>
    public static void Play(Creature creature, string characterId, string folder, string soundName, float volume = 0f) =>
        Play(characterId, folder, soundName, volume);

    public static void PlaySfx(string characterId, string soundName, float volume = 0f, float pitchVariation = 0f) =>
        Play(characterId, "", soundName, volume, pitchVariation);

    public static void PlayGlobalSfx(string characterId, string soundName, float volume = 0f)
    {
        if (!TryResolveRoot(characterId, out var modRoot))
            return;
        PlayGlobalSfxFromRoot(modRoot, soundName, volume);
    }

    public static void PlayGlobalSfx(CustomCharacterModel model, string soundName, float volume = 0f)
    {
        if (!TryResolveRoot(model, out var modRoot))
            return;
        PlayGlobalSfxFromRoot(modRoot, soundName, volume);
    }

    private static void PlayGlobalSfxFromRoot(string modRoot, string soundName, float volume)
    {
        var stream = GetOrLoadStream(modRoot, "", soundName);
        if (stream == null)
            return;

        var player = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = volume + SfxVolumeOffset,
            Bus = "SFX"
        };

        var tree = Engine.GetMainLoop() as SceneTree;
        var root = tree?.Root;
        if (root == null)
            return;

        root.AddChild(player);
        player.Play();
        player.Finished += () => player.QueueFree();
    }

    private static AudioStream? GetOrLoadStream(string modRoot, string folder, string soundName)
    {
        var key = $"{modRoot}|{(string.IsNullOrEmpty(folder) ? soundName : $"{folder}/{soundName}")}";
        if (CachedStreams.TryGetValue(key, out var cached))
            return cached;

        var path = string.IsNullOrEmpty(folder)
            ? $"{modRoot}/audio/sfx/{soundName}.ogg"
            : $"{modRoot}/audio/sfx/{folder}/{soundName}.ogg";
        var stream = GD.Load<AudioStream>(path);
        if (stream != null)
            CachedStreams[key] = stream;

        return stream;
    }

    public static void PlayMusic(string characterId, string[] musicOptions, float volumeDbOffset = 0f)
    {
        if (musicOptions == null || musicOptions.Length == 0)
            return;
        if (!TryResolveRoot(characterId, out var modRoot))
            return;

        var musicName = musicOptions[GD.RandRange(0, musicOptions.Length - 1)];
        var path = $"{modRoot}/audio/bgm/{musicName}.ogg";

        if (_currentMusicPath == path && _musicPlayer?.Playing == true)
            return;

        StopMusic();

        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
            return;

        if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = true;

        _musicPlayer = new AudioStreamPlayer
        {
            Stream = stream,
            Bus = "Master"
        };

        _currentVolumeOffset = volumeDbOffset;
        var bgmVolume = SaveManager.Instance.SettingsSave.VolumeBgm;
        _musicPlayer.VolumeDb = Mathf.LinearToDb(Mathf.Pow(bgmVolume, 2f)) + _currentVolumeOffset + MusicVolumeOffset;

        var runNode = NRun.Instance;
        if (runNode != null)
        {
            runNode.AddChild(_musicPlayer);
            _musicPlayer.Play();
            _currentMusicPath = path;
        }
    }

    public static void SetMusicVolume(float volume)
    {
        if (_musicPlayer != null && GodotObject.IsInstanceValid(_musicPlayer))
            _musicPlayer.VolumeDb = Mathf.LinearToDb(Mathf.Pow(volume, 2f)) + _currentVolumeOffset + MusicVolumeOffset;
    }

    public static void FadeIn(string characterId, string[] musicOptions, float duration = 1.0f, float volumeDbOffset = 0f)
    {
        if (musicOptions == null || musicOptions.Length == 0)
            return;
        if (!TryResolveRoot(characterId, out var modRoot))
            return;

        var musicName = musicOptions[GD.RandRange(0, musicOptions.Length - 1)];
        var path = $"{modRoot}/audio/bgm/{musicName}.ogg";

        if (_currentMusicPath == path && _musicPlayer?.Playing == true)
            return;

        if (_musicPlayer != null && GodotObject.IsInstanceValid(_musicPlayer))
        {
            _outgoingFadeTween?.Kill();
            _outgoingPlayer?.QueueFree();

            _outgoingPlayer = _musicPlayer;
            _outgoingFadeTween = _outgoingPlayer.CreateTween();
            _outgoingFadeTween.TweenProperty(_outgoingPlayer, "volume_db", -80f, duration)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.In);
            _outgoingFadeTween.TweenCallback(Callable.From(() =>
            {
                _outgoingPlayer?.QueueFree();
                _outgoingPlayer = null;
            }));
        }

        _fadeTween?.Kill();
        _musicPlayer = null;
        _currentMusicPath = null;

        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
            return;

        if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = true;

        _musicPlayer = new AudioStreamPlayer
        {
            Stream = stream,
            Bus = "Master",
            VolumeDb = -80f
        };

        _currentVolumeOffset = volumeDbOffset;

        var runNode = NRun.Instance;
        if (runNode != null)
        {
            runNode.AddChild(_musicPlayer);
            _musicPlayer.Play();
            _currentMusicPath = path;

            var targetDb = Mathf.LinearToDb(Mathf.Pow(SaveManager.Instance.SettingsSave.VolumeBgm, 2f))
                + _currentVolumeOffset + MusicVolumeOffset;

            _fadeTween = _musicPlayer.CreateTween();
            _fadeTween.TweenProperty(_musicPlayer, "volume_db", targetDb, duration)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
        }
    }

    public static void FadeOut(float duration = 1.0f)
    {
        if (_musicPlayer == null || !GodotObject.IsInstanceValid(_musicPlayer))
            return;

        _fadeTween?.Kill();
        _fadeTween = _musicPlayer.CreateTween();
        _fadeTween.TweenProperty(_musicPlayer, "volume_db", -80f, duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        _fadeTween.TweenCallback(Callable.From(StopMusicImmediate));
    }

    private static void StopMusicImmediate()
    {
        _fadeTween?.Kill();
        _fadeTween = null;
        _outgoingFadeTween?.Kill();
        _outgoingFadeTween = null;

        if (_musicPlayer != null && GodotObject.IsInstanceValid(_musicPlayer))
        {
            _musicPlayer.Stop();
            _musicPlayer.QueueFree();
        }
        _musicPlayer = null;
        _currentMusicPath = null;

        if (_outgoingPlayer != null && GodotObject.IsInstanceValid(_outgoingPlayer))
        {
            _outgoingPlayer.Stop();
            _outgoingPlayer.QueueFree();
        }
        _outgoingPlayer = null;
    }

    public static void StopMusic() => StopMusicImmediate();

    public static bool IsPlayingLegacyMusic() => _musicPlayer?.Playing == true;

    public static void PlayAmbience(string characterId, string ambienceName, float volumeDbOffset = 0f)
    {
        if (!TryResolveRoot(characterId, out var modRoot))
            return;

        var path = $"{modRoot}/audio/bgm/{ambienceName}.ogg";

        if (_currentAmbiencePath == path && _ambiencePlayer?.Playing == true)
            return;

        StopAmbience();

        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
            return;

        if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = true;

        _ambiencePlayer = new AudioStreamPlayer
        {
            Stream = stream,
            Bus = "Master"
        };

        var ambienceVolume = SaveManager.Instance.SettingsSave.VolumeAmbience;
        _ambiencePlayer.VolumeDb = Mathf.LinearToDb(Mathf.Pow(ambienceVolume, 2f)) + volumeDbOffset + AmbienceVolumeOffset;

        var runNode = NRun.Instance;
        if (runNode != null)
        {
            runNode.AddChild(_ambiencePlayer);
            _ambiencePlayer.Play();
            _currentAmbiencePath = path;
        }
    }

    public static void FadeInAmbience(string characterId, string ambienceName, float duration = 1.0f,
        float volumeDbOffset = 0f)
    {
        if (!TryResolveRoot(characterId, out var modRoot))
            return;

        var path = $"{modRoot}/audio/bgm/{ambienceName}.ogg";

        if (_currentAmbiencePath == path && _ambiencePlayer?.Playing == true)
            return;

        StopAmbience();

        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
            return;

        if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = true;

        _ambiencePlayer = new AudioStreamPlayer
        {
            Stream = stream,
            Bus = "Master",
            VolumeDb = -80f
        };

        var runNode = NRun.Instance;
        if (runNode != null)
        {
            runNode.AddChild(_ambiencePlayer);
            _ambiencePlayer.Play();
            _currentAmbiencePath = path;

            var targetDb = Mathf.LinearToDb(Mathf.Pow(SaveManager.Instance.SettingsSave.VolumeAmbience, 2f))
                + volumeDbOffset + AmbienceVolumeOffset;

            _ambienceFadeTween = _ambiencePlayer.CreateTween();
            _ambienceFadeTween.TweenProperty(_ambiencePlayer, "volume_db", targetDb, duration)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
        }
    }

    public static void FadeOutAmbience(float duration = 1.0f)
    {
        if (_ambiencePlayer == null || !GodotObject.IsInstanceValid(_ambiencePlayer))
            return;

        _ambienceFadeTween?.Kill();
        _ambienceFadeTween = _ambiencePlayer.CreateTween();
        _ambienceFadeTween.TweenProperty(_ambiencePlayer, "volume_db", -80f, duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        _ambienceFadeTween.TweenCallback(Callable.From(StopAmbience));
    }

    public static void StopAmbience()
    {
        _ambienceFadeTween?.Kill();
        _ambienceFadeTween = null;

        if (_ambiencePlayer != null && GodotObject.IsInstanceValid(_ambiencePlayer))
        {
            _ambiencePlayer.Stop();
            _ambiencePlayer.QueueFree();
        }
        _ambiencePlayer = null;
        _currentAmbiencePath = null;
    }

    public static void SetAmbienceVolume(float volume)
    {
        if (_ambiencePlayer != null && GodotObject.IsInstanceValid(_ambiencePlayer))
            _ambiencePlayer.VolumeDb = Mathf.LinearToDb(Mathf.Pow(volume, 2f)) + AmbienceVolumeOffset;
    }
}
