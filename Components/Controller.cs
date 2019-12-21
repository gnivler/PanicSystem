using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using Newtonsoft.Json;
using static PanicSystem.PanicSystem;
using static PanicSystem.Logger;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem.Components
{
    public static class Controller
    {
        internal static List<PilotTracker> TrackedActors;
        private static List<MetaTracker> metaTrackers;
        private static int currentIndex = -1;

        public static void Reset()
        {
            try
            {
                var combat = UnityGameInstance.BattleTechGame.Combat;
                var group = combat?.AllMechs.Except(combat.AllEnemies);
                if (group == null)
                {
                    return;
                }

                foreach (var mech in group)
                {
                    var pilot = mech.GetPilot();
                    var statCollection = pilot.StatCollection;
                    statCollection.Set("MechsEjected", 0);
                }

                TrackedActors = new List<PilotTracker>();
            }
            catch (Exception ex)
            {
                LogDebug(ex.ToString());
            }
        }

        // fired before a save deserializes itself through a patch on GameInstanceSave's PostDeserialization
        public static void Resync(DateTime previousSaveTime)
        {
            DeserializeStorageJson();
            var index = FindTrackerByTime(previousSaveTime);
            if (index > -1)
            {
                // part where everything seems to fall apart?  (legacy comment)
                if (metaTrackers[index]?.TrackedActors != null)
                {
                    TrackedActors = metaTrackers[index]?.TrackedActors;
                }

                currentIndex = index;
            }
            // we were unable to find a tracker, add our own
            else if (metaTrackers != null)
            {
                var tracker = new MetaTracker();

                tracker.SetTrackedActors(TrackedActors);
                metaTrackers.Add(tracker);
                // -1 due to zero-based arrays
                currentIndex = metaTrackers.Count - 1;
            }
        }

        //fired when player starts a new campaign
        public static void SyncNewCampaign()
        {
            DeserializeStorageJson();
            //we were unable to find a tracker, add our own
            if (metaTrackers != null)
            {
                var tracker = new MetaTracker();
                tracker.SetTrackedActors(TrackedActors);
                metaTrackers.Add(tracker);
                // -1 due to zero-based arrays
                currentIndex = metaTrackers.Count - 1;
            }
        }

        private static int FindTrackerByTime(DateTime previousSaveTime)
        {
            if (metaTrackers == null) return -1;
            for (var i = 0; i < metaTrackers.Count; i++)
            {
                if (metaTrackers[i].SaveGameTimeStamp == previousSaveTime) return i;
            }

            return -1;
        }

        /// <summary>
        ///   create or obtain tracker data and save it out
        /// </summary>
        /// <param name="GUID"></param>
        /// <param name="dateTime"></param>
        // ReSharper disable once InconsistentNaming
        public static void SerializeStorageJson(string GUID, DateTime dateTime)
        {
            if (metaTrackers == null)
            {
                metaTrackers = new List<MetaTracker>();
            }
            else if (currentIndex > -1)
            {
                var index = currentIndex;
                // have our meta tracker get the latest data
                if (metaTrackers[index] != null)
                {
                    metaTrackers[index].SetTrackedActors(TrackedActors);
                }

                metaTrackers[index].SetSaveGameTime(dateTime);

                //set GUID if it's applicable
                if (GUID != null)
                {
                    if (metaTrackers[index].SimGameGuid != GUID)
                    {
                        metaTrackers[index].SetGameGuid(GUID);
                    }
                }

                try
                {
                    if (metaTrackers != null)
                    {
                        File.WriteAllText(storageJsonPath, JsonConvert.SerializeObject(metaTrackers));
                    }
                }
                catch (Exception ex)
                {
                    LogDebug(ex);
                }
            }
        }

        //fired when we're close to using the json data
        private static void DeserializeStorageJson()
        {
            List<MetaTracker> trackers = null;
            try
            {
                trackers = JsonConvert.DeserializeObject<List<MetaTracker>>(File.ReadAllText(storageJsonPath));
            }
            catch
            {
                // ignored
            }

            if (trackers == null)
            {
                metaTrackers = new List<MetaTracker>();
            }
            else
            {
                metaTrackers = trackers;
            }
        }

        public static void SaveTrackedPilots()
        {
            try
            {
                if (TrackedActors != null)
                {
                    File.WriteAllText(activeJsonPath, JsonConvert.SerializeObject(TrackedActors));
                }
            }
            catch (Exception ex)
            {
                LogDebug(ex);
            }
        }

        private static void DeserializeActiveJson()
        {
            // we only need to deserialize if we have nothing here: this way resets should work properly
            if (TrackedActors != null) return;
            List<PilotTracker> panicTrackers = null;
            try
            {
                // read all text, then deserialize into an object
                panicTrackers = JsonConvert.DeserializeObject<List<PilotTracker>>(File.ReadAllText(activeJsonPath));
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch
            {
            }

            if (panicTrackers == null)
            {
                TrackedActors = new List<PilotTracker>();
            }
            else
            {
                TrackedActors = panicTrackers;
            }
        }

        public static int GetActorIndex(AbstractActor actor)
        {
            while (true)
            {
                if (actor == null)
                {
                    return -1;
                }

                if (TrackedActors == null)
                {
                    DeserializeActiveJson();
                }

                for (var i = 0; i < TrackedActors?.Count; i++)
                {
                    if (TrackedActors[i].Mech == actor.GUID)
                    {
                        return i;
                    }
                }

                TrackedActors?.Add(new PilotTracker(actor));
                SaveTrackedPilots();
            }
        }
    }
}
