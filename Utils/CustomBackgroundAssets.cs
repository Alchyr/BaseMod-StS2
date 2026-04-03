using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;

namespace BaseLib.Utils;

public class CustomBackgroundAssets : BackgroundAssets
{
    private static readonly MethodInfo BackgroundScenePathSetter =
        AccessTools.PropertySetter(typeof(BackgroundAssets), nameof(BackgroundScenePath));
    private static readonly MethodInfo FgLayerSetter =
        AccessTools.PropertySetter(typeof(BackgroundAssets), nameof(FgLayer));

    private const string FakeKey = "glory";
    
    public CustomBackgroundAssets(string layersPath, string bgScenePath, Rng rng) : base(FakeKey, Rng.Chaotic)
    {
        BgLayers.Clear();
        
        using (DirAccess dirAccess = DirAccess.Open(layersPath))
        {
            if (dirAccess == null)
                throw new InvalidOperationException("could not find directory " + layersPath);
            
            var bgLayers = new Dictionary<string, List<string>>();
            var stringList = new List<string>();
            dirAccess.ListDirBegin();
            
            for (var next = dirAccess.GetNext(); next != ""; next = dirAccess.GetNext())
            {
                if (dirAccess.CurrentIsDir())
                    throw new InvalidOperationException(
                        "there should be no other directories within the layers directory");
                
                if (next.Contains("_fg_"))
                {
                    stringList.Add($"{layersPath}/{next}");
                }
                else
                {
                    var key = next.Contains("_bg_")
                        ? next.Split("_bg_")[1].Split("_")[0]
                        : throw new InvalidOperationException("files must either contain '_fg_' or '_bg_'");
                    if (!bgLayers.ContainsKey(key))
                        bgLayers.Add(key, []);
                    bgLayers[key].Add($"{layersPath}/{next}");
                }
            }

            BackgroundScenePathSetter.Invoke(this, [bgScenePath]);
            BgLayers.AddRange(SelectRandomBackgroundAssetLayers(rng, bgLayers));
            FgLayerSetter.Invoke(this, [SelectRandomForegroundAssetLayer(rng, stringList)]);
        }
    }
}