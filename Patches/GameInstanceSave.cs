using BattleTech;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using Harmony;
using static PanicSystem.Components.Controller;
using static PanicSystem.Logger;

// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(GameInstanceSave), MethodType.Constructor)]
    [HarmonyPatch(new[] {typeof(GameInstance), typeof(SaveReason)})]
    public static class GameInstanceSave_Constructor_Patch
    {
        private static void Postfix(GameInstanceSave __instance) => SerializeStorageJson(__instance.InstanceGUID, __instance.SaveTime);
    }

    [HarmonyPatch(typeof(LanceSpawnerGameLogic), "OnTriggerSpawn")]
    public static class LanceSpawnerGameLogic_OnTriggerSpawn_Patch
    {
        // throw away the return of GetPilotIndex because the method is just adding the missing mechs
        public static void Postfix(LanceSpawnerGameLogic __instance)
        {
            Log("Lance spawn - building pilot index");
            __instance.Combat.AllMechs.ForEach(x => GetActorIndex(x));
        }
    }
}
