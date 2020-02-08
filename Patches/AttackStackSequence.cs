using System;
using System.Collections;
using System.Linq;
using BattleTech;
using BattleTech.Achievements;
using Harmony;
using HBS;
using PanicSystem.Components;
using UnityEngine;
using us.frostraptor.modUtils.CustomDialog;
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
        internal static float armorBeforeAttack;
        internal static float structureBeforeAttack;

        public static void Prefix(AttackStackSequence __instance)
        {
            if (__instance.directorSequences == null || __instance.directorSequences.Count == 0)
            {
                return;
            }

            var target = __instance.directorSequences[0].chosenTarget;
            armorBeforeAttack = target.SummaryArmorCurrent;
            structureBeforeAttack = target.SummaryStructureCurrent;
        }
    }

    [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
    public static class AttackStackSequence_OnAttackComplete_Patch
    {
        private static readonly Stopwatch stopwatch = new Stopwatch();

        public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
        {
            stopwatch.Restart();
            if (ShouldSkipProcessing(__instance, message))
            {
                return;
            }

            if (!(message is AttackCompleteMessage attackCompleteMessage))
            {
                return;
            }

            var director = __instance.directorSequences;
            if (director == null) return;

            LogReport(new string('═', 46));
            LogReport($"{director[0].attacker.DisplayName} attacks {director[0].chosenTarget.DisplayName}");

            // get the attacker in case they have mech quirks
            AbstractActor defender = null;
            switch (director[0]?.chosenTarget)
            {
                case Vehicle _:
                    defender = (Vehicle) director[0]?.chosenTarget;
                    break;
                case Mech _:
                    defender = (Mech) director[0]?.chosenTarget;
                    break;
            }

            // a building or turret?
            if (defender == null)
            {
                LogDebug("Not a mech or vehicle");
                return;
            }

            var attacker = director[0].attacker;
            var index = GetActorIndex(defender);
            if (!modSettings.AlwaysPanic &&
                !ShouldPanic(defender, attackCompleteMessage.attackSequence))
            {
                return;
            }

            // automatically eject a klutzy pilot on knockdown with an additional roll failing on 13
            if (defender.IsFlaggedForKnockdown)
            {
                var defendingMech = (Mech) defender;
                if (defendingMech.pilot.pilotDef.PilotTags.Contains("pilot_klutz"))
                {
                    if (Random.Range(1, 100) == 13)
                    {
                        defender.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                            (new ShowActorInfoSequence(defender, "WOOPS!", FloatieMessage.MessageNature.Debuff, false)));
                        LogReport("Very klutzy!");
                        return;
                    }
                }
            }

            // store saving throw
            // check it against panic
            // check it again ejection
            var savingThrow = SavingThrows.GetSavingThrow(defender, attacker);
            Mech_AddExternalHeat_Patch.heatDamage = 0;
            // panic saving throw
            if (SavingThrows.SavedVsPanic(defender, savingThrow))
            {
                return;
            }

            // stop if pilot isn't Panicked
            if (TrackedActors[index].PanicStatus != PanicStatus.Panicked)
            {
                return;
            }

            // eject saving throw
            if (!modSettings.AlwaysPanic &&
                SavingThrows.SavedVsEject(defender, savingThrow))
            {
                return;
            }

            // ejecting
            // random phrase
            if (modSettings.EnableEjectPhrases &&
                defender is Mech &&
                Random.Range(1, 100) <= modSettings.EjectPhraseChance)
            {
                var ejectMessage = ejectPhraseList[Random.Range(1, ejectPhraseList.Count)];
                // thank you IRBTModUtils
                LogDebug($"defender {defender}");
                var castDef = Coordinator.CreateCast(defender);
                var content = new DialogueContent(
                    ejectMessage, Color.white, castDef.id, null, null, DialogCameraDistance.Medium, DialogCameraHeight.Default, 0
                );
                content.ContractInitialize(defender.Combat);
                defender.Combat.MessageCenter.PublishMessage(new CustomDialogMessage(defender, content, 6));
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

            if (modSettings.VehiclesCanPanic &&
                defender is Vehicle)
            {
                // make the regular Pilot Ejected floatie not appear, for this ejection
                var original = AccessTools.Method(typeof(BattleTech.VehicleRepresentation), "PlayDeathFloatie");
                var prefix = AccessTools.Method(typeof(VehicleRepresentation), nameof(VehicleRepresentation.PrefixDeathFloatie));
                harmony.Patch(original, new HarmonyMethod(prefix));
                defender.EjectPilot(defender.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, true);
                harmony.Unpatch(original, HarmonyPatchType.Prefix);
                CastDef castDef = Coordinator.CreateCast(defender);
                DialogueContent content = new DialogueContent(
                    "Destroy the tech, let's get outta here!", Color.white, castDef.id, null, null, DialogCameraDistance.Medium, DialogCameraHeight.Default, 0
                );
                content.ContractInitialize(defender.Combat);
                defender.Combat.MessageCenter.PublishMessage(new CustomDialogMessage(defender, content, 5));
            }
            else
            {
                defender.EjectPilot(defender.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
            }

            LogReport("Ejected");
            LogDebug($"Runtime {stopwatch.Elapsed}");

            if (!modSettings.CountAsKills)
            {
                return;
            }

            try
            {
                // this seems pretty convoluted
                var attackerPilot = combat.AllMechs.Where(mech => mech.pilot.Team.IsLocalPlayer)
                    .Where(x => x.PilotableActorDef == attacker.PilotableActorDef).Select(y => y.pilot).FirstOrDefault();

                var statCollection = attackerPilot?.StatCollection;
                if (statCollection == null)
                {
                    return;
                }

                if (defender is Mech)
                {
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
                else if (modSettings.VehiclesCanPanic &&
                         defender is Vehicle)
                {
                    var value = statCollection.GetStatistic("VehiclesEjected")?.Value<int?>();
                    if (statCollection.GetStatistic("VehiclesEjected") == null)
                    {
                        statCollection.AddStatistic("VehiclesEjected", 1);
                    }
                    else
                    {
                        statCollection.Set("VehiclesEjected", value + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug(ex);
            }
        }
    }
}
