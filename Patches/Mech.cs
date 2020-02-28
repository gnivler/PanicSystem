using BattleTech;
using Harmony;
using static PanicSystem.PanicSystem;
using static PanicSystem.Components.Controller;

// ReSharper disable UnusedMember.Local
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(Mech), "AddExternalHeat")]
    public class Mech_AddExternalHeat_Patch
    {
        internal static int heatDamage;

        private static void Prefix(int amt)
        {
            heatDamage += amt;
        }
    }

    [HarmonyPatch(typeof(Mech), "OnLocationDestroyed")]
    public static class Mech_OnLocationDestroyed_Patch
    {
        private static void Postfix(Mech __instance)
        {
            var mech = __instance;
            if (!modSettings.LosingLimbAlwaysPanics ||
                mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
            {
                return;
            }

            var index = GetActorIndex(mech);
            if (TrackedActors[index].Guid != mech.GUID)
            {
                return;
            }

            if (TrackedActors[index].PanicWorsenedRecently && modSettings.OneChangePerTurn)
            {
                return;
            }

            TrackedActors[index].PanicStatus++;
        }
    }
}
