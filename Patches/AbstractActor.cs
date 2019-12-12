using System.Collections.Generic;
using BattleTech;
using Harmony;
using PanicSystem.Components;
using UnityEngine;
using static PanicSystem.Components.Controller;
using static PanicSystem.Logger;

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

            var index = GetPilotIndex(mech);
            // reduce panic level
            var originalStatus = TrackedPilots[index].PanicStatus;

            if (!TrackedPilots[index].PanicWorsenedRecently && (int) TrackedPilots[index].PanicStatus > 0)
            {
                TrackedPilots[index].PanicStatus--;
            }

            if (TrackedPilots[index].PanicStatus != originalStatus) // status has changed, reset modifiers
            {
                int Uid() => Random.Range(1, int.MaxValue);
                var effectManager = UnityGameInstance.BattleTechGame.Combat.EffectManager;

                // remove all PanicSystem effects first
                var effects = Traverse.Create(effectManager).Field("effects").GetValue<List<Effect>>();
                for (var i = 0; i < effects.Count; i++)
                {
                    if (effects[i].id.StartsWith("PanicSystem") && Traverse.Create(effects[i]).Field("target").GetValue<object>() == mech)
                    {
                        effectManager.CancelEffect(effects[i]);
                    }
                }

                // re-apply effects
                var message = __instance.Combat.MessageCenter;
                switch (TrackedPilots[index].PanicStatus)
                {
                    case PanicStatus.Unsettled:
                        LogReport($"{mech.DisplayName} condition improved: Unsettled");
                        message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO UNSETTLED!", FloatieMessage.MessageNature.Buff, false)));
                        effectManager.CreateEffect(StatusEffect.UnsettledToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                        break;
                    case PanicStatus.Stressed:
                        LogReport($"{mech.DisplayName} condition improved: Stressed");
                        message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO STRESSED!", FloatieMessage.MessageNature.Buff, false)));
                        effectManager.CreateEffect(StatusEffect.StressedToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                        effectManager.CreateEffect(StatusEffect.StressedToBeHit, "PanicSystemToBeHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                        break;
                    default:
                        LogReport($"{mech.DisplayName} condition improved: Confident");
                        message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO CONFIDENT!", FloatieMessage.MessageNature.Buff, false)));
                        break;
                }
            }

            // reset flag after reduction effect
            TrackedPilots[index].PanicWorsenedRecently = false;
            SaveTrackedPilots();
        }
    }
}
