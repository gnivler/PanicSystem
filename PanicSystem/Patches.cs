using BattleTech;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using BattleTech.UI;
using Harmony;
using System;
using System.Linq;
using static PanicSystem.Controller;
using static PanicSystem.PanicSystem;
using System.Collections.Generic;

namespace PanicSystem
{
    public static class Patches
    {
        [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
        public static class AttackStackSequence_OnAttackComplete_Patch
        {
            public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
            {
                Logger.Harmony(new string(c: '-', count: 80));
                Logger.Harmony($"{__instance.owningActor.DisplayName} attacked {__instance.targets.First().DisplayName}");
                AttackCompleteMessage attackCompleteMessage = message as AttackCompleteMessage;

                Mech mech = null;
                if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
                {
                    return;
                }

                // this bool makes ejection saves harder
                PanicStarted = false;

                // this bool is the normal operation, vs force-ejection from last-straw events
                bool hasReasonToPanic = false;

                var attack = Traverse.Create(__instance).Field("directorSequences").GetValue<List<AttackDirector.AttackSequence>>();
                var damaged = attack.Any(x => x.attackDidDamage);

                if (__instance.directorSequences[0].target is Mech)
                {
                    mech = (Mech)__instance.directorSequences[0].target;
                    // sets global variable that last-straw is met
                    Logger.Harmony($"Start on last straw? {LastStraw}");
                    LastStraw = (IsLastStrawPanicking(mech, ref PanicStarted) && damaged);
                    Logger.Harmony($"End on last straw? {LastStraw}");
                    Logger.Harmony($"Start with reason? {hasReasonToPanic}");
                    hasReasonToPanic = ShouldPanic(mech, attackCompleteMessage.attackSequence);
                    Logger.Harmony($"End with reason? {hasReasonToPanic}");
                }

                if (mech?.GUID == null)
                {
                    return;
                }

                SerializeActiveJson();

                // ejection check
                if (LastStraw || hasReasonToPanic &&
                    RollForEjectionResult(mech, attackCompleteMessage.attackSequence, PanicStarted))
                {
                    // ejecting, clean up
                    try
                    {
                        mech.EjectPilot(mech.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
                    }
                    catch (Exception e)
                    {
                        Logger.Harmony($"exception: {e.Message}\n{e.StackTrace}");
                    }
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

                index = GetTrackedPilotIndex(mech);
                if (index > -1)
                {
                    foundPilot = true;
                }

                if (!foundPilot)
                {
                    PanicTracker panicTracker = new PanicTracker(mech);
                    TrackedPilots.Add(panicTracker); //add a new tracker to tracked pilot, then we run it all over again;;
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
                        Logger.Harmony("IMPROVED PANIC TO UNSETTLED!");
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                                                                      (new ShowActorInfoSequence(mech, $"IMPROVED PANIC TO UNSETTLED", FloatieMessage.MessageNature.Buff, true)));
                        __instance.StatCollection.ModifyStat("Panic Turn: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, PanicSystem.Settings.UnsettledAttackModifier);
                    }

                    else if (TrackedPilots[index].PilotStatus == PanicStatus.Stressed)
                    {
                        Logger.Harmony("IMPROVED PANIC TO STRESSED!");
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                                                                      (new ShowActorInfoSequence(mech, $"IMPROVED PANIC TO STRESSED", FloatieMessage.MessageNature.Buff, true)));
                        __instance.StatCollection.ModifyStat("Panic Turn: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, PanicSystem.Settings.StressedAimModifier);
                        __instance.StatCollection.ModifyStat("Panic Turn: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, PanicSystem.Settings.StressedToHitModifier);
                    }

                    else //now Confident
                    {
                        Logger.Harmony("IMPROVED PANIC TO CONFIDENT!");
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED PANIC TO CONFIDENT", FloatieMessage.MessageNature.Buff, true)));
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
                var mech = __instance;
                if (mech == null || mech.IsDead ||
                    (mech.IsFlaggedForDeath && mech.HasHandledDeath))
                {
                    return;
                }

                int index = GetTrackedPilotIndex(mech);
                if (PanicSystem.Settings.LosingLimbAlwaysPanics)
                {
                    if (TrackedPilots[index].TrackedMech != mech.GUID)
                    {
                        return;
                    }

                    if (TrackedPilots[index].TrackedMech == mech.GUID &&
                        TrackedPilots[index].ChangedRecently &&
                        PanicSystem.Settings.AlwaysGatedChanges)
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
        [HarmonyPatch(new[] { typeof(GameInstance), typeof(SaveReason) })]
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
