using BattleTech;
using Harmony;
using PanicSystem.Components;
using System;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(CombatGameState))]
    [HarmonyPatch("OnCombatGameDestroyed")]
    [HarmonyPatch(MethodType.Normal)]
    [HarmonyPatch(new Type[] { })]
    public static class CombatGameState_OnCombatGameDestroyedMap
    {
            private static void Postfix() => TurnDamageTracker.Reset();
    }
}
