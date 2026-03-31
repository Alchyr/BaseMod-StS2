namespace BaseLib.Config;

public static class ModConfigRegistry
{
    private static readonly Dictionary<string, ModConfig> ModConfigs = new();

    public static void Register(string modId, ModConfig config)
    {
        if (!config.HasSettings()) return;
        ModConfigs[modId] = config;
    }

    public static ModConfig? Get(string? modId)
    {
        if (modId == null) return null;
        return ModConfigs.GetValueOrDefault(modId);
    }

    public static T? Get<T>() where T : ModConfig
    {
        return ModConfigs.Values.OfType<T>().FirstOrDefault();
    }

    public static List<ModConfig> GetAll() => ModConfigs.Values.ToList();
}