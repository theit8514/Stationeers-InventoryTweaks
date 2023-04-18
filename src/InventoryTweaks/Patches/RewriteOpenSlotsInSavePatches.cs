using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Serialization;
using Assets.Scripts.UI;
using HarmonyLib;
using InventoryTweaks.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;

namespace InventoryTweaks.Patches;

internal class RewriteOpenSlotsInSavePatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryWindowManager), nameof(InventoryWindowManager.LoadUserInterfaceData))]
    public static void LoadUserInterfaceData_Postfix(UserInterfaceSaveData userInterfaceSaveData)
    {
        static IEnumerable<Slot> RecurseFilledSlots(Thing parent)
        {
            foreach (var slot in parent.Slots.Where(x => x.Occupant != null))
            {
                yield return slot;
                foreach (var childSlot in RecurseFilledSlots(slot.Occupant))
                {
                    yield return childSlot;
                }
            }
        }

        var allSlots = RecurseFilledSlots(InventoryManager.ParentHuman).ToArray();
        foreach (var openSlot in userInterfaceSaveData.OpenSlots.Where(openSlot => openSlot.IsOpen))
        {
            Plugin.Log.LogDebug(
                $"Open slot: {openSlot.StringHash} {openSlot.SlotId} {openSlot.Position} {openSlot.IsOpen} {openSlot.IsUndocked}");
            var slot = allSlots.FirstOrDefault(x => x.StringHash == openSlot.StringHash);
            if (slot?.Display == null)
            {
                Plugin.Log.LogWarning($"Slot with StringHash {openSlot.StringHash} not found. Is it a ReferenceId?");
                // Try to find this slot by ReferenceId on our current player.
                slot = allSlots.FirstOrDefault(x => x.Occupant.ReferenceId == openSlot.StringHash);
            }

            if (slot?.Display == null)
            {
                Plugin.Log.LogWarning($"Slot with ReferenceId {openSlot.StringHash} not found.");
                continue;
            }

            Plugin.Log.LogInfo(
                $"Found slot {SlotHelper.GetSlotDisplayName(slot)} with ReferenceId {openSlot.StringHash}.");
            if (slot.Display.SlotWindow?.IsVisible == true)
            {
                Plugin.Log.LogInfo("Window for slot is already open.");
                continue;
            }

            Plugin.Log.LogInfo(
                $"Opening window for slot {SlotHelper.GetSlotDisplayName(slot)} with item {slot.Occupant.DisplayName}.");
            Traverse.Create(slot.Display).Method("OnPlayerInteract").GetValue();
            var inventoryWindow = slot.Display.SlotWindow;
            if (inventoryWindow != null && openSlot.IsUndocked)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(inventoryWindow.RectTransform);
                inventoryWindow.Undocked();
                inventoryWindow.RectTransform.position = openSlot.Position;
                inventoryWindow.ClampToScreen();
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XmlSaveLoad), "GetWorldData")]
    // ReSharper disable once InconsistentNaming
    public static void GetWorldData_Postfix(XmlSaveLoad.WorldData __result)
    {
        if (!GameManager.IsBatchMode)
        {
            Plugin.Log.LogInfo("Re-writing OpenSlots data in WorldData.");
            __result.UserInterface.OpenSlots = new List<WindowSaveData>();
            foreach (var window in InventoryWindowManager.Instance.Windows.Where(window => window.GameObject != null))
            {
                var stringHash = window.ParentSlot.StringHash;
                // For non-standard slots (e.g. an open tablet) the StringHash will be zero.
                // This is not useful for re-opening that same window, so rewrite it with the parent reference id.
                if (stringHash == 0)
                {
                    // Unfortunately, reference id is a long, and may run into overflow issues trying to fit into the StringHash's int value.
                    // Fortunately, reference ids appear to be sequential. This means that you would need 2147483647 items
                    // in the world to overflow this value.
                    try
                    {
                        stringHash = Convert.ToInt32(window.Parent.ReferenceId);
                        Plugin.Log.LogInfo($"Writing ReferenceId {stringHash} into StringHash.");
                    }
                    catch (OverflowException)
                    {
                        Plugin.Log.LogWarning(
                            $"Could not save open slot data for {window.Parent.DisplayName} because reference id {window.Parent.ReferenceId} won't fit into int.");
                    }
                }

                // Note, that if we were smarter, we could add a ReferenceId to this WindowSaveData and serialize it.
                // That would require us override the xml serialization, which does not seem advisable at this point.
                __result.UserInterface.OpenSlots.Add(new WindowSaveData // ExtendedWindowSaveData
                {
                    //ReferenceId = window.Parent?.ReferenceId ?? -1,
                    SlotId = window.ParentSlot.SlotId,
                    StringHash = stringHash,
                    IsOpen = window.IsVisible,
                    IsUndocked = window.IsUndocked,
                    Position = window.RectTransform.position
                });
            }
        }
    }

    //public class ExtendedWindowSaveData : WindowSaveData
    //{
    //    public long ReferenceId;
    //}
}