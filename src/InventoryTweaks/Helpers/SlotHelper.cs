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
}