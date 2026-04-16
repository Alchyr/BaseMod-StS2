using BaseLib.Diagnostics;
using Godot;

namespace BaseLib.Config;

[ConfigHoverTipsByDefault]
internal class BaseLibConfig : SimpleModConfig
{
    // Should likely be at the top, as an easy and obvious opt-out
    public static bool ShowModConfigInMainMenu { get; set; } = true;

    [ConfigSection("LogSection")]
    public static bool OpenLogWindowOnStartup { get; set; } = false;
    public static bool OpenLogWindowOnError { get; set; } = false;

    [ConfigSlider(128, 2048, 64, Format = "{0:0} lines")]
    public static int LimitedLogSize { get; set; } = 256;

    [ConfigSlider(8, 48, Format = "{0:0} px")]
    public static int LogFontSize { get; set; } = 14;

    [ConfigSection("HarmonyDumpSection")]
    [ConfigTextInput(MaxLength = 1024)]
    public static string HarmonyPatchDumpOutputPath { get; set; } = "";

    public static bool HarmonyPatchDumpOnFirstMainMenu { get; set; }

    [ConfigButton("HarmonyDumpBrowse")]
    public static void HarmonyDumpBrowseForOutput(ModConfig config)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null)
        {
            BaseLibMain.Logger.Warn("[HarmonyDump] Cannot open file dialog: SceneTree root is not available.");
            return;
        }

        var dialog = new FileDialog
        {
            Title = GetBaseLibLabelText("HarmonyDumpBrowseTitle"),
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            CurrentFile = "baselib_harmony_patch_dump.log",
        };
        dialog.AddFilter("*.log", "Log");
        dialog.AddFilter("*.txt", "Text");

        dialog.FileSelected += path =>
        {
            HarmonyPatchDumpOutputPath = path;
            config.Save();
            config.ConfigReloaded();
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;

        tree.Root.AddChild(dialog);
        dialog.PopupCenteredRatio(0.55f);
    }

    [ConfigButton("HarmonyDumpNow")]
    public static void HarmonyDumpWriteNow(ModConfig _)
    {
        HarmonyPatchDumpCoordinator.TryManualDumpFromSettings();
    }

    [ConfigHideInUI] public static int LastLogLevel { get; set; } = 3; // Default to Info
    [ConfigHideInUI] public static bool LogUseRegex { get; set; } = false;
    [ConfigHideInUI] public static bool LogInvertFilter { get; set; } = false;
    [ConfigHideInUI] public static string LogLastFilter { get; set; } = "";
    [ConfigHideInUI] public static int LogLastSizeX { get; set; } = 0;
    [ConfigHideInUI] public static int LogLastSizeY { get; set; } = 0;
    [ConfigHideInUI] public static int LogLastPosX { get; set; } = int.MinValue;
    [ConfigHideInUI] public static int LogLastPosY { get; set; } = int.MinValue;

    [ConfigHideInUI] public static string LastModConfigModId { get; set; } = "";
}