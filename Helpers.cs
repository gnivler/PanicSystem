using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using Harmony;
using PanicSystem.Components;
using PanicSystem.Patches;
using UnityEngine;
using UnityEngine.UI;
using static PanicSystem.Logger;
using static PanicSystem.Components.Controller;
using static PanicSystem.PanicSystem;
using Random = UnityEngine.Random;
// ReSharper disable InconsistentNaming

namespace PanicSystem
{
    public class Helpers
    {
        internal static int GetEjectionCount(UnitResult unitResult)
        {
            return unitResult.pilot.StatCollection.GetStatistic("MechsEjected") == null
                ? 0
                : unitResult.pilot.StatCollection.GetStatistic("MechsEjected").Value<int>();
        }

        //  adapted from AddKilledMech()    
        // ReSharper disable once InconsistentNaming
        internal static void AddEjectedMech(RectTransform KillGridParent)
        {
            try
            {
                var dm = UnityGameInstance.BattleTechGame.DataManager;
                const string id = "uixPrfIcon_AA_mechKillStamp";
                var prefab = dm.PooledInstantiate(id, BattleTechResourceType.Prefab, null, null, KillGridParent);
                var image = prefab.GetComponent<Image>();
                image.color = Color.red;
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        // used in strings
        internal static float MechHealth(Mech mech) =>
            (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) /
            (mech.SummaryArmorMax + mech.SummaryStructureMax) * 100;

        // used in calculations
        internal static float PercentPilot(Pilot pilot) => 1 - (float) pilot.Injuries / pilot.Health;

        internal static float PercentRightTorso(Mech mech) =>
            (mech.RightTorsoStructure +
             mech.RightTorsoFrontArmor +
             mech.RightTorsoRearArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.RightTorso) +
             mech.MaxArmorForLocation((int) ArmorLocation.RightTorso) +
             mech.MaxArmorForLocation((int) ArmorLocation.RightTorsoRear));

        internal static float PercentLeftTorso(Mech mech) =>
            (mech.LeftTorsoStructure +
             mech.LeftTorsoFrontArmor +
             mech.LeftTorsoRearArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.LeftTorso) +
             mech.MaxArmorForLocation((int) ArmorLocation.LeftTorso) +
             mech.MaxArmorForLocation((int) ArmorLocation.LeftTorsoRear));

        internal static float PercentCenterTorso(Mech mech) =>
            (mech.CenterTorsoStructure +
             mech.CenterTorsoFrontArmor +
             mech.CenterTorsoRearArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.CenterTorso) +
             mech.MaxArmorForLocation((int) ArmorLocation.CenterTorso) +
             mech.MaxArmorForLocation((int) ArmorLocation.CenterTorsoRear));

        internal static float PercentLeftLeg(Mech mech) =>
            (mech.LeftLegStructure + mech.LeftLegArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.LeftLeg) +
             mech.MaxArmorForLocation((int) ArmorLocation.LeftLeg));

        internal static float PercentRightLeg(Mech mech) =>
            (mech.RightLegStructure + mech.RightLegArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.RightLeg) +
             mech.MaxArmorForLocation((int) ArmorLocation.RightLeg));

        internal static float PercentHead(Mech mech) =>
            (mech.HeadStructure + mech.HeadArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.Head) +
             mech.MaxArmorForLocation((int) ArmorLocation.Head));


        // applies combat modifiers to tracked mechs based on panic status
        public static void ApplyPanicDebuff(Mech mech)
        {
            var index = GetPilotIndex(mech);
            if (TrackedPilots[index].Mech != mech.GUID)
            {
                Log("Pilot and mech mismatch; no status to change");
                return;
            }

            int Uid() => Random.Range(1, int.MaxValue);
            var effectManager = UnityGameInstance.BattleTechGame.Combat.EffectManager;
            var effects = Traverse.Create(effectManager).Field("effects").GetValue<List<Effect>>();
            foreach (var effect in effects.Where(effect => effect.id.StartsWith("PanicSystem") && Traverse.Create(effect).Field("target").GetValue<object>() == mech))
            {
                Log("Cancelling effect " + effect.id);
                effectManager.CancelEffect(effect);
            }

            switch (TrackedPilots[index].PanicStatus)
            {
                case PanicStatus.Confident:
                    LogReport($"{mech.DisplayName} condition worsened: Unsettled");
                    TrackedPilots[index].PanicStatus = PanicStatus.Unsettled;
                    effectManager.CreateEffect(StatusEffect.UnsettledToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    break;
                case PanicStatus.Unsettled:
                    LogReport($"{mech.DisplayName} condition worsened: Stressed");
                    TrackedPilots[index].PanicStatus = PanicStatus.Stressed;
                    effectManager.CreateEffect(StatusEffect.StressedToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    effectManager.CreateEffect(StatusEffect.StressedToBeHit, "PanicSystemToBeHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    break;
                default:
                    LogReport($"{mech.DisplayName} condition worsened: Panicked");
                    TrackedPilots[index].PanicStatus = PanicStatus.Panicked;
                    effectManager.CreateEffect(StatusEffect.PanickedToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    effectManager.CreateEffect(StatusEffect.PanickedToBeHit, "PanicSystemToBeHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    break;
            }

            TrackedPilots[index].PanicWorsenedRecently = true;
        }

        // check if panic roll is possible
        private static bool CanPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
            {
                LogReport($"{attackSequence?.attacker?.DisplayName} incapacitated {mech?.DisplayName}");
                return false;
            }

            if (attackSequence == null ||
                mech.team.IsLocalPlayer && !modSettings.PlayersCanPanic ||
                !mech.team.IsLocalPlayer && !modSettings.EnemiesCanPanic)
            {
                return false;
            }

            return true;
        }

        // Returns a float to modify panic roll difficulty based on existing panic level
        private static float GetPanicModifier(PanicStatus pilotStatus)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (pilotStatus)
            {
                case PanicStatus.Unsettled: return modSettings.UnsettledPanicModifier;
                case PanicStatus.Stressed: return modSettings.StressedPanicModifier;
                case PanicStatus.Panicked: return modSettings.PanickedPanicModifier;
                default: return 1f;
            }
        }

        // true is a successful saving throw
        public static float GetSavingThrow(Mech defender, AbstractActor attacker)
        {
            var pilot = defender.GetPilot();
            var weapons = defender.Weapons;
            var gutsAndTacticsSum = defender.SkillGuts * modSettings.GutsEjectionResistPerPoint +
                                    defender.SkillTactics * modSettings.TacticsEjectionResistPerPoint;
            float totalMultiplier = 0;

            DrawHeader();
            LogReport($"{$"Mech health {MechHealth(defender):#.##}%",-20} | {"",10} |");
            try
            {
                if (modSettings.QuirksEnabled && attacker is Mech mech &&
                    mech.MechDef.Chassis.ChassisTags.Contains("mech_quirk_distracting"))
                {
                    totalMultiplier += modSettings.DistractingModifier;
                    LogReport($"{"Distracting mech",-20} | {modSettings.DistractingModifier,10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (modSettings.HeatDamageModifier > 0)
                {
                    totalMultiplier += modSettings.HeatDamageModifier * Mech_AddExternalHeat_Patch.heatDamage;
                    LogReport($"{$"Heat damage {Mech_AddExternalHeat_Patch.heatDamage}",-20} | {modSettings.HeatDamageModifier * Mech_AddExternalHeat_Patch.heatDamage,10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (PercentPilot(pilot) < 1)
                {
                    totalMultiplier += modSettings.PilotHealthMaxModifier * PercentPilot(pilot);
                    LogReport($"{"Pilot injuries",-20} | {modSettings.PilotHealthMaxModifier * PercentPilot(pilot),10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (defender.IsUnsteady)
                {
                    totalMultiplier += modSettings.UnsteadyModifier;
                    LogReport($"{"Unsteady",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
                }

                if (defender.IsFlaggedForKnockdown)
                {
                    totalMultiplier += modSettings.UnsteadyModifier;
                    LogReport($"{"Knockdown",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
                }

                if (modSettings.OverheatedModifier > 0 && defender.OverheatLevel < defender.CurrentHeat)
                {
                    totalMultiplier += modSettings.OverheatedModifier;
                    LogReport($"{"Heat",-20} | {modSettings.OverheatedModifier,10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (modSettings.ShutdownModifier > 0 && defender.IsShutDown)
                {
                    totalMultiplier += modSettings.ShutdownModifier;
                    LogReport($"{"Shutdown",-20} | {modSettings.ShutdownModifier,10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (PercentHead(defender) < 1)
                {
                    totalMultiplier += modSettings.HeadMaxModifier * PercentHead(defender);
                    LogReport($"{"Head",-20} | {modSettings.HeadMaxModifier * PercentHead(defender),10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (PercentCenterTorso(defender) < 1)
                {
                    totalMultiplier += modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(defender));
                    LogReport($"{"CT",-20} | {modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(defender)),10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (PercentLeftTorso(defender) < 1)
                {
                    totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(defender));
                    LogReport($"{"LT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(defender)),10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (PercentRightTorso(defender) < 1)
                {
                    totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(defender));
                    LogReport($"{"RT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(defender)),10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (PercentLeftLeg(defender) < 1)
                {
                    totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(defender));
                    LogReport($"{"LL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(defender)),10:#.###} | {totalMultiplier,10:#.###}");
                }

                if (PercentRightLeg(defender) < 1)
                {
                    totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentRightLeg(defender));
                    LogReport($"{"RL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentRightLeg(defender)),10:#.###} | {totalMultiplier,10:#.###}");
                }

                // weaponless
                if (weapons.TrueForAll(w => w.DamageLevel != ComponentDamageLevel.Functional || !w.HasAmmo)) // only fully unusable
                {
                    if (Random.Range(1, 5) == 1) // 20% chance of appearing
                    {
                        SaySpamFloatie(defender, "NO WEAPONS!");
                    }

                    totalMultiplier += modSettings.WeaponlessModifier;
                    LogReport($"{"Weaponless",-20} | {modSettings.WeaponlessModifier,10} | {totalMultiplier,10:#.###}");
                }

                // alone
                if (defender.Combat.GetAllAlliesOf(defender).TrueForAll(m => m.IsDead || m == defender as AbstractActor))
                {
                    if (Random.Range(1, 5) == 0) // 20% chance of appearing
                    {
                        SaySpamFloatie(defender, "NO ALLIES!");
                    }

                    totalMultiplier += modSettings.AloneModifier;
                    LogReport($"{"Alone",-20} | {modSettings.AloneModifier,10} | {totalMultiplier,10:#.###}");
                }

                totalMultiplier -= gutsAndTacticsSum;
                LogReport($"{"Guts and Tactics",-20} | {$"-{gutsAndTacticsSum}",10} | {totalMultiplier,10:#.###}");

                var resolveModifier = modSettings.ResolveMaxModifier *
                                      (defender.Combat.LocalPlayerTeam.Morale - modSettings.MedianResolve) /
                                      modSettings.MedianResolve;
                totalMultiplier -= resolveModifier;
                LogReport($"{$"Resolve {defender.Combat.LocalPlayerTeam.Morale}",-20} | {resolveModifier * -1,10:#.###} | {totalMultiplier,10:#.###}");

                return totalMultiplier;
            }
            catch (Exception ex)
            {
                // BOMB
                LogReport(ex);
                return -1f;
            }
        }

        private static void DrawHeader()
        {
            LogReport(new string('-', 46));
            LogReport($"{"Factors",-20} | {"Change",10} | {"Total",10}");
            LogReport(new string('-', 46));
        }

        public static bool SavedVsEject(Mech mech, float savingThrow)
        {
            LogReport("Panic save failure requires eject save");

            DrawHeader();
            if (modSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
            {
                savingThrow -= modSettings.DependableModifier;
                LogReport($"{"Dependable",-20} | {modSettings.DependableModifier,10} | {savingThrow,10:#.###}");
            }

            // calculate result
            savingThrow = Math.Max(0f, savingThrow - modSettings.BaseEjectionResist);
            LogReport($"{"Base ejection resist",-20} | {modSettings.BaseEjectionResist,10} | {savingThrow,10:#.###}");
            savingThrow = (float) Math.Round(savingThrow);
            LogReport($"{"Eject multiplier",-20} | {modSettings.EjectChanceMultiplier,10} | {savingThrow,10:#.###}");
            var roll = Random.Range(1, 100);
            LogReport(new string('-', 46));
            LogReport($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            LogReport(new string('-', 46));
            if (savingThrow <= 0)
            {
                LogReport("Negative saving throw; skipping");
                SaySpamFloatie(mech, "EJECT RESIST!");
                return true;
            }

            // cap the saving throw by the setting
            savingThrow = (int) Math.Min(savingThrow, modSettings.MaxEjectChance);
            SaySpamFloatie(mech, $"SAVE: {savingThrow}  ROLL: {roll}!");
            if (roll >= savingThrow)
            {
                LogReport("Successful ejection save");
                SaySpamFloatie(mech, $"EJECT SAVE! HEALTH: {MechHealth(mech):#.#}%");
                return true;
            }

            if (modSettings.QuirksEnabled && mech.MechDef.Chassis.ChassisTags.Contains("mech_quirk_noeject"))
            {
                LogReport("This mech can't eject (quirk)");
                mech.Combat.MessageCenter.PublishMessage(
                    new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(mech, "Mech quirk: Can't eject", FloatieMessage.MessageNature.PilotInjury, true)));
                return true;
            }

            if (modSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_drunk") &&
                mech.pilot.pilotDef.TimeoutRemaining > 0)
            {
                LogReport("Drunkard - not ejecting");
                mech.Combat.MessageCenter.PublishMessage(
                    new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(mech, "Pilot quirk: Drunkard won't eject", FloatieMessage.MessageNature.PilotInjury, true)));
                return true;
            }

            LogReport("Failed ejection save: Punchin\' Out!!");
            return false;
        }

        public static bool SavedVsPanic(Mech defender, float savingThrow)
        {
            if (modSettings.QuirksEnabled && defender.pilot.pilotDef.PilotTags.Contains("pilot_brave"))
            {
                savingThrow -= modSettings.BraveModifier;
                LogReport($"{"Bravery",-20} | {modSettings.BraveModifier,10} | {savingThrow,10:#.###}");
            }

            var index = GetPilotIndex(defender);
            savingThrow *= GetPanicModifier(TrackedPilots[GetPilotIndex(defender)].PanicStatus);
            LogReport($"{"Panic multiplier",-20} | {GetPanicModifier(TrackedPilots[GetPilotIndex(defender)].PanicStatus),10} | {savingThrow,10:#.###}");
            savingThrow = (float) Math.Max(0f, Math.Round(savingThrow));
            if (!(savingThrow >= 1))
            {
                LogReport("Negative saving throw; skipping");
                return false;
            }

            var roll = Random.Range(1, 100);
            LogReport(new string('-', 46));
            LogReport($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            LogReport(new string('-', 46));
            SaySpamFloatie(defender, $"{$"SAVE:{savingThrow}",-6} {$"ROLL {roll}!",3}");

            // lower panic level
            if (roll == 100)
            {
                LogReport("Critical success");
                var status = TrackedPilots[index].PanicStatus;

                LogReport($"{status} {(int) status}");
                // don't lower below floor
                if ((int) status > 0)
                {
                    status--;
                    TrackedPilots[index].PanicStatus = status;
                }

                // prevent floatie if already at Confident
                if ((int) TrackedPilots[index].PanicStatus > 0)
                {
                    defender.Combat.MessageCenter.PublishMessage(
                        new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(defender, "CRIT SUCCESS!", FloatieMessage.MessageNature.Inspiration, false)));
                    SayStatusFloatie(defender, false);
                }

                return true;
            }

            // continue if roll wasn't 100
            if (roll >= savingThrow)
            {
                SaySpamFloatie(defender, "PANIC SAVE!");
                LogReport("Successful panic save");
                return true;
            }

            LogReport("Failed panic save");
            SaySpamFloatie(defender, "SAVE FAIL!");
            ApplyPanicDebuff(defender);

            SayStatusFloatie(defender, false);

            // check for crit
            if (MechHealth(defender) <= modSettings.MechHealthForCrit &&
                (roll < (int) savingThrow - modSettings.CritOver || roll == 1))
            {
                LogReport("Critical failure on panic save");
                // record status to see if it changes after
                var status = TrackedPilots[index].PanicStatus;
                TrackedPilots[index].PanicStatus = PanicStatus.Panicked;
                defender.Combat.MessageCenter.PublishMessage(
                    new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(defender, "PANIC LEVEL CRITICAL!", FloatieMessage.MessageNature.CriticalHit, true)));

                // show both floaties on a panic crit unless panicked already
                if (status != TrackedPilots[index].PanicStatus)
                {
                    SayStatusFloatie(defender, false);
                }
            }

            return false;
        }

        // method is called despite the setting, so it can be controlled in one place
        private static void SaySpamFloatie(Mech mech, string message)
        {
            if (!modSettings.FloatieSpam) return;
            mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                new ShowActorInfoSequence(mech, message, FloatieMessage.MessageNature.Neutral, false)));
        }

        // bool controls whether to display as buff or debuff
        private static void SayStatusFloatie(Mech mech, bool buff)
        {
            var index = GetPilotIndex(mech);

            var floatieString = $"{TrackedPilots[index].PanicStatus.ToString()}";
            if (buff)
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, floatieString, FloatieMessage.MessageNature.Inspiration, true)));
            }

            else
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, floatieString, FloatieMessage.MessageNature.Debuff, true)));
            }
        }

        // true implies a panic condition was met
        public static bool ShouldPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (!CanPanic(mech, attackSequence)) return false;
            return SufficientDamageWasDone(attackSequence);
        }

        public static bool SkipProcessingAttack(AttackStackSequence __instance, MessageCenterMessage message)
        {
            var attackCompleteMessage = message as AttackCompleteMessage;
            if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID) return true;

            // can't do stuff with vehicles and buildings
            if (!(__instance.directorSequences[0].chosenTarget is Mech)) return true;
            return __instance.directorSequences[0].chosenTarget?.GUID == null;
        }

        //     returns true if 10% armor damage was incurred or any structure damage
        private static bool SufficientDamageWasDone(AttackDirector.AttackSequence attackSequence)
        {
            if (attackSequence == null) return false;

            var id = attackSequence.chosenTarget.GUID;
            if (!attackSequence.GetAttackDidDamage(id))
            {
                LogReport("No damage");
                return false;
            }

            var previousArmor = AttackStackSequence_OnAttackBegin_Patch.mechArmorBeforeAttack;
            var previousStructure = AttackStackSequence_OnAttackBegin_Patch.mechStructureBeforeAttack;
            LogReport($"Damage >>> A: {attackSequence.GetArmorDamageDealt(id):#.###}" +
                      $" S: {attackSequence.GetStructureDamageDealt(id):#.###}" +
                      $" ({(attackSequence.GetArmorDamageDealt(id) + attackSequence.GetStructureDamageDealt(id)) / (previousArmor + previousStructure) * 100:#.##}%)  H: {Mech_AddExternalHeat_Patch.heatDamage}");
            if (attackSequence.GetStructureDamageDealt(id) >= modSettings.MinimumStructureDamageRequired)
            {
                LogReport("Structure damage requires panic save");
                return true;
            }

            // ReSharper disable once NotAccessedVariable
            float heatTaken = 0;
            if (attackSequence.chosenTarget is Mech defender)
            {
                heatTaken = defender.CurrentHeat - AttackStackSequence_OnAttackBegin_Patch.mechHeatBeforeAttack;
                LogReport($"Took {Mech_AddExternalHeat_Patch.heatDamage} heat");
            }

            if (attackSequence.GetArmorDamageDealt(id) + attackSequence.GetStructureDamageDealt(id) + Mech_AddExternalHeat_Patch.heatDamage * modSettings.HeatDamageModifier /
                (previousArmor + previousStructure) +
                100 <= modSettings.MinimumDamagePercentageRequired)
            {
                LogReport("Not enough damage");
                return false;
            }

            LogReport("Total damage requires a panic save");
            return true;
        }

        internal static void SetupEjectPhrases(string modDir)
        {
            if (!modSettings.EnableEjectPhrases)
            {
                return;
            }

            if (!modSettings.EnableEjectPhrases) return;

            try
            {
                ejectPhraseList = File.ReadAllText(Path.Combine(modDir, "phrases.txt")).Split('\n').ToList();
            }
            catch (Exception ex)
            {
                LogReport("Error - problem loading phrases.txt but the setting is enabled");
                Log(ex);
                // in case the file is missing but the setting is enabled
                modSettings.EnableEjectPhrases = false;
            }
        }
    }
}
