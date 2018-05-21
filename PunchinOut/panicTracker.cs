using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech;

namespace PunchinOut
{
    public enum PanicStatus
    {
        Normal,
        Fatigued,
        Stressed,
        Panicked
    }
    public class PanicTracker
    {
        public PanicStatus pilotStatus;
        public string trackedPilot;
        public bool ChangedRecently;

        public PanicTracker(Pilot pilot)
        {

            trackedPilot = pilot.GUID;
            pilotStatus = PanicStatus.Normal;
            ChangedRecently = false;
        }
    }
}
