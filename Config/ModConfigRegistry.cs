namespace BaseLib.Config;

public static class ModConfigRegistry
{
    private static readonly Dictionary<string, ModConfig> ModConfigs = new();

    public static void Register(string modId, ModConfig config)
    {
        if (!config.HasSettings()) return;
        ModConfigs[modId] = config;
    }

    public static ModConfig? Get(string modId)
    {
        return ModConfigs.GetValueOrDefault(modId);
    }
}
