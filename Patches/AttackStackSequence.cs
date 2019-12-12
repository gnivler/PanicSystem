using System;
using System.Collections;
using System.Linq;
using BattleTech;
using BattleTech.Achievements;
using Harmony;
using HBS;
using PanicSystem.Components;
using static PanicSystem.Logger;
using static PanicSystem.PanicSystem;
using static PanicSystem.Components.Controller;
using static PanicSystem.Helpers;
using Random = UnityEngine.Random;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    // save the pre-attack condition
    [HarmonyPatch(typeof(AttackStackSequence), "OnAttackBegin")]
    public static class AttackStackSequence_OnAttackBegin_Patch
    {
        internal static float mechArmorBeforeAttack;
        internal static float mechStructureBeforeAttack;
        internal static float mechHeatBeforeAttack;

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

    [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
    public static class AttackStackSequence_OnAttackComplete_Patch
    {
        public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
        {
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
            Mech_AddExternalHeat_Patch.heatDamage = 0;
            // panic saving throw
            if (SavedVsPanic(defender, savingThrow))
            {
                return;
            }

            // stop if pilot isn't Panicked
            if (TrackedPilots[index].PanicStatus != PanicStatus.Panicked)
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

            if (!modSettings.CountAsKills)
            {
                return;
            }

            try
            {
                var attackerPilot = combat.AllMechs.Where(mech => mech.pilot.Team.IsLocalPlayer)
                    .Where(x => x.PilotableActorDef == attacker.PilotableActorDef).Select(y => y.pilot).FirstOrDefault();

                var statCollection = attackerPilot?.StatCollection;
                if (statCollection == null)
                {
                    return;
                }

                // add UI icons.. and pilot history?   ... MechsKilled already incremented??
                statCollection.Set("MechsKilled", attackerPilot.MechsKilled + 1);
                var value = statCollection.GetStatistic("MechsEjected")?.Value<int?>();
                if (statCollection.GetStatistic("MechsEjected") == null)
                {
                    statCollection.AddStatistic("MechsEjected", 1);
                }
                else
                {
                    statCollection.Set("MechsEjected", value + 1);
                }

                // add achievement kill (more complicated)
                var combatProcessors = Traverse.Create(UnityGameInstance.BattleTechGame.Achievements).Field("combatProcessors").GetValue<AchievementProcessor[]>();
                var combatProcessor = combatProcessors.FirstOrDefault(x => x.GetType() == AccessTools.TypeByName("BattleTech.Achievements.CombatProcessor"));

                // field is of type Dictionary<string, CombatProcessor.MechCombatStats>
                var playerMechStats = Traverse.Create(combatProcessor).Field("playerMechStats").GetValue<IDictionary>();
                if (playerMechStats != null)
                {
                    foreach (DictionaryEntry kvp in playerMechStats)
                    {
                        if ((string) kvp.Key == attackerPilot.GUID)
                        {
                            Traverse.Create(kvp.Value).Method("IncrementKillCount").GetValue();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
    }
}