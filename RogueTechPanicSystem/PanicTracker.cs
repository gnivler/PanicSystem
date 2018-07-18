using BattleTech;
using System;
using System.Collections.Generic;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace RogueTechPanicSystem
{
    public enum PanicStatus
    {
        Normal,
        Unsettled,
        Stressed,
        Panicked
    }

    public class PanicTracker
    {
        public PanicStatus pilotStatus;
        public string trackedMech;
        public bool ChangedRecently;

        public PanicTracker()
        {
            //do nothing here, if this is called, then JSON is deserializing us
        }
        public PanicTracker(Mech mech)
        {
            trackedMech = mech.GUID;
            pilotStatus = PanicStatus.Normal;
            ChangedRecently = false;
        }
    }

    public class MetaTracker
    {
        public List<PanicTracker> TrackedPilots { get; set; }
        public DateTime SaveGameTimeStamp { get; set; }
        public string SimGameGUID { get; set; }

        public MetaTracker()
        {
            //do nothing for this is when we deserialize/serialize objects
        }

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
