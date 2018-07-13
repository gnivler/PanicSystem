using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace RogueTechPanicSystem
{
    public static class Holder
    {
        public static List<PanicTracker> TrackedPilots;
        private static List<MetaTracker> _metaTrackers;
        private static int _currentIndex = -1;
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
                if(_metaTrackers[index].TrackedPilots != null) //part where everything seems to fall apart?
                {
                    TrackedPilots = _metaTrackers[index].TrackedPilots;
                }
                _currentIndex = index;
            }
            else if(_metaTrackers != null)//we were unable to find a tracker, add our own
            {
                MetaTracker tracker = new MetaTracker();

                tracker.SetTrackedPilots(TrackedPilots);
                _metaTrackers.Add(tracker);
                _currentIndex = _metaTrackers.Count - 1; // -1 due to zero-based arrays

            }
        }

        public static void SyncNewCampaign() //fired when player starts a new campaign
        {
            DeserializeStorageJson();
            if (_metaTrackers != null)//we were unable to find a tracker, add our own
            {
                MetaTracker tracker = new MetaTracker();

                tracker.SetTrackedPilots(TrackedPilots);
                _metaTrackers.Add(tracker);
                _currentIndex = _metaTrackers.Count - 1; // -1 due to zero-based arrays

            }
        }

        public static int FindTrackerByTime(DateTime previousSaveTime)
        {
            if(_metaTrackers == null)
            {
                return -1;
            }
            for(int i = 0; i < _metaTrackers.Count; i++)
            {
                if(_metaTrackers[i].SaveGameTimeStamp == previousSaveTime)
                {
                    return i;
                }
            }
            return -1;
        }

        public static void SerializeStorageJson(string GUID, DateTime dateTime) //fired when a save game is made
        {
            if(_metaTrackers == null)
            {
                _metaTrackers = new List<MetaTracker>();
            }
            else if (_currentIndex > -1)
            {
                int index = _currentIndex;
                if(_metaTrackers[index] != null)
                {
                    _metaTrackers[index].SetTrackedPilots(TrackedPilots); //have our meta tracker get the latest data
                }
                if(dateTime != null)
                {
                    _metaTrackers[index].SetSaveGameTime(dateTime);
                }
                if (GUID != null) //set GUID if it's applicable
                {
                    if(_metaTrackers[index].SimGameGUID != GUID)
                    {
                        _metaTrackers[index].SetGameGUID(GUID);
                    }
                }
            }
            try
            {
                if (_metaTrackers != null)
                {
                    File.WriteAllText(StorageJsonPath, JsonConvert.SerializeObject(_metaTrackers));
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }

        private static void DeserializeStorageJson() //fired when we're close to using the json data
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
            _metaTrackers = trackers ?? new List<MetaTracker>();
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
            catch (Exception e)
            {
                Logger.LogError(e);
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
                TrackedPilots = panicTrackers ?? new List<PanicTracker>();
            }
        }
    }
}
