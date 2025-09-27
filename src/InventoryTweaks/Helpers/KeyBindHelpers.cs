using System;
using HarmonyLib;
using InputSystem;
using UnityEngine;

namespace InventoryTweaks.Helpers;

public static class KeyBindHelpers
{
    [HarmonyReversePatch]
    [HarmonyPatch(typeof(KeyManager), "AddKey")]
    public static void AddKey(
#pragma warning disable IDE0060
        string assignmentName,
        KeyCode keyCode,
        ControlsGroup controlsGroup,
        bool hidden = false)
#pragma warning restore IDE0060
    {
        // Stub method will not get called.
    }

    [HarmonyReversePatch]
    [HarmonyPatch("InputSystem.KeyWrapBindings, Assembly-CSharp", "Bind")]
    public static void Bind(
#pragma warning disable IDE0060
        KeyWrap keyWrap,
        InputPhase phase,
        Action callback,
        KeyInputState inputStates = KeyInputState.All)
#pragma warning restore IDE0060
    {
        // Stub method will not get called.
    }
}