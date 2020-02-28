using BattleTech;
using Harmony;
using PanicSystem.Components;
using static PanicSystem.Components.Controller;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(AbstractActor), "OnNewRound")]
    public static class AbstractActor_OnNewRound_Patch
    {
        public static void Prefix(AbstractActor __instance)
        {
            if (!(__instance is Mech mech) || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
            {
                return;
            }

            var pilot = mech.GetPilot();
            if (pilot == null)
            {
                return;
            }

            var index = GetActorIndex(mech);

            // reduce panic level
            if (!TrackedActors[index].PanicWorsenedRecently &&
                TrackedActors[index].PanicStatus > 0)
            {
                Logger.LogDebug($"No panic failures for {__instance.Nickname} last turn - improving to {(TrackedActors[index].PanicStatus - 1)}");
                TrackedActors[index].PanicStatus--;
            }

            SaveTrackedPilots();
        }
    }
}
