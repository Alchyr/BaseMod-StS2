using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.TestSupport;

namespace BaseLib.Patches.Misc;

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot), typeof(string), typeof(Dictionary<string, float>), typeof(float))]
class EnergyCounterPatch {
    static bool Prefix(string path, Dictionary<string, float> parameters, float volume, NAudioManager __instance) {
        if (TestMode.IsOn) return true;
        if (!path.StartsWith("res://")) return true;
        AudioStream audioStream = GD.Load<AudioStream>(path);
        if (audioStream is null) return true;
        AudioStreamPlayer2D audioPlayer = new() {
            Bus = "SFX",
            VolumeDb = Mathf.LinearToDb(volume),
            Stream = GD.Load<AudioStream>(path),
        };
        __instance.GetTree().Root.AddChild(audioPlayer);
        audioPlayer.Finished += audioPlayer.QueueFree;
        audioPlayer.Play();
        return false;
    }
}