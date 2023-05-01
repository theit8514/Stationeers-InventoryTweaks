using Assets.Scripts.Serialization;
using HarmonyLib;
using InventoryTweaks.Data;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace InventoryTweaks.Patches;

public class SaveLockedSlotsPatches
{
    public const string InventoryTweaksFileName = "InventoryTweaks.xml";
    public const string InventoryTweaksBackupFormat = "InventoryTweaks({0}).xml";

    [HarmonyPrefix]
    [HarmonyPatch(typeof(XmlSaveLoad), "LoadWorld")]
    public static void LoadWorld_Prefix()
    {
        // Load data from InventoryTweaks xml
        var inventoryTweaks = GetInventoryTweaksFile(XmlSaveLoad.Instance.CurrentWorldSave.World);
        if (!inventoryTweaks.Exists)
            return;

        Plugin.Log.LogInfo($"Loading InventoryTweaks data from {inventoryTweaks.FullName}");
        var saveData = InventoryTweaksSaveData.Deserialize(inventoryTweaks.FullName);
        NewInventoryManager.Data.Load(saveData);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(XmlSaveLoad), "RenameTemporarySave")]
    public static void RenameTemporarySave_Prefix(string worldDirectory)
    {
        // Write our data to temp_InventoryTweaks.xml
        var saveData = NewInventoryManager.Data.Save();
        var path = worldDirectory + "/temp_" + InventoryTweaksFileName;
        if (!saveData.Serialize(path))
            Plugin.Log.LogError("Failed to serialize data to disk.");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XmlSaveLoad), "RenameTemporarySave")]
    public static void RenameTemporarySave_Postfix(string worldDirectory)
    {
        // Rename temp_InventoryTweaks.xml to InventoryTweaks.xml
        if (File.Exists(worldDirectory + "/" + InventoryTweaksFileName))
            File.Delete(worldDirectory + "/" + InventoryTweaksFileName);
        File.Move(worldDirectory + "/temp_" + InventoryTweaksFileName, worldDirectory + "/" + InventoryTweaksFileName);
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(XmlSaveLoad), "BackupWorldFiles")]
    public static IEnumerable<CodeInstruction> BackupWorldFiles_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        //Plugin.Log.LogDebug(string.Join(Environment.NewLine, instructions));
        var getBackupWorldIndexMethod = AccessTools.Method(typeof(XmlSaveLoad), "get_BackupWorldIndex");
        Expression<Action<XmlSaveLoad, string, bool>> expression = (a, b, c) => BackupWorldFiles(a, b, c);
        var found = false;
        foreach (var instruction in instructions)
        {
            if (!found && instruction.Calls(getBackupWorldIndexMethod))
            {
                found = true;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return CodeInstruction.Call(expression);
            }

            yield return instruction;
        }
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(StationSaveContainer), nameof(StationSaveContainer.SendToSteamCloud))]
    public static IEnumerable<CodeInstruction> SendToSteamCloud_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var index = list.FindLastIndex(x => x.opcode == OpCodes.Ret);
        list.InsertRange(index, new[]
        {
            new(OpCodes.Ldarg_0),
            CodeInstruction.Call((StationSaveContainer container) => SendToSteamCloud(container))
        });
        return list.AsEnumerable();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(StationSaveContainer), nameof(StationSaveContainer.RemoveFromSteamCloud))]
    public static IEnumerable<CodeInstruction> RemoveFromSteamCloud_Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var index = list.FindLastIndex(x => x.opcode == OpCodes.Ret);
        list.InsertRange(index, new[]
        {
            new(OpCodes.Ldarg_0),
            CodeInstruction.Call((StationSaveContainer container) => RemoveFromSteamCloud(container))
        });
        return list.AsEnumerable();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(StationSaveContainer), nameof(StationSaveContainer.DeleteFromSteamCloud))]
    public static IEnumerable<CodeInstruction> DeleteFromSteamCloud_Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var index = list.FindLastIndex(x => x.opcode == OpCodes.Ret);
        list.InsertRange(index, new[]
        {
            new(OpCodes.Ldarg_0),
            CodeInstruction.Call((StationSaveContainer container) => DeleteFromSteamCloud(container))
        });
        return list.AsEnumerable();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(StationSaveContainer), nameof(StationSaveContainer.DeleteFiles))]
    public static IEnumerable<CodeInstruction> DeleteFiles_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var index = list.FindLastIndex(x => x.opcode == OpCodes.Ret);
        list.InsertRange(index, new[]
        {
            new(OpCodes.Ldarg_0),
            CodeInstruction.Call((StationSaveContainer container) => DeleteFiles(container))
        });
        return list.AsEnumerable();
    }

    private static void BackupWorldFiles(XmlSaveLoad instance, string worldDirectory, bool autoSave)
    {
        // Backup InventoryTweaks.xml
        var backupEachFiles = Traverse.Create(instance)
            .Method("BackupEachFiles", new[] { typeof(string), typeof(string), typeof(bool) });
        backupEachFiles.GetValue(worldDirectory, InventoryTweaksFileName, autoSave);
    }

    private static void SendToSteamCloud(StationSaveContainer container)
    {
        var inventoryTweaks = GetInventoryTweaksFile(container.World);
        if (inventoryTweaks != null)
        {
            SteamRemoteStorage.FileWrite(container.GetCloudFileName(inventoryTweaks),
                File.ReadAllBytes(inventoryTweaks.FullName));
        }
    }

    private static void RemoveFromSteamCloud(StationSaveContainer container)
    {
        var inventoryTweaks = GetInventoryTweaksFile(container.World);
        if (inventoryTweaks != null)
            SteamRemoteStorage.FileForget(container.GetCloudFileName(inventoryTweaks));
    }

    private static void DeleteFromSteamCloud(StationSaveContainer container)
    {
        var inventoryTweaks = GetInventoryTweaksFile(container.World);
        if (inventoryTweaks != null)
            SteamRemoteStorage.FileDelete(container.GetCloudFileName(inventoryTweaks));
    }

    private static void DeleteFiles(StationSaveContainer container)
    {
        var inventoryTweaks = GetInventoryTweaksFile(container.World);
        if (inventoryTweaks?.Exists ?? false)
            inventoryTweaks.Delete();
    }

    private static FileInfo GetInventoryTweaksFile(FileInfo world)
    {
        if (world.Name == "world.xml")
        {
            // Main world file
            return new FileInfo(world.DirectoryName + "/" + InventoryTweaksFileName);
        }

        var match = Regex.Match(world.Name, @"\((\d+)\)");
        if (match.Success)
        {
            var index = match.Groups[1].Value;
            return new FileInfo(world.DirectoryName + "/" + string.Format(InventoryTweaksBackupFormat, index));
        }

        Plugin.Log.LogWarning($"Could not determine InventoryTweaks save file from world file {world.Name}");
        return null;
    }
}