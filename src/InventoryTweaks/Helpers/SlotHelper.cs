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
        if (displayName == "None")
            displayName = slot.Parent?.GetPassiveUITooltip().Title;
        return $"{displayName} {slot.SlotIndex}";
    }
}