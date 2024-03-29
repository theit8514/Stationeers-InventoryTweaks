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
using UnityEngine;
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
            foreach (var slot in parent.Slots.Where(x => x.Get() != null))
            {
                yield return slot;
                foreach (var childSlot in RecurseFilledSlots(slot.Get()))
                {
                    yield return childSlot;
                }
            }
        }

        if (userInterfaceSaveData?.OpenSlots == null)
        {
            Plugin.Log.LogInfo("OpenSlots was null");
            return;
        }

        if (InventoryManager.ParentHuman == null)
        {
            Plugin.Log.LogInfo("ParentHuman was null");
            return;
        }

        var allSlots = RecurseFilledSlots(InventoryManager.ParentHuman).ToArray();
        foreach (var openSlot in userInterfaceSaveData.OpenSlots.Where(openSlot => openSlot.IsOpen))
        {
            if (openSlot.StringHash == 0)
                continue;

            Plugin.Log.LogDebug(
                $"Open slot: {openSlot.StringHash} {openSlot.SlotId} {openSlot.Position} {openSlot.IsOpen} {openSlot.IsUndocked}");
            var slot = allSlots.FirstOrDefault(x => x.StringHash == openSlot.StringHash);
            if (slot?.Display == null)
            {
                Plugin.Log.LogWarning($"Slot with StringHash {openSlot.StringHash} not found. Is it a ReferenceId?");
                // Try to find this slot by ReferenceId on our current player.
                slot = allSlots.FirstOrDefault(x => x.Get().ReferenceId == openSlot.StringHash);
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
                $"Opening window for slot {SlotHelper.GetSlotDisplayName(slot)} with item {slot.Get().DisplayName}.");
            Traverse.Create(slot.Display).Method("OnPlayerInteract").GetValue();
            var slotWindow = slot.Display.SlotWindow;
            if (slotWindow != null && openSlot.IsUndocked)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(slotWindow.RectTransform);
                slotWindow.Undocked();
                Plugin.Log.LogInfo($"Current position: {slotWindow.RectTransform.position}");
                Plugin.Log.LogInfo($"New position: {openSlot.Position}");
                slotWindow.RectTransform.position = openSlot.Position;
                slotWindow.ClampToScreen();
                Plugin.Log.LogInfo($"Clamped position: {slotWindow.RectTransform.position}");
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryWindowManager), nameof(InventoryWindowManager.GenerateUISaveData))]
    // ReSharper disable once InconsistentNaming
    public static void GenerateUISaveData_Postfix(UserInterfaceSaveData __result)
    {
        ReplaceUserInterfaceOpenSlots(__result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XmlSaveLoad), "GetWorldData")]
    // ReSharper disable once InconsistentNaming
    public static void GetWorldData_Postfix(XmlSaveLoad.WorldData __result)
    {
        if (GameManager.IsBatchMode)
            return;
        Plugin.Log.LogDebug("Re-writing OpenSlots data in WorldData.");
        var saveData = __result.UserInterface;
        ReplaceUserInterfaceOpenSlots(saveData);
    }

    private static void ReplaceUserInterfaceOpenSlots(UserInterfaceSaveData saveData)
    {
        saveData.OpenSlots ??= new List<WindowSaveData>();
        saveData.OpenSlots.Clear();
        foreach (var window in InventoryWindowManager.Instance.Windows.Where(window => window.GameObject != null))
        {
            var stringHash = window.ParentSlot.StringHash;
            // For non-standard slots (e.g. an open tablet) the StringHash will be zero.
            // This is not useful for re-opening that same window, so rewrite it with the parent reference id.
            if ((stringHash == 0 || stringHash == Animator.StringToHash("Tool")) && window.Parent != null)
            {
                // Unfortunately, reference id is a long, and may run into overflow issues trying to fit into the StringHash's int value.
                // Fortunately, reference ids appear to be sequential. This means that you would need 2147483647 items
                // in the world to overflow this value.
                try
                {
                    stringHash = Convert.ToInt32(window.Parent.ReferenceId);
                    Plugin.Log.LogDebug($"Writing ReferenceId {stringHash} into StringHash.");
                }
                catch (OverflowException)
                {
                    Plugin.Log.LogWarning(
                        $"Could not save open slot data for {window.Parent.DisplayName} because reference id {window.Parent.ReferenceId} won't fit into int.");
                }
            }

            // Note, that if we were smarter, we could add a ReferenceId to this WindowSaveData and serialize it.
            // That would require us override the xml serialization, which does not seem advisable at this point.
            var openSlot = new WindowSaveData // ExtendedWindowSaveData
            {
                //ReferenceId = window.Parent?.ReferenceId ?? -1,
                SlotId = window.ParentSlot.SlotId,
                StringHash = stringHash,
                IsOpen = window.IsVisible,
                IsUndocked = window.IsUndocked,
                Position = window.RectTransform.position
            };
            Plugin.Log.LogDebug(
                $"Open slot: {openSlot.StringHash} {openSlot.SlotId} {openSlot.Position} {openSlot.IsOpen} {openSlot.IsUndocked}");
            saveData.OpenSlots.Add(openSlot);
        }
    }

    //public class ExtendedWindowSaveData : WindowSaveData
    //{
    //    public long ReferenceId;
    //}
}