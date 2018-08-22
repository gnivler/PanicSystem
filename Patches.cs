using BattleTech;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using BattleTech.UI;
using Harmony;
using static PanicSystem.Controller;
using static PanicSystem.PanicSystem;
using static PanicSystem.Logger;
using Random = UnityEngine.Random;
using Stopwatch = HBS.Stopwatch;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class Patches
    {
        public static float mechArmorBeforeAttack;
        public static float mechStructureBeforeAttack;

        [HarmonyPatch(typeof(AAR_SalvageScreen), "OnCompleted")]
        public static class AAR_SalvageScreenPatch
        {
            private static void Postfix()
            {
                Reset(); //don't keep data we don't need after a mission
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

                var pilot = mech.GetPilot();
                if (pilot == null)
                {
                    return;
                }

                var index = GetTrackedPilotIndex(mech);
                if (index == -1)
                {
                    trackedPilots.Add(new PanicTracker(mech)); // add a new tracker to tracked pilot
                    SaveTrackedPilots(); // TODO ensure this isn't causing indexing errors by running overlaps or something
                    return;
                }

                // reduce panic level
                var originalStatus = trackedPilots[index].pilotStatus;
                var stats = __instance.StatCollection;
                if (!trackedPilots[index].panicWorsenedRecently && (int) trackedPilots[index].pilotStatus > 0)
                {
                    trackedPilots[index].pilotStatus--;
                }

                if (trackedPilots[index].pilotStatus != originalStatus) // status has changed, reset modifiers
                {
                    stats.ModifyStat("Panic Turn Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                    stats.ModifyStat("Panic Turn Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);

                    var message = __instance.Combat.MessageCenter;
                    if (trackedPilots[index].pilotStatus == PanicStatus.Unsettled)
                    {
                        Debug("Condition improved: Unsettled");
                        message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO UNSETTLED!", FloatieMessage.MessageNature.Buff, false)));
                        stats.ModifyStat("Panic Turn: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, modSettings.UnsettledAttackModifier);
                    }
                    else if (trackedPilots[index].pilotStatus == PanicStatus.Stressed)
                    {
                        Debug("Condition improved: Stressed");
                        message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO STRESSED!", FloatieMessage.MessageNature.Buff, false)));
                        stats.ModifyStat("Panic Turn: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, modSettings.StressedAimModifier);
                        stats.ModifyStat("Panic Turn: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, modSettings.StressedToHitModifier);
                    }
                    else
                    {
                        Debug("Condition improved: Confident");
                        message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO CONFIDENT!", FloatieMessage.MessageNature.Buff, false)));
                    }
                }

                trackedPilots[index].panicWorsenedRecently = false;
                SaveTrackedPilots();
                Debug($"mech.HealthAsRatio {mech.HealthAsRatio}, MechChecks.MechHealth(mech) {MechChecks.MechHealth(mech)}");
            }
        }

        [HarmonyPatch(typeof(AttackStackSequence), nameof(AttackStackSequence.OnAttackBegin))]
        public static class OnAttackBeginPatch
        {
            public static void Prefix(AttackStackSequence __instance)
            {
                var target = __instance.directorSequences[0].target;
                mechArmorBeforeAttack = target.SummaryArmorCurrent;
                mechStructureBeforeAttack = target.SummaryStructureCurrent;
            }
        }

        [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
        public static class AttackStackSequenceOnAttackCompletePatch
        {
            public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                if (SkipProcessingAttack(__instance, message))
                {
                    FlushLogBuffer();
                    return;
                }

                var attackCompleteMessage = message as AttackCompleteMessage;
                var director = __instance.directorSequences;

                Debug(new string('#', 46));
                Debug($"{director[0].attacker.DisplayName} attacks {director[0].target.DisplayName}");

                var targetMech = (Mech) director[0].target;
                if (!ShouldPanic(targetMech, attackCompleteMessage?.attackSequence))
                {
                    FlushLogBuffer();
                    return;
                }

                // automatically eject a klutzy pilot on an additional roll yielding 13
                if (targetMech.IsFlaggedForKnockdown && targetMech.pilot.pilotDef.PilotTags.Contains("pilot_klutz"))
                {
                    if (Random.Range(1, 100) == 13)
                    {
                        targetMech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                            (new ShowActorInfoSequence(targetMech, "WOOPS!", FloatieMessage.MessageNature.Debuff, false)));
                        Debug("Very klutzy!");
                        FlushLogBuffer();
                        return;
                    }
                }

                // store saving throw
                // check it against panic
                // check it again ejection
                var savingThrow = GetSavingThrow(targetMech, attackCompleteMessage?.attackSequence);

                // panic saving throw
                if (SavedVsPanic(targetMech, savingThrow, attackCompleteMessage?.attackSequence))
                {
                    FlushLogBuffer();
                    return;
                }

                // stop if pilot isn't Panicked
                var index = GetTrackedPilotIndex(targetMech);
                if (trackedPilots[index].pilotStatus != PanicStatus.Panicked)
                {
                    FlushLogBuffer();
                    return;
                }

                // eject saving throw
                if (SavedVsEject(targetMech, savingThrow, attackCompleteMessage?.attackSequence))
                {
                    FlushLogBuffer();
                    return;
                }

                Debug("Ejecting");
                if (modSettings.EnableEjectPhrases)
                {
                    var ejectMessage = ejectPhraseList[Random.Range(1, ejectPhraseList.Count)];
                    targetMech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                        (new ShowActorInfoSequence(targetMech, ejectMessage, FloatieMessage.MessageNature.Debuff, true)));
                }

                targetMech.EjectPilot(targetMech.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
                Debug("Ejected");
                Debug($"Runtime to exit {stopwatch.ElapsedMilliSeconds}ms");
                FlushLogBuffer();
            }
        }

        [HarmonyPatch(typeof(GameInstanceSave), MethodType.Constructor)]
        [HarmonyPatch(new[] {typeof(GameInstance), typeof(SaveReason)})]
        public static class GameInstanceSaveConstructorPatch
        {
            private static void Postfix(GameInstanceSave __instance)
            {
                SerializeStorageJson(__instance.InstanceGUID, __instance.SaveTime);
            }
        }

        [HarmonyPatch(typeof(LanceSpawnerGameLogic), nameof(LanceSpawnerGameLogic.OnTriggerSpawn))]
        public static class LanceSpawnerGameLogicPatch
        {
            public static void Postfix(LanceSpawnerGameLogic __instance)
            {
                foreach (var mech in __instance.Combat.AllMechs)
                {
                    var index = GetTrackedPilotIndex(mech);
                    if (index == -1)
                    {
                        trackedPilots.Add(new PanicTracker(mech)); // add a new tracker to tracked pilot
                        SaveTrackedPilots();
                        return;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(GameInstance), "LaunchContract", new[] {typeof(Contract), typeof(string)})]
        public static class LaunchContractPatch
        {
            private static void Postfix()
            {
                // reset on new contracts
                Reset();
                Debug("New contract; done reset");
            }
        }

        [HarmonyPatch(typeof(GameInstance), "Load")]
        public static class LoadPatch
        {
            private static void Prefix(GameInstanceSave save)
            {
                Resync(save.SaveTime);
            }
        }

        [HarmonyPatch(typeof(Mech), "OnLocationDestroyed")]
        public static class OnLocationDestroyedPatch
        {
            private static void Postfix(Mech __instance)
            {
                var mech = __instance;
                if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
                {
                    return;
                }

                var index = GetTrackedPilotIndex(mech);
                if (!modSettings.LosingLimbAlwaysPanics)
                {
                    return;
                }

                if (trackedPilots[index].trackedMech != mech.GUID)
                {
                    return;
                }

                if (trackedPilots[index].panicWorsenedRecently && modSettings.OneChangePerTurn)
                {
                    return;
                }

                if (index < 0)
                {
                    trackedPilots.Add(new PanicTracker(mech)); //add a new tracker to tracked pilot, then we run it all over again;
                    index = GetTrackedPilotIndex(mech);
                    if (index < 0) // G  Why does this matter?
                    {
                        return;
                    }
                }

                ApplyPanicDebuff(mech);
            }
        }

        [HarmonyPatch(typeof(SimGameState), "_OnFirstPlayInit")]
        public static class SimGameStateOnFirstPlayInitPatch
        {
            private static void Postfix() //we're doing a new campaign, so we need to sync the json with the new addition
            {
                SyncNewCampaign();
            }
        }
    }
}