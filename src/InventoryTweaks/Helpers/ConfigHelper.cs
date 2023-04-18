using BepInEx.Configuration;

namespace InventoryTweaks.Helpers;

internal class ConfigHelper
{
    public static void LoadConfig(ConfigFile configFile)
    {
        General.LoadConfig(configFile);
    }

    public static class General
    {
        private const string DescriptionEnableRewriteOpenSlots =
            "Change how OpenSlots is serialized to the world data. Instead of 0 for StringHash, put the" +
            " parent's ReferenceId. This will make changes to your save file, but will still be compatible" +
            " with the base game. Can be disabled without affecting saves.";

        private static ConfigEntry<bool> _configEnableRewriteOpenSlots;
        public static bool EnableRewriteOpenSlots => _configEnableRewriteOpenSlots.Value;

        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static void LoadConfig(ConfigFile configFile)
        {
            _configEnableRewriteOpenSlots = configFile.Bind(nameof(General),
                nameof(EnableRewriteOpenSlots),
                false, // Disabled by default
                DescriptionEnableRewriteOpenSlots);
        }
    }
}