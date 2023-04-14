using Assets.Scripts.Objects;
using Assets.Scripts.UI;
using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace InventoryTweaks.Patches;

internal class InventoryWindowManagerPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryWindow))]
    [HarmonyPatch(nameof(InventoryWindow.SetVisible))]
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once IdentifierTypo
    public static void SetVisible_Prefix(InventoryWindow __instance, bool isVisble)
    {
        // This fixes the phantom window issue. The layout will render all items that are active.
        // If we mark this game object as inactive, it will no longer be rendered in the layout.
        // This results in no gaps between open windows.
        __instance.RectTransform.gameObject.SetActive(isVisble);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryWindow))]
    [HarmonyPatch(nameof(InventoryWindow.Assign))]
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once IdentifierTypo
    public static void Assign(InventoryWindow __instance, Slot parentSlot)
    {
        // Minor fix to window names, used for debugging windows below.
        if (string.IsNullOrEmpty(parentSlot.StringKey))
            __instance.name = $"Window {parentSlot.Occupant?.DisplayName}";
    }

    public static void DebugWindows()
    {
        var instance = InventoryWindowManager.Instance;
        Plugin.Log.LogInfo($"Number of windows: {instance.Windows.Count}");
        var visibleWindows = instance.Windows.Count(x => x.IsVisible);
        Plugin.Log.LogInfo($"Number of visible windows: {visibleWindows}");
        var visibleDockedWindows = instance.Windows.Count(x => !x.IsUndocked && x.IsVisible);
        Plugin.Log.LogInfo($"Number of visible docked windows: {visibleDockedWindows}");

        var parentTransform = (RectTransform)instance.WindowGrid.transform;
        Plugin.Log.LogInfo(
            $"RectTransform parent: {parentTransform.name} {parentTransform?.anchorMin} {parentTransform?.anchorMax} {parentTransform?.anchoredPosition} {parentTransform?.offsetMin} {parentTransform?.offsetMax}");
        for (var i = 0; i < parentTransform.childCount; i++)
        {
            var childTransform = parentTransform.GetChild(i);
            Plugin.Log.LogInfo(!childTransform.TryGetComponent<InventoryWindow>(out var childWindow)
                ? $"Child {i}: {childTransform.name} A:{childTransform.gameObject.activeSelf}"
                : $"Child {i}: {childTransform.name} A:{childTransform.gameObject.activeSelf} V:{childWindow.IsVisible} D:{!childWindow.IsUndocked}");
            var rectTransform = childTransform as RectTransform;
            Plugin.Log.LogInfo(
                $"{rectTransform?.anchorMin} {rectTransform?.anchorMax} {rectTransform?.anchoredPosition} {rectTransform?.offsetMin} {rectTransform?.offsetMax}");
        }
    }
}