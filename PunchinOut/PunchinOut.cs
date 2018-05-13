using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BattleTech;
using Harmony;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace PunchinOut
{
    [UsedImplicitly]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
    public static class AttackStackSequence_OnAttackComplete_Patch
    {
        public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
        {
            var attackCompleteMessage = message as AttackCompleteMessage;

            if (attackCompleteMessage  == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
                return;

            if ((__instance.directorSequences[0].target is Mech mech) && PunchinOut.RollForEjectionResult(mech, attackCompleteMessage.attackSequence))
            {
                mech.EjectPilot(mech.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
            }
        }
    }

    internal class ModSettings
    {
        public bool PlayerCharacterAlwaysResists = true;
        public bool GutsTenAlwaysResists = true;
        public bool KnockedDownCannotEject = true;

        public float MaxEjectChance = 50;

        public float BaseEjectionResist = 10;
        public float GutsEjectionResistPerPoint = 2;

        public float UnsteadyModifier = 3;
        public float PilotHealthMaxModifier = 5;

        public float HeadDamageMaxModifier = 5;
        public float CTDamageMaxModifier = 10;
        public float SideTorsoInternalDamageMaxModifier = 5;
        public float LeggedMaxModifier = 10;

        public float NextShotLikeThatCouldKill = 10;
        
        public float WeaponlessModifier = 10;
        public float AloneModifier = 10;
    }

    public static class PunchinOut
    {
        internal static ModSettings Settings;

        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("io.github.mpstark.PunchinOut");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            try
            {
                Settings = JsonConvert.DeserializeObject<ModSettings>(modSettings);
            }
            catch (Exception)
            {
                Settings = new ModSettings();
            }
        }
        
        public static bool RollForEjectionResult(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && !mech.HasHandledDeath))
                return false;

            // knocked down mechs cannot eject
            if (mech.IsProne && Settings.KnockedDownCannotEject)
                return false;

            // have to do damage
            if (!attackSequence.attackDidDamage)
                return false;

            var pilot = mech.GetPilot();
            var weapons = mech.Weapons;
            var guts = mech.SkillGuts;

            float lowestRemaining = mech.CenterTorsoStructure + mech.CenterTorsoFrontArmor;
            float ejectModifiers = 0;
            
            // guts 10 makes you immune, player character cannot be forced to eject
            if ((guts >= 10 && Settings.GutsTenAlwaysResists) || (pilot != null && pilot.IsPlayerCharacter && Settings.PlayerCharacterAlwaysResists))
                return false;

            // pilots that cannot eject or be headshot shouldn't eject
            if (!mech.CanBeHeadShot || (pilot != null && !pilot.CanEject))
                return false;

            // pilot health
            if (pilot != null)
            {
                var pilotHealthPercent = 1 - (pilot.Injuries / pilot.Health);

                if (pilotHealthPercent < 1)
                {
                    ejectModifiers += Settings.PilotHealthMaxModifier * (1 - pilotHealthPercent);
                }
            }

            if (mech.IsUnsteady)
            {
                ejectModifiers += Settings.UnsteadyModifier;
            }

            // Head
            var headHealthPercent = (mech.HeadArmor + mech.HeadStructure) / (mech.GetMaxArmor(ArmorLocation.Head) + mech.GetMaxStructure(ChassisLocations.Head));
            if (headHealthPercent < 1)
            {
                ejectModifiers += Settings.HeadDamageMaxModifier * (1 - headHealthPercent);
            }

            // CT
            var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure) / (mech.GetMaxArmor(ArmorLocation.CenterTorso) + mech.GetMaxStructure(ChassisLocations.CenterTorso));
            if (ctPercent < 1)
            {
                ejectModifiers += Settings.CTDamageMaxModifier * (1 - ctPercent);
                lowestRemaining = Math.Min(mech.CenterTorsoStructure, lowestRemaining);
            }

            // side torsos
            var ltStructurePercent = mech.LeftTorsoStructure / mech.GetMaxStructure(ChassisLocations.LeftTorso);
            if (ltStructurePercent < 1)
            {
                ejectModifiers += Settings.SideTorsoInternalDamageMaxModifier * (1 - ltStructurePercent);
            }

            var rtStructurePercent = mech.RightTorsoStructure / mech.GetMaxStructure(ChassisLocations.RightTorso);
            if (rtStructurePercent < 1)
            {
                ejectModifiers += Settings.SideTorsoInternalDamageMaxModifier * (1 - rtStructurePercent);
            }
            
            // legs
            if (mech.RightLegDamageLevel == LocationDamageLevel.Destroyed || mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                float legPercent;

                if (mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
                {
                    legPercent = (mech.RightLegStructure + mech.RightLegArmor) / (mech.GetMaxStructure(ChassisLocations.RightLeg) + mech.GetMaxArmor(ArmorLocation.RightLeg));
                }
                else
                {
                    legPercent = (mech.LeftLegStructure + mech.LeftLegArmor) / (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
                }

                if (legPercent < 1)
                {
                    lowestRemaining = Math.Min(legPercent * (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg)), lowestRemaining);
                    ejectModifiers += Settings.LeggedMaxModifier * (1 - legPercent);
                }
            }

            // next shot could kill
            if (lowestRemaining <= attackSequence.cumulativeDamage)
            {
                ejectModifiers += Settings.NextShotLikeThatCouldKill;
            }
            
            // weaponless
            if (weapons.TrueForAll(w =>
                w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional))
            {
                ejectModifiers += Settings.WeaponlessModifier;
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech))
            {
                ejectModifiers += Settings.AloneModifier;
            }

            var modifiers = (ejectModifiers - Settings.BaseEjectionResist - Settings.GutsEjectionResistPerPoint * guts) * 5;

            if (modifiers < 0)
                return false;
            
            var rng = (new Random()).Next(100);
            var rollToBeat = Math.Min(modifiers, Settings.MaxEjectChance);

            mech.Combat.MessageCenter.PublishMessage(!(rng < rollToBeat)
                ? new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Guts Check Passed {Math.Floor(rollToBeat)}%", FloatieMessage.MessageNature.Buff, true))
                : new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Punchin' Out! {Math.Floor(rollToBeat)}%", FloatieMessage.MessageNature.Debuff, true)));

            return rng < rollToBeat;
        }
    }
}
