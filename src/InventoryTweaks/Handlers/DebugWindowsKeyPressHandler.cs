using System.Linq;
using Assets.Scripts.UI;
using UnityEngine;

namespace InventoryTweaks.Handlers;

public class DebugWindowsKeyPressHandler : KeyPressHandler
{
    /// <inheritdoc />
    public override string Name => "DebugWindows";

    /// <inheritdoc />
    public override KeyCode DefaultKey => KeyCode.F8;

    protected override void Execute()
    {
        var instance = InventoryWindowManager.Instance;
        Plugin.Log.LogInfo($"Number of windows: {instance.Windows.Count}");
        var visibleWindows = instance.Windows.Count(x => x.IsVisible);
        Plugin.Log.LogInfo($"Number of visible windows: {visibleWindows}");
        var visibleDockedWindows = instance.Windows.Count(x => !x.IsUndocked && x.IsVisible);
        Plugin.Log.LogInfo($"Number of visible docked windows: {visibleDockedWindows}");

        var parentTransform = (RectTransform)instance.WindowGrid.transform;
        Plugin.Log.LogInfo(
            $"RectTransform parent: {parentTransform.name} {parentTransform?.anchorMin} {parentTransform?.anchorMax} {parentTransform?.anchoredPosition} {parentTransform?.offsetMin} {parentTransform?.offsetMax}");
        for (var i = 0; i < parentTransform.childCount; i++)
        {
            var childTransform = parentTransform.GetChild(i);
            Plugin.Log.LogInfo(!childTransform.TryGetComponent<InventoryWindow>(out var childWindow)
                ? $"Child {i}: {childTransform.name} A:{childTransform.gameObject.activeSelf}"
                : $"Child {i}: {childTransform.name} A:{childTransform.gameObject.activeSelf} V:{childWindow.IsVisible} D:{!childWindow.IsUndocked}");
            var rectTransform = childTransform as RectTransform;
            Plugin.Log.LogInfo(
                $"{rectTransform?.anchorMin} {rectTransform?.anchorMax} {rectTransform?.anchoredPosition} {rectTransform?.offsetMin} {rectTransform?.offsetMax}");
        }
    }
}