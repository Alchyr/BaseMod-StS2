using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(RunManager))]
public static class RunManagerPatches
{

    internal static List<Type> customMessageTypes = [..ReflectionHelper.GetSubtypesInMods<CustomMessage>()];

    // currently duplicates the registration for CustomRewardMessages? maybe remove that part since it happens at the same time
    [HarmonyPatch(nameof(RunManager.InitializeShared))]
    [HarmonyPostfix]
    public static void InitializeCustomMessageHandlers(RunManager __instance)
    {
        foreach (var messageType in customMessageTypes)
        {
            if (messageType.CreateInstance() is not CustomMessage dummyMessage)
            {
                BaseLibMain.Logger.Error($"Message instance creation for type {messageType.GetType()} from {messageType.Assembly} failed during Initialize");
                continue;
            }
            dummyMessage.Initialize(__instance.RunLocationTargetedBuffer);
        }
    }

    [HarmonyPatch(nameof(RunManager.CleanUp))]
    [HarmonyPostfix]
    public static void DisposeCustomMessageHandlers(RunManager __instance)
    {
        foreach (var messageType in customMessageTypes)
        {
            if (messageType.CreateInstance() is not CustomMessage dummyMessage)
            {
                BaseLibMain.Logger.Error($"Message instance creation for type {messageType.GetType()} from {messageType.Assembly} failed during Dispose");
                continue;
            }
            dummyMessage.Dispose(__instance.RunLocationTargetedBuffer);
        }
    }
}
