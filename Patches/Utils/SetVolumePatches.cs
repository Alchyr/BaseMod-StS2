using BaseLib.Audio;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;

namespace BaseLib.Patches.Utils;

static class SetVolumePatches
{
    [HarmonyPatch(typeof(SettingsSave), nameof(SettingsSave.VolumeMaster), MethodType.Setter)]
    static class MasterVol
    {
        [HarmonyPostfix]
        static void UpdateVolumes()
        {
            ModAudio.UpdateVolumes();
        }
    }
    /*[HarmonyPatch(nameof(SettingsSave.VolumeSfx), MethodType.Setter)]
    static class SfxVol
    {
        [HarmonyPostfix]
        static void UpdateVolumes()
        {
            ModAudio.UpdateVolumes();
        }
    }*/
    [HarmonyPatch(typeof(SettingsSave), nameof(SettingsSave.VolumeBgm), MethodType.Setter)]
    static class MusicVol
    {
        [HarmonyPostfix]
        static void UpdateVolumes()
        {
            ModAudio.UpdateVolumes();
        }
    }
    [HarmonyPatch(typeof(SettingsSave), nameof(SettingsSave.VolumeAmbience), MethodType.Setter)]
    static class AmbienceVol
    {
        [HarmonyPostfix]
        static void UpdateVolumes()
        {
            ModAudio.UpdateVolumes();
        }
    }
}