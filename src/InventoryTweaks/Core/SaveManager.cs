using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Assets.Scripts.Localization2;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using InventoryTweaks.Data.Serialized;
using InventoryTweaks.Utilities;
using UI;

namespace InventoryTweaks.Core;

/// <summary>
///     Handles all save-related operations for InventoryTweaks data, including file path resolution,
///     serialization, and cleanup.
/// </summary>
public static class SaveManager
{
    /// <summary>
    ///     Handles the save operation by serializing data to a temporary file and copying it to the final location.
    /// </summary>
    /// <param name="saveDirectory">The directory where the world save is written.</param>
    /// <param name="saveFileName">The world save file name.</param>
    /// <param name="newSave">Whether this is a new save.</param>
    /// <param name="cancellationToken">Token used to cancel asynchronous I/O.</param>
    /// <param name="originalResult">The original UniTask result to continue with.</param>
    /// <returns>Updated UniTask that includes the copy step.</returns>
    public static UniTask<SaveResult> HandleSaveOperation(
        DirectoryInfo saveDirectory,
        string saveFileName,
        bool newSave,
        CancellationToken cancellationToken,
        UniTask<SaveResult> originalResult)
    {
        Plugin.Log.LogInfo("Starting save for inventory data");
        var saveData = CustomInventoryManager.Data.Save();
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
        var tempFile = new FileInfo(tempFilePath);
        if (!saveData.Serialize(tempFile.FullName))
        {
            Plugin.Log.LogError("Failed to serialize data to disk.");
            return originalResult;
        }

        return originalResult.ContinueWith(async result =>
        {
            await UniTask.SwitchToThreadPool();
            if (result.Success)
            {
                var saveFileInfo = new FileInfo(Path.Combine(saveDirectory.FullName, saveFileName));
                var destFile = GetInventoryTweaksFile(saveFileInfo);
                Plugin.Log.LogInfo($"Copying inventory data file {tempFile.FullName} to {destFile.FullName}");
                try
                {
                    destFile.Directory?.Create();
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
    ///     Handles rolling save files by cleaning up old InventoryTweaks files.
    /// </summary>
    /// <param name="directoryInfo">Directory containing world save files.</param>
    /// <param name="maxCount">Maximum number of save files to keep.</param>
    /// <returns>True if files were processed, false if no action was needed.</returns>
    public static bool RollSaveFiles(DirectoryInfo directoryInfo, int maxCount)
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
        return true;
    }

    /// <summary>
    ///     Loads InventoryTweaks data from the appropriate save file for the given world path.
    /// </summary>
    /// <param name="path">The path to the world save file.</param>
    /// <param name="stationName">The name of the station (unused but kept for compatibility).</param>
    public static void LoadGameData(string path, string stationName)
    {
        CustomInventoryManager.Data.Clear();
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
        CustomInventoryManager.Data.Load(saveData);
    }

    /// <summary>
    ///     Handles the major update popup for migrating old save files to the new format.
    /// </summary>
    public static void HandleMajorUpdatePopup()
    {
        var saveFolder = StationSaveUtils.GetSavePathSavesSubDir();
        var files = Migrations.MigrateInventoryTweaksSaveStorage(saveFolder, true);
        if (files.Length <= 0)
            return;

        Singleton<ConfirmationPanel>.Instance.ShowRaw("InventoryTweaks",
            $"""
             An old InventoryTweaks save file has been found in your saves folder. In 0.4.2, InventoryTweaks changed how saves are stored to prevent issues with mod uninstallation. Would you like to migrate these save files to the new format?
             If {Assets.Scripts.Localization.GetInterface("ButtonClose")} is clicked, the saves will not be migrated and you may lose your locked slots information.
             """,
            GameStrings.OkayConfirmation,
            () => Migrations.MigrateInventoryTweaksSaveStorage(saveFolder),
            Assets.Scripts.Localization.GetInterface("ButtonClose"), closeOnEscape: false);
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
            var inventoryTweaksFileName = world.Name + "." + Constants.SaveData.InventoryTweaksFileName;
            switch (pathParts.Length)
            {
                case >= 2:
                {
                    // If more than 2 parts, could be an odd folder format? not sure if that's supported but handle it anyways
                    // Extract last part, e.g. quicksave, autosave, manualsave, or root folder.
                    var typePart = pathParts.Last();
                    var worldPart = Path.Combine(pathParts.SkipLast(1).ToArray());
                    // Reconstruct: savePath + worldName + InventoryTweaks + type + filename
                    var saveFile = Path.Combine(savePath, worldPart, Constants.SaveData.InventoryTweaksFolder, typePart,
                        inventoryTweaksFileName);
                    Plugin.Log.LogDebug($"Determined InventoryTweaks save file of {saveFile}");
                    return new FileInfo(saveFile);
                }
                case 1:
                {
                    // If one part, this should be a main save file.
                    var saveFile = Path.Combine(worldPath, Constants.SaveData.InventoryTweaksFolder,
                        inventoryTweaksFileName);
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

    /// <summary>
    ///     Computes the legacy InventoryTweaks sidecar file path for a given world save file.
    /// </summary>
    /// <param name="world">The world save file (e.g. <c>*.save</c>).</param>
    /// <returns>A <see cref="FileInfo" /> pointing to the sidecar xml file.</returns>
    private static FileInfo GetLegacyInventoryTweaksFile(FileInfo world)
    {
        return new FileInfo(world.FullName + "." + Constants.SaveData.InventoryTweaksFileName);
    }
}