using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using Harmony;
using PanicSystem.Components;
using static PanicSystem.Logger;
using static PanicSystem.PanicSystem;
using Random = UnityEngine.Random;
#if NO_CAC
#else
using CustomAmmoCategoriesPatches;
#endif

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace PanicSystem
{
    public class Helpers
    {

        // used in strings
        internal static float ActorHealth(AbstractActor actor)
        {
            //This is probably bugged for bonus/armor structure components.
            //return
            //(actor.SummaryArmorCurrent + actor.SummaryStructureCurrent) /
            //(actor.SummaryArmorMax + actor.SummaryStructureMax) * 100;
            float ah = 100;
            if (actor is Mech mech)
            {
                ah=((mech.RightTorsoStructure + mech.LeftTorsoStructure + mech.CenterTorsoStructure + mech.LeftLegStructure + mech.RightLegStructure + mech.HeadStructure) +
                    (mech.RightTorsoFrontArmor + mech.RightTorsoRearArmor + mech.LeftTorsoFrontArmor + mech.LeftTorsoRearArmor +
                     mech.CenterTorsoFrontArmor + mech.CenterTorsoRearArmor + mech.LeftLegArmor + mech.RightLegArmor + mech.HeadArmor)) /
                     ((MaxStructureForLocation(mech, (int)ChassisLocations.RightTorso) + MaxStructureForLocation(mech, (int)ChassisLocations.LeftTorso) + MaxStructureForLocation(mech, (int)ChassisLocations.CenterTorso)
                    + MaxStructureForLocation(mech, (int)ChassisLocations.LeftLeg) + MaxStructureForLocation(mech, (int)ChassisLocations.RightLeg) + MaxStructureForLocation(mech, (int)ChassisLocations.Head)) +
                    (MaxArmorForLocation(mech, (int)ArmorLocation.RightTorso) + MaxArmorForLocation(mech, (int)ArmorLocation.RightTorsoRear)
             + MaxArmorForLocation(mech, (int)ArmorLocation.LeftTorso) + MaxArmorForLocation(mech, (int)ArmorLocation.LeftTorsoRear)
             + MaxArmorForLocation(mech, (int)ArmorLocation.CenterTorso) + MaxArmorForLocation(mech, (int)ArmorLocation.CenterTorsoRear)
             + MaxArmorForLocation(mech, (int)ArmorLocation.LeftLeg) + MaxArmorForLocation(mech, (int)ArmorLocation.RightLeg)
             + MaxArmorForLocation(mech, (int)ArmorLocation.Head) ))
                     *100;
            }
            else if (actor is Vehicle v)
            {
                ah=((v.LeftSideStructure + v.RightSideStructure + v.FrontStructure + v.RearStructure + v.TurretStructure) + 
                    (v.LeftSideArmor + v.RightSideArmor + v.FrontArmor + v.RearArmor + v.TurretArmor)) / 
                    ((MaxStructureForLocation(v, (int)VehicleChassisLocations.Left) + MaxStructureForLocation(v, (int)VehicleChassisLocations.Right) + MaxStructureForLocation(v, (int)VehicleChassisLocations.Front) + MaxStructureForLocation(v, (int)VehicleChassisLocations.Rear) + MaxStructureForLocation(v, (int)VehicleChassisLocations.Turret)) +
                     (MaxArmorForLocation(v, (int)VehicleChassisLocations.Left) + MaxArmorForLocation(v, (int)VehicleChassisLocations.Right) + MaxArmorForLocation(v, (int)VehicleChassisLocations.Front) + MaxArmorForLocation(v, (int)VehicleChassisLocations.Rear) + MaxArmorForLocation(v, (int)VehicleChassisLocations.Turret))
                    )*100;

            }
            else
            {
                LogReport("Not mech or vehicle");
                return ah;
            }
            LogReport($"ActorHealth {actor.Nickname} - {actor.DisplayName} - {actor.GUID} -{ah:F3}%");
            return ah;
        }


        // used in calculations
        internal static float PercentPilot(Pilot pilot) => 1 - (float) pilot.Injuries / pilot.Health;

        public static float MaxArmorForLocation(Mech mech, int Location)
        {
            if (mech != null)
            {
                Statistic stat = mech.StatCollection.GetStatistic(mech.GetStringForArmorLocation((ArmorLocation)Location));
                if (stat == null)
                {
                    LogDebug($"Can't get armor stat  { mech.DisplayName } location:{ Location.ToString()}");
                    return 0;
                }
                //LogDebug($"armor stat  { mech.DisplayName } location:{ Location.ToString()} :{stat.DefaultValue<float>()}");
                return stat.DefaultValue<float>();
            }
            LogDebug($"Mech null");
            return 0;
        }
        public static float MaxStructureForLocation(Mech mech, int Location)
        {
            if (mech != null)
            {
                Statistic stat = mech.StatCollection.GetStatistic(mech.GetStringForStructureLocation((ChassisLocations)Location));
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

       	internal static float PercentForArmorLocation(Mech mech, int Location)
	{
		// Invalid locations are always 100%
		// This helps makes the functions generic for the missing leg back armor, for example
		if (Location == 0)
			return 1;

		if (mech != null)
		{
			Statistic stat = mech.StatCollection.GetStatistic(mech.GetStringForArmorLocation((ArmorLocation)Location));
			if(stat == null) {
            			LogDebug($"Can't get armor stat  { mech.DisplayName } location:{ Location}");
            			return 0;
          		}

			//LogDebug($"Armor stat  { mech.DisplayName } location:{ Location} cur:{stat.Value<float>()} max:{stat.DefaultValue<float>()}");
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

			//LogDebug($"Structure stat  { mech.DisplayName } location:{ Location} cur:{stat.Value<float>()} max:{stat.DefaultValue<float>()}");
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
			float numAdditions = 2;

			// Use the minimum percentage between structure and armor
			// This emphasizes internal damage from a blow through (back armor gone or tandem weapons)
			percentLocation += Math.Min(percentFront, percentStructure);

			if (LocationBack != 0)
			{
                percentLocation += Math.Min(percentBack, percentStructure);
                numAdditions++;
 			}

			percentLocation /= numAdditions;
            LogReport($"{((ChassisLocations)LocationStructure).ToString(),-20} | A:{percentFront * 100,7:F3}% | S:{percentStructure * 100,7:F3}%");
            if (LocationBack != 0)
            {
                LogReport($"{" ",-20} | [{percentBack * 100,7:F3}%] | ");
            }
                return percentLocation;
		}
		LogDebug($"Mech null");
		return 0;
	}

        private static float PercentForLocation(Vehicle v, VehicleChassisLocations location)
        {
            if (v != null)
            {
                var maxs=MaxStructureForLocation(v,(int)location);
                var maxa = MaxArmorForLocation(v, (int)location);
                var cs = maxs;
                var ca = maxa;
                if (maxs == 0 || maxa == 0)
                {
                    LogDebug($"Invalid location in vehicle {location.ToString()}");
                    return 1;
                }

                switch (location)
                {
                    case VehicleChassisLocations.Turret:
                        cs = v.TurretStructure;
                        ca = v.TurretArmor;
                    break;
                    case VehicleChassisLocations.Left:
                        cs = v.LeftSideStructure;
                        ca = v.LeftSideArmor;
                        break;
                    case VehicleChassisLocations.Right:
                        cs = v.RightSideStructure;
                        ca = v.RightSideArmor;
                        break;
                    case VehicleChassisLocations.Front:
                        cs = v.FrontStructure;
                        ca = v.FrontArmor;
                        break;
                    case VehicleChassisLocations.Rear:
                        cs = v.RearStructure;
                        ca = v.RearArmor;
                        break;
                    default:
                        LogDebug($"Invalid location {location}");
                        break;
                }

                float percentArmor = ca/maxa;
                float percentStructure = cs/maxs;

                //since its easy to kill vehicles once past armor use the armor instead of structure unless structure is damaged.
                //this is reverse of mechs.
                //Remember the vehicle pilot motto - in armor we trust , structure is for the dead and defeated.
                float percentLocation = percentArmor;
                float numAdditions = 2;

                // Use the minimum percentage between structure and armor
                // This emphasizes internal damage from a blow through (back armor gone or tandem weapons)
                percentLocation += Math.Min(percentArmor, percentStructure);
                percentLocation /= numAdditions;
                LogReport($"{location.ToString(),-20} | A:{ca:F3}/{maxa:F3} = {percentArmor * 100,10}% , S:{cs:F3}/{maxs:F3} = {percentStructure * 100,10:F3}%");
                return percentLocation;
            }
            LogDebug($"Vehicle null");
            return 0;
        }

        internal static float PercentRightTorso(Mech mech) =>
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
			(int) ChassisLocations.LeftLeg));

        internal static float PercentRightLeg(Mech mech) =>
            (PercentForLocation(mech, (int) ArmorLocation.RightLeg, 0, 
			(int) ChassisLocations.RightLeg));

        internal static float PercentHead(Mech mech) =>
            (PercentForLocation(mech, (int) ArmorLocation.Head, 0, 
			(int) ChassisLocations.Head));

        internal static float PercentTurret(Vehicle v) =>
            (PercentForLocation(v, VehicleChassisLocations.Turret));

        internal static float PercentLeft(Vehicle v) =>
            (PercentForLocation(v, VehicleChassisLocations.Left));
        internal static float PercentRight(Vehicle v) =>
            (PercentForLocation(v, VehicleChassisLocations.Right));

        internal static float PercentFront(Vehicle v) =>
            (PercentForLocation(v, VehicleChassisLocations.Front));

        internal static float PercentRear(Vehicle v) =>
            (PercentForLocation(v, VehicleChassisLocations.Rear));

        // check if panic roll is possible
        private static bool CanPanic(AbstractActor actor, AbstractActor attacker)
        {
            if (actor == null || actor.IsDead || actor.IsFlaggedForDeath && actor.HasHandledDeath)
            {
                LogReport($"{attacker?.DisplayName} incapacitated {actor?.DisplayName}");
                return false;
            }

            if ((actor.team.IsLocalPlayer && !modSettings.PlayersCanPanic) ||
                (!actor.team.IsLocalPlayer && !modSettings.EnemiesCanPanic) ||
                (actor is Vehicle && !modSettings.VehiclesCanPanic))
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
        public static bool ShouldPanic(AbstractActor actor, AbstractActor attacker, out int heatdamage, out float damageIncludingHeatDamage)
        {
            if (!CanPanic(actor, attacker))
            {
                damageIncludingHeatDamage = 0;
                heatdamage = 0;
                return false;
            }

            return SufficientDamageWasDone(actor, out heatdamage, out damageIncludingHeatDamage);
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
            TurnDamageTracker.DamageDuringTurn(actor, out armorDamage, out structureDamage, out previousArmor, out previousStructure, out heatdamage);

            // used in SavingThrows.cs
            damageIncludingHeatDamage = armorDamage + structureDamage;
#if NO_CAC
            if(true){
#else
            if (!(actor is Mech) || actor.isHasHeat()){//Battle Armor doesn't have heat
#endif

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

            if ((actor is Mech &&
                structureDamage >= modSettings.MinimumMechStructureDamageRequired) ||
                (modSettings.VehiclesCanPanic &&
                actor is Vehicle &&
                structureDamage >= modSettings.MinimumVehicleStructureDamageRequired))
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
