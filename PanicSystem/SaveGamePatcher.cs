using BattleTech;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using Harmony;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    [HarmonyPatch(typeof(GameInstanceSave))]
    [HarmonyPatch(new[] { typeof(GameInstance), typeof(SaveReason) })]
    public static class GameInstanceSave_Constructor_Patch
    {
        static void Postfix(GameInstanceSave __instance)
        {
            Controller.SerializeStorageJson(__instance.InstanceGUID, __instance.SaveTime);
        }
    }

    [HarmonyPatch(typeof(GameInstance), "Load")]
    public static class GameInstance_Load_Patch
    {
        static void Prefix(GameInstanceSave save)
        {
            Controller.Resync(save.SaveTime);
        }
    }

    [HarmonyPatch(typeof(SimGameState), "_OnFirstPlayInit")]
    public static class SimGameState_FirstPlayInit_Patch
    {
        static void Postfix(SimGameState __instance) //we're doing a new campaign, so we need to sync the json with the new addition
        {
            Controller.SyncNewCampaign();
        }
    }
}
