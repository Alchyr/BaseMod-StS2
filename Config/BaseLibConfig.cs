namespace BaseLib.Config;

[HoverTipsByDefault]
internal class BaseLibConfig : SimpleModConfig
{
    [ConfigSection("LogSection")]
    public static bool OpenLogWindowOnStartup { get; set; } = false;

    [SliderRange(128, 2048, 64)]
    [SliderLabelFormat("{0:0} lines")]
    public static double LimitedLogSize { get; set; } = 256;

    [SliderRange(8, 48)]
    [SliderLabelFormat("{0:0} px")]
    public static double LogFontSize { get; set; } = 14;

    [ConfigHideInUI] public static int LastLogLevel { get; set; } = 3; // Default to Info
    [ConfigHideInUI] public static bool LogUseRegex { get; set; } = false;
    [ConfigHideInUI] public static bool LogInvertFilter { get; set; } = false;
    [ConfigHideInUI] public static string LogLastFilter { get; set; } = "";
}