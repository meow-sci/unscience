using System;
using System.Collections.Generic;

namespace KSA
{
    public sealed class KittenRosterEntryData
    {
        public const string EvaAssignmentId = "EVA";
        public string Name { get; set; } = "";
        public string Character { get; set; } = "";
        public string AssignedVehicleId { get; set; } = "";
        public string NameHash => Name.ToUpperInvariant();
        public bool AssignedToVehicle => AssignedVehicleId.Length > 0;
        public bool Kia { get; set; }
        public bool Retired { get; set; }
        public KittenDeathCause CauseOfDeath { get; set; }
        public int MissionCount { get; set; }
        public double CurrentMissionStartTime { get; set; }
        public double TotalMissionTime { get; set; }
        public double LifetimeDose { get; set; }
        public double TravelledMeters { get; set; }
        public KittenRosterEntryData() { }
        public KittenRosterEntryData(string name, string character) { Name = name; Character = character; }
        public void StartMission(string vehicleId)
        {
            AssignedVehicleId = vehicleId;
            CurrentMissionStartTime = Universe.Now;
            MissionCount++;
        }
        public void TransferToVehicle(string vehicleId) => AssignedVehicleId = vehicleId;
        public void EndMission()
        {
            if (!AssignedToVehicle) return;
            TotalMissionTime += Universe.Now - CurrentMissionStartTime;
            AssignedVehicleId = "";
            CurrentMissionStartTime = 0;
        }
        public void Kill(bool hasLaunched, KittenDeathCause cause)
        {
            if (Kia) return;
            Kia = true; CauseOfDeath = cause;
            if (hasLaunched) EndMission();
            else AssignedVehicleId = "";
        }
    }

    internal sealed class KittenRosterData
    {
        public KittenRosterData() { }
        public List<KittenRosterEntryData> Kittens { get; set; } = new();
        public KittenRosterEntryData? Find(string name) => Kittens.Find(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

}
