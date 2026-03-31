using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace BaseLib.Patches.UI;

class RoomIconPathPatch
{
    [HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconPath))]
    static class MainImage
    {
        [HarmonyPrefix]
        static bool CustomPath(MapPointType mapPointType, RoomType roomType, ModelId? modelId, ref string? __result)
        {
            if (modelId != null && ModelDb.GetById<AbstractModel>(modelId) is ICustomModel customModel)
            {
                switch (customModel)
                {
                    case CustomAncientModel ancient:
                        BaseLibMain.Logger.Info("Using custom ancient room path");
                        __result = ancient.CustomRunHistoryIconPath;
                        return __result == null;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconOutlinePath))]
    static class OutlineImage
    {
        [HarmonyPrefix]
        static bool CustomOutlinePath(MapPointType mapPointType, RoomType roomType, ModelId? modelId, ref string? __result)
        {
            if (modelId != null && ModelDb.GetById<AbstractModel>(modelId) is ICustomModel customModel)
            {
                switch (customModel)
                {
                    case CustomAncientModel ancient:
                        BaseLibMain.Logger.Info("Using custom ancient outline path");
                        __result = ancient.CustomRunHistoryIconOutlinePath;
                        return __result == null;
                }
            }

            return true;
        }
    }
}