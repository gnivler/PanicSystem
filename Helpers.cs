using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using Harmony;
using PanicSystem.Components;
using PanicSystem.Patches;
using static PanicSystem.Logger;
using static PanicSystem.PanicSystem;
using Random = UnityEngine.Random;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace PanicSystem
{
    public class Helpers
    {
        // values for combining melee with support weapon fire
        private static float initialArmorMelee;
        private static float initialStructureMelee;
        private static float armorDamageMelee;
        private static float structureDamageMelee;
        private static bool hadMeleeAttack;
        internal static float damageIncludingHeatDamage;

        // used in strings
        internal static float ActorHealth(AbstractActor actor) =>
            (actor.SummaryArmorCurrent + actor.SummaryStructureCurrent) /
            (actor.SummaryArmorMax + actor.SummaryStructureMax) * 100;

        // used in calculations
        internal static float PercentPilot(Pilot pilot) => 1 - (float) pilot.Injuries / pilot.Health;

	internal static float MaxArmorForLocation(Mech mech, int Location)
	{
		if (mech != null)
		{
			Statistic stat = mech.StatCollection.GetStatistic(mech.GetStringForArmorLocation((ArmorLocation)Location));
			if(stat == null) {
            			Log.TWL(0, "Can't get armor stat " + new Text(mech.DisplayName).ToString() + " location:" +Location, true);
            			return 0;
          		}

			return stat.DefaultValue<float>();
		}
		return 0;
	}

        internal static float PercentRightTorso(Mech mech) =>
            (mech.RightTorsoStructure +
             mech.RightTorsoFrontArmor +
             mech.RightTorsoRearArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.RightTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.RightTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.RightTorsoRear));

        internal static float PercentLeftTorso(Mech mech) =>
            (mech.LeftTorsoStructure +
             mech.LeftTorsoFrontArmor +
             mech.LeftTorsoRearArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.LeftTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.LeftTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.LeftTorsoRear));

        internal static float PercentCenterTorso(Mech mech) =>
            (mech.CenterTorsoStructure +
             mech.CenterTorsoFrontArmor +
             mech.CenterTorsoRearArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.CenterTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.CenterTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.CenterTorsoRear));

        internal static float PercentLeftLeg(Mech mech) =>
            (mech.LeftLegStructure + mech.LeftLegArmor) /
            (MaxStructureForLocation((int) ChassisLocations.LeftLeg) +
             MaxArmorForLocation(mech, (int) ArmorLocation.LeftLeg));

        internal static float PercentRightLeg(Mech mech) =>
            (mech.RightLegStructure + mech.RightLegArmor) /
            (MaxStructureForLocation((int) ChassisLocations.RightLeg) +
             MaxArmorForLocation(mech, (int) ArmorLocation.RightLeg));

        internal static float PercentHead(Mech mech) =>
            (mech.HeadStructure + mech.HeadArmor) /
            (MaxStructureForLocation((int) ChassisLocations.Head) +
             MaxArmorForLocation(mech, (int) ArmorLocation.Head));

        // check if panic roll is possible
        private static bool CanPanic(AbstractActor actor, AttackDirector.AttackSequence attackSequence)
        {
            if (actor == null || actor.IsDead || actor.IsFlaggedForDeath && actor.HasHandledDeath)
            {
                LogReport($"{attackSequence?.attacker?.DisplayName} incapacitated {actor?.DisplayName}");
                return false;
            }

            if (attackSequence == null ||
                actor.team.IsLocalPlayer && !modSettings.PlayersCanPanic ||
                !actor.team.IsLocalPlayer && !modSettings.EnemiesCanPanic)
            {
                return false;
            }

            return true;
        }

        // Returns a float to modify panic roll difficulty based on existing panic level
        internal static float GetPanicModifier(PanicStatus pilotStatus)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (pilotStatus)
            {
                case PanicStatus.Unsettled: return modSettings.UnsettledPanicFactor;
                case PanicStatus.Stressed: return modSettings.StressedPanicFactor;
                case PanicStatus.Panicked: return modSettings.PanickedPanicFactor;
                default: return 1f;
            }
        }

        internal static void DrawHeader()
        {
            LogReport(new string('-', 46));
            LogReport($"{"Factors",-20} | {"Change",10} | {"Total",10}");
            LogReport(new string('-', 46));
        }

        internal static void SaySpamFloatie(AbstractActor actor, string message)
        {
            if (!modSettings.FloatieSpam ||
                string.IsNullOrEmpty(message))
            {
                return;
            }

            actor.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                new ShowActorInfoSequence(actor, message, FloatieMessage.MessageNature.Neutral, false)));
        }

        // true implies a panic condition was met
        public static bool ShouldPanic(AbstractActor actor, AttackDirector.AttackSequence attackSequence)
        {
            if (!CanPanic(actor, attackSequence))
            {
                return false;
            }

            return SufficientDamageWasDone(attackSequence);
        }

        public static bool ShouldSkipProcessing(AttackStackSequence __instance, MessageCenterMessage message)
        {
            var attackCompleteMessage = (AttackCompleteMessage) message;
            if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
            {
                return true;
            }

            // can't do stuff with buildings
            if (!(__instance.directorSequences[0].chosenTarget is Vehicle) &&
                !(__instance.directorSequences[0].chosenTarget is Mech))
            {
                return true;
            }

            return __instance.directorSequences[0].chosenTarget?.GUID == null;
        }

        // returns true if enough damage was inflicted to trigger a panic save
        private static bool SufficientDamageWasDone(AttackDirector.AttackSequence attackSequence)
        {
            if (attackSequence == null)
            {
                return false;
            }

            var id = attackSequence.chosenTarget.GUID;
            if (!attackSequence.GetAttackDidDamage(id))
            {
                LogReport("No damage");
                return false;
            }

            // Account for melee attacks so separate panics are not triggered.
            if (attackSequence.isMelee && MechMeleeSequence_FireWeapons_Patch.meleeHasSupportWeapons)
            {
                initialArmorMelee = AttackStackSequence_OnAttackBegin_Patch.armorBeforeAttack;
                initialStructureMelee = AttackStackSequence_OnAttackBegin_Patch.structureBeforeAttack;
                armorDamageMelee = attackSequence.GetArmorDamageDealt(id);
                structureDamageMelee = attackSequence.GetStructureDamageDealt(id);
                hadMeleeAttack = true;
                LogReport("Stashing melee damage for support weapon firing");
                return false;
            }

            var previousArmor = AttackStackSequence_OnAttackBegin_Patch.armorBeforeAttack;
            var previousStructure = AttackStackSequence_OnAttackBegin_Patch.structureBeforeAttack;

            if (hadMeleeAttack)
            {
                LogReport("Adding stashed melee damage");
                previousArmor = initialArmorMelee;
                previousStructure = initialStructureMelee;
            }
            else
            {
                armorDamageMelee = 0;
                structureDamageMelee = 0;
            }

            var armorDamage = attackSequence.GetArmorDamageDealt(id) + armorDamageMelee;
            var structureDamage = attackSequence.GetStructureDamageDealt(id) + structureDamageMelee;
            var heatDamage = Mech_AddExternalHeat_Patch.heatDamage * modSettings.HeatDamageFactor;
            // used in SavingThrows.cs
            damageIncludingHeatDamage = armorDamage + structureDamage + heatDamage;
            var percentDamageDone =
                damageIncludingHeatDamage / (previousArmor + previousStructure) * 100;

            // clear melee values
            initialArmorMelee = 0;
            initialStructureMelee = 0;
            armorDamageMelee = 0;
            structureDamageMelee = 0;
            hadMeleeAttack = false;

            // have to check structure here AFTER armor, despite it being the priority, because we need to set the global
            LogReport($"Damage >>> A: {armorDamage:F3} S: {structureDamage:F3} ({percentDamageDone:F2}%) H: {Mech_AddExternalHeat_Patch.heatDamage}");
            if (modSettings.AlwaysPanic)
            {
                LogReport("AlwaysPanic");
                return true;
            }

            if (attackSequence.chosenTarget is Mech &&
                attackSequence.GetStructureDamageDealt(id) >= modSettings.MinimumMechStructureDamageRequired ||
                modSettings.VehiclesCanPanic &&
                attackSequence.chosenTarget is Vehicle &&
                attackSequence.GetStructureDamageDealt(id) >= modSettings.MinimumVehicleStructureDamageRequired)
            {
                LogReport("Structure damage requires panic save");
                return true;
            }

            if (percentDamageDone <= modSettings.MinimumDamagePercentageRequired)
            {
                LogReport("Not enough damage");
                Mech_AddExternalHeat_Patch.heatDamage = 0;
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
                LogDebug(ex);
                // in case the file is missing but the setting is enabled
                modSettings.EnableEjectPhrases = false;
            }
        }

        internal static void ApplyPanicStatus(AbstractActor __instance, PanicStatus panicStatus, bool worsened)
        {
            var actor = __instance;
            int Uid() => Random.Range(1, int.MaxValue);
            var effectManager = UnityGameInstance.BattleTechGame.Combat.EffectManager;
            // remove all PanicSystem effects first
            ClearPanicEffects(actor, effectManager);

            // re-apply effects
            var messageNature = worsened ? FloatieMessage.MessageNature.Debuff : FloatieMessage.MessageNature.Buff;
            var verb = worsened ? modSettings.PanicWorsenedString : modSettings.PanicImprovedString;
            // account for the space, append it when the verb is defined
            if (!string.IsNullOrEmpty(verb))
            {
                verb += " ";
            }
            var message = actor.Combat.MessageCenter;
            var dummyWeapon = new WeaponHitInfo();
            switch (panicStatus)
            {
                case PanicStatus.Unsettled:
                    LogReport($"{actor.DisplayName} {verb}{modSettings.PanicStates[1]}");
                    message.PublishMessage(new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(actor,
                            $"{verb}{modSettings.PanicStates[1]}",
                            messageNature,
                            false)));
                    effectManager.CreateEffect(StatusEffect.UnsettledToHit, "PanicSystemToHit", Uid(), actor, actor, dummyWeapon, 0);
                    break;
                case PanicStatus.Stressed:
                    LogReport($"{actor.DisplayName} {verb}{modSettings.PanicStates[2]}");
                    message.PublishMessage(new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(actor,
                            $"{verb}{modSettings.PanicStates[2]}",
                            messageNature,
                            false)));
                    effectManager.CreateEffect(StatusEffect.StressedToHit, "PanicSystemToHit", Uid(), actor, actor, dummyWeapon, 0);
                    effectManager.CreateEffect(StatusEffect.StressedToBeHit, "PanicSystemToBeHit", Uid(), actor, actor, dummyWeapon, 0);
                    break;
                case PanicStatus.Panicked:
                    LogReport($"{actor.DisplayName} {verb}{modSettings.PanicStates[3]}");
                    message.PublishMessage(new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(actor,
                            $"{verb}{modSettings.PanicStates[3]}",
                            messageNature,
                            false)));
                    effectManager.CreateEffect(StatusEffect.PanickedToHit, "PanicSystemToHit", Uid(), actor, actor, dummyWeapon, 0);
                    effectManager.CreateEffect(StatusEffect.PanickedToBeHit, "PanicSystemToBeHit", Uid(), actor, actor, dummyWeapon, 0);
                    break;

                default:
                    LogReport($"{actor.DisplayName} {verb}{modSettings.PanicStates[0]}");
                    message.PublishMessage(new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(actor,
                            $"{verb}{modSettings.PanicStates[0]}",
                            messageNature,
                            false)));
                    break;
            }
        }

        private static void ClearPanicEffects(AbstractActor actor, EffectManager effectManager)
        {
            var effects = Traverse.Create(effectManager).Field("effects").GetValue<List<Effect>>();
            for (var i = 0; i < effects.Count; i++)
            {
                if (effects[i].id.StartsWith("PanicSystem") && Traverse.Create(effects[i]).Field("target").GetValue<object>() == actor)
                {
                    effectManager.CancelEffect(effects[i]);
                }
            }
        }
    }
}
