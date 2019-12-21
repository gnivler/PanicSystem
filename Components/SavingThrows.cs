using System;
using System.Linq;
using BattleTech;
using BattleTech.Save.Core;
using PanicSystem.Patches;
using static PanicSystem.PanicSystem;
using static PanicSystem.Logger;
using static PanicSystem.Components.Controller;
using static PanicSystem.Helpers;
using Random = UnityEngine.Random;

// ReSharper disable ClassNeverInstantiated.Global

namespace PanicSystem.Components
{
    public class SavingThrows
    {
        public static bool SavedVsPanic(AbstractActor actor, float savingThrow)
        {
            try
            {
                AbstractActor defender = null;
                if (modSettings.VehiclesCanPanic &&
                    actor is Vehicle vehicle)
                {
                    defender = vehicle;
                }
                else if (actor is Mech mech)
                {
                    defender = mech;
                }

                if (modSettings.QuirksEnabled)
                {
                    if (defender is Mech m)
                    {
                        if (m.pilot.pilotDef.PilotTags.Contains("pilot_brave"))
                        {
                            savingThrow -= modSettings.BraveModifier;
                            LogReport($"{"Bravery",-20} | {modSettings.BraveModifier,10} | {savingThrow,10:F3}");
                        }
                    }
                }

                var index = GetActorIndex(defender);
                savingThrow *= GetPanicModifier(TrackedActors[index].PanicStatus);
                LogReport($"{"Panic multiplier",-20} | {GetPanicModifier(TrackedActors[index].PanicStatus),10} | {savingThrow,10:F3}");
                savingThrow = (float) Math.Max(0f, Math.Round(savingThrow));

                if (!(savingThrow >= 1))
                {
                    LogReport(new string('-', 46));
                    LogReport("Negative saving throw| skipping");
                    return false;
                }

                var roll = Random.Range(1, 100);
                LogReport(new string('-', 46));
                LogReport($"{"Saving throw",-20} | {savingThrow,-5}{roll,5} | {"Roll",10}");
                LogReport(new string('-', 46));
                SaySpamFloatie(defender, $"{$"SAVE:{savingThrow}",-6} {$"ROLL {roll}!",3}");

                var status = TrackedActors[index].PanicStatus;
                // lower panic level
                if (roll == 100)
                {
                    LogReport("Critical success");
                    LogDebug($"{status} {(int) status}");
                    // don't lower below floor
                    if ((int) status > 0)
                    {
                        status--;
                        TrackedActors[index].PanicStatus = status;
                    }

                    // prevent floatie if already at Confident
                    if ((int) TrackedActors[index].PanicStatus > 0)
                    {
                        defender.Combat.MessageCenter.PublishMessage(
                            new AddSequenceToStackMessage(
                                new ShowActorInfoSequence(defender, "CRIT SUCCESS!", FloatieMessage.MessageNature.Inspiration, false)));
                        SayStatusFloatie(defender, false);
                    }

                    return true;
                }

                // continue if roll wasn't 100
                if (roll >= savingThrow)
                {
                    SaySpamFloatie(defender, "PANIC SAVE!");
                    LogReport("Successful panic save");
                    return true;
                }

                LogReport("Failed panic save");
                SaySpamFloatie(defender, "SAVE FAIL!");
                ApplyPanicDebuff(defender);
                SayStatusFloatie(defender, false);

                status = TrackedActors[index].PanicStatus;
                // check for crit
                if (ActorHealth(defender) <= modSettings.MechHealthForCrit &&
                    (roll < (int) savingThrow - modSettings.CritOver || roll == 1))
                {
                    LogReport("Critical failure on panic save");
                    // record status to see if it changes after

                    TrackedActors[index].PreventEjection = status < PanicStatus.Stressed;
                    TrackedActors[index].PanicStatus = PanicStatus.Panicked;
                    ApplyPanicDebuff(defender);
                    // show both floaties on a panic crit unless panicked already
                    if (status != PanicStatus.Panicked)
                    {
                        SayStatusFloatie(defender, false);
                    }

                    defender.Combat.MessageCenter.PublishMessage(
                        new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(defender, "PANIC LEVEL CRITICAL!", FloatieMessage.MessageNature.CriticalHit, true)));
                }
            }
            catch (Exception ex)
            {
                LogDebug(ex);
            }

            return false;
        }

        public static float GetSavingThrow(AbstractActor defender, AbstractActor attacker)
        {
            var pilot = defender.GetPilot();
            var weapons = defender.Weapons;
            var gutsAndTacticsSum = defender.SkillGuts * modSettings.GutsEjectionResistPerPoint +
                                    defender.SkillTactics * modSettings.TacticsEjectionResistPerPoint;
            float totalMultiplier = 0;

            DrawHeader();
            LogReport($"{$"Unit health {ActorHealth(defender):F2}%",-20} | {"",10} |");

            if (defender is Mech defendingMech)
            {
                try
                {
                    if (modSettings.QuirksEnabled &&
                        attacker is Mech mech &&
                        mech.MechDef.Chassis.ChassisTags.Contains("mech_quirk_distracting"))
                    {
                        totalMultiplier += modSettings.DistractingModifier;
                        LogReport($"{"Distracting mech",-20} | {modSettings.DistractingModifier,10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (modSettings.HeatDamageFactor > 0)
                    {
                        totalMultiplier += modSettings.HeatDamageFactor * Mech_AddExternalHeat_Patch.heatDamage;
                        LogReport($"{$"Heat damage {Mech_AddExternalHeat_Patch.heatDamage}",-20} | {modSettings.HeatDamageFactor * Mech_AddExternalHeat_Patch.heatDamage,10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (PercentPilot(pilot) < 1)
                    {
                        totalMultiplier += modSettings.PilotHealthMaxModifier * PercentPilot(pilot);
                        LogReport($"{"Pilot injuries",-20} | {modSettings.PilotHealthMaxModifier * PercentPilot(pilot),10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (defendingMech.IsUnsteady)
                    {
                        totalMultiplier += modSettings.UnsteadyModifier;
                        LogReport($"{"Unsteady",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:F3}");
                    }

                    if (defendingMech.IsFlaggedForKnockdown)
                    {
                        totalMultiplier += modSettings.UnsteadyModifier;
                        LogReport($"{"Knockdown",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:F3}");
                    }

                    if (modSettings.OverheatedModifier > 0 && defendingMech.OverheatLevel < defendingMech.CurrentHeat)
                    {
                        totalMultiplier += modSettings.OverheatedModifier;
                        LogReport($"{"Heat",-20} | {modSettings.OverheatedModifier,10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (modSettings.ShutdownModifier > 0 && defendingMech.IsShutDown)
                    {
                        totalMultiplier += modSettings.ShutdownModifier;
                        LogReport($"{"Shutdown",-20} | {modSettings.ShutdownModifier,10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (PercentHead(defendingMech) < 1)
                    {
                        totalMultiplier += modSettings.HeadMaxModifier * PercentHead(defendingMech);
                        LogReport($"{"Head",-20} | {modSettings.HeadMaxModifier * PercentHead(defendingMech),10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (PercentCenterTorso(defendingMech) < 1)
                    {
                        totalMultiplier += modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(defendingMech));
                        LogReport($"{"CT",-20} | {modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(defendingMech)),10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (PercentLeftTorso(defendingMech) < 1)
                    {
                        totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(defendingMech));
                        LogReport($"{"LT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(defendingMech)),10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (PercentRightTorso(defendingMech) < 1)
                    {
                        totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(defendingMech));
                        LogReport($"{"RT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(defendingMech)),10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (PercentLeftLeg(defendingMech) < 1)
                    {
                        totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(defendingMech));
                        LogReport($"{"LL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(defendingMech)),10:F3} | {totalMultiplier,10:F3}");
                    }

                    if (PercentRightLeg(defendingMech) < 1)
                    {
                        totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentRightLeg(defendingMech));
                        LogReport($"{"RL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentRightLeg(defendingMech)),10:F3} | {totalMultiplier,10:F3}");
                    }

                    // alone
                    if (defendingMech.Combat.GetAllAlliesOf(defendingMech).TrueForAll(m => m.IsDead || m == defendingMech))
                    {
                        if (Random.Range(1, 5) == 0) // 20% chance of appearing
                        {
                            SaySpamFloatie(defendingMech, "NO ALLIES!");
                        }

                        totalMultiplier += modSettings.AloneModifier;
                        LogReport($"{"Alone",-20} | {modSettings.AloneModifier,10} | {totalMultiplier,10:F3}");
                    }
                }
                catch (Exception ex)
                {
                    // BOMB
                    LogReport(ex);
                    return -1f;
                }
            }

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel != ComponentDamageLevel.Functional || !w.HasAmmo)) // only fully unusable
            {
                if (Random.Range(1, 5) == 1) // 20% chance of appearing
                {
                    SaySpamFloatie(defender, "NO WEAPONS!");
                }

                totalMultiplier += modSettings.WeaponlessModifier;
                LogReport($"{"Weaponless",-20} | {modSettings.WeaponlessModifier,10} | {totalMultiplier,10:F3}");
            }

            // directly override the multiplier for vehicles
            if (modSettings.VehiclesCanPanic &&
                defender is Vehicle)
            {
                // total damage inflicted THIS ATTACK is the saving throw
                totalMultiplier += damageWithHeatDamage;
            }

            var resolveModifier = modSettings.ResolveMaxModifier *
                                  (defender.Combat.LocalPlayerTeam.Morale - modSettings.MedianResolve) / modSettings.MedianResolve;

            if (modSettings.VehiclesCanPanic &&
                defender is Vehicle)
            {
                resolveModifier *= modSettings.VehicleResolveFactor;
            }

            totalMultiplier -= resolveModifier;
            LogReport($"{$"Resolve {defender.Combat.LocalPlayerTeam.Morale}",-20} | {resolveModifier * -1,10:F3} | {totalMultiplier,10:F3}");

            if (modSettings.VehiclesCanPanic &&
                defender is Vehicle)
            {
                gutsAndTacticsSum *= modSettings.VehicleGutAndTacticsFactor;
            }

            totalMultiplier -= gutsAndTacticsSum;

            LogReport($"{"Guts and Tactics",-20} | {$"-{gutsAndTacticsSum}",10} | {totalMultiplier,10:F3}");
            return totalMultiplier;
        }

        // false is punchin' out
        public static bool SavedVsEject(AbstractActor actor, float savingThrow)
        {
            LogReport("Panic save failure requires eject save");

            var pilotTracker = TrackedActors.First(tracker => tracker.Mech == actor.GUID);
            if (pilotTracker.PreventEjection)
            {
                LogReport("Ejection forbidden after crit unless already stressed or panicked");
                pilotTracker.PreventEjection = false;
                return true;
            }

            DrawHeader();

            if (actor is Mech mech && modSettings.QuirksEnabled)
            {
                if (mech.pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
                {
                    savingThrow -= modSettings.DependableModifier;
                    LogReport($"{"Dependable",-20} | {modSettings.DependableModifier,10} | {savingThrow,10:F3}");
                }
            }

            // calculate result
            if (modSettings.VehiclesCanPanic &&
                actor is Vehicle)
            {
                savingThrow = Math.Max(0f, savingThrow - modSettings.BaseVehicleEjectionResist);
                LogReport($"{"Base ejection resist",-20} | {modSettings.BaseVehicleEjectionResist,10} | {savingThrow,10:F3}");
            }
            else if (actor is Mech)
            {
                savingThrow = Math.Max(0f, savingThrow - modSettings.BaseEjectionResist);
                LogReport($"{"Base ejection resist",-20} | {modSettings.BaseEjectionResist,10} | {savingThrow,10:F3}");
            }

            if (modSettings.VehiclesCanPanic &&
                actor is Vehicle)
            {
                savingThrow = damageWithHeatDamage;
            }

            savingThrow = (float) Math.Round(savingThrow);
            LogReport($"{"Eject multiplier",-20} | {modSettings.EjectChanceFactor,10} | {savingThrow,10:F3}");
            var roll = Random.Range(1, 100);
            LogReport(new string('-', 46));
            LogReport($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            LogReport(new string('-', 46));
            if (savingThrow <= 0)
            {
                LogReport("Negative saving throw| skipping");
                SaySpamFloatie(actor, "EJECT RESIST!");
                return true;
            }

            // cap the saving throw by the setting
            savingThrow = (int) Math.Min(savingThrow, modSettings.MaxEjectChance);

            SaySpamFloatie(actor, $"SAVE: {savingThrow}  ROLL: {roll}!");
            if (roll >= savingThrow)
            {
                LogReport("Successful ejection save");
                SaySpamFloatie(actor, $"EJECT SAVE! HEALTH: {ActorHealth(actor):#.#}%");
                return true;
            }

            // TODO can it be written if (mech != null) ? I don't know and testing it is a PITA!
            if (actor is Mech m)
            {
                if (modSettings.QuirksEnabled && m.MechDef.Chassis.ChassisTags.Contains("mech_quirk_noeject"))
                {
                    LogReport("This mech can't eject (quirk)");
                    actor.Combat.MessageCenter.PublishMessage(
                        new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(actor, "Mech quirk: Can't eject", FloatieMessage.MessageNature.PilotInjury, true)));
                    return true;
                }

                if (modSettings.QuirksEnabled && m.pilot.pilotDef.PilotTags.Contains("pilot_drunk") &&
                    m.pilot.pilotDef.TimeoutRemaining > 0)
                {
                    LogReport("Drunkard - not ejecting");
                    actor.Combat.MessageCenter.PublishMessage(
                        new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(actor, "Pilot quirk: Drunkard won't eject", FloatieMessage.MessageNature.PilotInjury, true)));
                    return true;
                }
            }

            LogReport("Failed ejection save: Punchin\' Out!!");
            return false;
        }
    }
}
