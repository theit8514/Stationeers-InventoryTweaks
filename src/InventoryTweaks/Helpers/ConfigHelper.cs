using BepInEx.Configuration;

namespace InventoryTweaks.Helpers;

internal static class ConfigHelper
{
    public static void LoadConfig(ConfigFile configFile)
    {
        General.InitConfig(configFile);
    }

    public static class General
    {
        private const string DescriptionEnableRewriteOpenSlots =
            """
            This will enable or disable the Rewrite Open Slots feature.
            If enabled, it will rewrite your save data for Open Slots so that tablets, tools, etc will remain open when loading your save.

            This option rewrites the StringHash (an integer value) with the ReferenceId (a long value) of the slot that is open.
            When loading it will attempt to open the windows matching the StringHash first, then the ReferenceId.
            There should not be a problem with loading a save that has been modified this way, as Stationeers ignores unknown Open Slot StringHash values.
            """;

        private const string DescriptionEnableLockedSlots =
            """
            This will enable or disable the save feature of Locked Slots.
            If enabled, this will save the locked slots into a new InventoryTweaks.xml file in your game save folder.

            This does not modify the existing world save, only creates a new file in the folder as the game is saving.
            It has been updated to better support mod uninstallation.
            """;

        private static ConfigEntry<bool> _configEnableRewriteOpenSlots;
        private static ConfigEntry<bool> _configEnableSaveLockedSlots;
        public static bool EnableRewriteOpenSlots => _configEnableRewriteOpenSlots.Value;
        public static bool EnableSaveLockedSlots => _configEnableSaveLockedSlots.Value;

        public static void InitConfig(ConfigFile configFile)
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