using BattleTech;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using BattleTech.UI;
using Harmony;
using System.Collections.Generic;
using System.Linq;
using static PanicSystem.Controller;
using static PanicSystem.PanicSystem;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class Patches
    {
        [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
        public static class AttackStackSequence_OnAttackComplete_Patch
        {
            public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
            {
                Logger.Debug(new string(c: '-', count: 60));
                Logger.Debug($"{__instance.owningActor.DisplayName} attacked {__instance.targets.First().DisplayName}");
                AttackCompleteMessage attackCompleteMessage = message as AttackCompleteMessage;

                Mech mech = null;
                if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID) return;

                // this bool makes ejection saves harder
                PanicStarted = false;

                // this bool is the normal panic reason, which can be saved against
                bool hasReasonToPanic = false;

                // simple flag to see if damage was done
                var attack = Traverse.Create(__instance).Field("directorSequences").GetValue<List<AttackDirector.AttackSequence>>();
                var damaged = attack.Any(x => x.attackDidDamage);

                if (__instance.directorSequences[0].target is Mech)
                {
                    mech = (Mech) __instance.directorSequences[0].target;

                    // sets global variable that last-straw is met and only when it damages target
                    LastStraw = (IsLastStrawPanicking(mech, ref PanicStarted) && damaged);
                    hasReasonToPanic = ShouldPanic(mech, attackCompleteMessage.attackSequence);
                }

                if (mech?.GUID == null)
                {
                    return;
                }

                SerializeActiveJson();

                // ejection check
                if (KlutzEject | LastStraw || hasReasonToPanic && RollForEjectionResult(mech, attackCompleteMessage.attackSequence, PanicStarted))
                {
                    Logger.Debug($"FAILED SAVE: Punchin' Out!!");
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"FAILED SAVE: Punchin' Out!!", FloatieMessage.MessageNature.Debuff, true)));

                    // this is necessary to avoid vanilla hangs.  the list has nulls so the try/catch deals with silently
                    Logger.Debug($"Cancelling all mech effects.");
                    var combat = Traverse.Create(__instance).Property("Combat").GetValue<CombatGameState>();
                    List<Effect> effectsTargeting = combat.EffectManager.GetAllEffectsTargeting(mech);

                    foreach (Effect effect in effectsTargeting)
                    {
                        try
                        {
                            mech.CancelEffect(effect);
                        }
                        catch
                        {
                        }
                    }

                    Logger.Debug($"Done removing effects.");
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

                if (pilot == null) return;

                index = GetTrackedPilotIndex(mech);
                if (index > -1)
                {
                    foundPilot = true;
                }

                if (!foundPilot)
                {
                    PanicTracker panicTracker = new PanicTracker(mech);
                    TrackedPilots.Add(panicTracker); //add a new tracker to tracked pilot, then we run it all over again
                    index = GetTrackedPilotIndex(mech);
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
                        Logger.Debug("IMPROVED PANIC TO UNSETTLED!");
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"IMPROVED PANIC TO UNSETTLED", FloatieMessage.MessageNature.Buff, true)));
                        __instance.StatCollection.ModifyStat("Panic Turn: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, PanicSystem.ModSettings.UnsettledAttackModifier);
                    }

                    else if (TrackedPilots[index].PilotStatus == PanicStatus.Stressed)
                    {
                        Logger.Debug("IMPROVED PANIC TO STRESSED!");
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"IMPROVED PANIC TO STRESSED", FloatieMessage.MessageNature.Buff, true)));
                        __instance.StatCollection.ModifyStat("Panic Turn: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, PanicSystem.ModSettings.StressedAimModifier);
                        __instance.StatCollection.ModifyStat("Panic Turn: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, PanicSystem.ModSettings.StressedToHitModifier);
                    }

                    else
                    {
                        Logger.Debug("IMPROVED PANIC TO CONFIDENT!");
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED PANIC TO CONFIDENT", FloatieMessage.MessageNature.Buff, true)));
                    }
                }

                SerializeActiveJson();
            }
        }

        [HarmonyPatch(typeof(GameInstance), "LaunchContract", new[] {typeof(Contract), typeof(string)})]
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
                var mech = __instance;
                if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
                {
                    return;
                }

                int index = GetTrackedPilotIndex(mech);
                if (PanicSystem.ModSettings.LosingLimbAlwaysPanics)
                {
                    if (TrackedPilots[index].TrackedMech != mech.GUID)
                    {
                        return;
                    }

                    if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].ChangedRecently && PanicSystem.ModSettings.OneChangePerTurn)
                    {
                        return;
                    }

                    if (index < 0)
                    {
                        TrackedPilots.Add(new PanicTracker(mech)); //add a new tracker to tracked pilot, then we run it all over again;
                        index = GetTrackedPilotIndex(mech);
                        if (index < 0) // G  Why does this matter?
                        {
                            return;
                        }
                    }

                    ApplyPanicDebuff(mech, index);
                }
            }
        }

        [HarmonyPatch(typeof(GameInstanceSave))]
        [HarmonyPatch(new[] {typeof(GameInstance), typeof(SaveReason)})]
        public static class GameInstanceSave_Constructor_Patch
        {
            static void Postfix(GameInstanceSave __instance)
            {
                SerializeStorageJson(__instance.InstanceGUID, __instance.SaveTime);
            }
        }

        [HarmonyPatch(typeof(GameInstance), "Load")]
        public static class GameInstance_Load_Patch
        {
            static void Prefix(GameInstanceSave save)
            {
                Resync(save.SaveTime);
            }
        }

        [HarmonyPatch(typeof(SimGameState), "_OnFirstPlayInit")]
        public static class SimGameState_FirstPlayInit_Patch
        {
            static void Postfix(SimGameState __instance) //we're doing a new campaign, so we need to sync the json with the new addition
            {
                SyncNewCampaign();
            }
        }
    }
}