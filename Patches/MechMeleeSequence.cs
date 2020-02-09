using System.Collections.Generic;
using BattleTech;
using Harmony;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    // for reasons, AttackStackSequence et al have no?? way to tell if support weapons are firing so we stash a global
    [HarmonyPatch(typeof(MechMeleeSequence), "FireWeapons")]
    public static class MechMeleeSequence_FireWeapons_Patch
    {
        internal static bool meleeHasSupportWeapons;
        
        public static void Postfix(MechMeleeSequence __instance)
        {
            meleeHasSupportWeapons =
                Traverse.Create(__instance).Field("requestedWeapons").GetValue<List<Weapon>>().Count > 0;
        }
    }
}
