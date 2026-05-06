using BaseLib.Audio;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.TestSupport;

namespace BaseLib.Patches.Utils;

static class PlayResourcePatch
{
    [HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot), 
        typeof(string), typeof(Dictionary<string, float>), typeof(float))]
    static class OneShot
    {
        [HarmonyPrefix]
        static bool PlayResource(string path, Dictionary<string, float> parameters, float volume)
        {
            if (TestMode.IsOn) return true;

            if (path.StartsWith("res://") && ResourceLoader.Exists(path))
            {
                ModAudio.PlaySoundInRun(path, volumeMult: volume);
                return false;
            }

            return true;
        }
    }
    
    [HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayMusic))]
    static class Music
    {
        [HarmonyPrefix]
        static bool PlayMusic(string music)
        {
            if (TestMode.IsOn) return true;

            if (music.StartsWith("res://") && ResourceLoader.Exists(music))
            {
                ModAudio.PlaySoundInRun(new ModSound(music, ModAudio.SoundType.Music));
                return false;
            }

            return true;
        }
    }
}