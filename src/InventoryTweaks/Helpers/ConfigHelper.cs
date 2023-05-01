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

        private const string DescriptionEnableLockedSlots =
            "Allows locked slots to be saved to a file along with the world data. Experimental feature" +
            " and may cause issues with loading/saving the world. It is recommended to backup any saves" +
            " before enabling this feature and perform a manual save once loaded to test saving.";

        private static ConfigEntry<bool> _configEnableRewriteOpenSlots;
        private static ConfigEntry<bool> _configEnableSaveLockedSlots;
        public static bool EnableRewriteOpenSlots => _configEnableRewriteOpenSlots.Value;
        public static bool EnableSaveLockedSlots => _configEnableSaveLockedSlots.Value;

        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static void LoadConfig(ConfigFile configFile)
        {
            _configEnableRewriteOpenSlots = configFile.Bind(nameof(General),
                nameof(EnableRewriteOpenSlots),
                false, // Disabled by default
                DescriptionEnableRewriteOpenSlots);

            _configEnableSaveLockedSlots = configFile.Bind(nameof(General),
                nameof(EnableSaveLockedSlots),
                false, // Disabled by default
                DescriptionEnableLockedSlots);
        }
    }
}