using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace KSA;

public sealed class SubPartTemplate { }
public sealed class TransformDataReference<T> { }
public sealed class Mod { }
public sealed class ModuleStateList { }
public abstract class ModuleBase
{
    public abstract class SaveDataBase
    {
        [XmlAttribute] public string InstanceOf = "";
        [XmlAttribute] public uint LocalInstanceId;
        public abstract void OnDataLoad(Mod mod);
    }
    public string TemplateId;
    public required Part Parent { get; set; }
    protected ModuleBase(string id) => TemplateId = id;
    public virtual void ApplySaveData(SaveDataBase data, ModuleStateList states) { }
}
public abstract class Module<T>(string id) : ModuleBase(id) { public virtual void Dispose() { } }
public sealed class ModuleList
{
    public List<ModuleBase> Items = new();
    public void Add<T>(T module) where T : ModuleBase => Items.Add(module);
}
public sealed class UniverseData { public PartInstance? Root; }
public sealed class VehicleSaveData { public PartInstance? Root; }
public static class XmlHelper
{
    public static XmlAttributeOverrides BuildAttributeOverrides()
    {
        var result = new XmlAttributeOverrides();
        result.Add(typeof(PartInstance), nameof(PartInstance.Components), new XmlAttributes());
        return result;
    }
}
public static class GameSaves
{
    public static XmlSerializer UniverseSerializer
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get => new(typeof(UniverseData));
    }
}
public static class VehicleSaves
{
    public static XmlSerializer VehicleSerializer
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get => new(typeof(VehicleSaveData));
    }
}
