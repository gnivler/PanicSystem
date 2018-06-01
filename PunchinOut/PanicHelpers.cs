using BattleTech;
using PunchinOut;

namespace BasicPanic
{
    public static class PanicHelpers
    {
        public static bool IsPanicking(Mech mech, ref bool IsEarlyPanic)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
                return false;

            Pilot pilot = mech.GetPilot();

            if (mech != null)
            {
                int i = GetTrackedPilotIndex(mech);
                var weapons = mech.Weapons;
                if (i > -1)
                {
                    if (Holder.TrackedPilots[i].trackedMech == mech.GUID &&
                        Holder.TrackedPilots[i].pilotStatus == PanicStatus.Panicked)
                    {
                        return true;
                    }
                }

                if (pilot != null && pilot.Health - pilot.Injuries <= BasicPanic.Settings.MinimumHealthToAlwaysEjectRoll && !pilot.LethalInjuries)
                {
                    return true;
                }
                if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional) && BasicPanic.Settings.ConsiderEjectingWithNoWeaps)
                {
                    return true; 
                }

                if

            }
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID))
            {
                return true;
            }

            return false;
        }

        public static int GetTrackedPilotIndex(Mech mech)
        {
            if (mech == null)
            {
                return -1;
            }

            if(Holder.TrackedPilots == null)
            {
                  Holder.DeserializeActiveJson();
            }

            for (int i = 0; i < Holder.TrackedPilots.Count; i++)
            {

                if (Holder.TrackedPilots[i].trackedMech == mech.GUID)
                {
                    return i;
                }
            }

            return -1;
        }

    }
}
