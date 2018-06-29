using System;
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

                if (pilot != null && pilot.Health - pilot.Injuries <= Logger.Settings.MinimumHealthToAlwaysEjectRoll && !pilot.LethalInjuries)
                {
                    return true;
                }
                if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional) && Logger.Settings.ConsiderEjectingWithNoWeaps)
                {
                    return true;
                }

                if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID))
                {
                    return true;
                }

                if (i > -1)
                {
                    if (Holder.TrackedPilots[i].trackedMech == mech.GUID &&
                        Holder.TrackedPilots[i].pilotStatus == PanicStatus.Panicked)
                    {
                        return true;
                    }


                    if (CanEarlyPanic(mech, i))
                    {
                        IsEarlyPanic = true;
                        return true;
                    }
                }

            }

            return false;
        }

        private static bool CanEarlyPanic(Mech mech, int i)
        {
            if (Holder.TrackedPilots[i].trackedMech == mech.GUID)
            {
                if (mech.team == mech.Combat.LocalPlayerTeam)
                {
                    if (Logger.Settings.PlayerLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= Logger.Settings.LightMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }

                    else if (Logger.Settings.PlayerMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= Logger.Settings.MediumMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }
                    else if (Logger.Settings.PlayerHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= Logger.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }

                    else if (Logger.Settings.PlayerAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= Logger.Settings.AssaultMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    if (Logger.Settings.EnemyLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= Logger.Settings.LightMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }

                    else if (Logger.Settings.EnemyMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= Logger.Settings.MediumMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }
                    else if (Logger.Settings.EnemyHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= Logger.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }

                    else if (Logger.Settings.EnemyAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= Logger.Settings.AssaultMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }
                }

            }

            return false;
        }

        public static int GetTrackedPilotIndex(Mech mech)
        {
            if (mech == null)
            {
                return -1;
            }

            if (Holder.TrackedPilots == null)
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
