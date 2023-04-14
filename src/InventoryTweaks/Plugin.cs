using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using InventoryTweaks.Helpers;
using InventoryTweaks.Patches;
using JetBrains.Annotations;

namespace InventoryTweaks;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInProcess("rocketstation.exe")]
[PublicAPI]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; }

    private void Awake()
    {
        Log = Logger;
        Log.LogMessage($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

        var instance = new Harmony("InventoryTweaksPatches");
        instance.PatchAll(typeof(KeyBindHelpers));
        instance.PatchAll(typeof(InventoryManagerPatches));
        instance.PatchAll(typeof(InventoryWindowManagerPatches));
        instance.PatchAll(typeof(KeyManagerPatches));
    }
}