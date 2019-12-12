using BattleTech;
using Harmony;
using static PanicSystem.Components.Controller;

// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(SimGameState), "_OnFirstPlayInit")]
    public static class SimGameState__OnFirstPlayInit_Patch
    {
        // we're doing a new campaign, so we need to sync the json with the new addition
        private static void Postfix()
        {
            // if campaigns are added this way, why does deleting the storage jsons not break it?
            SyncNewCampaign();
        }
    }
}
