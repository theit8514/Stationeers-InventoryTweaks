using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using InventoryTweaks.Extensions;
using InventoryTweaks.KeyBinding.Handlers;

namespace InventoryTweaks.KeyBinding;

public static class ModKeyManager
{
    private static readonly FieldInfo KeyManagerOnControlsChangedField =
        AccessTools.Field(typeof(KeyManager), nameof(KeyManager.OnControlsChanged));

    private static readonly MethodInfo InjectSetupKeyBindings =
        SymbolExtensions.GetMethodInfo(() => SetupKeyBindings());

    private static readonly MethodInfo InjectLoadKeyboardSetting =
        SymbolExtensions.GetMethodInfo(() => LoadKeyboardSetting());

    public static readonly List<KeyPressHandler> Keys = new();
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
        RegisterIgnoredConflicts();
        RegisterKeyConflicts();
    }

    private static void OnRegisterKeyPressHandlers(RegisterKeyPressHandlersArguments e)
    {
        RegisterKeyPressHandlers?.Invoke(null, e);
    }

    /// <summary>
    ///     Registers key ignore conflicts for all registered handlers.
    ///     This method registers conflicts where other keys should ignore our mod keys.
    /// </summary>
    private static void RegisterKeyConflicts()
    {
        foreach (var handler in Keys)
        {
            var keyConflicts = handler.GetKeyConflicts();
            if (keyConflicts == null) continue;

            var conflictList = keyConflicts.ToList();
            if (conflictList.Count == 0) continue;

            Plugin.Log.LogDebug($"Registering reverse conflicts for {handler.Name}: {string.Join(", ", conflictList)}");

            foreach (var conflictKey in conflictList)
                // Register that conflictKey should ignore handler.Name (conflictKey takes precedence)
                RegisterConflicts(conflictKey, handler.Name);
        }
    }

    /// <summary>
    ///     Registers ignored conflicts for all registered key handlers.
    ///     This method registers conflicts where our mod keys should ignore other keys.
    /// </summary>
    private static void RegisterIgnoredConflicts()
    {
        foreach (var handler in Keys)
        {
            var ignoredConflicts = handler.GetIgnoredConflicts()?.ToArray();
            if (ignoredConflicts == null) continue;

            Plugin.Log.LogDebug(
                $"Registering ignored conflicts for {handler.Name}: {string.Join(", ", ignoredConflicts)}");

            RegisterConflicts(handler.Name, ignoredConflicts);
        }
    }

    /// <summary>
    ///     Registers conflicts for a given key name with the provided conflicting keys.
    /// </summary>
    /// <param name="keyName">The name of the key to register conflicts for</param>
    /// <param name="conflictKeys">The keys that conflict with the main key</param>
    private static void RegisterConflicts(string keyName, params string[] conflictKeys)
    {
        if (!KeyManager.IgnoreConflictKeyMaps.TryAdd(keyName, conflictKeys.ToList()))
            KeyManager.IgnoreConflictKeyMaps[keyName].AddRange(conflictKeys);
    }
}