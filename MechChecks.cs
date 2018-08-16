using System.Linq;
using BattleTech;
using static PanicSystem.PanicSystem;
using static PanicSystem.Logger;

namespace PanicSystem
{
    public class MechChecks
    {
        /// <summary>
        ///     returns the sum of all enemy armour and structure
        /// </summary>
        /// <param name="mech"></param>
        /// <returns></returns>
        public static float GetAllEnemiesHealth(Mech mech)
        {
            var enemies = mech.Combat.GetAllEnemiesOf(mech);
            var enemiesHealth = 0f;

            enemiesHealth += enemies.Select(e => e.SummaryArmorCurrent + e.SummaryStructureCurrent).Sum();
            return enemiesHealth;
        }

        internal static void EvalLeftTorso(Mech mech, ref float modifiers)
        {
            if (mech.LeftTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                modifiers += ModSettings.SideTorsoMaxModifier;
                Debug($"{"LT destroyed",-20} | {ModSettings.SideTorsoMaxModifier,10:#.###} | {modifiers,10:#.###}");
            }
            else if (PercentLeftTorso(mech) < 1)
            {
                modifiers += ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech);
                Debug($"{"LT",-20} | {ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech),10:#.###} | {modifiers,10:#.###}");
            }
        }

        internal static void EvalRightTorso(Mech mech, ref float modifiers)
        {
            if (mech.RightTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                modifiers += ModSettings.SideTorsoMaxModifier;
                Debug($"{"RT destroyed",-20} | {ModSettings.SideTorsoMaxModifier,10:#.###} | {modifiers,10:#.###}");
            }
            else
            {
                modifiers += ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech);
                Debug($"{"RT",-20} | {ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech),10:#.###} | {modifiers,10:#.###}");
            }
        }

        internal static void EvalLeftLeg(Mech mech, ref float modifiers)
        {
            if (mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                modifiers += ModSettings.LeggedMaxModifier;
                Debug($"{"LL destroyed",-20} | {ModSettings.LeggedMaxModifier,10:#.###} | {modifiers,10:#.###}");
            }
            else
            {
                modifiers += ModSettings.LeggedMaxModifier * PercentLeftLeg(mech);
                Debug($"{"LL",-20} | {ModSettings.LeggedMaxModifier * PercentLeftLeg(mech),10:#.###} | {modifiers,10:#.###}");
            }
        }

        internal static void EvalRightLeg(Mech mech, ref float modifiers)
        {
            if (mech.RightLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                modifiers += ModSettings.LeggedMaxModifier;
                Debug($"{"RL destroyed",-20} | {ModSettings.LeggedMaxModifier,10:#.###} | {modifiers,10:#.###}");
            }
            else
            {
                modifiers += ModSettings.LeggedMaxModifier * PercentRightLeg(mech);
                Debug($"{"RL",-20} | {ModSettings.LeggedMaxModifier,10:#.###} | {modifiers,10:#.###}");
            }
        }

        // produces an integer percentage (20% instead of 0.20)
        public static float MechHealth(Mech mech) =>
            (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) /
            (mech.SummaryArmorMax + mech.SummaryStructureMax) * 100;

        // these methods all produce straight percentages
        internal static float PercentPilot(Pilot pilot) => 1 - (float) pilot.Injuries / pilot.Health;

        internal static float PercentRightTorso(Mech mech)
        {
            return 1 -
                   (mech.RightTorsoStructure + mech.RightTorsoFrontArmor + mech.RightTorsoRearArmor) /
                   (mech.MaxStructureForLocation((int) ChassisLocations.RightTorso) + mech.MaxArmorForLocation((int) ArmorLocation.RightTorso) + mech.MaxArmorForLocation((int) ArmorLocation.RightTorsoRear));
        }

        internal static float PercentLeftTorso(Mech mech)
        {
            return 1 -
                   (mech.LeftTorsoStructure + mech.LeftTorsoFrontArmor + mech.LeftTorsoRearArmor) /
                   (mech.MaxStructureForLocation((int) ChassisLocations.LeftTorso) + mech.MaxArmorForLocation((int) ArmorLocation.LeftTorso) + mech.MaxArmorForLocation((int) ArmorLocation.LeftTorsoRear));
        }

        internal static float PercentCenterTorso(Mech mech)
        {
            return 1 -
                   (mech.CenterTorsoStructure + mech.CenterTorsoFrontArmor + mech.CenterTorsoRearArmor) /
                   (mech.MaxStructureForLocation((int) ChassisLocations.CenterTorso) + mech.MaxArmorForLocation((int) ArmorLocation.CenterTorso) + mech.MaxArmorForLocation((int) ArmorLocation.CenterTorsoRear));
        }

        internal static float PercentLeftLeg(Mech mech)
        {
            return 1 - (mech.LeftLegStructure + mech.LeftLegArmor) / (mech.MaxStructureForLocation((int) ChassisLocations.LeftLeg) + mech.MaxArmorForLocation((int) ArmorLocation.LeftLeg));
        }

        internal static float PercentRightLeg(Mech mech)
        {
            return 1 - (mech.RightLegStructure + mech.RightLegArmor) / (mech.MaxStructureForLocation((int) ChassisLocations.RightLeg) + mech.MaxArmorForLocation((int) ArmorLocation.RightLeg));
        }

        internal static float PercentHead(Mech mech)
        {
            return 1 - (mech.HeadStructure + mech.HeadArmor) / (mech.MaxStructureForLocation((int) ChassisLocations.Head) + mech.MaxArmorForLocation((int) ArmorLocation.Head));
        }
    }
}