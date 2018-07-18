using BattleTech;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class PanicHelpers
    {
        public static bool IsPanicking(Mech mech, ref bool panicStarted)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
                return false;

            Pilot pilot = mech.GetPilot();

            if (mech != null)
            {
                int i = GetTrackedPilotIndex(mech);
                var weapons = mech.Weapons;

                if (pilot != null && pilot.Health - pilot.Injuries <= PanicSystem.Settings.MinimumHealthToAlwaysEjectRoll && !pilot.LethalInjuries)
                {
                    Logger.Debug($"Panicking due to health.");
                    return true;
                }
                if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional) && PanicSystem.Settings.ConsiderEjectingWithNoWeaps)
                {
                    Logger.Debug($"Panicking due to components being affected.");
                    return true;
                }

                if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID) && PanicSystem.Settings.ConsiderEjectingWhenAlone)
                {
                    Logger.Debug($"Panicking due to being the last alive.");
                    return true;
                }

                if (i > -1)
                {
                    if (Holder.TrackedPilots[i].trackedMech == mech.GUID &&
                        Holder.TrackedPilots[i].pilotStatus == PanicStatus.Panicked)
                    {
                        Logger.Debug($"Panicking due to health.");
                        return true;
                    }

                    if (CanEarlyPanic(mech, i))
                    {
                        Logger.Debug($"In early panic.");
                        panicStarted = true;
                        return true;
                    }
                }
            }
            Logger.Debug($"Not panicking.");
            return false;
        }

        private static bool CanEarlyPanic(Mech mech, int i)
        {
            if (Holder.TrackedPilots[i].trackedMech == mech.GUID)
            {
                if (mech.team.IsLocalPlayer)
                {
                    if (PanicSystem.Settings.PlayerLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= PanicSystem.Settings.LightMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.PlayerMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= PanicSystem.Settings.MediumMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.PlayerHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= PanicSystem.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.PlayerAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= PanicSystem.Settings.AssaultMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }
                }
                else
                {
                    if (PanicSystem.Settings.EnemyLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= PanicSystem.Settings.LightMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.EnemyMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= PanicSystem.Settings.MediumMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.EnemyHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= PanicSystem.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.EnemyAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (Holder.TrackedPilots[i].pilotStatus >= PanicSystem.Settings.AssaultMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }
                }
            }
            Logger.Debug($"Not panicking early.");
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
