using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using BattleTech.UI;
using Harmony;
using static PanicSystem.Controller;
using static PanicSystem.PanicSystem;

// ReSharper disable UnusedMember.Local

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class Patches
    {
        [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
        public static class AttackStackSequenceOnAttackCompletePatch
        {
            public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
            {
                var attackCompleteMessage = message as AttackCompleteMessage;

                Mech mech = null;
                if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
                {
                    return;
                }

                PanicStarted = false; //  makes ejection saves harder
                var hasReasonToPanic = false; // is the normal panic reason, which can be saved against

                // simple flag to see if damage was done
                var attack = Traverse.Create(__instance).Field("directorSequences").GetValue<List<AttackDirector.AttackSequence>>();
                var damaged = attack.Any(x => x.attackDidDamage);

                if (__instance.directorSequences[0].target is Mech) // can't do stuff with vehicles and buildings
                {
                    Logger.Debug(new string(c: '-', count: 60));
                    Logger.Debug($"{__instance.directorSequences[0].attacker.LogDisplayName}\n-> attacks ->\n{__instance.directorSequences[0].target.LogDisplayName}");
                    mech = (Mech) __instance.directorSequences[0].target;

                    // sets global variable that last-straw is met and only when it damages target
                    LastStraw = IsLastStrawPanicking(mech, ref PanicStarted) && damaged;
                    hasReasonToPanic = ShouldPanic(mech, attackCompleteMessage.attackSequence);
                }

                if (mech?.GUID == null)
                {
                    return;
                }

                SerializeActiveJson();

                // Klutz and LastStraw immediately eject, otherwise it has to have a reason and fail a save
                if (KlutzEject | LastStraw || hasReasonToPanic && RollForEjectionResult(mech, attackCompleteMessage.attackSequence, PanicStarted))
                {
                    Logger.Debug($"FAILED SAVE: Punchin' Out!!");

                    // this is necessary to avoid vanilla hangs.  the list has nulls so the try/catch deals with silently.  thanks jo
                    var combat = Traverse.Create(__instance).Property("Combat").GetValue<CombatGameState>();
                    var effectsTargeting = combat.EffectManager.GetAllEffectsTargeting(mech);

                    foreach (var effect in effectsTargeting)
                        try
                        {
                            mech.CancelEffect(effect);
                        }
                        // ReSharper disable once EmptyGeneralCatchClause
                        catch // deliberately silent
                        {
                        }

                    mech.EjectPilot(mech.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
                    KlutzEject = false; // reset global variable after ejecting pilot
                }
            }
        }

        [HarmonyPatch(typeof(AbstractActor), "OnNewRound")]
        public static class AbstractActorBeginNewRoundPatch
        {
            public static void Prefix(AbstractActor __instance)
            {
                if (!(__instance is Mech mech) || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
                {
                    return;
                }

                var foundPilot = false;
                var pilot = mech.GetPilot();

                if (pilot == null)
                {
                    return;
                }

                var index = GetTrackedPilotIndex(mech);
                if (index > -1)
                {
                    foundPilot = true;
                }

                if (!foundPilot)
                {
                    var panicTracker = new PanicTracker(mech);
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

// todo make sure this isn't fucked either
                var originalStatus = TrackedPilots[index].PilotStatus;
                if (!TrackedPilots[index].ChangedRecently)
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

                // reset panic values to account for panic level changes if we get this far, and we recovered
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
                        Logger.Debug("IMPROVED TO UNSETTLED!");
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"IMPROVED TO UNSETTLED", FloatieMessage.MessageNature.Buff, true)));
                        __instance.StatCollection.ModifyStat("Panic Turn: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.UnsettledAttackModifier);
                    }
                    else if (TrackedPilots[index].PilotStatus == PanicStatus.Stressed)
                    {
                        Logger.Debug("IMPROVED TO STRESSED!");
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"IMPROVED TO STRESSED", FloatieMessage.MessageNature.Buff, true)));
                        __instance.StatCollection.ModifyStat("Panic Turn: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.StressedAimModifier);
                        __instance.StatCollection.ModifyStat("Panic Turn: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, ModSettings.StressedToHitModifier);
                    }
                    else
                    {
                        Logger.Debug("IMPROVED TO CONFIDENT!");
                        __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO CONFIDENT", FloatieMessage.MessageNature.Buff, true)));
                    }
                }

                SerializeActiveJson();
            }
        }

        [HarmonyPatch(typeof(GameInstance), "LaunchContract", new[] {typeof(Contract), typeof(string)})]
        public static class BattleTech_GameInstance_LaunchContract_Patch
        {
            private static void Postfix()
            {
                // reset on new contracts
                Reset();
            }
        }

        [HarmonyPatch(typeof(AAR_SalvageScreen), "OnCompleted")]
        public static class BattletechSalvageScreenPatch
        {
            private static void Postfix()
            {
                Reset(); //don't keep data we don't need after a mission
            }
        }

        [HarmonyPatch(typeof(Mech), "OnLocationDestroyed")]
        public static class BattletechMechLocationDestroyedPatch
        {
            private static void Postfix(Mech __instance)
            {
                var mech = __instance;
                if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath) return;

                var index = GetTrackedPilotIndex(mech);
                if (!ModSettings.LosingLimbAlwaysPanics)
                {
                    return;
                }

                if (TrackedPilots[index].TrackedMech != mech.GUID)
                {
                    return;
                }

                if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].ChangedRecently && ModSettings.OneChangePerTurn)
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

        [HarmonyPatch(typeof(GameInstanceSave))]
        [HarmonyPatch(new[] {typeof(GameInstance), typeof(SaveReason)})]
        public static class GameInstanceSaveConstructorPatch
        {
            private static void Postfix(GameInstanceSave __instance)
            {
                SerializeStorageJson(__instance.InstanceGUID, __instance.SaveTime);
            }
        }

        [HarmonyPatch(typeof(GameInstance), "Load")]
        public static class GameInstanceLoadPatch
        {
            private static void Prefix(GameInstanceSave save)
            {
                Resync(save.SaveTime);
            }
        }

        [HarmonyPatch(typeof(SimGameState), "_OnFirstPlayInit")]
        public static class SimGameStateFirstPlayInitPatch
        {
            private static void Postfix(SimGameState __instance) //we're doing a new campaign, so we need to sync the json with the new addition
            {
                SyncNewCampaign();
            }
        }
    }
}