using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using PunchinOut;
using System.IO;

namespace BasicPanic
{
    public static class Holder
    {
        public static List<PanicTracker> TrackedPilots;
        public static List<MetaTracker> metaTrackers;
        private static int CurrentIndex;
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
                if(metaTrackers[index].TrackedPilots != null)
                {
                    TrackedPilots = metaTrackers[index].TrackedPilots;
                    CurrentIndex = index;
                }
            }
        }

        public static int FindTrackerByTime(DateTime previousSaveTime)
        {
            for(int i = 0; i < metaTrackers.Count; i++)
            {
                if(metaTrackers[i].SaveGameTimeStamp == previousSaveTime)
                {
                    return i;
                }
            }

            return -1;
        }

        public static void SerializeStorageJson() //fired when a save game is made
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
            }

            try
            {
                if (metaTrackers != null)
                {
                    File.WriteAllText(StorageJsonPath, JsonConvert.SerializeObject(TrackedPilots));
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
