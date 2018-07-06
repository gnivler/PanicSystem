using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace RogueTechPanicSystem
{
    public static class Holder
    {
        public static List<PanicTracker> TrackedPilots;
        public static List<MetaTracker> metaTrackers;
        private static int CurrentIndex = -1;
        public static string ActiveJsonPath; //store current tracker here
        public static string StorageJsonPath; //store our meta trackers here
        public static string ModDirectory;

        public static void Reset()
        {
            TrackedPilots = new List<PanicTracker>();
        }
        public static void Resync(DateTime previousSaveTime) //fired before a save deserializes itself through a patch on GameInstanceSave's PostDeserialization
        {
            DeserializeStorageJson();
            int index = FindTrackerByTime(previousSaveTime);

            if(index > -1)
            {
                if(metaTrackers[index].TrackedPilots != null) //part where everything seems to fall apart?
                {
                    TrackedPilots = metaTrackers[index].TrackedPilots;
                }
                CurrentIndex = index;
            }
            else if(metaTrackers != null)//we were unable to find a tracker, add our own
            {
                MetaTracker tracker = new MetaTracker();

                tracker.SetTrackedPilots(TrackedPilots);
                metaTrackers.Add(tracker);
                CurrentIndex = metaTrackers.Count - 1; // -1 due to zero-based arrays

            }
        }

        public static void SyncNewCampaign() //fired when player starts a new campaign
        {
            DeserializeStorageJson();
            if (metaTrackers != null)//we were unable to find a tracker, add our own
            {
                MetaTracker tracker = new MetaTracker();

                tracker.SetTrackedPilots(TrackedPilots);
                metaTrackers.Add(tracker);
                CurrentIndex = metaTrackers.Count - 1; // -1 due to zero-based arrays

            }
        }

        public static int FindTrackerByTime(DateTime previousSaveTime)
        {
            if(metaTrackers == null)
            {
                return -1;
            }
            for(int i = 0; i < metaTrackers.Count; i++)
            {
                if(metaTrackers[i].SaveGameTimeStamp == previousSaveTime)
                {
                    return i;
                }
            }
            return -1;
        }

        public static void SerializeStorageJson(string GUID, DateTime dateTime) //fired when a save game is made
        {
            if(metaTrackers == null)
            {
                metaTrackers = new List<MetaTracker>();
            }
            else if (CurrentIndex > -1)
            {
                int index = CurrentIndex;
                if(metaTrackers[index] != null)
                {
                    metaTrackers[index].SetTrackedPilots(TrackedPilots); //have our meta tracker get the latest data
                }
                if(dateTime != null)
                {
                    metaTrackers[index].SetSaveGameTime(dateTime);
                }
                if (GUID != null) //set GUID if it's applicable
                {
                    if(metaTrackers[index].SimGameGUID != GUID)
                    {
                        metaTrackers[index].SetGameGUID(GUID);
                    }
                }
            }
            try
            {
                if (metaTrackers != null)
                {
                    File.WriteAllText(StorageJsonPath, JsonConvert.SerializeObject(metaTrackers));
                }
            }
            catch (Exception)
            {
                return;
            }
        }
        public static void DeserializeStorageJson() //fired when we're close to using the json data
        {
            List<MetaTracker> trackers;
            try
            {
                trackers = JsonConvert.DeserializeObject<List<MetaTracker>>(File.ReadAllText(StorageJsonPath));
            }
            catch (Exception)
            {
                trackers = null;
            }
            if(trackers == null)
            {
                metaTrackers = new List<MetaTracker>();
            }
            else
            {
                metaTrackers = trackers;
            }
        }

        public static void SerializeActiveJson()
        {
            try
            {
                if (TrackedPilots != null)
                {
                    File.WriteAllText(ActiveJsonPath, JsonConvert.SerializeObject(TrackedPilots));
                }
            }
            catch (Exception)
            {
                return;
            }
        }

        public static void DeserializeActiveJson()
        {
            if (TrackedPilots == null) //we only need to deserialize if we have nothing here: this way resets should work properly
            {
                // read all text, then deserialize into an object
                List<PanicTracker> panicTrackers;
                try
                {
                    panicTrackers = JsonConvert.DeserializeObject<List<PanicTracker>>(File.ReadAllText(ActiveJsonPath));
                }
                catch (Exception)
                {
                    panicTrackers = null;
                }
                if (panicTrackers == null)
                {
                    TrackedPilots = new List<PanicTracker>();
                }
                else
                {
                    TrackedPilots = panicTrackers;
                }
            }
        }
    }
}
