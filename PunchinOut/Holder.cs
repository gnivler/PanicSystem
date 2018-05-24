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
        public static int CurrentIndex;
        public static string ActiveJsonPath; //store current tracker here
        public static string StorageJsonPath; //store our meta trackers here
        public static string ModDirectory;

        public static void Reset()
        {
            TrackedPilots = new List<PanicTracker>();
        }
        public static void Resync(DateTime previousSaveTime) //fired before a save deserializes itself through a patch on GameInstanceSave's PostDeserialization
        {
            int index = FindTracker(previousSaveTime);

            if(index > -1)
            {
                if(metaTrackers[index].TrackedPilots != null)
                {
                    TrackedPilots = metaTrackers[index].TrackedPilots;
                }
            }
        }

        public static int FindTracker(DateTime previousSaveTime)
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
        public static void DeserializeStorageJson() //fired on game start
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
