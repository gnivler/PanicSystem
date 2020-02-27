using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using BattleTech.UI;
using Harmony;
using PanicSystem.Components;
using static PanicSystem.Components.Controller;
using static PanicSystem.Logger;
using static PanicSystem.PanicSystem;
using Random = UnityEngine.Random;

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
            var originalStatus = TrackedActors[index].PanicStatus;
            if (!TrackedActors[index].PanicWorsenedRecently && (int) TrackedActors[index].PanicStatus > 0)
            {
                TrackedActors[index].PanicStatus--;
            }

            if (TrackedActors[index].PanicStatus != originalStatus) // status has changed, reset modifiers
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
                switch (TrackedActors[index].PanicStatus)
                {
                    case PanicStatus.Unsettled:
                        LogReport($"{mech.DisplayName} condition improved: Unsettled");
                        message.PublishMessage(new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(mech,
                                $"{modSettings.PanicImprovedString} {modSettings.PanicStates[1]}!",
                                FloatieMessage.MessageNature.Buff,
                                false)));
                        effectManager.CreateEffect(StatusEffect.UnsettledToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                        break;
                    case PanicStatus.Stressed:
                        LogReport($"{mech.DisplayName} condition improved: Stressed");
                        message.PublishMessage(new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(mech,
                                $"{modSettings.PanicImprovedString} {modSettings.PanicStates[2]}!",
                                FloatieMessage.MessageNature.Buff,
                                false)));
                        effectManager.CreateEffect(StatusEffect.StressedToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                        effectManager.CreateEffect(StatusEffect.StressedToBeHit, "PanicSystemToBeHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                        break;
                    default:
                        LogReport($"{mech.DisplayName} condition improved: Confident");
                        message.PublishMessage(new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(mech,
                                $"{modSettings.PanicImprovedString} {modSettings.PanicStates[0]}!",
                                FloatieMessage.MessageNature.Buff,
                                false)));
                        break;
                }
            }

            // reset flag after reduction effect
            TrackedActors[index].PanicWorsenedRecently = false;
            SaveTrackedPilots();
        }
    }
}
