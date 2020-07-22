using BattleTech;
using Harmony;
using PanicSystem.Components;
using System;
using static PanicSystem.Components.Controller;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(ActorMovementSequence))]
    [HarmonyPatch("CompleteOrders")]
    [HarmonyPatch(MethodType.Normal)]
    [HarmonyPatch(new Type[] { })]
    public static class ActorMovementSequence_CompleteOrders
    {
        public static void Postfix(ActorMovementSequence __instance)
        {
                TurnDamageTracker.hintAttackComplete("ActorMovementSequence:CompleteOrders");
        }
    }
}
