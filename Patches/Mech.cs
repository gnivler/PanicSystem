using BattleTech;
using Harmony;
using System;
using static PanicSystem.PanicSystem;
using static PanicSystem.Components.Controller;
using static PanicSystem.Logger;
using PanicSystem.Components;

// ReSharper disable UnusedMember.Local
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(Mech), "AddExternalHeat")]
    public static class Mech_AddExternalHeat_Patch
    {

        private static void Postfix(Mech __instance, string reason, int amt)
        {
            if (__instance == null)
            {
                LogDebug("No mech");
                return;
            }
            LogReport($"\n{new string('^', 46)}");
            LogReport($"{__instance.DisplayName} :{__instance.GUID } took {amt} Heat Damage from {reason ?? "null"}");
            DamageHandler.ProcessDamage(__instance, 0, 0, amt);

        }
    }

    [HarmonyPatch(typeof(Mech))]
    [HarmonyPatch("TakeWeaponDamage")]
    [HarmonyPatch(MethodType.Normal)]
    [HarmonyPatch(new Type[] { typeof(WeaponHitInfo), typeof(int), typeof(Weapon), typeof(float), typeof(float), typeof(int), typeof(DamageType) })]
    public static class Mech_TakeWeaponDamage
    {
        public static void Postfix(Mech __instance, WeaponHitInfo hitInfo, int hitLocation, Weapon weapon, float damageAmount, float directStructureDamage, int hitIndex, DamageType damageType)
        {
            if (__instance == null)
            {
                LogDebug("No mech");
                return;
            }
            LogReport($"\n{new string('^', 46)}");
            string wname = (weapon != null) ? (weapon.Name ?? "null") : "null";
            LogReport($"{__instance.DisplayName} :{__instance.GUID } took Damage from {wname} - {damageType.ToString()}");
            DamageHandler.ProcessDamage(__instance, damageAmount, directStructureDamage, 0);
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
