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
        public readonly string Guid;

        public bool PanicWorsenedRecently;
        public bool PreventEjection;

        private AbstractActor actor;

        public PilotTracker()
        {
            // do nothing here, if this is called, then JSON is deserializing us
        }

        public PilotTracker(AbstractActor actor)
        {
            Guid = actor.GUID;
            this.actor = actor;
            PanicWorsenedRecently = false;
        }

        private Statistic PanicStat()
        {
            if (actor.StatCollection.GetStatistic("PanicStatus") == null)
            {
                return actor.StatCollection.AddStatistic("PanicStatus", 0);
            }

            return actor.StatCollection.GetStatistic("PanicStatus");
        }

        public PanicStatus PanicStatus
        {
            get => (PanicStatus) PanicStat().Value<int>();
            set => PanicStat().SetValue((int) value);
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
