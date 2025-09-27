using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using InventoryTweaks.Handlers;

namespace InventoryTweaks;

public static class ModKeyManager
{
    private static readonly FieldInfo KeyManagerOnControlsChangedField =
        AccessTools.Field(typeof(KeyManager), nameof(KeyManager.OnControlsChanged));

    private static readonly MethodInfo InjectSetupKeyBindings =
        SymbolExtensions.GetMethodInfo(() => SetupKeyBindings());

    private static readonly MethodInfo InjectLoadKeyboardSetting =
        SymbolExtensions.GetMethodInfo(() => LoadKeyboardSetting());

    private static readonly List<KeyPressHandler> Keys = new();
    public static event EventHandler<RegisterKeyPressHandlersArguments> RegisterKeyPressHandlers;

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(KeyManager))]
    [HarmonyPatch(nameof(KeyManager.SetupKeyBindings))]
    private static IEnumerable<CodeInstruction> SetupKeyBindings_Transpiler(IEnumerable<CodeInstruction> instructions)
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
    private static IEnumerable<CodeInstruction> LoadKeyboardSetting_Transpiler(
        IEnumerable<CodeInstruction> instructions)
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
        Keys.ForEach(x => x.Bind());
    }

    private static void LoadKeyboardSetting()
    {
        Keys.ForEach(x => x.AssignKey());
    }

    private static void SetupKeyBindings()
    {
        OnRegisterKeyPressHandlers(new RegisterKeyPressHandlersArguments());
    }

    private static void OnRegisterKeyPressHandlers(RegisterKeyPressHandlersArguments e)
    {
        RegisterKeyPressHandlers?.Invoke(null, e);
    }

    public class RegisterKeyPressHandlersArguments
    {
        public void AddKey(KeyPressHandler handler, ControlsGroup controlsGroup)
        {
            handler.AddKey(controlsGroup);
            Keys.Add(handler);
        }

        public ControlsGroup AddControlsGroup(string name)
        {
            var controlsGroup = ControlsGroup.AllControlGroups.FirstOrDefault(x => x.Name == name);
            if (controlsGroup != null)
                return controlsGroup;

            Plugin.Log.LogInfo($"Adding new controls group {name}");
            return new ControlsGroup(name);
        }
    }
}