using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts;

namespace InventoryTweaks.Helpers;

internal static class LocalizationHelper
{
    private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Type LocalizationType;

    static LocalizationHelper()
    {
        LocalizationType = typeof(Localization);
    }

    public static class Interactable
    {
        private static readonly FieldInfo FieldLocalizationName;
        private static readonly FieldInfo FieldFallbackLocalizationName;

        static Interactable()
        {
            FieldLocalizationName = LocalizationType.GetField("InteractableName", StaticNonPublic);
            if (FieldLocalizationName == null)
                Plugin.Log.LogFatal($"Failed to find InteractableName field of {nameof(Localization)}");
            FieldFallbackLocalizationName = LocalizationType.GetField("FallbackInteractableName", StaticNonPublic);
            if (FieldFallbackLocalizationName == null)
                Plugin.Log.LogFatal($"Failed to find FallbackInteractableName field of {nameof(Localization)}");
        }

        public static string GetLocalizedDisplayName(int hashKey, string fallback)
        {
            // Get the 'Next' button's display name from the Localization dictionary.
            string GetValue(FieldInfo field)
            {
                var dictionary = field.GetValue(null) as Dictionary<int, string>;
                return dictionary?.TryGetValue(hashKey, out var v) == true
                    ? v
                    : null;
            }

            var value = GetValue(FieldLocalizationName);
            value ??= GetValue(FieldFallbackLocalizationName);
            return value ?? fallback;
        }
    }
}