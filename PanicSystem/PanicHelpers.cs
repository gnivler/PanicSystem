using BattleTech;
using Harmony;
using static PanicSystem.PanicSystem;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class PanicHelpers
    {
        public static bool IsLastStrawPanicking(Mech mech, ref bool panicStarted)
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
                    if (TrackedPilots[i].TrackedMech == mech.GUID &&
                        TrackedPilots[i].PilotStatus == PanicStatus.Panicked)
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
            if (TrackedPilots[i].TrackedMech == mech.GUID)
            {
                if (mech.team.IsLocalPlayer)
                {
                    if (PanicSystem.Settings.PlayerLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (TrackedPilots[i].PilotStatus >= PanicSystem.Settings.LightMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.PlayerMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (TrackedPilots[i].PilotStatus >= PanicSystem.Settings.MediumMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.PlayerHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (TrackedPilots[i].PilotStatus >= PanicSystem.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.PlayerAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (TrackedPilots[i].PilotStatus >= PanicSystem.Settings.AssaultMechEarlyPanicThreshold)
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
                        if (TrackedPilots[i].PilotStatus >= PanicSystem.Settings.LightMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.EnemyMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (TrackedPilots[i].PilotStatus >= PanicSystem.Settings.MediumMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.EnemyHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (TrackedPilots[i].PilotStatus >= PanicSystem.Settings.HeavyMechEarlyPanicThreshold)
                        {
                            Logger.Debug($"Panicking early.");
                            return true;
                        }
                    }

                    else if (PanicSystem.Settings.EnemyAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (TrackedPilots[i].PilotStatus >= PanicSystem.Settings.AssaultMechEarlyPanicThreshold)
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

            if (TrackedPilots == null)
            {
                Holder.DeserializeActiveJson();
            }

            for (int i = 0; i < TrackedPilots.Count; i++)
            {

                if (TrackedPilots[i].TrackedMech == mech.GUID)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
