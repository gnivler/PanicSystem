using System.Collections.Generic;
using BattleTech;
using BattleTech.UI;
using Harmony;
using static PanicSystem.Controller;
using static PanicSystem.PanicSystem;

namespace PanicSystem
{
    public class Patches
    {
        [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
        public static class AttackStackSequence_OnAttackComplete_Patch
        {
            public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
            {
                Logger.Debug("OnAttackComplete!");
                AttackCompleteMessage attackCompleteMessage = message as AttackCompleteMessage;
                Logger.Debug($"set message to {attackCompleteMessage}!");

                bool hasReasonToPanic = false;
                Mech mech = null;
                if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
                {
                    return;
                }

                if (__instance.directorSequences[0].target is Mech)
                {
                    mech = (Mech) __instance.directorSequences[0].target;
                    hasReasonToPanic = ShouldPanic(mech, attackCompleteMessage.attackSequence);
                }

                if (mech == null || mech.GUID == null || attackCompleteMessage == null)
                {
                    return;
                }

                SerializeActiveJson();
                if (PanicHelpers.IsLastStrawPanicking(mech, ref hasReasonToPanic) &&
                    RollForEjectionResult(mech, attackCompleteMessage.attackSequence, hasReasonToPanic))
                {
                    var combat = Traverse.Create(__instance).Property("Combat").GetValue<CombatGameState>();
                    List<Effect> effectsTargeting = combat.EffectManager.GetAllEffectsTargeting(mech);
                    foreach (Effect effect in effectsTargeting)
                    {
                        mech.CancelEffect(effect);
                    }

                    mech.EjectPilot(mech.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
                }
            }
        }

        [HarmonyPatch(typeof(AbstractActor), "OnNewRound")]
        public static class AbstractActor_BeginNewRound_Patch
        {
            public static void Prefix(AbstractActor __instance)
            {
                if (!(__instance is Mech mech) || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
                {
                    return;
                }

                bool foundPilot = false;
                Pilot pilot = mech.GetPilot();
                int index = -1;

                if (pilot == null)
                {
                    return;
                }

                index = PanicHelpers.GetTrackedPilotIndex(mech);
                if (index > -1)
                {
                    foundPilot = true;
                }

                if (!foundPilot)
                {
                    PanicTracker panicTracker = new PanicTracker(mech);
                    TrackedPilots
                        .Add(panicTracker); //add a new tracker to tracked pilot, then we run it all over again;;
                    index = PanicHelpers.GetTrackedPilotIndex(mech);
                    if (index > -1)
                    {
                        foundPilot = true;
                    }
                    else
                    {
                        return;
                    }
                }

                PanicStatus originalStatus = TrackedPilots[index].PilotStatus;
                if (foundPilot && !TrackedPilots[index].ChangedRecently)
                {
                    switch (TrackedPilots[index].PilotStatus)
                    {
                        case PanicStatus.Unsettled:
                            TrackedPilots[index].PilotStatus = PanicStatus.Confident;
                            break;
                        case PanicStatus.Stressed:
                            TrackedPilots[index].PilotStatus = PanicStatus.Unsettled;
                            break;
                        case PanicStatus.Panicked:
                            TrackedPilots[index].PilotStatus = PanicStatus.Stressed;
                            break;
                    }
                }

                //reset panic values to account for panic level changes if we get this far, and we recovered.
                if (TrackedPilots[index].ChangedRecently)
                {
                    TrackedPilots[index].ChangedRecently = false;
                }
                else if (TrackedPilots[index].PilotStatus != originalStatus)
                {
                    __instance.StatCollection.ModifyStat("Panic Turn Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                    __instance.StatCollection.ModifyStat("Panic Turn Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);

                    if (TrackedPilots[index].PilotStatus == PanicStatus.Unsettled)
                    {
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                                                                      (new ShowActorInfoSequence(mech, $"Unsettled", FloatieMessage.MessageNature.Debuff, true)));
                        __instance.StatCollection.ModifyStat("Panic Turn: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, PanicSystem.Settings.UnsettledAttackModifier, -1, true);
                    }

                    else if (TrackedPilots[index].PilotStatus == PanicStatus.Stressed)
                    {
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage 
                                                                      (new ShowActorInfoSequence(mech, $"Stressed", FloatieMessage.MessageNature.Debuff, true)));
                        __instance.StatCollection.ModifyStat("Panic Turn: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, PanicSystem.Settings.StressedAimModifier);
                        __instance.StatCollection.ModifyStat("Panic Turn: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, PanicSystem.Settings.StressedToHitModifier);
                    }

                    else //now Confident
                    {
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage (new ShowActorInfoSequence(mech, "Confident", FloatieMessage.MessageNature.Buff, true)));
                    }
                }
                SerializeActiveJson();
            }
        }

        [HarmonyPatch(typeof(GameInstance), "LaunchContract", new[] { typeof(Contract), typeof(string) })]
        public static class BattleTech_GameInstance_LaunchContract_Patch
        {
            static void Postfix()
            {
                // reset on new contracts
                Reset();
            }
        }

        [HarmonyPatch(typeof(AAR_SalvageScreen), "OnCompleted")]
        public static class Battletech_SalvageScreen_Patch
        {
            static void Postfix()
            {
                Reset(); //don't keep data we don't need after a mission
            }
        }

        [HarmonyPatch(typeof(Mech), "OnLocationDestroyed")]
        public static class Battletech_Mech_LocationDestroyed_Patch
        {
            static void Postfix(Mech __instance)
            {
                if (__instance == null || __instance.IsDead ||
                    (__instance.IsFlaggedForDeath && __instance.HasHandledDeath))
                {
                    return;
                }

                int index = PanicHelpers.GetTrackedPilotIndex(__instance);
                if (PanicSystem.Settings.LosingLimbAlwaysPanics)
                {
                    if (TrackedPilots[index].TrackedMech != __instance.GUID)
                    {
                        return;
                    }

                    if (TrackedPilots[index].TrackedMech == __instance.GUID &&
                        TrackedPilots[index].ChangedRecently &&
                        PanicSystem.Settings.AlwaysGatedChanges)
                    {
                        return;
                    }

                    if (index < 0)
                    {
                        TrackedPilots.Add(new PanicTracker(__instance)); //add a new tracker to tracked pilot, then we run it all over again;
                        index = PanicHelpers.GetTrackedPilotIndex(__instance);
                        if (index < 0) // G  Why does this matter?
                        {
                            return;
                        }
                    }

                    ApplyPanicDebuff(__instance, index);
                }
            }
        }
    }
}
