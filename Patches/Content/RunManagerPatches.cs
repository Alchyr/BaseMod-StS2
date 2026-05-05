using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(RunManager))]
internal static class RunManagerPatches
{
    /// <summary>
    /// Potentially for future usage if we get the basegame messages to not automatically include mod messages
    /// </summary>
    private static readonly List<Type> allCustomMessages = [..ReflectionHelper.GetSubtypesInMods<ICustomMessage>()];
    private static readonly List<Type> customMessageTypes = [..ReflectionHelper.GetSubtypesInMods<CustomMessage>()];
    private static readonly List<Type> customTargetedMessageTypes = [..ReflectionHelper.GetSubtypesInMods<CustomTargetedMessage>()];

    [HarmonyPatch(nameof(RunManager.InitializeShared))]
    [HarmonyPostfix]
    private static void InitializeCustomMessageHandlers(RunManager __instance)
    {
        foreach (var messageType in customMessageTypes)
        {
            var dummyMessage = messageType.CreateInstance();
            if (dummyMessage == null)
            {
                BaseLibMain.Logger.Error(
                        $"CustomMessage instance creation for type {messageType.GetType()} from {messageType.Assembly} failed during Initialize");
                continue;
            }

            if (dummyMessage is CustomMessage customMessage)
            {
                customMessage.Initialize(__instance.NetService);
            }
        }


        foreach (var targetedMessageType in customTargetedMessageTypes)
        {
            var dummyTargetedMessage = targetedMessageType.CreateInstance();
            if (dummyTargetedMessage == null)
            {
                BaseLibMain.Logger.Error(
                        $"CustomTargetedMessage instance creation for type {targetedMessageType.GetType()} from {targetedMessageType.Assembly} failed during Initialize");
                continue;
            }
            // Need to double check that all the targeted messages are sent to this one handler in the base game
            if (dummyTargetedMessage is CustomTargetedMessage targetedMessage)
            {
                targetedMessage.Initialize(__instance.RunLocationTargetedBuffer);
            }
        }
    }

    [HarmonyPatch(nameof(RunManager.CleanUp))]
    [HarmonyPostfix]
    private static void DisposeCustomMessageHandlers(RunManager __instance)
    {
        foreach (var messageType in customMessageTypes)
        {
            if (messageType.CreateInstance() is not CustomMessage dummyMessage)
            {
                BaseLibMain.Logger.Error(
                        $"CustomMessage instance creation for type {messageType.GetType()} from {messageType.Assembly} failed during Dispose");
                continue;
            }
            dummyMessage.Dispose(__instance.NetService);
        }

        foreach (var targetedMessageType in customTargetedMessageTypes)
        {
            if (targetedMessageType.CreateInstance() is not CustomTargetedMessage dummyMessage)
            {
                BaseLibMain.Logger.Error(
                        $"CustomTargetedMessage instance creation for type {targetedMessageType.GetType()} from {targetedMessageType.Assembly} failed during Dispose");
                continue;
            }
            dummyMessage.Dispose(__instance.RunLocationTargetedBuffer);
        }
    }
}
