using System;
using System.Collections.Generic;
using System.IO;
using BattleTech;
using Newtonsoft.Json;
using static PanicSystem.PanicSystem;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class Controller
    {
        public static List<PanicTracker> trackedPilots;
        public static List<MetaTracker> metaTrackers;
        public static int currentIndex = -1;

        public static void Reset()
        {
            trackedPilots = new List<PanicTracker>();
        }

        public static void Resync(DateTime previousSaveTime) // fired before a save deserializes itself through a patch on GameInstanceSave's PostDeserialization
        {
            DeserializeStorageJson();
            var index = FindTrackerByTime(previousSaveTime);
            if (index > -1)
            {
                if (metaTrackers[index]?.TrackedPilots != null) // part where everything seems to fall apart?  (legacy comment)
                {
                    trackedPilots = metaTrackers[index]?.TrackedPilots;
                }

                currentIndex = index;
            }
            else if (metaTrackers != null) // we were unable to find a tracker, add our own
            {
                var tracker = new MetaTracker();

                tracker.SetTrackedPilots(trackedPilots);
                metaTrackers.Add(tracker);
                currentIndex = metaTrackers.Count - 1; // -1 due to zero-based arrays
            }
        }

        public static void SyncNewCampaign() //fired when player starts a new campaign
        {
            DeserializeStorageJson();
            if (metaTrackers != null) //we were unable to find a tracker, add our own
            {
                var tracker = new MetaTracker();
                tracker.SetTrackedPilots(trackedPilots);
                metaTrackers.Add(tracker);
                currentIndex = metaTrackers.Count - 1; // -1 due to zero-based arrays
            }
        }

        public static int FindTrackerByTime(DateTime previousSaveTime)
        {
            if (metaTrackers == null) return -1;
            for (var i = 0; i < metaTrackers.Count; i++)
            {
                if (metaTrackers[i].SaveGameTimeStamp == previousSaveTime)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        ///   create or obtain tracker data and save it out
        /// </summary>
        /// <param name="GUID"></param>
        /// <param name="dateTime"></param>
        public static void SerializeStorageJson(string GUID, DateTime dateTime)
        {
            if (metaTrackers == null)
            {
                metaTrackers = new List<MetaTracker>();
            }
            else if (currentIndex > -1)
            {
                var index = currentIndex;
                if (metaTrackers[index] != null) //have our meta tracker get the latest data
                {
                    metaTrackers[index].SetTrackedPilots(trackedPilots);
                }

                metaTrackers[index].SetSaveGameTime(dateTime);

                if (GUID != null) //set GUID if it's applicable
                {
                    if (metaTrackers[index].SimGameGUID != GUID)
                    {
                        metaTrackers[index].SetGameGUID(GUID);
                    }
                }

                try
                {
                    if (metaTrackers != null)
                    {
                        File.WriteAllText(storageJsonPath, JsonConvert.SerializeObject(metaTrackers));
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError(e);
                }
            }
        }

        public static void DeserializeStorageJson() //fired when we're close to using the json data
        {
            List<MetaTracker> trackers;
            try
            {
                trackers = JsonConvert.DeserializeObject<List<MetaTracker>>(File.ReadAllText(storageJsonPath));
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                trackers = null;
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
                if (trackedPilots != null)
                {
                    File.WriteAllText(activeJsonPath, JsonConvert.SerializeObject(trackedPilots));
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }

        public static void DeserializeActiveJson()
        {
            if (trackedPilots == null) //we only need to deserialize if we have nothing here: this way resets should work properly
            {
                List<PanicTracker> panicTrackers;
                try
                {
                    panicTrackers = JsonConvert.DeserializeObject<List<PanicTracker>>(File.ReadAllText(activeJsonPath)); // read all text, then deserialize into an object
                }
                catch (Exception e)
                {
                    Logger.LogError(e);
                    panicTrackers = null;
                }

                if (panicTrackers == null)
                {
                    trackedPilots = new List<PanicTracker>();
                }
                else
                {
                    trackedPilots = panicTrackers;
                }
            }
        }

        public static int GetTrackedPilotIndex(Mech mech)
        {
            if (mech == null)
            {
                return -1;
            }

            if (trackedPilots == null)
            {
                DeserializeActiveJson();
            }

            for (var i = 0; i < trackedPilots.Count; i++)
            {
                if (trackedPilots[i].trackedMech == mech.GUID)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}