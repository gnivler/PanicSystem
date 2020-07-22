using BattleTech;
using Harmony;
using PanicSystem.Components;
using System;
using static PanicSystem.Components.Controller;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(AttackDirector))]
    [HarmonyPatch("OnAttackComplete")]
    [HarmonyPatch(MethodType.Normal)]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(new Type[] { typeof(MessageCenterMessage) })]
    public static class AttackDirector_OnAttackCompleteTA
    {
        public static void Postfix(AttackDirector __instance, MessageCenterMessage message)
        {
                TurnDamageTracker.hintAttackComplete("AttackDirector:OnAttackComplete");
        }
    }
}
