using BaseLib.Config;
using BaseLib.Extensions;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;

namespace BaseLib.Audio;

//TODO: Likely additional patches are needed for music/ambience playback to behave alongside basegame music/ambience.

/// <summary>
/// A class used to play sounds for mods.
/// </summary>
public static class ModAudio
{
    private class StreamPlayerPool
    {
        public Queue<AudioStreamPlayer> Players = [];
        public int MaxCount = 0;
    }
    
    public static readonly SpireField<AudioStreamPlayer, Func<float, float>> VolumeModDb = new(() => (val => val));
    
    public enum SoundType
    {
        Sfx,
        Music,
        Ambience
    }
    
    //These are the default godot buses; they are not actually used by basegame.
    internal static readonly StringName SfxBus = "SFX";
    internal static readonly StringName MasterBus = "Master";
    
    private static Dictionary<SoundType, StreamPlayerPool> _playerPools = [];
    private static List<AudioStreamPlayer> _activeMusic = [];
    private static List<AudioStreamPlayer> _activeAmbience = [];
    
    static ModAudio()
    {
        _playerPools[SoundType.Sfx] = new();
        _playerPools[SoundType.Music] = new();
        _playerPools[SoundType.Ambience] = new();
    }

    private static int LimitForSoundType(SoundType soundType) => soundType switch
    {
        SoundType.Sfx => BaseLibConfig.SfxPlayerLimit,
        SoundType.Music => 2,
        SoundType.Ambience => 2,
        _ => 0
    };

    private static float MasterVol => SaveManager.Instance.SettingsSave.VolumeMaster;
    private static float VolumeForSound(SoundType soundType) => soundType switch
    {
        SoundType.Music => Mathf.LinearToDb(MasterVol * SaveManager.Instance.SettingsSave.VolumeBgm) + 3,
        SoundType.Ambience => Mathf.LinearToDb(MasterVol * SaveManager.Instance.SettingsSave.VolumeAmbience) + 3,
        _ => Mathf.LinearToDb(MasterVol * SaveManager.Instance.SettingsSave.VolumeSfx) + 3
    };

    private static StringName BusForSound(SoundType soundType) => soundType switch
    {
        SoundType.Sfx => SfxBus,
        _ => MasterBus
    };

    public static void UpdateVolumes()
    {
        float musicVol = VolumeForSound(SoundType.Music);
        float ambienceVol = VolumeForSound(SoundType.Ambience);

        foreach (var player in _activeAmbience)
        {
            AudioStreamPlayerExtensions.CurrentTween[player]?.Kill();
            player.VolumeDb = VolumeModDb[player]!(ambienceVol);
        }

        foreach (var player in _activeMusic)
        {
            AudioStreamPlayerExtensions.CurrentTween[player]?.Kill();
            player.VolumeDb = VolumeModDb[player]!(musicVol);
        }
    }

    internal static AudioStreamPlayer? GetPlayerForSound(SoundType soundType)
    {
        if (!_playerPools.TryGetValue(soundType, out var players))
        {
            throw new ArgumentException($"Sound type '{(int) soundType}' not found");
        }
        
        //BaseLibMain.Logger.Info($"Players for sound type {soundType}: {players.MaxCount}");
        
        if (!players.Players.TryDequeue(out var player))
        {
            if (players.MaxCount >= LimitForSoundType(soundType))
            {
                BaseLibMain.Logger.Warn($"Too many sounds for sound type '{soundType}'!");
                return null;
            }
            
            BaseLibMain.Logger.Info($"Creating new player for {soundType} (Count: {players.MaxCount + 1})");
            
            player = new AudioStreamPlayer { Bus = BusForSound(soundType) };
            player.TreeEntered += () =>
            {
                player.Play();
            };
            player.Finished += () =>
            {
                player.GetParent()?.RemoveChildSafely(player);
            };
            player.TreeExited += () =>
            {
                player.Stream = null;
                players.Players.Enqueue(player);
            };
            switch (soundType)
            {
                case SoundType.Music:
                    player.TreeEntered += () =>
                    {
                        _activeMusic.Add(player);
                    };
                    player.TreeExited += () =>
                    {
                        _activeMusic.Remove(player);
                    };
                    break;
                case SoundType.Ambience:
                    player.TreeEntered += () =>
                    {
                        _activeAmbience.Add(player);
                    };
                    player.TreeExited += () =>
                    {
                        _activeAmbience.Remove(player);
                    };
                    break;
                
            }
            players.MaxCount += 1;
        }
        
        return player;
    }

    /// <summary>
    /// A sound that will be played attached to the entire game (not cancelled if exiting run)
    /// </summary>
    public static AudioStreamPlayer? PlaySoundGlobal(ModSound sound, float volumeAdd = 0f, float volumeMult = 1f, float pitchVariation = 0f,
        float basePitch = 1f)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        return PlaySound(sound, volumeAdd, volumeMult, pitchVariation, basePitch, tree?.Root);
    }
    
    /// <summary>
    /// A sound that will be played tied to the current run scene.
    /// </summary>
    public static AudioStreamPlayer? PlaySoundInRun(ModSound sound, float volumeAdd = 0f, float volumeMult = 1f, float pitchVariation = 0f,
        float basePitch = 1f)
    {
        return PlaySound(sound, volumeAdd, volumeMult, pitchVariation, basePitch, NRun.Instance);
    }

    /// <param name="sound">The sound to play</param>
    /// <param name="volumeAdd">Adjustment to volume in dB</param>
    /// <param name="volumeMult">Multiplier on final volume</param>
    /// <param name="pitchVariation">Random pitch (and speed) variation; range is centered on basePitch</param>
    /// <param name="basePitch">Pitch (and speed) to play sound at. 2f is twice the pitch, 0.5f is half the pitch.</param>
    public static AudioStreamPlayer? PlaySound(ModSound sound, float volumeAdd = 0f, float volumeMult = 1f, float pitchVariation = 0f,
        float basePitch = 1f, Node? targetNode = null)
    {
        if (MasterVol <= 0)
            return null;

        if (sound.SoundType switch
            {
                SoundType.Music => SaveManager.Instance.SettingsSave.VolumeBgm,
                SoundType.Ambience => SaveManager.Instance.SettingsSave.VolumeAmbience,
                _ => SaveManager.Instance.SettingsSave.VolumeSfx
            } < 0)
            return null;
        
        if (sound.SoundType == SoundType.Music)
        {
            foreach (var music in _activeMusic)
            {
                if (music.Name == sound.File)
                {
                    return null;
                }
                music.Stop();
                music.GetParent()?.RemoveChildSafely(music);
            }
        }
        else if (sound.SoundType == SoundType.Ambience)
        {
            foreach (var ambience in _activeAmbience)
            {
                if (ambience.Name == sound.File)
                {
                    return null;
                }
                ambience.Stop();
                ambience.GetParent()?.RemoveChildSafely(ambience);
            }
        }
        
        var stream = sound.GetOrLoadStream();
        if (stream == null)
        {
            BaseLibMain.Logger.Warn($"Failed to get stream for sound {sound.File}");
            return null;
        }

        var player = GetPlayerForSound(sound.SoundType);
        if (player == null)
            return null;

        player.Name = sound.File;
        player.Stream = stream;
        player.VolumeDb = (VolumeForSound(sound.SoundType) + volumeAdd) * volumeMult;
        VolumeModDb[player] = val => (val + volumeAdd) * volumeMult;
        player.PitchScale = pitchVariation > 0f
            ? basePitch + (float)Rng.Chaotic.NextDouble() * 2f * pitchVariation - pitchVariation
            : basePitch;

        if (targetNode == null && sound.SoundType == SoundType.Sfx)
        {
            targetNode = NCombatRoom.Instance;
        }

        if (targetNode == null)
        {
            targetNode = NRun.Instance;
        }

        if (targetNode == null)
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            targetNode = tree?.Root;
        }

        if (targetNode != null)
        {
            targetNode.AddChildSafely(player);
            return player;
        }
        
        BaseLibMain.Logger.Warn($"Failed to play sound {sound.File}; unable to find node to attach player");
        player.Stream = null;
        if (_playerPools.TryGetValue(sound.SoundType, out var players))
        {
            players.Players.Enqueue(player);
        }

        return null;
    }
}

/// <summary>
/// A class that allows playing of all audio files within a folder using shortened names.
/// Given a file "res://ModMod/audio/boop.ogg" and an AutoModAudio initialized using the path "res://MyMod/audio",
/// it can be played with PlaySfx("boop.ogg").
///
/// You are suggested to define a static variable in your main file with an instance of this class.
/// Alternatively, you can define a class with static ModSound variables to manually define your sounds.
/// </summary>
/// <param name="folder"></param>
public class AutoModAudio(string folder)
{
    protected readonly string _folder = folder;

    private readonly Dictionary<string, ModSound> _sounds = [];

    /// <param name="volume">Adjustment to volume in dB</param>
    /// <param name="pitchVariation">Random pitch (and speed) variation; range is centered on basePitch</param>
    /// <param name="basePitch">Pitch (and speed) to play sound at. 2f is twice the pitch, 0.5f is half the pitch.</param>
    public AudioStreamPlayer? PlaySfx(string path, float volume = 0f, float volumeMult = 1f, float pitchVariation = 0f,
        float basePitch = 1f)
    {
        if (!_sounds.TryGetValue(path, out var sound))
        {
            sound = new ModSound(ResourceLoader.Exists(path) ? path : Path.Join(_folder, path));
            _sounds[path] = sound;
        }
        
        return ModAudio.PlaySound(sound, volume, pitchVariation, basePitch);
    }
    
    /// <param name="volume">Adjustment to volume in dB</param>
    /// <param name="pitchVariation">Random pitch (and speed) variation; range is centered on basePitch</param>
    /// <param name="basePitch">Pitch (and speed) to play sound at. 2f is twice the pitch, 0.5f is half the pitch.</param>
    public AudioStreamPlayer? PlayMusic(string path, float volume = 0f, float volumeMult = 1f, float pitchVariation = 0f,
        float basePitch = 1f)
    {
        if (!_sounds.TryGetValue(path, out var sound))
        {
            sound = new ModSound(ResourceLoader.Exists(path) ? path : Path.Join(_folder, path), ModAudio.SoundType.Music);
            _sounds[path] = sound;
        }
        
        return ModAudio.PlaySound(sound, volume, pitchVariation, basePitch);
    }
    
    /// <param name="volume">Adjustment to volume in dB</param>
    /// <param name="pitchVariation">Random pitch (and speed) variation; range is centered on basePitch</param>
    /// <param name="basePitch">Pitch (and speed) to play sound at. 2f is twice the pitch, 0.5f is half the pitch.</param>
    public AudioStreamPlayer? PlayAmbience(string path, float volume = 0f, float volumeMult = 1f, float pitchVariation = 0f,
        float basePitch = 1f)
    {
        if (!_sounds.TryGetValue(path, out var sound))
        {
            sound = new ModSound(ResourceLoader.Exists(path) ? path : Path.Join(_folder, path), ModAudio.SoundType.Ambience);
            _sounds[path] = sound;
        }
        
        return ModAudio.PlaySound(sound, volume, pitchVariation, basePitch);
    }
}

public record ModSound
{
    private static readonly Dictionary<string, AudioStream> CachedStreams = new();
    
    public ModSound(string file)
    {
        File = file;
        SoundType = ModAudio.SoundType.Sfx;
    }
    
    public ModSound(string file, ModAudio.SoundType soundType)
    {
        File = file;
        SoundType = soundType;
    }

    public string File { get; set; }
    public ModAudio.SoundType SoundType { get; set; }
    public float VolumeOffset { get; set; } = 0f;


    public virtual AudioStream? GetOrLoadStream()
    {
        if (CachedStreams.TryGetValue(File, out var cached))
            return cached;
        
        var stream = GD.Load<AudioStream>(File);
        if (stream != null && stream.GetLength() < 15)
            CachedStreams[File] = stream;

        return stream;
    }

    /// <param name="volumeAdd">Adjustment to volume in dB</param>
    /// <param name="volumeMult">Multiplier on final volume</param>
    /// <param name="pitchVariation">Random pitch (and speed) variation; range is centered on basePitch</param>
    /// <param name="basePitch">Pitch (and speed) to play sound at. 2f is twice the pitch, 0.5f is half the pitch.</param>
    public AudioStreamPlayer? Play(float volumeAdd = 0f, float volumeMult = 1f, float pitchVariation = 0f, float basePitch = 1f)
    {
        return ModAudio.PlaySound(this, volumeAdd + VolumeOffset, volumeMult, pitchVariation, basePitch);
    }


    private static readonly Dictionary<string, ModSound> _convertedSounds = [];

    /// <summary>
    /// Auto-conversion of string to ModSound, where the string is a resource file path.
    /// </summary>
    /// <returns></returns>
    public static implicit operator ModSound(string path)
    {
        if (!_convertedSounds.TryGetValue(path, out var sound))
        {
            _convertedSounds[path] = sound = new ModSound(path);
        }

        return sound;
    }
}