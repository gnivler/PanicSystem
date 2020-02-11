using System;
using System.Linq;
using BattleTech;
using Harmony;
using TScript.Ops;
using UnityEngine;
using UnityEngine.UI;
using static PanicSystem.Logger;

// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming

namespace PanicSystem.Components
{
    public static class AARIcons
    {
        internal static int GetMechEjectionCount(UnitResult unitResult)
        {
            LogDebug("GetMechEjectionCount");
            return unitResult.pilot.StatCollection.GetStatistic("MechsEjected") == null
                ? 0
                : unitResult.pilot.StatCollection.GetStatistic("MechsEjected").Value<int>();
        }

        internal static int GetVehicleEjectionCount(UnitResult unitResult)
        {
            LogDebug("GetVehicleEjectionCount");
            return unitResult.pilot.StatCollection.GetStatistic("VehiclesEjected") == null
                ? 0
                : unitResult.pilot.StatCollection.GetStatistic("VehiclesEjected").Value<int>();
        }

        // adapted from AddKilledMech()
        internal static void AddEjectedMech(RectTransform KillGridParent)
        {
            try
            {
                var dm = UnityGameInstance.BattleTechGame.DataManager;
                const string id = "uixPrfIcon_AA_mechKillStamp";
                var prefab = dm.PooledInstantiate(id, BattleTechResourceType.UIModulePrefabs, null, null, KillGridParent);
                var image = prefab.GetComponent<Image>();
                image.color = Color.red;
                var biggerDropsRunning =
                    AppDomain.CurrentDomain.GetAssemblies().Count(x => x.FullName.ToLower().Contains("biggerdrop")) == 1;
                if (biggerDropsRunning)
                {
                    LogDebug("Scaling stamps due to BiggerDrops");
                    prefab.transform.localScale = Vector3.one;
                }
                else
                {
                    AppDomain.CurrentDomain.GetAssemblies().Do(LogDebug);
                }
            }
            catch (Exception ex)
            {
                LogDebug(ex);
            }
        }

        internal static void AddEjectedVehicle(RectTransform KillGridParent)
        {
            try
            {
                var dm = UnityGameInstance.BattleTechGame.DataManager;
                const string id = "uixPrfIcon_AA_vehicleKillStamp";
                var prefab = dm.PooledInstantiate(id, BattleTechResourceType.UIModulePrefabs, null, null, KillGridParent);
                var image = prefab.GetComponent<Image>();
                image.color = Color.red;
            }
            catch (Exception ex)
            {
                LogDebug(ex);
            }
        }
    }
}
