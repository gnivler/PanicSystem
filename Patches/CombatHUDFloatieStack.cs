using System;
using BattleTech;
using BattleTech.UI;
using Harmony;
using Localize;
using PanicSystem.Components;
using static PanicSystem.Logger;
using static PanicSystem.PanicSystem;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    // have to patch both because they're used in different situations, with the same messages
    [HarmonyPatch(typeof(CombatHUDFloatieStack), "AddFloatie", typeof(FloatieMessage))]
    public static class CombatHUDFloatieStack_AddFloatie_Patch1
    {
        public static void Postfix(CombatHUDFloatieStack __instance)
        {
            if (modSettings.ColorizeFloaties)
            {
                try
                {
                    ColorFloaties.Colorize(__instance);
                }
                catch (Exception ex)
                {
                    LogDebug(ex);
                }
            }
        }
    }

    [HarmonyPatch(typeof(CombatHUDFloatieStack), "AddFloatie", typeof(Text), typeof(FloatieMessage.MessageNature))]
    public static class CombatHUDFloatieStack_AddFloatie_Patch2
    {
        public static void Postfix(CombatHUDFloatieStack __instance)
        {
            if (modSettings.ColorizeFloaties)
            {
                try
                {
                    ColorFloaties.Colorize(__instance);
                }
                catch (Exception ex)
                {
                    LogDebug(ex);
                }
            }
        }
    }
}
