using System.Diagnostics.CodeAnalysis;

namespace InventoryTweaks.Helpers;

/// <summary>
///     Subset of the attributes recognized by the optional in-game BepInEx Configuration Manager.
///     Configuration Manager discovers this type by name via reflection, so only the field names
///     matter. Attach an instance as a tag in a <see cref="BepInEx.Configuration.ConfigDescription" />
///     to influence how an entry is displayed.
/// </summary>
/// <remarks>
///     See https://github.com/BepInEx/BepInEx.ConfigurationManager for the full attribute set.
/// </remarks>
[SuppressMessage("ReSharper", "NotAccessedField.Global")]
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>
    ///     When <see langword="false" />, the entry is hidden from the Configuration Manager UI.
    ///     The value is still written to and read from the config file.
    /// </summary>
    public bool? Browsable;

    /// <summary>
    ///     When <see langword="true" />, the entry is only shown while the Configuration Manager's
    ///     "Advanced settings" filter is enabled.
    /// </summary>
    public bool? IsAdvanced;
}