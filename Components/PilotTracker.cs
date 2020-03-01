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
        private PanicStatus panicStatus;

        public PilotTracker()
        {
            // do nothing here, if this is called, then JSON is deserializing us
        }

        public PilotTracker(AbstractActor actor)
        {
            Guid = actor.GUID;
            PanicStatus = PanicStatus.Confident;
        }

        public PanicStatus PanicStatus
        {
            get => panicStatus;
            set
            {
                try
                {
                    if (UnityGameInstance.BattleTechGame.Combat == null ||
                        panicStatus == value)
                    {
                        return;
                    }

                    var clamped = (PanicStatus) Mathf.Clamp((int) value, 0, 3);
                    var actor = UnityGameInstance.BattleTechGame.Combat.FindActorByGUID(Guid);
                    Helpers.ApplyPanicStatus(actor, clamped, clamped >= panicStatus);
                    panicStatus = clamped;
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
