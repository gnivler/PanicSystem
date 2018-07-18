using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using static PanicSystem.PanicSystem;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class Holder
    {
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
                if(MetaTrackers[index].TrackedPilots != null) //part where everything seems to fall apart?
                {
                    TrackedPilots = MetaTrackers[index].TrackedPilots;
                }
                CurrentIndex = index;
            }
            else if(MetaTrackers != null)//we were unable to find a tracker, add our own
            {
                MetaTracker tracker = new MetaTracker();

                tracker.SetTrackedPilots(TrackedPilots);
                MetaTrackers.Add(tracker);
                CurrentIndex = MetaTrackers.Count - 1; // -1 due to zero-based arrays

            }
        }

        public static void SyncNewCampaign() //fired when player starts a new campaign
        {
            DeserializeStorageJson();
            if (MetaTrackers != null)//we were unable to find a tracker, add our own
            {
                MetaTracker tracker = new MetaTracker();

                tracker.SetTrackedPilots(TrackedPilots);
                MetaTrackers.Add(tracker);
                CurrentIndex = MetaTrackers.Count - 1; // -1 due to zero-based arrays

            }
        }

        public static int FindTrackerByTime(DateTime previousSaveTime)
        {
            if(MetaTrackers == null)
            {
                return -1;
            }
            for(int i = 0; i < MetaTrackers.Count; i++)
            {
                if(MetaTrackers[i].SaveGameTimeStamp == previousSaveTime)
                {
                    return i;
                }
            }
            return -1;
        }

        public static void SerializeStorageJson(string GUID, DateTime dateTime) //fired when a save game is made
        {
            if(MetaTrackers == null)
            {
                MetaTrackers = new List<MetaTracker>();
            }
            else if (CurrentIndex > -1)
            {
                int index = CurrentIndex;
                if(MetaTrackers[index] != null)
                {
                    MetaTrackers[index].SetTrackedPilots(TrackedPilots); //have our meta tracker get the latest data
                }
                if(dateTime != null)
                {
                    MetaTrackers[index].SetSaveGameTime(dateTime);
                }
                if (GUID != null) //set GUID if it's applicable
                {
                    if(MetaTrackers[index].SimGameGUID != GUID)
                    {
                        MetaTrackers[index].SetGameGUID(GUID);
                    }
                }
            }
            try
            {
                if (MetaTrackers != null)
                {
                    File.WriteAllText(StorageJsonPath, JsonConvert.SerializeObject(MetaTrackers));
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
                MetaTrackers = new List<MetaTracker>();
            }
            else
            {
                MetaTrackers = trackers;
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
