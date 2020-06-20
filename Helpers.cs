using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using FluffyUnderware.DevTools;
using Harmony;
using PanicSystem.Components;
using PanicSystem.Patches;
using static PanicSystem.Logger;
using static PanicSystem.PanicSystem;
using Random = UnityEngine.Random;
using CustomAmmoCategoriesPatches;
using UnityEngine;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace PanicSystem
{
    public class Helpers
    {

        // used in strings
        internal static float ActorHealth(AbstractActor actor) =>
            (actor.SummaryArmorCurrent + actor.SummaryStructureCurrent) /
            (actor.SummaryArmorMax + actor.SummaryStructureMax) * 100;

        // used in calculations
        internal static float PercentPilot(Pilot pilot) => 1 - (float) pilot.Injuries / pilot.Health;

<<<<<<< HEAD
	public static float MaxArmorForLocation(Mech mech, int Location)
=======
	internal static float PercentForArmorLocation(Mech mech, int Location)
>>>>>>> pr/2
	{
		// Invalid locations are always 100%
		// This helps makes the functions generic for the missing leg back armor, for example
		if (Location == 0)
			return 1;

		if (mech != null)
		{
			Statistic stat = mech.StatCollection.GetStatistic(mech.GetStringForArmorLocation((ArmorLocation)Location));
			if(stat == null) {
<<<<<<< HEAD
                        LogDebug($"Can't get armor stat  { mech.DisplayName } location:{ Location.ToString()}");
            			return 0;
          		}
                //LogDebug($"armor stat  { mech.DisplayName } location:{ Location.ToString()} :{stat.DefaultValue<float>()}");
                return stat.DefaultValue<float>();
		}
            LogDebug($"Mech null");
            return 0;
=======
            			LogDebug($"Can't get armor stat  { mech.DisplayName } location:{ Location}");
            			return 0;
          		}

			LogDebug($"Armor stat  { mech.DisplayName } location:{ Location} cur:{stat.Value<float>()} max:{stat.DefaultValue<float>()}");
//			if (mech.team.IsLocalPlayer)
//                  LogReport($"Armor stat  { mech.DisplayName } location:{ Location} cur:{stat.Value<float>()} max:{stat.DefaultValue<float>()}");

                float maxArmor = stat.DefaultValue<float>();

            // Limit the max armor to ArmorDamageThreshold and the percent value to 1 (100%)
            // This helps reduce panic effects for heavier, well-armored mechs
            // Losing 30% of your armor isn't as distressing when you have 300 max as when you have 60
            // Heavily armored mechs should brush this off until they're seriously damaged
            if (maxArmor > modSettings.ArmorDamageThreshold && modSettings.ArmorDamageThreshold > 0)
				maxArmor = modSettings.ArmorDamageThreshold;

			float percentArmor = stat.Value<float>() / maxArmor;
			if (percentArmor > 1)
				percentArmor = 1;

			return percentArmor;
		}
		LogDebug($"Mech null");
		return 0;
	}

	internal static float PercentForStructureLocation(Mech mech, int Location)
	{
		// Invalid locations are always 100%
		// This helps makes the functions generic for the missing leg back armor, for example
		if (Location == 0)
			return 1;

		if (mech != null)
		{
			Statistic stat = mech.StatCollection.GetStatistic(mech.GetStringForStructureLocation((ChassisLocations)Location));
			if(stat == null)
			{
            			LogDebug($"Can't get structure stat  { mech.DisplayName } location:{ Location}");
            			return 0;
          		}

			LogDebug($"Structure stat  { mech.DisplayName } location:{ Location} cur:{stat.Value<float>()} max:{stat.DefaultValue<float>()}");
			return (stat.Value<float>() / stat.DefaultValue<float>());
		}
		LogDebug($"Mech null");
		return 0;
	}

	internal static float PercentForLocation(Mech mech, int LocationFront, int LocationBack, int LocationStructure)
	{
		if (mech != null)
		{
			float percentFront = PercentForArmorLocation(mech, LocationFront);
			float percentBack = PercentForArmorLocation(mech, LocationBack);
			float percentStructure = PercentForStructureLocation(mech, LocationStructure);

			float percentLocation = percentStructure;
			float numAdditions = 1;

			// If the structure is damaged, then use that over any armor values
			// If an armor value is lower than the structure percent, then factor it in too
			// This emphasizes internal damage from a blow through (back armor gone or tandem weapons)
			if (percentStructure < 1)
			{
				if (percentFront < percentStructure)
				{
					percentLocation += percentFront;
					numAdditions++;
				}

				if (LocationBack != 0)
				{
					if (percentBack < percentStructure)
					{
						percentLocation += percentBack;
						numAdditions++;
					}
				}
			}
			else
			{
				percentLocation += percentFront;
				numAdditions++;

				if (LocationBack != 0)
				{
					percentLocation += percentBack;
					numAdditions++;
				}
			}

			percentLocation /= numAdditions;
			return percentLocation;
		}
		LogDebug($"Mech null");
		return 0;
>>>>>>> pr/2
	}
    public static float MaxStructureForLocation(Mech mech, int Location)
    {
        if (mech != null)
        {
            Statistic stat = mech.StatCollection.GetStatistic(mech.GetStringForStructureLocation((ChassisLocations) Location));
            if (stat == null)
            {
                LogDebug($"Can't get structure stat  { mech.DisplayName } location:{ Location.ToString()}");
                return 0;
            }
                //LogDebug($"structure stat  { mech.DisplayName } location:{ Location.ToString()}:{stat.DefaultValue<float>()}");
                return stat.DefaultValue<float>();
        }
            LogDebug($"Mech null");
            return 0;
    }

        public static float MaxArmorForLocation(Vehicle v, int Location)
        {
            if (v != null)
            {
                Statistic stat = v.StatCollection.GetStatistic(v.GetStringForArmorLocation((VehicleChassisLocations)Location));
                if (stat == null)
                {
                    LogDebug($"Can't get armor stat  { v.DisplayName } location:{ Location.ToString()}");
                    return 0;
                }
                //LogDebug($"armor stat  { v.DisplayName } location:{ Location.ToString()} :{stat.DefaultValue<float>()}");
                return stat.DefaultValue<float>();
            }
            LogDebug($"Vehicle null");
            return 0;
        }
        public static float MaxStructureForLocation(Vehicle v, int Location)
        {
            if (v != null)
            {
                Statistic stat = v.StatCollection.GetStatistic(v.GetStringForStructureLocation((VehicleChassisLocations)Location));
                if (stat == null)
                {
                    LogDebug($"Can't get structure stat  { v.DisplayName } location:{ Location.ToString()}");
                    return 0;
                }
                //LogDebug($"structure stat  { mech.DisplayName } location:{ Location.ToString()}:{stat.DefaultValue<float>()}");
                return stat.DefaultValue<float>();
            }
            LogDebug($"Vehicle null");
            return 0;
        }

        internal static float PercentRightTorso(Mech mech) =>
<<<<<<< HEAD
            (mech.RightTorsoStructure +
             mech.RightTorsoFrontArmor +
             mech.RightTorsoRearArmor) /
            (MaxStructureForLocation(mech,(int) ChassisLocations.RightTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.RightTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.RightTorsoRear));

        internal static float PercentLeftTorso(Mech mech) =>
            (mech.LeftTorsoStructure +
             mech.LeftTorsoFrontArmor +
             mech.LeftTorsoRearArmor) /
            (MaxStructureForLocation(mech,(int) ChassisLocations.LeftTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.LeftTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.LeftTorsoRear));

        internal static float PercentCenterTorso(Mech mech) =>
            (mech.CenterTorsoStructure +
             mech.CenterTorsoFrontArmor +
             mech.CenterTorsoRearArmor) /
            (MaxStructureForLocation(mech,(int) ChassisLocations.CenterTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.CenterTorso) +
             MaxArmorForLocation(mech, (int) ArmorLocation.CenterTorsoRear));

        internal static float PercentLeftLeg(Mech mech) =>
            (mech.LeftLegStructure + mech.LeftLegArmor) /
            (MaxStructureForLocation(mech, (int) ChassisLocations.LeftLeg) +
             MaxArmorForLocation(mech, (int) ArmorLocation.LeftLeg));

        internal static float PercentRightLeg(Mech mech) =>
            (mech.RightLegStructure + mech.RightLegArmor) /
            (MaxStructureForLocation(mech, (int) ChassisLocations.RightLeg) +
             MaxArmorForLocation(mech, (int) ArmorLocation.RightLeg));

        internal static float PercentHead(Mech mech) =>
            (mech.HeadStructure + mech.HeadArmor) /
            (MaxStructureForLocation(mech, (int) ChassisLocations.Head) +
             MaxArmorForLocation(mech, (int) ArmorLocation.Head));
=======
            (PercentForLocation(mech, (int) ArmorLocation.RightTorso, (int) ArmorLocation.RightTorsoRear, 
			(int) ChassisLocations.RightTorso));

        internal static float PercentLeftTorso(Mech mech) =>
            (PercentForLocation(mech, (int) ArmorLocation.LeftTorso, (int) ArmorLocation.LeftTorsoRear, 
			(int) ChassisLocations.LeftTorso));

        internal static float PercentCenterTorso(Mech mech) =>
            (PercentForLocation(mech, (int) ArmorLocation.CenterTorso, (int) ArmorLocation.CenterTorsoRear, 
			(int) ChassisLocations.CenterTorso));

        internal static float PercentLeftLeg(Mech mech) =>
            (PercentForLocation(mech, (int) ArmorLocation.LeftLeg, 0, 
			(int) ChassisLocations.LeftTorso));

        internal static float PercentRightLeg(Mech mech) =>
            (PercentForLocation(mech, (int) ArmorLocation.RightLeg, 0, 
			(int) ChassisLocations.RightLeg));

        internal static float PercentHead(Mech mech) =>
            (PercentForLocation(mech, (int) ArmorLocation.Head, 0, 
			(int) ChassisLocations.Head));
>>>>>>> pr/2

        // check if panic roll is possible
        private static bool CanPanic(AbstractActor actor, AbstractActor attacker)
        {
            if (actor == null || actor.IsDead || actor.IsFlaggedForDeath && actor.HasHandledDeath)
            {
                LogReport($"{attacker?.DisplayName} incapacitated {actor?.DisplayName}");
                return false;
            }

<<<<<<< HEAD
            if (actor.team.IsLocalPlayer && !modSettings.PlayersCanPanic ||
                !actor.team.IsLocalPlayer && !modSettings.EnemiesCanPanic)
=======
            if (attackSequence == null ||
                (actor.team.IsLocalPlayer && !modSettings.PlayersCanPanic) ||
                (!actor.team.IsLocalPlayer && !modSettings.EnemiesCanPanic) ||
                (actor is Vehicle && !modSettings.VehiclesCanPanic))
>>>>>>> pr/2
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
        public static bool ShouldPanic(AbstractActor actor, AbstractActor attacker,out int heatdamage, out float damageIncludingHeatDamage)
        {
            if (!CanPanic(actor,attacker))
            {
                damageIncludingHeatDamage = 0;
                heatdamage = 0;
                return false;
            }

            return SufficientDamageWasDone(actor, out heatdamage,out damageIncludingHeatDamage);
        }

        public static bool ShouldSkipProcessing(AbstractActor actor)
        {

            // can't do stuff with buildings
            if (!(actor is Vehicle) &&
                !(actor is Mech))
            {
                return true;
            }

            return actor?.GUID == null;
        }

        // returns true if enough damage was inflicted to trigger a panic save
        private static bool SufficientDamageWasDone(AbstractActor actor, out int heatdamage, out float damageIncludingHeatDamage)
        {
            if (actor == null)
            {
                damageIncludingHeatDamage = 0;
                heatdamage = 0;
                return false;
            }

            float armorDamage;
            float structureDamage;
            float previousArmor;
            float previousStructure;
            //don't need the damage numbers as we can check the actor itself.
            TurnDamageTracker.DamageDuringTurn(actor,out armorDamage,out structureDamage,out previousArmor,out previousStructure,out heatdamage);
            
            // used in SavingThrows.cs
            damageIncludingHeatDamage =armorDamage+structureDamage;

            if (!(actor is Mech) ||actor.isHasHeat())
            {//Battle Armor doesn't have heat
                damageIncludingHeatDamage = damageIncludingHeatDamage + (heatdamage * modSettings.HeatDamageFactor);
            }

            if (damageIncludingHeatDamage <= 0)//potentially negative if repairs happened.
            {
                LogReport($"Damage >>> A: {armorDamage:F3}/{previousArmor:F3} S: {structureDamage:F3}/{previousStructure:F3} NA%) H: {heatdamage}");
                LogReport("No damage");
                return false;
            }


            var percentDamageDone =
                damageIncludingHeatDamage / (previousArmor + previousStructure) * 100;

            LogReport($"Damage >>> A: {armorDamage:F3}/{previousArmor:F3} S: {structureDamage:F3}/{previousStructure:F3} ({percentDamageDone:F2}%) H: {heatdamage}");
            if (modSettings.AlwaysPanic)
            {
                LogReport("AlwaysPanic");
                return true;
            }

<<<<<<< HEAD
            if ((actor is Mech &&
                structureDamage >= modSettings.MinimumMechStructureDamageRequired) ||
                (modSettings.VehiclesCanPanic &&
                actor is Vehicle &&
                structureDamage >= modSettings.MinimumVehicleStructureDamageRequired))
=======
            if (attackSequence.chosenTarget is Mech &&
                attackSequence.GetStructureDamageDealt(id) > modSettings.MinimumMechStructureDamageRequired ||
                modSettings.VehiclesCanPanic &&
                attackSequence.chosenTarget is Vehicle &&
                attackSequence.GetStructureDamageDealt(id) > modSettings.MinimumVehicleStructureDamageRequired)
>>>>>>> pr/2
            {
                LogReport("Structure damage requires panic save");
                return true;
            }

            if (percentDamageDone <= modSettings.MinimumDamagePercentageRequired)
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
