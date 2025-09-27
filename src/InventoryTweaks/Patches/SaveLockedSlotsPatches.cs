using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Assets.Scripts.Serialization;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using InventoryTweaks.Data;

namespace InventoryTweaks.Patches;

/// <summary>
///     Harmony patches that integrate InventoryTweaks save data with Stationeers' save pipeline.
///     - Writes inventory data alongside the world save.
///     - Loads inventory data when a world loads.
///     - Cleans up associated inventory files when rolling old saves.
/// </summary>
public class SaveLockedSlotsPatches
{
    /// <summary>
    ///     Base file name used for InventoryTweaks data files.
    /// </summary>
    public const string InventoryTweaksFileName = "InventoryTweaks.xml";

    /// <summary>
    ///     Postfix for <see cref="SaveHelper.Save(DirectoryInfo,string,bool,CancellationToken)" />.
    ///     Serializes InventoryTweaks data to a temporary file, then after the base save completes,
    ///     copies it to a sidecar file next to the world save (e.g. <c>world.save.InventoryTweaks.xml</c>).
    ///     Any temporary file is removed afterwards. Modifies <paramref name="__result" /> to await the copy.
    /// </summary>
    /// <param name="saveDirectory">The directory where the world save is written.</param>
    /// <param name="saveFileName">The world save file name.</param>
    /// <param name="newSave">Whether this is a new save.</param>
    /// <param name="cancellationToken">Token used to cancel asynchronous I/O.</param>
    /// <param name="__result">The original <see cref="UniTask{TResult}" /> result, updated to include the copy step.</param>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveHelper), "Save", typeof(DirectoryInfo), typeof(string), typeof(bool),
        typeof(CancellationToken))]
    public static void Save_Postfix(
        DirectoryInfo saveDirectory,
        string saveFileName,
        bool newSave,
        CancellationToken cancellationToken,
        // ReSharper disable once InconsistentNaming
        ref UniTask<SaveResult> __result)
    {
        Plugin.Log.LogInfo("Starting save for inventory data");
        var saveData = NewInventoryManager.Data.Save();
        var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempFile = GetInventoryTweaksFile(new FileInfo(tempFilePath));
        if (!saveData.Serialize(tempFile.FullName))
        {
            Plugin.Log.LogError("Failed to serialize data to disk.");
            return;
        }

        __result = __result.ContinueWith(async result =>
        {
            await UniTask.SwitchToThreadPool();
            if (result.Success)
            {
                var saveFileInfo = new FileInfo(Path.Combine(saveDirectory.FullName, saveFileName));
                var destFile = GetInventoryTweaksFile(saveFileInfo);
                Plugin.Log.LogInfo($"Copying inventory data file {tempFile.FullName} to {destFile.FullName}");
                try
                {
                    var copyStream = tempFile.OpenRead();
                    try
                    {
                        using var destinationStream = File.Open(destFile.FullName, FileMode.Create, FileAccess.Write);
                        await copyStream.CopyToAsync(destinationStream, 4096, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        return SaveResult.Fail(ex.Message);
                    }

                    copyStream.Close();
                    tempFile.Delete();
                    Plugin.Log.LogInfo("Inventory data file successfully written.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError("Failed to move inventory data file. Check log for more information");
                    Plugin.Log.LogInfo(ex);
                }
            }

            try
            {
                tempFile.Delete();
            }
            catch
            {
                // ignored
            }

            await UniTask.SwitchToMainThread();
            return result;
        });
    }

    /// <summary>
    ///     Prefix for <see cref="LoadHelper.LoadGameTask" />. Attempts to locate and load the
    ///     InventoryTweaks sidecar file for the current world, then restores data into the mod state.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(LoadHelper), "LoadGameTask", typeof(string), typeof(string))]
    public static void LoadGameTask_Prefix(string path, string stationName)
    {
        var fileInfo = new FileInfo(path);
        var inventoryTweaksFile = GetInventoryTweaksFile(fileInfo);
        if (!inventoryTweaksFile.Exists)
            return;

        Plugin.Log.LogInfo($"Loading InventoryTweaks data from {inventoryTweaksFile.FullName}");
        var saveData = InventoryTweaksSaveData.Deserialize(inventoryTweaksFile.FullName);
        NewInventoryManager.Data.Load(saveData);
    }

    // [HarmonyPrefix]
    // [HarmonyPatch(typeof(XmlSaveLoad), "LoadWorld")]
    // public static void LoadWorld_Prefix()
    // {
    //     Plugin.Log.LogDebug($"LoadWorld called for ${XmlSaveLoad.Instance.CurrentWorldSave.World.FullName}");
    //     // Load data from InventoryTweaks xml
    //     var inventoryTweaks = GetInventoryTweaksFile(XmlSaveLoad.Instance.CurrentWorldSave.World);
    //     if (!inventoryTweaks.Exists)
    //         return;
    //
    //     Plugin.Log.LogInfo($"Loading InventoryTweaks data from {inventoryTweaks.FullName}");
    //     var saveData = InventoryTweaksSaveData.Deserialize(inventoryTweaks.FullName);
    //     NewInventoryManager.Data.Load(saveData);
    // }

    /// <summary>
    ///     Prefix for <see cref="SaveHelper.RollSaveFiles" />. Ensures the oldest InventoryTweaks sidecar
    ///     file is deleted together with the corresponding world save when rolling saves.
    ///     Returns <c>false</c> to skip the original method (custom handling only).
    /// </summary>
    /// <param name="directoryInfo">Directory containing world save files.</param>
    /// <param name="maxCount">Maximum number of save files to keep.</param>
    /// <returns><c>false</c> to prevent execution of the original method.</returns>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SaveHelper), "RollSaveFiles")]
    public static bool RollSaveFiles_Prefix(DirectoryInfo directoryInfo, int maxCount)
    {
        var saveFiles = directoryInfo.GetFiles().Where(x => x.Extension == ".save").ToArray();
        if (saveFiles.Length <= maxCount)
            return false;

        var valueTupleList = new List<(FileInfo file, DateTime dateTime)>();
        foreach (var file in saveFiles)
        {
            if (DateTime.TryParseExact(file.Name.Substring(0, SaveLoadConstants.DateTimeFormat.Length),
                    SaveLoadConstants.DateTimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var result))
                valueTupleList.Add((file, result));
        }

        valueTupleList.Sort((a, b) => a.dateTime.CompareTo(b.dateTime));
        try
        {
            var inventoryTweaksFile = GetInventoryTweaksFile(valueTupleList[0].file);
            if (inventoryTweaksFile.Exists)
                inventoryTweaksFile.Delete();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Failed to delete inventory tweaks file: " + ex.Message);
        }

        valueTupleList[0].Item1.Delete();
        return false;
    }

    /// <summary>
    ///     Computes the InventoryTweaks sidecar file path for a given world save file.
    /// </summary>
    /// <param name="world">The world save file (e.g. <c>*.save</c>).</param>
    /// <returns>A <see cref="FileInfo" /> pointing to the sidecar xml file.</returns>
    private static FileInfo GetInventoryTweaksFile(FileInfo world)
    {
        return new FileInfo(world.FullName + "." + InventoryTweaksFileName);
    }
}