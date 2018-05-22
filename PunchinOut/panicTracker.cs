using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech;
using BattleTech.Serialization;

namespace PunchinOut
{

    public enum PanicStatus
    {
        Normal,
        Fatigued,
        Stressed,
        Panicked
    }

    [SerializableContract("Holder")]
    public class PanicTracker
    {
        [SerializableMember(SerializationTarget.SaveGame)]
        public PanicStatus pilotStatus;

        [SerializableMember(SerializationTarget.SaveGame)]
        public string trackedMech;

        [SerializableMember(SerializationTarget.SaveGame)]
        public bool ChangedRecently;

        public PanicTracker(Mech mech)
        {

            trackedMech = mech.GUID;
            pilotStatus = PanicStatus.Normal;
            ChangedRecently = false;
        }
    }
}
