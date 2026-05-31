using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using InventoryTweaks.Data;

namespace InventoryTweaks.Helpers;

internal static class ConfigHelper
{
    public static void LoadConfig(ConfigFile configFile)
    {
        General.InitConfig(configFile);
        SmartStow.InitConfig(configFile);
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

        private const string DescriptionEnableOverrideInventorySelect =
            """
            Enable the override of the Inventory Select keybinding to use Smart Stow to store items instead of the
            selected slot.
            """;

        private const string DescriptionSlotExclusions =
            """
            List of slot exclusions in the format "PrefabName:SlotName".
            This prevents automated stowing from placing specific items in certain slots of specific prefabs.
            Players can still manually place items in these slots if desired.
            Entries should be separated by commas.
            Example: ItemHardSuit:Programmable Chip,ItemSuitHARM:Chip
            """;

        private static ConfigEntry<bool> _configEnableRewriteOpenSlots;
        private static ConfigEntry<bool> _configEnableSaveLockedSlots;
        private static ConfigEntry<bool> _configEnableOverrideInventorySelect;
        private static ConfigEntry<string> _configSlotExclusions;
        public static bool EnableRewriteOpenSlots => _configEnableRewriteOpenSlots.Value;
        public static bool EnableSaveLockedSlots => _configEnableSaveLockedSlots.Value;
        public static bool EnableOverrideInventorySelect => _configEnableOverrideInventorySelect.Value;
        public static string SlotExclusions => _configSlotExclusions.Value;

        /// <summary>
        ///     Cached dictionary of slot exclusions for efficient runtime lookup.
        ///     Key: PrefabName, Value: HashSet of excluded slot names.
        ///     This cache is automatically updated when the configuration changes.
        /// </summary>
        public static Dictionary<string, HashSet<string>> SlotExclusionsDictionary { get; set; }

        /// <summary>
        ///     Initializes the configuration system and sets up event handlers.
        /// </summary>
        /// <param name="configFile">The BepInEx configuration file instance</param>
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

            _configEnableOverrideInventorySelect = configFile.Bind(nameof(General),
                nameof(EnableOverrideInventorySelect),
                false, // Disabled by default
                DescriptionEnableOverrideInventorySelect);

            _configSlotExclusions = configFile.Bind(nameof(General),
                nameof(SlotExclusions),
                "ItemHardSuit:Programmable Chip,ItemSuitHARM:Chip", // Default exclusions
                DescriptionSlotExclusions);

            // Set up event handler to automatically update the cache when configuration changes
            configFile.SettingChanged += ConfigFileOnSettingChanged;

            // Initialize the cache with the current configuration
            SlotExclusionsDictionary = GetSlotExclusions();
        }

        /// <summary>
        ///     Event handler that automatically updates the slot exclusions cache when configuration changes.
        ///     This ensures that runtime changes to the configuration are immediately reflected without requiring a restart.
        /// </summary>
        /// <param name="sender">The configuration file that triggered the change</param>
        /// <param name="e">Event arguments containing information about the changed setting</param>
        private static void ConfigFileOnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            SlotExclusionsDictionary = GetSlotExclusions();
        }

        /// <summary>
        ///     Parses the slot exclusions configuration into a dictionary for efficient lookup.
        ///     Key: PrefabName, Value: HashSet of excluded slot names
        /// </summary>
        /// <returns>Dictionary containing parsed slot exclusions</returns>
        private static Dictionary<string, HashSet<string>> GetSlotExclusions()
        {
            var exclusions = new Dictionary<string, HashSet<string>>();

            var exclusionEntries = SlotExclusions.Split(',');
            foreach (var exclusion in exclusionEntries)
            {
                if (string.IsNullOrWhiteSpace(exclusion))
                    continue;

                var parts = exclusion.Split(':');
                if (parts.Length != 2)
                {
                    Plugin.Log.LogWarning(
                        $"Invalid slot exclusion format: '{exclusion}'. Expected format: 'PrefabName:SlotName'");
                    continue;
                }

                var prefabName = parts[0].Trim();
                var slotName = parts[1].Trim();

                if (!exclusions.ContainsKey(prefabName))
                    exclusions[prefabName] = new HashSet<string>();

                exclusions[prefabName].Add(slotName);
            }

            return exclusions;
        }
    }

    /// <summary>
    ///     Configuration for Smart Stow slot ordering. Each criterion has an integer priority;
    ///     lower values sort first. Set a priority to 0 to disable that criterion.
    /// </summary>
    public static class SmartStow
    {
        private const string PriorityDescriptionSuffix = " Lower priority wins. Set to 0 to disable.";
        private static readonly AcceptableValueRange<int> PriorityRange = new(0, 25);

        private static ConfigEntry<int> _configPriorityExistingStack;
        private static ConfigEntry<int> _configPriorityLockedSlot;
        private static ConfigEntry<int> _configPriorityTypedSlot;
        private static ConfigEntry<int> _configPriorityEmptyRegularSlot;
        private static ConfigEntry<int> _configPriorityVisibleWindow;
        private static ConfigEntry<bool> _configOnlyVisibleWindows;

        /// <summary>
        ///     Enabled sort criteria in priority order, rebuilt when configuration changes.
        /// </summary>
        public static IReadOnlyList<SmartStowSortCriterion> OrderedCriteria { get; private set; }

        /// <summary>
        ///     When true, Smart Stow excludes slots that belong to inventory windows that are not currently visible.
        ///     Hand slots and the player's body slots are unaffected.
        /// </summary>
        public static bool OnlyVisibleWindows => _configOnlyVisibleWindows.Value;

        /// <summary>
        ///     Initializes Smart Stow priority configuration and builds the initial criteria order.
        /// </summary>
        /// <param name="configFile">The BepInEx configuration file instance.</param>
        public static void InitConfig(ConfigFile configFile)
        {
            _configPriorityExistingStack = BindPriority(configFile,
                "PriorityExistingStack",
                1,
                "Prefer occupied stacks of the same item with room to merge." + PriorityDescriptionSuffix);

            _configPriorityLockedSlot = BindPriority(configFile,
                "PriorityLockedSlot",
                2,
                "Prefer empty slots locked to this item." + PriorityDescriptionSuffix);

            _configPriorityVisibleWindow = BindPriority(configFile,
                "PriorityVisibleWindow",
                3,
                "Prefer slots in visible inventory windows." + PriorityDescriptionSuffix);

            _configPriorityTypedSlot = BindPriority(configFile,
                "PriorityTypedSlot",
                4,
                "Prefer empty typed slots matching the item (e.g. tool slot for tools)." + PriorityDescriptionSuffix);

            _configPriorityEmptyRegularSlot = BindPriority(configFile,
                "PriorityEmptyRegularSlot",
                5,
                "Prefer empty regular (untyped, unlocked) slots." + PriorityDescriptionSuffix);

            _configOnlyVisibleWindows = configFile.Bind(nameof(SmartStow),
                "OnlyVisibleWindows",
                false,
                "When true, Smart Stow only considers slots that belong to visible inventory windows. " +
                "Slots inside closed windows are excluded entirely.");

            configFile.SettingChanged += ConfigFileOnSettingChanged;
            OrderedCriteria = BuildOrderedCriteria();
        }

        private static ConfigEntry<int> BindPriority(ConfigFile configFile,
            string key,
            int defaultValue,
            string description)
        {
            return configFile.Bind(nameof(SmartStow),
                key,
                defaultValue,
                new ConfigDescription(description, PriorityRange));
        }

        private static void ConfigFileOnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            OrderedCriteria = BuildOrderedCriteria();
        }

        private static IReadOnlyList<SmartStowSortCriterion> BuildOrderedCriteria()
        {
            return new (SmartStowSortCriterion criterion, int priority)[]
                {
                    (SmartStowSortCriterion.ExistingStack, _configPriorityExistingStack.Value),
                    (SmartStowSortCriterion.LockedSlot, _configPriorityLockedSlot.Value),
                    (SmartStowSortCriterion.TypedSlot, _configPriorityTypedSlot.Value),
                    (SmartStowSortCriterion.EmptyRegularSlot, _configPriorityEmptyRegularSlot.Value),
                    (SmartStowSortCriterion.VisibleWindow, _configPriorityVisibleWindow.Value)
                }
                .Where(x => x.priority > 0)
                .OrderBy(x => x.priority)
                .ThenBy(x => x.criterion)
                .Select(x => x.criterion)
                .ToList();
        }
    }
}