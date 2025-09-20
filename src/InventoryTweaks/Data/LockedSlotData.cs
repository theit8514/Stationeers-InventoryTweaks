using System.Xml.Serialization;

namespace InventoryTweaks.Data;

public class LockedSlotData
{
    [XmlElement]
    public int SlotIndex { get; set; }

    [XmlElement]
    public string PrefabName { get; set; }
}