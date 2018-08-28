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

    public class PanicTracker
    {
        public bool panicWorsenedRecently;
        public PanicStatus pilotStatus;
        public string trackedMech;

        public PanicTracker()
        {
            //do nothing here, if this is called, then JSON is deserializing us
        }

        public PanicTracker(Mech mech)
        {
            trackedMech = mech.GUID;
            pilotStatus = PanicStatus.Confident;
            panicWorsenedRecently = false;
        }
    }

    public class MetaTracker
    {
        public List<PanicTracker> TrackedPilots { get; set; }
        public DateTime SaveGameTimeStamp { get; set; }
        public string SimGameGUID { get; set; }

        public void SetGameGUID(string GUID)
        {
            SimGameGUID = GUID;
        }

        public void SetSaveGameTime(DateTime savedate)
        {
            SaveGameTimeStamp = savedate;
        }

        public void SetTrackedPilots(List<PanicTracker> trackers)
        {
            TrackedPilots = trackers;
        }
    }
}