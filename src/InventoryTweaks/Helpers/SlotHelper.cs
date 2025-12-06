using System.Collections.Generic;
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

    /// <summary>
    ///     Gets the path of a slot in the player's inventory hierarchy.
    ///     Returns a string describing the nested location, e.g., "slot in item in toolbelt in jetpack".
    /// </summary>
    /// <param name="slot">The slot to trace</param>
    /// <returns>A string describing the slot's location in the inventory hierarchy</returns>
    public static string GetSlotLocationPath(Slot slot)
    {
        if (slot == null)
            return "Unknown location";

        var pathParts = new List<string>();
        var currentSlot = slot;
        var player = InventoryManager.ParentHuman;

        // Start with the slot itself
        var slotName = !string.IsNullOrWhiteSpace(currentSlot.DisplayName)
            ? currentSlot.DisplayName
            : currentSlot.Parent?.DisplayName ?? "slot";
        pathParts.Add(slotName);

        // Trace up through parent items (from slot to player)
        var currentParent = currentSlot.Parent as DynamicThing;
        while (currentParent != null)
        {
            // If we've reached the player, stop (don't add player to path)
            if (currentParent == player)
                break;

            var parentName = currentParent.GetPassiveUITooltip().Title ??
                             currentParent.DisplayName ?? currentParent.PrefabName;
            pathParts.Add(parentName);

            // Move to the parent's parent slot
            var parentSlot = currentParent.ParentSlot;
            if (parentSlot == null)
                break;

            currentParent = parentSlot.Parent as DynamicThing;
        }

        // Join with " in " to get "slot in item in toolbelt in jetpack"
        return string.Join(" in ", pathParts);
    }
}