using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;

namespace InventoryTweaks.Helpers;

internal static class SlotHelper
{
    public static bool IsHandSlot(Slot slot)
    {
        return slot == InventoryManager.LeftHandSlot ||
               slot == InventoryManager.RightHandSlot;
    }

    public static string GetSlotDisplayName(Slot slot)
    {
        var parentName = slot.Parent?.GetPassiveUITooltip().Title ?? "Unknown";
        var parentPrefab = slot.Parent?.PrefabName;
        var slotName = !string.IsNullOrWhiteSpace(slot.DisplayName) ? slot.DisplayName : slot.Parent?.DisplayName;
        return $"{parentName} ({parentPrefab}) slot {slotName} {slot.SlotIndex}";
    }

    /// <summary>
    ///     Checks if a slot is excluded for a specific item based on configuration.
    ///     Uses the cached slot exclusions dictionary for optimal performance.
    /// </summary>
    /// <param name="slot">The slot to check</param>
    /// <param name="item">The item to check against exclusions</param>
    /// <returns>True if the slot is excluded for this item, false otherwise</returns>
    public static bool IsSlotExcludedForItem(Slot slot, DynamicThing item)
    {
        if (slot?.Parent == null || item == null)
            return false;

        var parentPrefabName = slot.Parent.PrefabName;
        if (!ConfigHelper.General.SlotExclusionsDictionary.TryGetValue(parentPrefabName, out var excludedSlots))
            return false;

        var slotName = !string.IsNullOrWhiteSpace(slot.DisplayName) ? slot.DisplayName : slot.Parent?.DisplayName;

        return excludedSlots.Contains(slotName);
    }
}