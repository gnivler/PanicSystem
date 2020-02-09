using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using Harmony;
using PanicSystem.Components;
using PanicSystem.Patches;
using static PanicSystem.Logger;
using static PanicSystem.Components.Controller;
using static PanicSystem.PanicSystem;
using Random = UnityEngine.Random;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace PanicSystem
{
    public class Helpers
    {
        // values for combining melee with support weapon fire
        internal static float initialArmorMelee;
        internal static float initialStructureMelee;
        internal static float armorDamageMelee;
        internal static float structureDamageMelee;
        internal static bool hadMeleeAttack;
        internal static float damageIncludingHeatDamage;

        // used in strings
        internal static float ActorHealth(AbstractActor actor) =>
            (actor.SummaryArmorCurrent + actor.SummaryStructureCurrent) /
            (actor.SummaryArmorMax + actor.SummaryStructureMax) * 100;

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
        public static void ApplyPanicDebuff(AbstractActor actor)
        {
            var index = GetActorIndex(actor);
            if (TrackedActors[index].Mech != actor.GUID)
            {
                LogDebug("Pilot and mech mismatch; no status to change");
                return;
            }

            // remove existing panic debuffs first
            int Uid() => Random.Range(1, int.MaxValue);
            var effectManager = UnityGameInstance.BattleTechGame.Combat.EffectManager;
            var effects = Traverse.Create(effectManager).Field("effects").GetValue<List<Effect>>();
            for (var i = 0; i < effects.Count; i++)
            {
                if (effects[i].id.StartsWith("PanicSystem") && Traverse.Create(effects[i]).Field("target").GetValue<object>() == actor)
                {
                    effectManager.CancelEffect(effects[i]);
                }
            }

            if (modSettings.VehiclesCanPanic &&
                actor is Vehicle)
            {
                LogReport($"{actor.DisplayName} condition worsened: Panicked");
                TrackedActors[index].PanicStatus = PanicStatus.Panicked;
                TrackedActors[index].PreventEjection = true;
                effectManager.CreateEffect(StatusEffect.PanickedToHit, "PanicSystemToHit", Uid(), actor, actor, new WeaponHitInfo(), 0);
                effectManager.CreateEffect(StatusEffect.PanickedToBeHit, "PanicSystemToBeHit", Uid(), actor, actor, new WeaponHitInfo(), 0);
            }
            else
            {
                switch (TrackedActors[index].PanicStatus)
                {
                    case PanicStatus.Confident:
                        LogReport($"{actor.DisplayName} condition worsened: Unsettled");
                        TrackedActors[index].PanicStatus = PanicStatus.Unsettled;
                        effectManager.CreateEffect(StatusEffect.UnsettledToHit, "PanicSystemToHit", Uid(), actor, actor, new WeaponHitInfo(), 0);
                        break;
                    case PanicStatus.Unsettled:
                        LogReport($"{actor.DisplayName} condition worsened: Stressed");
                        TrackedActors[index].PanicStatus = PanicStatus.Stressed;
                        effectManager.CreateEffect(StatusEffect.StressedToHit, "PanicSystemToHit", Uid(), actor, actor, new WeaponHitInfo(), 0);
                        effectManager.CreateEffect(StatusEffect.StressedToBeHit, "PanicSystemToBeHit", Uid(), actor, actor, new WeaponHitInfo(), 0);
                        break;
                    default:
                        LogReport($"{actor.DisplayName} condition worsened: Panicked");
                        TrackedActors[index].PanicStatus = PanicStatus.Panicked;
                        effectManager.CreateEffect(StatusEffect.PanickedToHit, "PanicSystemToHit", Uid(), actor, actor, new WeaponHitInfo(), 0);
                        effectManager.CreateEffect(StatusEffect.PanickedToBeHit, "PanicSystemToBeHit", Uid(), actor, actor, new WeaponHitInfo(), 0);
                        break;
                }
            }

            TrackedActors[index].PanicWorsenedRecently = true;
        }

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

        // method is called despite the setting, so it can be controlled in one place
        internal static void SaySpamFloatie(AbstractActor actor, string message)
        {
            if (!modSettings.FloatieSpam) return;
            actor.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                new ShowActorInfoSequence(actor, message, FloatieMessage.MessageNature.Neutral, false)));
        }

        // bool controls whether to display as buff or debuff
        internal static void SayStatusFloatie(AbstractActor actor, bool buff)
        {
            var index = GetActorIndex(actor);

            var floatieString = $"{TrackedActors[index].PanicStatus.ToString()}";
            if (buff)
            {
                actor.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(actor, floatieString, FloatieMessage.MessageNature.Inspiration, true)));
            }

            else
            {
                actor.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(actor, floatieString, FloatieMessage.MessageNature.Debuff, true)));
            }
        }

        // true implies a panic condition was met
        public static bool ShouldPanic(AbstractActor actor, AttackDirector.AttackSequence attackSequence)
        {
            if (modSettings.AlwaysPanic)
            {
                return true;
            }

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

            var armorDamage = attackSequence.GetArmorDamageDealt(id) + armorDamageMelee;
            var structureDamage = attackSequence.GetStructureDamageDealt(id) + structureDamageMelee;
            var heatDamage = Mech_AddExternalHeat_Patch.heatDamage * modSettings.HeatDamageFactor;
            // used in SavingThrows.cs
            damageIncludingHeatDamage = armorDamage + structureDamage + heatDamage;
            var percentDamageDone =
                (damageIncludingHeatDamage) / (previousArmor + previousStructure) * 100;

            // clear melee values
            initialArmorMelee = 0;
            initialStructureMelee = 0;
            armorDamageMelee = 0;
            structureDamageMelee = 0;
            hadMeleeAttack = false;

            // have to check structure here AFTER armor, despite it being the priority, because we need to set the global
            LogReport($"Damage >>> A: {armorDamage:F3} S: {structureDamage:F3} ({percentDamageDone:F2}%) H: {Mech_AddExternalHeat_Patch.heatDamage}");
            if (attackSequence.chosenTarget is Mech &&
                attackSequence.GetStructureDamageDealt(id) >= modSettings.MinimumMechStructureDamageRequired ||
                modSettings.VehiclesCanPanic &&
                attackSequence.chosenTarget is Vehicle &&
                attackSequence.GetStructureDamageDealt(id) >= modSettings.MinimumVehicleStructureDamageRequired)
            {
                LogReport("Structure damage requires panic save");
                return true;
            }

            if (armorDamage + structureDamage + heatDamage <= modSettings.MinimumDamagePercentageRequired)
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
    }
}
