using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;

namespace InventoryTweaks.Helpers;

internal class SlotHelper
{
    public static bool IsHandSlot(Slot slot)
    {
        return slot == InventoryManager.LeftHandSlot ||
               slot == InventoryManager.RightHandSlot;
    }

    public static string GetSlotDisplayName(Slot slot)
    {
        var displayName = !string.IsNullOrWhiteSpace(slot.DisplayName) ? slot.DisplayName : slot.Parent?.DisplayName;
        return $"{displayName} {slot.SlotIndex}";
    }
}