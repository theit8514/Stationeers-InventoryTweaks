using System.Collections.Generic;
using System.Xml.Serialization;
using Assets.Scripts.Serialization;

namespace InventoryTweaks.Data;

[XmlRoot("InventoryTweaks")]
public class InventoryTweaksSaveData
{
    public static XmlSerializer Serializer = new(typeof(InventoryTweaksSaveData), new[]
    {
        typeof(LockedContainerData)
    });

    [XmlArray("LockedSlotsData")]
    [XmlArrayItem("LockedContainer")]
    public List<LockedContainerData> LockedSlotsData = new();

    public static InventoryTweaksSaveData Deserialize(string path)
    {
        return XmlSerialization.Deserialize(Serializer, path) as InventoryTweaksSaveData;
    }

    public bool Serialize(string path)
    {
        return XmlSerialization.Serialization(Serializer, this, path);
    }
}