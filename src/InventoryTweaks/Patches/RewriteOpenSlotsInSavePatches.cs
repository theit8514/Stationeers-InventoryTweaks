using Assets.Scripts.Serialization;
using Assets.Scripts.UI;
using HarmonyLib;
using InventoryTweaks.Core;

namespace InventoryTweaks.Patches;

internal class RewriteOpenSlotsInSavePatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryWindowManager), nameof(InventoryWindowManager.LoadUserInterfaceData))]
    public static void LoadUserInterfaceData_Postfix(UserInterfaceSaveData userInterfaceSaveData)
    {
        UIWindowManager.LoadUserInterfaceData(userInterfaceSaveData);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryWindowManager), nameof(InventoryWindowManager.GenerateUISaveData))]
    // ReSharper disable once InconsistentNaming
    public static void GenerateUISaveData_Postfix(UserInterfaceSaveData __result)
    {
        UIWindowManager.ReplaceUserInterfaceOpenSlots(__result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XmlSaveLoad), "GetWorldData")]
    // ReSharper disable once InconsistentNaming
    public static void GetWorldData_Postfix(XmlSaveLoad.WorldData __result)
    {
        UIWindowManager.ReplaceUserInterfaceOpenSlots(__result.UserInterface);
    }
}