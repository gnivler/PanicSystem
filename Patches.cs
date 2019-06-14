using System;
using System.Collections.Generic;
using BattleTech;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using BattleTech.UI;
using Harmony;
using HBS;
using static PanicSystem.Controller;
using static PanicSystem.PanicSystem;
using static PanicSystem.Logger;
using Random = UnityEngine.Random;

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
            private static void Postfix() => Reset();
        }

        [HarmonyPatch(typeof(AbstractActor), "OnNewRound")]
        public static class AbstractActorBeginNewRoundPatch
        {
            public static void Prefix(AbstractActor __instance)
            {
                if (!(__instance is Mech mech) || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath) return;

                var pilot = mech.GetPilot();
                if (pilot == null) return;

                var index = GetPilotIndex(mech);
                // reduce panic level
                var originalStatus = trackedPilots[index].panicStatus;
                var stats = __instance.StatCollection;
                if (!trackedPilots[index].panicWorsenedRecently && (int) trackedPilots[index].panicStatus > 0)
                {
                    trackedPilots[index].panicStatus--;
                }

                if (trackedPilots[index].panicStatus != originalStatus) // status has changed, reset modifiers
                {
                    stats.ModifyStat("Panic Turn Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                    stats.ModifyStat("Panic Turn Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);

                    var message = __instance.Combat.MessageCenter;
                    switch (trackedPilots[index].panicStatus)
                    {
                        case PanicStatus.Unsettled:
                            LogDebug($"{mech.DisplayName} condition improved: Unsettled");
                            message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO UNSETTLED!", FloatieMessage.MessageNature.Buff, false)));
                            stats.ModifyStat("Panic Turn: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, modSettings.UnsettledAttackModifier);
                            break;
                        case PanicStatus.Stressed:
                            LogDebug($"{mech.DisplayName} condition improved: Stressed");
                            message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO STRESSED!", FloatieMessage.MessageNature.Buff, false)));
                            stats.ModifyStat("Panic Turn: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, modSettings.StressedAimModifier);
                            stats.ModifyStat("Panic Turn: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, modSettings.StressedToHitModifier);
                            break;
                        default:
                            LogDebug($"{mech.DisplayName} condition improved: Confident");
                            message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO CONFIDENT!", FloatieMessage.MessageNature.Buff, false)));
                            break;
                    }
                }

                // reset flag after reduction effect
                trackedPilots[index].panicWorsenedRecently = false;
                SaveTrackedPilots();
            }
        }

        [HarmonyPatch(typeof(AttackStackSequence), "OnAttackBegin")]
        public static class OnAttackBeginPatch
        {
            public static void Prefix(AttackStackSequence __instance)
            {
                var target = __instance.directorSequences[0].chosenTarget;
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

                if (SkipProcessingAttack(__instance, message)) return;

                if (!(message is AttackCompleteMessage attackCompleteMessage)) return;

                var director = __instance.directorSequences;
                if (director == null) return;

                LogDebug(new string('#', 46));
                LogDebug($"{director[0].attacker.DisplayName} attacks {director[0].chosenTarget.DisplayName}");

                var targetMech = (Mech) director[0]?.chosenTarget;
                var index = GetPilotIndex(targetMech);
                if (!ShouldPanic(targetMech, attackCompleteMessage.attackSequence)) return;

                // automatically eject a klutzy pilot on an additional roll yielding 13
                if (targetMech.IsFlaggedForKnockdown && targetMech.pilot.pilotDef.PilotTags.Contains("pilot_klutz"))
                {
                    if (Random.Range(1, 100) == 13)
                    {
                        targetMech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                            (new ShowActorInfoSequence(targetMech, "WOOPS!", FloatieMessage.MessageNature.Debuff, false)));
                        LogDebug("Very klutzy!");
                        return;
                    }
                }

                // store saving throw
                // check it against panic
                // check it again ejection
                var savingThrow = GetSavingThrow(targetMech, attackCompleteMessage.attackSequence);

                // panic saving throw
                if (SavedVsPanic(targetMech, savingThrow)) return;

                // stop if pilot isn't Panicked
                if (trackedPilots[index].panicStatus != PanicStatus.Panicked) return;

                // eject saving throw
                if (SavedVsEject(targetMech, savingThrow, attackCompleteMessage?.attackSequence)) return;

                // ejecting
                // random phrase
                try
                {
                    if (modSettings.EnableEjectPhrases &&
                        Random.Range(1, 100) <= modSettings.EjectPhraseChance)
                    {
                        var ejectMessage = ejectPhraseList[Random.Range(1, ejectPhraseList.Count)];
                        targetMech.Combat.MessageCenter.PublishMessage(
                            new AddSequenceToStackMessage(
                                new ShowActorInfoSequence(targetMech, ejectMessage, FloatieMessage.MessageNature.Debuff, true)));
                    }
                }
                catch (Exception e)
                {
                    LogError(e);
                }

                // remove effects, to prevent exceptions that occur for unknown reasons
                try
                {
                    var combat = UnityGameInstance.BattleTechGame.Combat;
                    List<Effect> effectsTargeting = combat.EffectManager.GetAllEffectsTargeting(targetMech);
                    foreach (var effect in effectsTargeting)
                    {
                        // some effects removal throw, so silently drop them
                        try
                        {
                            targetMech.CancelEffect(effect);
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception e)
                {
                    LogError(e);
                }

                targetMech.EjectPilot(targetMech.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
                LogDebug($"Ejected.  Runtime {stopwatch.ElapsedMilliSeconds}ms");
            }
        }

        [HarmonyPatch(typeof(GameInstanceSave), MethodType.Constructor)]
        [HarmonyPatch(new[] {typeof(GameInstance), typeof(SaveReason)})]
        public static class GameInstanceSaveConstructorPatch
        {
            private static void Postfix(GameInstanceSave __instance) => SerializeStorageJson(__instance.InstanceGUID, __instance.SaveTime);
        }

        [HarmonyPatch(typeof(LanceSpawnerGameLogic), "OnTriggerSpawn")]
        public static class LanceSpawnerGameLogicPatch
        {
            // throw away the return of GetPilotIndex because the method is just adding the missing mechs
            public static void Postfix(LanceSpawnerGameLogic __instance) => __instance.Combat.AllMechs.ForEach(x => GetPilotIndex(x));
        }

        [HarmonyPatch(typeof(GameInstance), "LaunchContract", typeof(Contract), typeof(string))]
        public static class LaunchContractPatch
        {
            // reset on new contracts
            private static void Postfix() => Reset();
        }

        [HarmonyPatch(typeof(GameInstance), "Load")]
        public static class LoadPatch
        {
            private static void Prefix(GameInstanceSave save) => Resync(save.SaveTime);
        }

        [HarmonyPatch(typeof(Mech), "OnLocationDestroyed")]
        public static class OnLocationDestroyedPatch
        {
            private static void Postfix(Mech __instance)
            {
                var mech = __instance;
                if (!modSettings.LosingLimbAlwaysPanics ||
                    mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
                {
                    return;
                }

                var index = GetPilotIndex(mech);
                if (trackedPilots[index].mech != mech.GUID) return;

                if (trackedPilots[index].panicWorsenedRecently && modSettings.OneChangePerTurn) return;

                ApplyPanicDebuff(mech);
            }
        }

        [HarmonyPatch(typeof(SimGameState), "_OnFirstPlayInit")]
        public static class SimGameStateOnFirstPlayInitPatch
        {
            // we're doing a new campaign, so we need to sync the json with the new addition
            private static void Postfix()
            {
                // if campaigns are added this way, why does deleting the storage jsons not break it?
                SyncNewCampaign();
            }
        }
    }
}