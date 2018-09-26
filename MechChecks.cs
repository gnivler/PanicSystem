using System.Linq;
using BattleTech;

namespace PanicSystem
{
    public class MechChecks
    {
        // used in strings
        public static float MechHealth(Mech mech) =>
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
    }
}