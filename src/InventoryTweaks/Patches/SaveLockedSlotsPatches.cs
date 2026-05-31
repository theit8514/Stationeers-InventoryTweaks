using System;
using System.IO;
using System.Threading;
using Assets.Scripts;
using Assets.Scripts.Serialization;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using InventoryTweaks.Core;

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
    ///     Postfix for <see cref="SaveHelper.Save(DirectoryInfo,string,bool,CancellationToken)" />.
    ///     Serializes InventoryTweaks data to a temporary file, then after the base save completes,
    ///     copies it to a sidecar file next to the world save (e.g. <c>world.save.InventoryTweaks.xml</c>).
    ///     Any temporary file is removed afterward. Modifies <paramref name="__result" /> to await the copy.
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
        __result = SaveManager.HandleSaveOperation(saveDirectory, saveFileName, newSave, cancellationToken, __result);
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
        SaveManager.LoadGameData(path, stationName);
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
        return !SaveManager.RollSaveFiles(directoryInfo, maxCount);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameManager), "MajorUpdatePopup")]
    public static void GameManager_MajorUpdatePopup_Postfix()
    {
        SaveManager.HandleMajorUpdatePopup();
    }

    /// <summary>
    ///     Prefix for <see cref="SaveHelper.RenameStation" />. Captures the original head save file name
    ///     before the base game moves it, so the postfix can rename the matching InventoryTweaks sidecar.
    ///     The captured value is passed forward via Harmony's <paramref name="__state" /> parameter.
    /// </summary>
    /// <param name="oldStationName">The current station name (directory still exists under this name).</param>
    /// <param name="__state">The original head save file name (e.g. <c>StationName.save</c>), or <see langword="null" />.</param>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SaveHelper), nameof(SaveHelper.RenameStation))]
    [HarmonyWrapSafe]
    // ReSharper disable once InconsistentNaming
    public static void RenameStation_Prefix(string oldStationName, out string __state)
    {
        __state = null;
        try
        {
            var savesDir = StationSaveUtils.GetSavePathSavesSubDir();
            var stationDirectory = new DirectoryInfo(Path.Combine(savesDir.FullName, oldStationName));
            var headSaveFiles = stationDirectory.GetFiles(SaveLoadConstants.SaveFileSearchPattern);
            if (headSaveFiles.Length > 0)
                __state = headSaveFiles[0].Name;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Failed to capture head save before station rename: " + ex.Message);
        }
    }

    /// <summary>
    ///     Postfix for <see cref="SaveHelper.RenameStation" />. When the base game has successfully
    ///     renamed the station, renames the corresponding InventoryTweaks sidecar to match the new
    ///     head save file name.
    /// </summary>
    /// <param name="oldStationName">The previous station name.</param>
    /// <param name="newStationName">The new station name.</param>
    /// <param name="__result">The base game's return value; the rename is only mirrored when <see langword="true" />.</param>
    /// <param name="__state">The original head save file name captured by the prefix.</param>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveHelper), nameof(SaveHelper.RenameStation))]
    [HarmonyWrapSafe]
    // ReSharper disable once InconsistentNaming
    public static void RenameStation_Postfix(string oldStationName, string newStationName, bool __result, string __state)
    {
        if (!__result || string.IsNullOrEmpty(__state))
            return;
        SaveManager.HandleRenameStation(oldStationName, newStationName, __state);
    }
}