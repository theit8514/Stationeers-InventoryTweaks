using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Assets.Scripts;
using Assets.Scripts.Localization2;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using InventoryTweaks.Data;
using UI;

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
    ///     Folder name used for InventoryTweaks data.
    /// </summary>
    public const string InventoryTweaksFolder = "InventoryTweaks";

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
    [HarmonyWrapSafe]
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
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
        var tempFile = new FileInfo(tempFilePath);
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
                if (tempFile.Exists)
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
    [HarmonyWrapSafe]
    public static void LoadGameTask_Prefix(string path, string stationName)
    {
        NewInventoryManager.Data.Clear();
        var fileInfo = new FileInfo(path);
        // Check the new format first
        var inventoryTweaksFile = GetInventoryTweaksFile(fileInfo);
        // If that doesn't exist, check the legacy format (if user did not run migration)
        if (!inventoryTweaksFile.Exists)
            inventoryTweaksFile = GetLegacyInventoryTweaksFile(fileInfo);
        // If neither exists, don't load anything.
        if (!inventoryTweaksFile.Exists)
            return;

        Plugin.Log.LogInfo($"Loading InventoryTweaks data from {inventoryTweaksFile.FullName}");
        var saveData = InventoryTweaksSaveData.Deserialize(inventoryTweaksFile.FullName);
        NewInventoryManager.Data.Load(saveData);
    }

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
    [HarmonyWrapSafe]
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

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameManager), "MajorUpdatePopup")]
    public static void GameManager_MajorUpdatePopup_Postfix()
    {
        var saveFolder = StationSaveUtils.GetSavePathSavesSubDir();
        var files = Migrations.MigrateInventoryTweaksSaveStorage(saveFolder, true);
        if (files.Length <= 0)
            return;

        Singleton<ConfirmationPanel>.Instance.ShowRaw("InventoryTweaks",
            $"""
             An old InventoryTweaks save file has been found in your saves folder. In 0.4.2, InventoryTweaks changed how saves are stored to prevent issues with mod uninstallation. Would you like to migrate these save files to the new format?
             If {Localization.GetInterface("ButtonClose")} is clicked, the saves will not be migrated and you may lose your locked slots information.
             """,
            GameStrings.OkayConfirmation,
            () => Migrations.MigrateInventoryTweaksSaveStorage(saveFolder),
            Localization.GetInterface("ButtonClose"), closeOnEscape: false);
    }

    /// <summary>
    ///     Computes the legacy InventoryTweaks sidecar file path for a given world save file.
    /// </summary>
    /// <param name="world">The world save file (e.g. <c>*.save</c>).</param>
    /// <returns>A <see cref="FileInfo" /> pointing to the sidecar xml file.</returns>
    private static FileInfo GetLegacyInventoryTweaksFile(FileInfo world)
    {
        return new FileInfo(world.FullName + "." + InventoryTweaksFileName);
    }

    /// <summary>
    ///     Computes the InventoryTweaks file path for a given world save file.
    /// </summary>
    /// <param name="world">The world save file (e.g. <c>*.save</c>).</param>
    /// <returns>A <see cref="FileInfo" /> pointing to the sidecar xml file.</returns>
    private static FileInfo GetInventoryTweaksFile(FileInfo world)
    {
        try
        {
            var savePath = Path.GetFullPath(StationSaveUtils.GetSavePathSavesSubDir().FullName);
            var worldPath = world.Directory == null ? null : Path.GetFullPath(world.Directory.FullName);
            if (worldPath == null || worldPath.Length <= savePath.Length || !worldPath.Substring(0, savePath.Length)
                    .Equals(savePath, StringComparison.OrdinalIgnoreCase))
            {
                // If we're not in the save folder, don't attempt to access the file.
                return null;
            }

            var relativeSavePath = worldPath.Substring(savePath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Split the path to inject InventoryTweaks between world name and save name
            var pathParts = relativeSavePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var inventoryTweaksFileName = world.Name + "." + InventoryTweaksFileName;
            switch (pathParts.Length)
            {
                case >= 2:
                {
                    // If more than 2 parts, could be an odd folder format? not sure if that's supported but handle it anyways
                    // Extract last part, e.g. quicksave, autosave, manualsave, or root folder.
                    var typePart = pathParts.Last();
                    var worldPart = Path.Combine(pathParts.SkipLast(1).ToArray());
                    // Reconstruct: savePath + worldName + InventoryTweaks + type + filename
                    var saveFile = Path.Combine(savePath, worldPart, InventoryTweaksFolder, typePart,
                        inventoryTweaksFileName);
                    Plugin.Log.LogDebug($"Determined InventoryTweaks save file of {saveFile}");
                    return new FileInfo(saveFile);
                }
                case 1:
                {
                    // If one part, this should be a main save file.
                    var saveFile = Path.Combine(worldPath, InventoryTweaksFolder, inventoryTweaksFileName);
                    Plugin.Log.LogDebug($"Determined InventoryTweaks save file of {saveFile}");
                    return new FileInfo(saveFile);
                }
                default:
                    Plugin.Log.LogError($"Could not determine world save file format: {world.FullName}");
                    return null;
            }
        }
        catch
        {
            Plugin.Log.LogError($"Could not determine world save file format: {world.FullName}");
            return null;
        }
    }
}