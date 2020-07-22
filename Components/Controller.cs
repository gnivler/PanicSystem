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
        private static List<PilotTracker.MetaTracker> metaTrackers;
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

                // prevent stat history build-up
                foreach (var mech in group)
                {
                    var pilot = mech.GetPilot();
                    var statCollection = pilot.StatCollection;
                    statCollection.RemoveStatistic("MechsEjected");
                    statCollection.RemoveStatistic("VehiclesEjected");
                }

                TrackedActors = new List<PilotTracker>();
                TurnDamageTracker.Reset();
            }
            catch (Exception ex)
            {
                LogDebug(ex);
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
                var tracker = new PilotTracker.MetaTracker();

                tracker.SetTrackedActors(TrackedActors);
                metaTrackers.Add(tracker);
                // -1 due to zero-based arrays
                currentIndex = metaTrackers.Count - 1;
            }
        }

        // fired when player starts a new campaign
        public static void SyncNewCampaign()
        {
            DeserializeStorageJson();
            // we were unable to find a tracker, add our own
            if (metaTrackers != null)
            {
                var tracker = new PilotTracker.MetaTracker();
                tracker.SetTrackedActors(TrackedActors);
                metaTrackers.Add(tracker);
                // -1 due to zero-based arrays
                currentIndex = metaTrackers.Count - 1;
            }
        }

        private static int FindTrackerByTime(DateTime previousSaveTime)
        {
            if (metaTrackers == null)
            {
                return -1;
            }

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
        ///  create or obtain tracker data and save it out
        /// </summary>
        /// <param name="GUID"></param>
        /// <param name="dateTime"></param>
        // ReSharper disable once InconsistentNaming
        public static void SerializeStorageJson(string GUID, DateTime dateTime)
        {
            if(!modSettings.CombatSaves)
                return;
            if (metaTrackers == null)
            {
                metaTrackers = new List<PilotTracker.MetaTracker>();
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
            if (!modSettings.CombatSaves)
                return;
            List<PilotTracker.MetaTracker> trackers = null;
            try
            {
                trackers = JsonConvert.DeserializeObject<List<PilotTracker.MetaTracker>>(File.ReadAllText(storageJsonPath));
            }
            catch (Exception ex)
            {
                //LogDebug("DeserializeStorageJson");
                LogDebug(ex.Message);
            }

            if (trackers == null)
            {
                metaTrackers = new List<PilotTracker.MetaTracker>();
            }
            else
            {
                metaTrackers = trackers;
            }
        }

        public static void SaveTrackedPilots()
        {
            if (!modSettings.CombatSaves)
                return;
            try
            {
                if (TrackedActors != null)
                {
                    File.WriteAllText(activeJsonPath, JsonConvert.SerializeObject(TrackedActors));
                }
            }
            catch (Exception ex)
            {
                LogDebug("SaveTrackedPilots");
                LogDebug(ex);
            }
        }

        private static void DeserializeActiveJson()
        {

            // we only need to deserialize if we have nothing here: this way resets should work properly
            if (TrackedActors != null)
            {
                return;
            }

            List<PilotTracker> panicTrackers = null;
            try
            {
                if (modSettings.CombatSaves)
                {
                    // read all text, then deserialize into an object
                    panicTrackers = JsonConvert.DeserializeObject<List<PilotTracker>>(File.ReadAllText(activeJsonPath));
                }
            }
            catch (Exception ex)
            {
                LogDebug("DeserializeActiveJson");
                LogDebug(ex);
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
                    LogDebug("actor is null");
                    return -1;
                }

                if (TrackedActors == null)
                {
                    LogDebug("DeserializeActiveJson");
                    DeserializeActiveJson();
                }

                // Count could be 0...
                for (var i = 0; i < TrackedActors?.Count; i++)
                {
                    if (TrackedActors[i].Guid == actor.GUID)
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
