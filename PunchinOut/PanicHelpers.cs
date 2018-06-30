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

                if (pilot != null && pilot.Health - pilot.Injuries <= BasicPanic.Settings.MinimumHealthToAlwaysEjectRoll && !pilot.LethalInjuries)
                {
                    Logging.Debug($"Panicking due to health.");
                    return true;
                }
                if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional) && BasicPanic.Settings.ConsiderEjectingWithNoWeaps)
                {
                    Logging.Debug($"Panicking due to components being affected.");
                    return true;
                }

                if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID))
                {
                    Logging.Debug($"Panicking due to being the last alive.");
                    return true;
                }

                if (i > -1)
                {
                    if (Holder.TrackedPilots[i].trackedMech == mech.GUID &&
                        Holder.TrackedPilots[i].pilotStatus == PanicStatus.Panicked)
                    {
                        Logging.Debug($"Panicking due to health.");
                        return true;
                    }

                    if (CanEarlyPanic(mech, i))
                    {
                        Logging.Debug($"In early panic.");
                        IsEarlyPanic = true;
                        return true;
                    }
                }

            }
            Logging.Debug($"Not panicking.");
            return false;
        }

        private static bool CanEarlyPanic(Mech mech, int i)
        {
            if (Holder.TrackedPilots[i].trackedMech == mech.GUID)
            {
                if (mech.team.IsLocalPlayer)
                {
                    if (BasicPanic.Settings.PlayerLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= BasicPanic.Settings.LightMechEarlyPanicThreshold)
                        {
                            Logging.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (BasicPanic.Settings.PlayerMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= BasicPanic.Settings.MediumMechEarlyPanicThreshold)
                        {
                            Logging.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (BasicPanic.Settings.PlayerHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= BasicPanic.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            Logging.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (BasicPanic.Settings.PlayerAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= BasicPanic.Settings.AssaultMechEarlyPanicThreshold)
                        {
                            Logging.Debug($"Panicking early.");
                            return true;
                        }
                    }
                }
                else
                {
                    if (BasicPanic.Settings.EnemyLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= BasicPanic.Settings.LightMechEarlyPanicThreshold)
                        {
                            Logging.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (BasicPanic.Settings.EnemyMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= BasicPanic.Settings.MediumMechEarlyPanicThreshold)
                        {
                            Logging.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (BasicPanic.Settings.EnemyHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= BasicPanic.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            Logging.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (BasicPanic.Settings.EnemyAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= BasicPanic.Settings.AssaultMechEarlyPanicThreshold)
                        {
                            Logging.Debug($"Panicking early.");
                            return true;
                        }
                    }
                }
            }
            Logging.Debug($"Not panicking early.");
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
