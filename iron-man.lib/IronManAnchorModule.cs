using System;
using System.Xml.Serialization;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Native module-save envelope for the existing v1 anchor payload (KSA 5801 removed PartInstance.Id).</summary>
public sealed class IronManAnchorModule : Module<IronManAnchorModule>, IDisposable
{
    public const string SaveKey = "unscience.iron-man.anchors";

    [XmlType("UnscienceIronManAnchors")]
    public sealed class SaveData : ModuleBase.SaveDataBase
    {
        [XmlAttribute]
        public string Payload = "";

        public SaveData() => InstanceOf = SaveKey;
        public override void OnDataLoad(Mod mod) { }
    }

    public IronManAnchorModule() : base(SaveKey) { }

    // The constructor patch must restore connectors before native index reconnection. This module
    // claims that already-applied record so native module replay does not report it as dropped.
    public override void ApplySaveData(ModuleBase.SaveDataBase saveData, ModuleStateList states) { }
    public override void Dispose() { }
}
