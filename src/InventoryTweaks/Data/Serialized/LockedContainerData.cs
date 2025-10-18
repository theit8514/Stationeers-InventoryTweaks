using System.Collections.Generic;
using System.Xml.Serialization;

namespace InventoryTweaks.Data.Serialized;

public class LockedContainerData
{
    [XmlElement]
    public long ContainerId { get; set; }

    [XmlArray("LockedSlots")]
    [XmlArrayItem("LockedSlot")]
    public List<LockedSlotData> LockedSlots { get; set; }
}