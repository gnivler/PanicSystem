using System;
using BattleTech;
using UnityEngine;
using UnityEngine.UI;
using static PanicSystem.Logger;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Components
{
    public static class AARIcons
    {
        internal static int GetEjectionCount(UnitResult unitResult)
        {
            return unitResult.pilot.StatCollection.GetStatistic("MechsEjected") == null
                ? 0
                : unitResult.pilot.StatCollection.GetStatistic("MechsEjected").Value<int>();
        }

        //  adapted from AddKilledMech()    
        // ReSharper disable once InconsistentNaming
        internal static void AddEjectedMech(RectTransform KillGridParent)
        {
            try
            {
                var dm = UnityGameInstance.BattleTechGame.DataManager;
                const string id = "uixPrfIcon_AA_mechKillStamp";
                var prefab = dm.PooledInstantiate(id, BattleTechResourceType.Prefab, null, null, KillGridParent);
                var image = prefab.GetComponent<Image>();
                image.color = Color.red;
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
    }
}
