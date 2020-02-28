using System;
using System.Collections.Generic;
using BattleTech;
using UnityEngine;

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
        private readonly AbstractActor actor;

        public PilotTracker()
        {
            // do nothing here, if this is called, then JSON is deserializing us
        }

        public PilotTracker(AbstractActor actor)
        {
            Guid = actor.GUID;
            this.actor = actor;
            Stat.SetValue(0);
        }

        private Statistic Stat
        {
            get
            {
                var sc = actor.StatCollection;
                return sc.GetStatistic("PanicStatus") ?? sc.AddStatistic("PanicStatus", 0);
            }
        }

        internal PanicStatus PanicStatus
        {
            get => (PanicStatus) Stat.Value<int>();
            set
            {
                try
                {
                    if (PanicStatus != value)
                    {
                        var clamped = Mathf.Clamp((int) value, 0, 3);
                        Helpers.ApplyPanicStatus(actor, (PanicStatus) clamped, (PanicStatus) clamped > PanicStatus);
                        Stat.SetValue(clamped);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex);
                }
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
}
