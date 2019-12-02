using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using BattleTech.Achievements;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using BattleTech.UI;
using Harmony;
using UnityEngine;
using UnityEngine.UI;
using static PanicSystem.Controller;
using static PanicSystem.PanicSystem;
using static PanicSystem.Logger;
using Random = UnityEngine.Random;
using Stopwatch = HBS.Stopwatch;
using Text = Localize.Text;

// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class Patches
    {
        public static float mechArmorBeforeAttack;
        public static float mechStructureBeforeAttack;
        public static float mechHeatBeforeAttack;
        public static float heatDamage;

        public static void Init()
        {
           FileLog.Log(new string('=', 100));
           FileLog.Log(new string('=', 100));
           FileLog.Log(new string('=', 100));
            HarmonyInstance.DEBUG = true;
            harmony.Patch(AccessTools.Method(typeof(AttackStackSequence), "OnAttackComplete"), new HarmonyMethod(AccessTools.Method(typeof(Patches), "Foo")));
            HarmonyInstance.DEBUG = false;
        }

        private static AttackStackSequence previousSequence;

        public static void Foo(AttackStackSequence __instance)
        {
            // seeing multiple invocations of this method without any in-game impact
            // but it makes panic status levels jump up and down by twos
            // assuming every duplicate is adjacent...
            LogReport(new string('>', 20) + "OnAttackComplete");
            if (previousSequence == __instance)
            {
                LogReport(new string('>', 20) + "Dropped duplicate");
                return;
            }

            previousSequence = __instance;
            return;
        }

        //[HarmonyPatch(typeof(AAR_UnitStatusWidget), "FillInData")]
        //public static class AAR_UnitStatusWidget_FillInData_Patch
        //{
        //    private static int? ejections;
        //
        //    public static void Prefix(UnitResult ___UnitData)
        //    {
        //        try
        //        {
        //            // get the total and decrement it globally
        //            ejections = ___UnitData.pilot.StatCollection.GetStatistic("MechsEjected")?.Value<int>();
        //            Log($"{___UnitData.pilot.Callsign} MechsEjected {ejections}");
        //        }
        //        catch (Exception ex)
        //        {
        //            Log(ex);
        //        }
        //    }
        //
        //    // subtract ejection kills to limit the number of regular kill stamps drawn
        //    // then draw red ones in Postfix
        //    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        //    {
        //        var codes = instructions.ToList();
        //        try
        //        {
        //            // subtract our value right as the getter comes back
        //            // makes the game draw fewer normal stamps
        //            var index = codes.FindIndex(x => x.operand is MethodInfo info &&
        //                                             info == AccessTools.Method(typeof(Pilot), "get_MechsKilled"));
        //
        //            var newStack = new List<CodeInstruction>
        //            {
        //                new CodeInstruction(OpCodes.Ldarg_0),
        //                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(AAR_UnitStatusWidget), "UnitData")),
        //                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patches), "GetEjectionCount")),
        //                new CodeInstruction(OpCodes.Sub)
        //            };
        //            codes.InsertRange(index + 1, newStack);
        //        }
        //        catch (Exception ex)
        //        {
        //            Log(ex);
        //        }
        //
        //        return codes.AsEnumerable();
        //    }
        //
        //    public static void Postfix(UnitResult ___UnitData, RectTransform ___KillGridParent)
        //    {
        //        try
        //        {
        //            // weird loop
        //            for (var x = 0; x < ejections--; x++)
        //            {
        //                Log("Adding stamp");
        //                AddEjectedMech(___KillGridParent);
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            Log(ex);
        //        }
        //    }
        //}
        //
        //public static int GetEjectionCount(UnitResult unitResult)
        //{
        //    return unitResult.pilot.StatCollection.GetStatistic("MechsEjected") == null
        //        ? 0
        //        : unitResult.pilot.StatCollection.GetStatistic("MechsEjected").Value<int>();
        //}
        //
        ////  adapted from AddKilledMech()    
        //private static void AddEjectedMech(RectTransform KillGridParent)
        //{
        //    try
        //    {
        //        var dm = UnityGameInstance.BattleTechGame.DataManager;
        //        const string id = "uixPrfIcon_AA_mechKillStamp";
        //        var prefab = dm.PooledInstantiate(id, BattleTechResourceType.Prefab, null, null, KillGridParent);
        //        var image = prefab.GetComponent<Image>();
        //        image.color = Color.red;
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Log(ex);
        //    }
        //}

        // have to patch both because they're used in different situations, with the same messages
        //[HarmonyPatch(typeof(CombatHUDFloatieStack), "AddFloatie", typeof(FloatieMessage))]
        //public static class CombatHUDFloatieStack_AddFloatie_Patch1
        //{
        //    public static void Postfix(CombatHUDFloatieStack __instance)
        //    {
        //        if (modSettings.ColorizeFloaties)
        //        {
        //            try
        //            {
        //                ColorFloaties.Colorize(__instance);
        //            }
        //            catch (Exception ex)
        //            {
        //                Log(ex);
        //            }
        //        }
        //    }
        //}

        //[HarmonyPatch(typeof(CombatHUDFloatieStack), "AddFloatie", typeof(Text), typeof(FloatieMessage.MessageNature))]
        //public static class CombatHUDFloatieStack_AddFloatie_Patch2
        //{
        //    public static void Postfix(CombatHUDFloatieStack __instance)
        //    {
        //        if (modSettings.ColorizeFloaties)
        //        {
        //            try
        //            {
        //                ColorFloaties.Colorize(__instance);
        //            }
        //            catch (Exception ex)
        //            {
        //                Log(ex);
        //            }
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(AbstractActor), "OnNewRound")]
        public static class AbstractActor_OnNewRound_Patch
        {
            private static AbstractActor previousAbstractActor;

            public static void Prefix(AbstractActor __instance)
            {
                LogReport(new string('>', 20) + "OnNewRound - " + __instance.GUID);

                // deal with duplicate invocations for random reasons
                if (previousAbstractActor == __instance)
                {
                    LogReport(new string('>', 20) + "dropped duplicate");
                    return;
                }

                if (!(__instance is Mech mech) || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
                {
                    return;
                }

                previousAbstractActor = __instance;
                return;
                var pilot = mech.GetPilot();
                if (pilot == null)
                {
                    return;
                }

                var index = GetPilotIndex(mech);
                // reduce panic level
                var originalStatus = trackedPilots[index].panicStatus;

                if (!trackedPilots[index].panicWorsenedRecently && (int) trackedPilots[index].panicStatus > 0)
                {
                    trackedPilots[index].panicStatus--;
                }

                if (trackedPilots[index].panicStatus != originalStatus) // status has changed, reset modifiers
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
                    switch (trackedPilots[index].panicStatus)
                    {
                        case PanicStatus.Unsettled:
                            LogReport($"{mech.DisplayName} condition improved: Unsettled");
                            message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO UNSETTLED!", FloatieMessage.MessageNature.Buff, false)));
                            //effectManager.CreateEffect(StatusEffect.UnsettledToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                            break;
                        case PanicStatus.Stressed:
                            LogReport($"{mech.DisplayName} condition improved: Stressed");
                            message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO STRESSED!", FloatieMessage.MessageNature.Buff, false)));
                            //effectManager.CreateEffect(StatusEffect.StressedToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                            //effectManager.CreateEffect(StatusEffect.StressedToBeHit, "PanicSystemToBeHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                            break;
                        default:
                            LogReport($"{mech.DisplayName} condition improved: Confident");
                            message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO CONFIDENT!", FloatieMessage.MessageNature.Buff, false)));
                            break;
                    }
                }

                // reset flag after reduction effect
                trackedPilots[index].panicWorsenedRecently = false;
                SaveTrackedPilots();
            }
        }

        // save the pre-attack condition
        [HarmonyPatch(typeof(AttackStackSequence), "OnAttackBegin")]
        public static class OnAttackBeginPatch
        {
            public static void Prefix(AttackStackSequence __instance)
            {
                if (__instance.directorSequences == null || __instance.directorSequences.Count == 0)
                    return;

                var target = __instance.directorSequences[0].chosenTarget;
                mechArmorBeforeAttack = target.SummaryArmorCurrent;
                mechStructureBeforeAttack = target.SummaryStructureCurrent;

                // get defender's current heat
                if (__instance.directorSequences[0].chosenTarget is Mech defender)
                {
                    mechHeatBeforeAttack = defender.CurrentHeat;
                }
            }
        }

        public static void ManualPatching()
        {
            //var targetMethod = AccessTools.Method(typeof(Mech), "CheckForHeatDamage");
            //var transpiler = SymbolExtensions.GetMethodInfo(() => Mech_CheckForHeatDamage_Patch.Transpiler(null));
            //var postfix = SymbolExtensions.GetMethodInfo(() => Mech_CheckForHeatDamage_Patch.Postfix());
            //harmony.Patch(targetMethod, null, new HarmonyMethod(postfix), new HarmonyMethod(transpiler));
        }

        // patch works to determine how much heat damage was done by overheating... which isn't really required
        // multiply a local variable by 7 and aggregate it on global `static float heatDamage`
        //public class Mech_CheckForHeatDamage_Patch
        //{
        //    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        //    {
        //        var codes = instructions.ToList();
        //        var heatDamageField = AccessTools.Field(typeof(Patches), nameof(heatDamage));
        //        var log = SymbolExtensions.GetMethodInfo(() => LogDebug(null));
        //
        //        var newStack = new List<CodeInstruction>
        //        {
        //            new CodeInstruction(OpCodes.Ldloc_0), // push float (2)
        //            new CodeInstruction(OpCodes.Ldc_R4, 7f), // push float (7)
        //            new CodeInstruction(OpCodes.Mul), // multiply   (14)
        //            new CodeInstruction(OpCodes.Ldsfld, heatDamageField), // push float (0)
        //            new CodeInstruction(OpCodes.Add), // add        (14)
        //            new CodeInstruction(OpCodes.Stsfld, heatDamageField), // store result
        //        };
        //
        //        codes.InsertRange(codes.Count - 1, newStack);
        //        return codes.AsEnumerable();
        //    }
        //
        //    public static void Postfix() => LogDebug($"heatDamage: {heatDamage}");
        //}
        //

        // properly aggregates heat damage?
        [HarmonyPatch(typeof(Mech), "AddExternalHeat")]
        public class Mech_AddExternalHeat
        {
            static void Prefix(int amt)
            {
                heatDamage += amt;
                LogReport($"Running heat total: {heatDamage}");
            }
        }

        //[HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
        public static class AttackStackSequenceOnAttackCompletePatch
        {
            private static AttackStackSequence previousSequence;

            public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
            {
                // seeing multiple invocations of this method without any in-game impact
                // but it makes panic status levels jump up and down by twos
                // assuming every duplicate is adjacent...
                LogReport(new string('>', 20) + "OnAttackComplete");
                if (previousSequence == __instance)
                {
                    LogReport(new string('>', 20) + "Dropped duplicate");
                    return;
                }

                previousSequence = __instance;
                return;
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                if (SkipProcessingAttack(__instance, message)) return;

                if (!(message is AttackCompleteMessage attackCompleteMessage)) return;

                var director = __instance.directorSequences;
                if (director == null) return;

                LogReport(new string('═', 46));
                LogReport($"{director[0].attacker.DisplayName} attacks {director[0].chosenTarget.DisplayName}");

                // get the attacker in case they have mech quirks
                var defender = (Mech) director[0]?.chosenTarget;
                var attacker = director[0].attacker;

                var index = GetPilotIndex(defender);
                if (!ShouldPanic(defender, attackCompleteMessage.attackSequence)) return;

                // automatically eject a klutzy pilot on an additional roll yielding 13
                if (defender.IsFlaggedForKnockdown && defender.pilot.pilotDef.PilotTags.Contains("pilot_klutz"))
                {
                    if (Random.Range(1, 100) == 13)
                    {
                        defender.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                            (new ShowActorInfoSequence(defender, "WOOPS!", FloatieMessage.MessageNature.Debuff, false)));
                        LogReport("Very klutzy!");
                        return;
                    }
                }

                // store saving throw
                // check it against panic
                // check it again ejection
                var savingThrow = GetSavingThrow(defender, attacker);
                heatDamage = 0;
                // panic saving throw
                if (SavedVsPanic(defender, savingThrow))
                {
                    return;
                }

                // stop if pilot isn't Panicked
                if (trackedPilots[index].panicStatus != PanicStatus.Panicked)
                {
                    return;
                }

                // eject saving throw
                if (SavedVsEject(defender, savingThrow))
                {
                    return;
                }

                // ejecting
                // random phrase
                try
                {
                    if (modSettings.EnableEjectPhrases &&
                        Random.Range(1, 100) <= modSettings.EjectPhraseChance)
                    {
                        var ejectMessage = ejectPhraseList[Random.Range(1, ejectPhraseList.Count)];
                        defender.Combat.MessageCenter.PublishMessage(
                            new AddSequenceToStackMessage(
                                new ShowActorInfoSequence(defender, $"{ejectMessage}", FloatieMessage.MessageNature.Debuff, true)));
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }

                // remove effects, to prevent exceptions that occur for unknown reasons
                var combat = UnityGameInstance.BattleTechGame.Combat;
                var effectsTargeting = combat.EffectManager.GetAllEffectsTargeting(defender);
                foreach (var effect in effectsTargeting)
                {
                    // some effects removal throw, so silently drop them
                    try
                    {
                        defender.CancelEffect(effect);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                defender.EjectPilot(defender.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
                LogReport($"Ejected.  Runtime {stopwatch.ElapsedMilliSeconds}ms");

                //if (!modSettings.CountAsKills)
                //{
                //    return;
                //}
                //
                //try
                //{
                //    var attackerPilot = combat.AllMechs.Where(mech => mech.pilot.Team.IsLocalPlayer)
                //        .Where(x => x.PilotableActorDef == attacker.PilotableActorDef).Select(y => y.pilot).FirstOrDefault();
                //
                //    var statCollection = attackerPilot?.StatCollection;
                //    if (statCollection == null)
                //    {
                //        return;
                //    }
                //
                //    // add UI icons.. and pilot history?   ... MechsKilled already incremented??
                //    statCollection.Set("MechsKilled", attackerPilot.MechsKilled + 1);
                //    var value = statCollection.GetStatistic("MechsEjected")?.Value<int?>();
                //    if (statCollection.GetStatistic("MechsEjected") == null)
                //    {
                //        statCollection.AddStatistic("MechsEjected", 1);
                //    }
                //    else
                //    {
                //        statCollection.Set("MechsEjected", value + 1);
                //    }
                //
                //    //attackerPilot.pilotDef.AddMechKillCount(1);
                //
                //    // add achievement kill (more complicated)
                //    var combatProcessors = Traverse.Create(UnityGameInstance.BattleTechGame.Achievements).Field("combatProcessors").GetValue<AchievementProcessor[]>();
                //    var combatProcessor = combatProcessors.FirstOrDefault(x => x.GetType() == AccessTools.TypeByName("BattleTech.Achievements.CombatProcessor"));
                //
                //    // field is of type Dictionary<string, CombatProcessor.MechCombatStats>
                //    var playerMechStats = Traverse.Create(combatProcessor).Field("playerMechStats").GetValue<IDictionary>();
                //    if (playerMechStats != null)
                //    {
                //        foreach (DictionaryEntry kvp in playerMechStats)
                //        {
                //            if ((string) kvp.Key == attackerPilot.GUID)
                //            {
                //                Traverse.Create(kvp.Value).Method("IncrementKillCount").GetValue();
                //            }
                //        }
                //    }
                //}
                //catch (Exception ex)
                //{
                //    Log(ex);
                //}
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
            public static void Postfix(LanceSpawnerGameLogic __instance)
            {
                Log("Lance spawn - building pilot index");
                __instance.Combat.AllMechs.ForEach(x => GetPilotIndex(x));
            }
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