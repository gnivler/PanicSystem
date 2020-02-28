using BattleTech;
using Harmony;
using static PanicSystem.Components.Controller;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(AbstractActor), "OnNewRound")]
    public static class AbstractActor_OnNewRound_Patch
    {
        public static void Prefix(AbstractActor __instance)
        {
            if (__instance.IsDead || __instance.IsFlaggedForDeath && __instance.HasHandledDeath)
            {
                return;
            }

            var pilot = __instance.GetPilot();
            if (pilot == null)
            {
                Logger.LogDebug($"No pilot found for {__instance.Nickname}:{__instance.GUID}");
                return;
            }

            var index = GetActorIndex(__instance);

            // reduce panic level
            if (!TrackedActors[index].PanicWorsenedRecently &&
                TrackedActors[index].PanicStatus > 0)
            {
                Logger.LogDebug($"No panic failures for {__instance.Nickname} last turn - improving to {(TrackedActors[index].PanicStatus - 1)}");
                TrackedActors[index].PanicStatus--;
            }

            TrackedActors[index].PanicWorsenedRecently = false;
            SaveTrackedPilots();
        }
    }
}
