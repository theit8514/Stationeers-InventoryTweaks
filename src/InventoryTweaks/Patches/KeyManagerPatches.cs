using HarmonyLib;
using InventoryTweaks.Handlers;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace InventoryTweaks.Patches;

internal class KeyManagerPatches
{
    private static readonly FieldInfo KeyManagerOnControlsChangedField =
        AccessTools.Field(typeof(KeyManager), nameof(KeyManager.OnControlsChanged));

    private static readonly MethodInfo InjectSetupKeyBindings =
        SymbolExtensions.GetMethodInfo(() => SetupKeyBindings());

    private static readonly MethodInfo InjectLoadKeyboardSetting =
        SymbolExtensions.GetMethodInfo(() => LoadKeyboardSetting());

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(KeyManager))]
    [HarmonyPatch(nameof(KeyManager.SetupKeyBindings))]
    public static IEnumerable<CodeInstruction> SetupKeyBindings_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // looks for LDSFLD KeyManager.OnControlsChanged and inserts CALL KeyManagerPatches.SetupKeyBindings before it
        var found = false;
        foreach (var instruction in instructions)
        {
            if (!found && instruction.LoadsField(KeyManagerOnControlsChangedField))
            {
                yield return new CodeInstruction(OpCodes.Call, InjectSetupKeyBindings);
                found = true;
            }

            yield return instruction;
        }

        if (found is false)
        {
            Plugin.Log.LogError(
                $"Cannot find <ldsfld {nameof(KeyManager.OnControlsChanged)}> in {nameof(KeyManager.SetupKeyBindings)}");
        }
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(KeyManager))]
    [HarmonyPatch(nameof(KeyManager.LoadKeyboardSetting))]
    public static IEnumerable<CodeInstruction> LoadKeyboardSetting_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // looks for LDSFLD KeyManager.OnControlsChanged and inserts CALL KeyManagerPatches.LoadKeyboardSetting before it
        var found = false;
        foreach (var instruction in instructions)
        {
            if (!found && instruction.LoadsField(KeyManagerOnControlsChangedField))
            {
                yield return new CodeInstruction(OpCodes.Call, InjectLoadKeyboardSetting);
                found = true;
            }

            yield return instruction;
        }

        if (found is false)
        {
            Plugin.Log.LogError(
                $"Cannot find <ldsfld {nameof(KeyManager.OnControlsChanged)}> in {nameof(KeyManager.LoadKeyboardSetting)}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(KeyManager))]
    [HarmonyPatch("SetBindings")]
    public static void SetBindings_Prefix()
    {
        Plugin.Log.LogDebug("Adding bindings");
        KeyMapExtended.HeldItemNext.Bind();
        KeyMapExtended.DebugWindows.Bind();
    }

    private static void SetupKeyBindings()
    {
        Plugin.Log.LogInfo("Adding new controls group InventoryTweaks");
        var controlsGroup = new ControlsGroup("InventoryTweaks");
        KeyMapExtended.HeldItemNext.AddKey(controlsGroup);
        KeyMapExtended.DebugWindows.AddKey(controlsGroup);
    }

    private static void LoadKeyboardSetting()
    {
        KeyMapExtended.HeldItemNext.AssignKey();
        KeyMapExtended.DebugWindows.AssignKey();
    }

    private static class KeyMapExtended
    {
        public static readonly HeldItemNextKeyPressHandler HeldItemNext = new();
        public static readonly DebugWindowsKeyPressHandler DebugWindows = new();
    }
}