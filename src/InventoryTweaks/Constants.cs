namespace InventoryTweaks;

/// <summary>
///     Contains constant values used throughout the mod.
/// </summary>
public static class Constants
{
    /// <summary>
    ///     The name of the controls group for this mod.
    /// </summary>
    public const string ControlsGroupName = "InventoryTweaks";

    /// <summary>
    ///     Key conflict names for the mod's key bindings.
    /// </summary>
    public static class KeyConflicts
    {
        public const string PingHighlight = "PingHighlight";
        public const string ZoopAddWaypoint = "Zoop Add Waypoint";
    }

    /// <summary>
    ///     File and folder names used by the mod.
    /// </summary>
    public static class SaveData
    {
        public const string InventoryTweaksFolder = "InventoryTweaks";
        public const string InventoryTweaksFileName = "InventoryTweaks.xml";
    }

    /// <summary>
    ///     Harmony patch identifiers.
    /// </summary>
    public static class HarmonyIds
    {
        public const string InventoryTweaksPatches = "InventoryTweaksPatches";
    }
}