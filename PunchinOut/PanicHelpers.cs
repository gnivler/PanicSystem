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

                if(CanEarlyPanic(mech, i))
                {
                    IsEarlyPanic = true;
                    return true;
                }

            }
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID))
            {
                return true;
            }

            return false;
        }

        private static bool CanEarlyPanic(Mech mech, int i)
        {
            if (Holder.TrackedPilots[i].trackedMech == mech.GUID)
            {
               if(mech.team == mech.Combat.LocalPlayerTeam)
               {
                    if (BasicPanic.Settings.PlayerLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus == BasicPanic.Settings.LightMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }

                    else if(BasicPanic.Settings.PlayerMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus == BasicPanic.Settings.MediumMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }
                    else if (BasicPanic.Settings.PlayerHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus == BasicPanic.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }

                    else if (BasicPanic.Settings.PlayerAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus == BasicPanic.Settings.AssaultMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }
                }
               else
               {
                    if (BasicPanic.Settings.EnemyLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus == BasicPanic.Settings.LightMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }

                    else if (BasicPanic.Settings.EnemyMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus == BasicPanic.Settings.MediumMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }
                    else if (BasicPanic.Settings.EnemyHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus == BasicPanic.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            return true;
                        }
                    }

                    else if (BasicPanic.Settings.EnemyAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus == BasicPanic.Settings.AssaultMechEarlyPanicThreshold)
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
