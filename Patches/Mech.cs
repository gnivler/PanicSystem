using BattleTech;
using Harmony;
using static PanicSystem.Logger;
using static PanicSystem.PanicSystem;
using static PanicSystem.Components.Controller;
using static PanicSystem.Helpers;

// ReSharper disable UnusedMember.Local
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    // properly aggregates heat damage?
    [HarmonyPatch(typeof(Mech), "AddExternalHeat")]
    public class Mech_AddExternalHeat_Patch
    {
        internal static int heatDamage;

        private static void Prefix(int amt)
        {
            heatDamage += amt;
            LogReport($"Running heat total: {heatDamage}");
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

            var index = GetPilotIndex(mech);
            if (TrackedPilots[index].Mech != mech.GUID) return;

            if (TrackedPilots[index].PanicWorsenedRecently && modSettings.OneChangePerTurn) return;

            ApplyPanicDebuff(mech);
        }
    }
}
