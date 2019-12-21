using System;
using System.Collections.Generic;
using BattleTech;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem.Components
{
    public enum PanicStatus
    {
        Confident,
        Unsettled,
        Stressed,
        Panicked
    }

    public class PilotTracker
    {
        public bool PanicWorsenedRecently;
        public PanicStatus PanicStatus;
        public readonly string Mech;
        public bool PreventEjection;

        public PilotTracker()
        {
            // do nothing here, if this is called, then JSON is deserializing us
        }

        public PilotTracker(IGuid mech)
        {
            Mech = mech.GUID;
            PanicStatus = PanicStatus.Confident;
            PanicWorsenedRecently = false;
        }
    }

    public class MetaTracker
    {
        public List<PilotTracker> TrackedActors { get; set; }
        public DateTime SaveGameTimeStamp { get; set; }
        public string SimGameGuid { get; set; }
        
        public void SetGameGuid(string guid)
        {
            SimGameGuid = guid;
        }

        public void SetSaveGameTime(DateTime savedate)
        {
            SaveGameTimeStamp = savedate;
        }

        public void SetTrackedActors(List<PilotTracker> trackers)
        {
            TrackedActors = trackers;
        }
    }
}
