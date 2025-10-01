using System.Collections.Generic;
using System.IO;
using Assets.Scripts.Serialization;
using InventoryTweaks.Patches;

namespace InventoryTweaks;

public static class Migrations
{
    /// <summary>
    ///     Migrates the InventoryTweaks xml files from the game's folders into a new InventoryTweaks folder
    /// </summary>
    public static FileInfo[] MigrateInventoryTweaksSaveStorage(DirectoryInfo saveFolder, bool dryRun = false)
    {
        var saveDirectories = saveFolder.GetDirectories();
        List<FileInfo> files = [];
        foreach (var directory in saveDirectories)
        {
            var target =
                new DirectoryInfo(Path.Combine(directory.FullName, SaveLockedSlotsPatches.InventoryTweaksFolder));
            // Save Subfolders
            MigrateInventoryTweaksSaveFolder(directory, target, SaveLoadConstants.QuickSaveFolder);
            MigrateInventoryTweaksSaveFolder(directory, target, SaveLoadConstants.AutoSaveFolder);
            MigrateInventoryTweaksSaveFolder(directory, target, SaveLoadConstants.ManualSaveFolder);
            // Main game save
            MigrateInventoryTweaksSaveFiles(directory, target);
        }

        return files.ToArray();

        void MigrateInventoryTweaksSaveFolder(DirectoryInfo source, DirectoryInfo target, string folderName)
        {
            var sourceFolder = new DirectoryInfo(Path.Combine(source.FullName, folderName));
            var targetFolder = new DirectoryInfo(Path.Combine(target.FullName, folderName));
            MigrateInventoryTweaksSaveFiles(sourceFolder, targetFolder);
        }

        void MigrateInventoryTweaksSaveFiles(DirectoryInfo source, DirectoryInfo target)
        {
            if (!source.Exists)
                return;
            if (!target.Exists)
            {
                Plugin.Log.LogInfo($"Migration: Create directory {target.FullName}");

                if (!dryRun)
                    Directory.CreateDirectory(target.FullName);
            }

            var itFiles = source.GetFiles($"*.{SaveLockedSlotsPatches.InventoryTweaksFileName}");
            foreach (var itFile in itFiles)
            {
                files.Add(itFile);
                var targetFile = new FileInfo(Path.Combine(target.FullName, itFile.Name));

                Plugin.Log.LogInfo($"Migrate {itFile.FullName} to {targetFile.FullName}");
                if (targetFile.Exists)
                {
                    Plugin.Log.LogWarning(
                        $"The target file {targetFile.FullName} exists, will overwrite it");
                    if (!dryRun)
                        targetFile.Delete();
                }

                if (!dryRun)
                    itFile.MoveTo(targetFile.FullName);
            }
        }
    }
}