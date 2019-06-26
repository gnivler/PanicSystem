using System;
using System.Collections.Generic;
using BattleTech;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
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
        public bool panicWorsenedRecently;
        public PanicStatus panicStatus;
        public string mech;

        public PilotTracker()
        {
            // do nothing here, if this is called, then JSON is deserializing us
        }

        public PilotTracker(Mech mech)
        {
            this.mech = mech.GUID;
            panicStatus = PanicStatus.Confident;
            panicWorsenedRecently = false;
        }
    }

    public class MetaTracker
    {
        public List<PilotTracker> TrackedPilots { get; set; }
        public DateTime SaveGameTimeStamp { get; set; }
        public string SimGameGUID { get; set; }

        // ReSharper disable once InconsistentNaming
        public void SetGameGUID(string GUID)
        {
            SimGameGUID = GUID;
        }

        public void SetSaveGameTime(DateTime savedate)
        {
            SaveGameTimeStamp = savedate;
        }

        public void SetTrackedPilots(List<PilotTracker> trackers)
        {
            TrackedPilots = trackers;
        }
    }
}
