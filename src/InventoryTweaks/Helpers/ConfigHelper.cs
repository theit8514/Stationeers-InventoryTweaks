using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using InventoryTweaks.Data;

namespace InventoryTweaks.Helpers;

internal static class ConfigHelper
{
    public static void LoadConfig(ConfigFile configFile)
    {
        // Capture this before binding any entries: the first Bind call creates and saves the file,
        // so a missing file here reliably means a brand-new config that needs no migration.
        var isFreshConfig = !File.Exists(configFile.ConfigFilePath);

        General.InitConfig(configFile);
        SmartStow.InitConfig(configFile);

        Migrations.Apply(isFreshConfig);
    }

    /// <summary>
    ///     Versioned configuration migrations. A hidden <c>General/ConfigVersion</c> entry records
    ///     how many migration steps have been applied. On startup, any steps newer than the stored
    ///     version run in order, then the version is stamped to the latest. Brand-new configs are
    ///     stamped to the latest immediately and skip all steps, since their defaults are current.
    /// </summary>
    public static class Migrations
    {
        /// <summary>
        ///     Ordered migration steps. Index <c>i</c> upgrades a config from version <c>i</c> to
        ///     <c>i + 1</c>. Append new steps to the end; never reorder or remove existing ones.
        /// </summary>
        private static readonly Action[] Steps =
        {
            SmartStow.MigrateInsertBodySlotPriority // v0 -> v1
        };

        private static int LatestVersion => Steps.Length;

        /// <summary>
        ///     Applies any outstanding migration steps based on the stored <see cref="General.ConfigVersion" />.
        /// </summary>
        /// <param name="isFreshConfig">
        ///     <see langword="true" /> when no config file existed prior to this load, meaning the
        ///     bound defaults are already current and no migration is required.
        /// </param>
        public static void Apply(bool isFreshConfig)
        {
            if (isFreshConfig)
            {
                General.ConfigVersion = LatestVersion;
                return;
            }

            // Legacy configs created before versioning have no key and therefore read as version 0.
            var fromVersion = Math.Max(0, Math.Min(General.ConfigVersion, LatestVersion));
            if (fromVersion >= LatestVersion)
                return;

            for (var version = fromVersion; version < LatestVersion; version++)
            {
                Plugin.Log.LogInfo($"Migrating InventoryTweaks config from version {version} to {version + 1}");
                Steps[version]();
            }

            General.ConfigVersion = LatestVersion;
        }
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
            Both sides support glob-style "*" wildcards (matching any run of characters, including none)
            and are matched case-insensitively:
              - "*:Output" excludes every prefab's slot named "Output".
              - "ItemHardSuit:*" excludes every slot on the ItemHardSuit prefab.
              - "Appliance*:*" excludes any slot of all appliances.
            Example: ItemHardSuit:Programmable Chip,ItemSuitHARM:Chip,Appliance*:*,*:Output
            """;

        private const string DescriptionConfigVersion =
            """
            Internal config schema version used to apply automatic migrations between mod updates.
            Do not edit this value by hand.
            """;

        private static ConfigEntry<bool> _configEnableRewriteOpenSlots;
        private static ConfigEntry<bool> _configEnableSaveLockedSlots;
        private static ConfigEntry<bool> _configEnableOverrideInventorySelect;
        private static ConfigEntry<string> _configSlotExclusions;
        private static ConfigEntry<int> _configVersion;
        public static bool EnableRewriteOpenSlots => _configEnableRewriteOpenSlots.Value;
        public static bool EnableSaveLockedSlots => _configEnableSaveLockedSlots.Value;
        public static bool EnableOverrideInventorySelect => _configEnableOverrideInventorySelect.Value;
        public static string SlotExclusions => _configSlotExclusions.Value;

        /// <summary>
        ///     Hidden config schema version, owned by <see cref="Migrations" />. Records how many
        ///     migration steps have been applied so future updates can upgrade older config files.
        /// </summary>
        public static int ConfigVersion
        {
            get => _configVersion.Value;
            set => _configVersion.Value = value;
        }

        /// <summary>
        ///     Cached, compiled list of slot exclusion rules for efficient runtime lookup.
        ///     Each rule carries glob-style prefab and slot patterns parsed from the configuration.
        ///     This cache is automatically rebuilt when the configuration changes.
        /// </summary>
        public static IReadOnlyList<SlotExclusionRule> SlotExclusionRules { get; private set; }

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
                "ItemHardSuit:Programmable Chip,ItemSuitHARM:Chip,Appliance*:*,*:Output", // Default exclusions
                DescriptionSlotExclusions);

            // Legacy configs created before versioning have no key and therefore read as version 0.
            _configVersion = configFile.Bind(nameof(General),
                nameof(ConfigVersion),
                0,
                new ConfigDescription(DescriptionConfigVersion,
                    null,
                    // Hide from the BepInEx Configuration Manager UI...
                    new ConfigurationManagerAttributes { Browsable = false, IsAdvanced = true },
                    // ...and from the StationeersLaunchPad config UI, which reads its own tags.
                    new KeyValuePair<string, bool>("Visible", false)));

            // Set up event handler to automatically update the cache when configuration changes
            configFile.SettingChanged += ConfigFileOnSettingChanged;

            // Initialize the cache with the current configuration
            SlotExclusionRules = BuildSlotExclusionRules();
        }

        /// <summary>
        ///     Event handler that automatically updates the slot exclusions cache when configuration changes.
        ///     This ensures that runtime changes to the configuration are immediately reflected without requiring a restart.
        /// </summary>
        /// <param name="sender">The configuration file that triggered the change</param>
        /// <param name="e">Event arguments containing information about the changed setting</param>
        private static void ConfigFileOnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            SlotExclusionRules = BuildSlotExclusionRules();
        }

        /// <summary>
        ///     Parses the slot exclusions configuration into a compiled list of rules for efficient lookup.
        ///     Each entry is "PrefabPattern:SlotPattern", where both sides may use glob-style "*" wildcards.
        /// </summary>
        /// <returns>Read-only list of compiled slot exclusion rules.</returns>
        private static IReadOnlyList<SlotExclusionRule> BuildSlotExclusionRules()
        {
            var rules = new List<SlotExclusionRule>();

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

                var prefabPattern = parts[0].Trim();
                var slotPattern = parts[1].Trim();
                rules.Add(SlotExclusionRule.Create(prefabPattern, slotPattern));
            }

            return rules;
        }
    }

    /// <summary>
    ///     Configuration for Smart Stow slot ordering. Each criterion has an integer priority;
    ///     lower values sort first. Set a priority to 0 to disable that criterion.
    /// </summary>
    public static class SmartStow
    {
        private const string PriorityDescriptionSuffix = " Lower priority wins. Set to 0 to disable.";

        private const string DescriptionPriorityExistingStack =
            "Prefer occupied stacks of the same item with room to merge." + PriorityDescriptionSuffix;

        private const string DescriptionPriorityLockedSlot =
            "Prefer empty slots locked to this item." + PriorityDescriptionSuffix;

        private const string DescriptionPriorityVisibleWindow =
            "Prefer slots in visible inventory windows." + PriorityDescriptionSuffix;

        private const string DescriptionPriorityTypedSlot =
            "Prefer empty typed slots matching the item (e.g. tool slot for tools)." + PriorityDescriptionSuffix;

        private const string DescriptionPriorityEmptyRegularSlot =
            "Prefer empty regular (untyped, unlocked) slots." + PriorityDescriptionSuffix;

        private const string DescriptionPriorityBodySlot =
            "Prefer empty slots on the player's body/hands over slots nested in containers." +
            PriorityDescriptionSuffix;

        private const string DescriptionOnlyVisibleWindows =
            """
            When true, Smart Stow only considers slots that belong to visible inventory windows.
            Slots inside closed windows are excluded entirely.
            """;

        private const string DescriptionAllowReagentMerging =
            """
            When true, Reagent Mix (Slag) stacks that fall through Smart Stow's recipe-matching
            merge step are still allowed to merge with same-prefab occupants of placement targets
            (e.g. via the original-slot or free-slot strategies).
            When false, Reagent Mixes are kept strictly separated: Smart Stow will only place
            them into truly empty slots, never co-mingling with an existing Reagent Mix that
            differs in recipe from the source.
            Disable this if you frequently see different Reagent Mix recipes silently combining,
            for example on multiplayer clients where the source recipe is not visible.
            """;

        private static readonly AcceptableValueRange<int> PriorityRange = new(0, 25);

        private static ConfigEntry<int> _configPriorityExistingStack;
        private static ConfigEntry<int> _configPriorityLockedSlot;
        private static ConfigEntry<int> _configPriorityTypedSlot;
        private static ConfigEntry<int> _configPriorityEmptyRegularSlot;
        private static ConfigEntry<int> _configPriorityBodySlot;
        private static ConfigEntry<int> _configPriorityVisibleWindow;
        private static ConfigEntry<bool> _configOnlyVisibleWindows;
        private static ConfigEntry<bool> _configAllowReagentMerging;

        /// <summary>
        ///     Priority for merging into occupied stacks of the same item with room to spare.
        /// </summary>
        public static int PriorityExistingStack => _configPriorityExistingStack.Value;

        /// <summary>
        ///     Priority for placing into empty slots locked to this item.
        /// </summary>
        public static int PriorityLockedSlot => _configPriorityLockedSlot.Value;

        /// <summary>
        ///     Priority for placing into empty typed slots that match the item (e.g. tool slot for tools).
        /// </summary>
        public static int PriorityTypedSlot => _configPriorityTypedSlot.Value;

        /// <summary>
        ///     Priority for placing into empty regular (untyped, unlocked) slots.
        /// </summary>
        public static int PriorityEmptyRegularSlot => _configPriorityEmptyRegularSlot.Value;

        /// <summary>
        ///     Priority for placing into empty slots on the player's body/hands over container slots.
        /// </summary>
        public static int PriorityBodySlot => _configPriorityBodySlot.Value;

        /// <summary>
        ///     Priority for placing into slots that belong to visible inventory windows.
        /// </summary>
        public static int PriorityVisibleWindow => _configPriorityVisibleWindow.Value;

        /// <summary>
        ///     When true, Smart Stow excludes slots that belong to inventory windows that are not currently visible.
        ///     Hand slots and the player's body slots are unaffected.
        /// </summary>
        public static bool OnlyVisibleWindows => _configOnlyVisibleWindows.Value;

        /// <summary>
        ///     When true, Reagent Mix (Slag) stacks may merge with same-prefab occupants of fall-through
        ///     placement targets after the initial recipe-matching merge step. When false, Smart Stow
        ///     keeps Reagent Mixes strictly separated and only places them into empty slots.
        /// </summary>
        public static bool AllowReagentMerging => _configAllowReagentMerging.Value;

        /// <summary>
        ///     Enabled sort criteria in priority order, rebuilt when configuration changes.
        /// </summary>
        public static IReadOnlyList<SmartStowSortCriterion> OrderedCriteria { get; private set; }

        /// <summary>
        ///     Initializes Smart Stow priority configuration and builds the initial criteria order.
        /// </summary>
        /// <param name="configFile">The BepInEx configuration file instance.</param>
        public static void InitConfig(ConfigFile configFile)
        {
            _configPriorityExistingStack = BindPriority(configFile,
                nameof(PriorityExistingStack),
                1, // Default: prefer merging into existing stacks first
                DescriptionPriorityExistingStack);

            _configPriorityLockedSlot = BindPriority(configFile,
                nameof(PriorityLockedSlot),
                2, // Default: prefer item-locked slots second
                DescriptionPriorityLockedSlot);

            _configPriorityVisibleWindow = BindPriority(configFile,
                nameof(PriorityVisibleWindow),
                3, // Default: prefer slots in visible windows third
                DescriptionPriorityVisibleWindow);

            _configPriorityTypedSlot = BindPriority(configFile,
                nameof(PriorityTypedSlot),
                4, // Default: prefer typed slots fourth
                DescriptionPriorityTypedSlot);

            _configPriorityBodySlot = BindPriority(configFile,
                nameof(PriorityBodySlot),
                5, // Default: prefer the player's own body/hand slots over container slots
                DescriptionPriorityBodySlot);

            _configPriorityEmptyRegularSlot = BindPriority(configFile,
                nameof(PriorityEmptyRegularSlot),
                6, // Default: prefer empty regular slots last
                DescriptionPriorityEmptyRegularSlot);

            _configOnlyVisibleWindows = configFile.Bind(nameof(SmartStow),
                nameof(OnlyVisibleWindows),
                false, // Disabled by default
                DescriptionOnlyVisibleWindows);

            _configAllowReagentMerging = configFile.Bind(nameof(SmartStow),
                nameof(AllowReagentMerging),
                true, // Enabled by default
                DescriptionAllowReagentMerging);

            // Set up event handler to automatically rebuild the ordered criteria when configuration changes
            configFile.SettingChanged += ConfigFileOnSettingChanged;

            // Initialize the ordered criteria with the current configuration
            OrderedCriteria = BuildOrderedCriteria();
        }

        /// <summary>
        ///     Event handler that automatically rebuilds the ordered criteria list when configuration changes.
        ///     This ensures that runtime changes to priority values are immediately reflected without requiring a restart.
        /// </summary>
        /// <param name="sender">The configuration file that triggered the change</param>
        /// <param name="e">Event arguments containing information about the changed setting</param>
        private static void ConfigFileOnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            OrderedCriteria = BuildOrderedCriteria();
        }

        /// <summary>
        ///     Binds a Smart Stow priority entry to the configuration file with the shared priority range validator.
        /// </summary>
        /// <param name="configFile">The BepInEx configuration file instance.</param>
        /// <param name="key">The configuration key for this priority entry.</param>
        /// <param name="defaultValue">The default priority value when no user value is present.</param>
        /// <param name="description">The user-facing description of the priority entry.</param>
        /// <returns>The bound configuration entry.</returns>
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

        /// <summary>
        ///     Migration step (config v0 -> v1): <see cref="PriorityBodySlot" /> was introduced with a
        ///     default of 5, which collided with the original <see cref="PriorityEmptyRegularSlot" />
        ///     default of 5. On legacy configs that still carry the old value, shift empty regular slots
        ///     to 6 so body slots sort ahead of them. Customized values (anything other than 5) are left
        ///     untouched.
        /// </summary>
        internal static void MigrateInsertBodySlotPriority()
        {
            if (PriorityEmptyRegularSlot == 5)
                _configPriorityEmptyRegularSlot.Value = 6;
        }

        /// <summary>
        ///     Builds the ordered list of enabled sort criteria from the current priority configuration.
        ///     Criteria with a priority of 0 are excluded; remaining criteria are sorted by ascending priority.
        /// </summary>
        /// <returns>Read-only list of enabled criteria in evaluation order.</returns>
        private static IReadOnlyList<SmartStowSortCriterion> BuildOrderedCriteria()
        {
            return new (SmartStowSortCriterion criterion, int priority)[]
                {
                    (SmartStowSortCriterion.ExistingStack, PriorityExistingStack),
                    (SmartStowSortCriterion.LockedSlot, PriorityLockedSlot),
                    (SmartStowSortCriterion.TypedSlot, PriorityTypedSlot),
                    (SmartStowSortCriterion.EmptyRegularSlot, PriorityEmptyRegularSlot),
                    (SmartStowSortCriterion.BodySlot, PriorityBodySlot),
                    (SmartStowSortCriterion.VisibleWindow, PriorityVisibleWindow)
                }
                .Where(x => x.priority > 0)
                .OrderBy(x => x.priority)
                .ThenBy(x => x.criterion)
                .Select(x => x.criterion)
                .ToList();
        }
    }
}