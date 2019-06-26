using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BattleTech;
using Harmony;
using Newtonsoft.Json;
using static PanicSystem.Controller;
using static PanicSystem.Logger;
using static PanicSystem.MechChecks;
using Random = UnityEngine.Random;

// ReSharper disable InconsistentNaming

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class PanicSystem
    {
        internal static Settings modSettings = new Settings();
        internal static string activeJsonPath; //store current tracker here
        internal static string storageJsonPath; //store our meta trackers here
        internal static string modDirectory;
        internal static List<string> ejectPhraseList = new List<string>();
        private static HarmonyInstance harmony;

        public static void Init(string modDir, string settings)
        {
            harmony = HarmonyInstance.Create("com.BattleTech.PanicSystem");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            modDirectory = modDir;
            activeJsonPath = Path.Combine(modDir, "PanicSystem.json");
            storageJsonPath = Path.Combine(modDir, "PanicSystemStorage.json");
            try
            {
                modSettings = JsonConvert.DeserializeObject<Settings>(settings);
                if (modSettings.Debug)
                {
                    LogClear();
                }
            }
            catch (Exception e)
            {
                LogError(e);
                modSettings = new Settings();
            }

            if (!modSettings.EnableEjectPhrases)
            {
                return;
            }

            if (!modSettings.EnableEjectPhrases) return;

            try
            {
                ejectPhraseList = File.ReadAllText(Path.Combine(modDir, "phrases.txt")).Split('\n').ToList();
            }
            catch (Exception e)
            {
                LogDebug("Error - problem loading phrases.txt but the setting is enabled");
                LogError(e);
                // in case the file is missing but the setting is enabled
                modSettings.EnableEjectPhrases = false;
            }
        }

        /// <summary>
        /// applies combat modifiers to tracked mechs based on panic status
        /// </summary>
        public static void ApplyPanicDebuff(Mech mech)
        {
            var index = GetPilotIndex(mech);
            if (trackedPilots[index].mech != mech.GUID)
            {
                LogDebug("Pilot and mech mismatch; no status to change");
                return;
            }

            int Uid() => Random.Range(1, int.MaxValue);
            var effectManager = UnityGameInstance.BattleTechGame.Combat.EffectManager;
            var effects = Traverse.Create(effectManager).Field("effects").GetValue<List<Effect>>();
            for (var i = 0; i < effects.Count; i++)
            {
                if (effects[i].id.StartsWith("PanicSystem") && Traverse.Create(effects[i]).Field("target").GetValue<object>() == mech)
                {
                    effectManager.CancelEffect(effects[i]);
                }
            }

            switch (trackedPilots[index].panicStatus)
            {
                case PanicStatus.Confident:
                    LogDebug($"{mech.DisplayName} condition worsened: Unsettled");
                    trackedPilots[index].panicStatus = PanicStatus.Unsettled;
                    effectManager.CreateEffect(StatusEffect.UnsettledToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    break;
                case PanicStatus.Unsettled:
                    LogDebug($"{mech.DisplayName} condition worsened: Stressed");
                    trackedPilots[index].panicStatus = PanicStatus.Stressed;
                    effectManager.CreateEffect(StatusEffect.StressedToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    effectManager.CreateEffect(StatusEffect.StressedToBeHit, "PanicSystemToBeHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    break;
                default:
                    LogDebug($"{mech.DisplayName} condition worsened: Panicked");
                    trackedPilots[index].panicStatus = PanicStatus.Panicked;
                    effectManager.CreateEffect(StatusEffect.PanickedToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    effectManager.CreateEffect(StatusEffect.PanickedToBeHit, "PanicSystemToBeHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                    break;
            }

            trackedPilots[index].panicWorsenedRecently = true;
        }

        /// <summary>
        ///     Checks to see if panic roll is possible
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        private static bool CanPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
            {
                LogDebug($"{attackSequence?.attacker?.DisplayName} incapacitated {mech?.DisplayName}");
                return false;
            }

            if (attackSequence == null ||
                mech.team.IsLocalPlayer && !modSettings.PlayersCanPanic ||
                !mech.team.IsLocalPlayer && !modSettings.EnemiesCanPanic)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns a float to modify panic roll difficulty based on existing panic level
        /// </summary>
        private static float GetPanicModifier(PanicStatus pilotStatus)
        {
            switch (pilotStatus)
            {
                case PanicStatus.Unsettled:
                {
                    return modSettings.UnsettledPanicModifier;
                }

                case PanicStatus.Stressed:
                {
                    return modSettings.StressedPanicModifier;
                }

                case PanicStatus.Panicked:
                {
                    return modSettings.PanickedPanicModifier;
                }

                default:
                    return 1f;
            }
        }

        /// <summary>
        /// true is a successful saving throw
        /// </summary>
        public static float GetSavingThrow(Mech defender, Mech attacker)
        {
            var pilot = defender.GetPilot();
            var weapons = defender.Weapons;
            var gutsAndTacticsSum = defender.SkillGuts * modSettings.GutsEjectionResistPerPoint +
                                    defender.SkillTactics * modSettings.TacticsEjectionResistPerPoint;
            float totalMultiplier = 0;

            DrawHeader();

            LogDebug($"{$"Mech health {MechHealth(defender):#.##}%",-20} | {"",10} |");

            if (attacker != null && modSettings.QuirksEnabled && attacker.MechDef.MechTags.Contains("mech_quirk_distracting"))
            {
                totalMultiplier += modSettings.DistractingModifier;
            }

            if (PercentPilot(pilot) < 1)
            {
                totalMultiplier += modSettings.PilotHealthMaxModifier * PercentPilot(pilot);
                LogDebug($"{"Pilot injuries",-20} | {modSettings.PilotHealthMaxModifier * PercentPilot(pilot),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (defender.IsUnsteady)
            {
                totalMultiplier += modSettings.UnsteadyModifier;
                LogDebug($"{"Unsteady",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
            }

            if (defender.IsFlaggedForKnockdown)
            {
                totalMultiplier += modSettings.UnsteadyModifier;
                LogDebug($"{"Knockdown",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
            }

            if (PercentHead(defender) < 1)
            {
                totalMultiplier += modSettings.HeadMaxModifier * PercentHead(defender);
                LogDebug($"{"Head",-20} | {modSettings.HeadMaxModifier * PercentHead(defender),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentCenterTorso(defender) < 1)
            {
                totalMultiplier += modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(defender));
                LogDebug($"{"CT",-20} | {modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(defender)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentLeftTorso(defender) < 1)
            {
                totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(defender));
                LogDebug($"{"LT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(defender)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentRightTorso(defender) < 1)
            {
                totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(defender));
                LogDebug($"{"RT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(defender)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentLeftLeg(defender) < 1)
            {
                totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(defender));
                LogDebug($"{"LL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(defender)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentRightLeg(defender) < 1)
            {
                totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentRightLeg(defender));
                LogDebug($"{"RL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentRightLeg(defender)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel != ComponentDamageLevel.Functional || !w.HasAmmo)) // only fully unusable
            {
                if (Random.Range(1, 5) == 0) // 20% chance of appearing
                {
                    SaySpamFloatie(defender, "NO WEAPONS!");
                }

                totalMultiplier += modSettings.WeaponlessModifier;
                LogDebug($"{"Weaponless",-20} | {modSettings.WeaponlessModifier,10} | {totalMultiplier,10:#.###}");
            }

            // alone
            if (defender.Combat.GetAllAlliesOf(defender).TrueForAll(m => m.IsDead || m == defender as AbstractActor))
            {
                if (Random.Range(1, 5) == 0) // 20% chance of appearing
                {
                    SaySpamFloatie(defender, "NO ALLIES!");
                }

                totalMultiplier += modSettings.AloneModifier;
                LogDebug($"{"Alone",-20} | {modSettings.AloneModifier,10} | {totalMultiplier,10:#.###}");
            }

            totalMultiplier -= gutsAndTacticsSum;
            LogDebug($"{"Guts and Tactics",-20} | {$"-{gutsAndTacticsSum}",10} | {totalMultiplier,10:#.###}");

            var resolveModifier = modSettings.ResolveMaxModifier *
                                  (defender.Combat.LocalPlayerTeam.Morale - modSettings.MedianResolve) /
                                  modSettings.MedianResolve;
            totalMultiplier -= resolveModifier;
            LogDebug($"{$"Resolve {defender.Combat.LocalPlayerTeam.Morale}",-20} | {resolveModifier * -1,10:#.###} | {totalMultiplier,10:#.###}");

            return totalMultiplier;
        }

        private static void DrawHeader()
        {
            LogDebug(new string('-', 46));
            LogDebug($"{"Factors",-20} | {"Change",10} | {"Total",10}");
            LogDebug(new string('-', 46));
        }

        public static bool SavedVsEject(Mech mech, float savingThrow)
        {
            LogDebug("Panic save failure requires eject save");
            DrawHeader();

            if (modSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_drunk") &&
                mech.pilot.pilotDef.TimeoutRemaining > 0)
            {
                LogDebug("Drunkard - not ejecting");
                mech.Combat.MessageCenter.PublishMessage(
                    new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(mech, "..HIC!  I ain't.. ejettin'", FloatieMessage.MessageNature.PilotInjury, true)));
                return false;
            }

            if (modSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
            {
                savingThrow -= modSettings.DependableModifier;
                LogDebug($"{"Dependable",-20} | {modSettings.DependableModifier,10} | {savingThrow,10:#.###}");
            }

            // calculate result
            savingThrow = Math.Max(0f, savingThrow - modSettings.BaseEjectionResist);
            LogDebug($"{"Base ejection resist",-20} | {modSettings.BaseEjectionResist,10} | {savingThrow,10:#.###}");

            savingThrow = (float) Math.Round(savingThrow);
            LogDebug($"{"Eject multiplier",-20} | {modSettings.EjectChanceMultiplier,10} | {savingThrow,10:#.###}");
            var roll = Random.Range(1, 100);

            LogDebug(new string('-', 46));
            LogDebug($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            LogDebug(new string('-', 46));

            if (savingThrow <= 0)
            {
                LogDebug("Negative saving throw; skipping");
                SaySpamFloatie(mech, "EJECT RESIST!");
                return true;
            }

            // cap the saving throw by the setting
            savingThrow = (int) Math.Min(savingThrow, modSettings.MaxEjectChance);

            SaySpamFloatie(mech, $"SAVE: {savingThrow}  ROLL: {roll}!");
            if (roll >= savingThrow)
            {
                LogDebug("Successful ejection save");
                SaySpamFloatie(mech, $"EJECT SAVE! HEALTH: {MechHealth(mech):#.#}%");
                return true;
            }

            LogDebug("Failed ejection save: Punchin\' Out!!");
            return false;
        }

        public static bool SavedVsPanic(Mech defender, float savingThrow)
        {
            if (modSettings.QuirksEnabled && defender.pilot.pilotDef.PilotTags.Contains("pilot_brave"))
            {
                savingThrow -= modSettings.BraveModifier;
                LogDebug($"{"Bravery",-20} | {modSettings.BraveModifier,10} | {savingThrow,10:#.###}");
            }

            var index = GetPilotIndex(defender);
            savingThrow *= GetPanicModifier(trackedPilots[GetPilotIndex(defender)].panicStatus);
            LogDebug($"{"Panic multiplier",-20} | {GetPanicModifier(trackedPilots[GetPilotIndex(defender)].panicStatus),10} | {savingThrow,10:#.###}");

            savingThrow = (float) Math.Max(0f, Math.Round(savingThrow));
            if (!(savingThrow >= 1))
            {
                LogDebug("Negative saving throw; skipping");
                return false;
            }

            var roll = Random.Range(1, 100);

            LogDebug(new string('-', 46));
            LogDebug($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            LogDebug(new string('-', 46));

            SaySpamFloatie(defender, $"{$"SAVE:{savingThrow}",-6} {$"ROLL {roll}!",3}");

            // lower panic level
            if (roll == 100)
            {
                LogDebug("Critical success");
                var status = trackedPilots[index].panicStatus;

                LogDebug($"{status} {(int) status}");
                // don't lower below floor
                if ((int) status > 0)
                {
                    status--;
                    trackedPilots[index].panicStatus = status;
                }

                // prevent floatie if already at Confident
                if ((int) trackedPilots[index].panicStatus > 0)
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
                LogDebug("Successful panic save");
                return true;
            }

            LogDebug("Failed panic save");
            SaySpamFloatie(defender, "SAVE FAIL!");
            ApplyPanicDebuff(defender);
            SayStatusFloatie(defender, false);

            // check for crit
            if (MechHealth(defender) <= modSettings.MechHealthForCrit &&
                (roll < (int) savingThrow - modSettings.CritOver || roll == 1))
            {
                LogDebug("Critical failure on panic save");
                // record status to see if it changes after
                var status = trackedPilots[index].panicStatus;
                trackedPilots[index].panicStatus = PanicStatus.Panicked;
                defender.Combat.MessageCenter.PublishMessage(
                    new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(defender, "PANIC LEVEL CRITICAL!", FloatieMessage.MessageNature.CriticalHit, true)));

                // show both floaties on a panic crit unless panicked already
                if (status != trackedPilots[index].panicStatus)
                {
                    SayStatusFloatie(defender, false);
                }
            }

            return false;
        }

        // method is called despite the setting, so it can be controlled in one place
        private static void SaySpamFloatie(Mech mech, string message)
        {
            if (!modSettings.FloatieSpam) return;

            mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                new ShowActorInfoSequence(mech, message, FloatieMessage.MessageNature.Neutral, false)));
        }

        // bool controls whether to display as buff or debuff
        private static void SayStatusFloatie(Mech mech, bool buff)
        {
            var index = GetPilotIndex(mech);
            var floatieString = $"{trackedPilots[index].panicStatus.ToString()}";
            if (buff)
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, floatieString, FloatieMessage.MessageNature.Inspiration, true)));
            }
            else
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, floatieString, FloatieMessage.MessageNature.Debuff, true)));
            }
        }

        /// <summary>
        /// true implies a panic condition was met
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        public static bool ShouldPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (!CanPanic(mech, attackSequence)) return false;

            return SufficientDamageWasDone(attackSequence);
        }

        public static bool SkipProcessingAttack(AttackStackSequence __instance, MessageCenterMessage message)
        {
            var attackCompleteMessage = message as AttackCompleteMessage;
            if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID) return true;

            // can't do stuff with vehicles and buildings
            if (!(__instance.directorSequences[0].chosenTarget is Mech)) return true;

            return __instance.directorSequences[0].chosenTarget?.GUID == null;
        }

        /// <summary>
        ///     returns true if 10% armor damage was incurred or any structure damage
        /// </summary>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        private static bool SufficientDamageWasDone(AttackDirector.AttackSequence attackSequence)
        {
            if (attackSequence == null) return false;

            var id = attackSequence.chosenTarget.GUID;

            if (!attackSequence.GetAttackDidDamage(id))
                //if (!attackSequence.attackDidDamage)
            {
                LogDebug("No damage");
                return false;
            }

            var previousArmor = Patches.mechArmorBeforeAttack;
            var previousStructure = Patches.mechStructureBeforeAttack;

            LogDebug($"Damage >>> A: {attackSequence.GetArmorDamageDealt(id)} S: {attackSequence.GetStructureDamageDealt(id)} ({(attackSequence.GetArmorDamageDealt(id) + attackSequence.GetStructureDamageDealt(id)) / (previousArmor + previousStructure) * 100:#.##}%)");
            //LogDebug($"Damage >>> A: {attackSequence.attackArmorDamage} S: {attackSequence.attackStructureDamage} ({(attackSequence.attackArmorDamage + attackSequence.attackStructureDamage) / (previousArmor + previousStructure) * 100:#.##}%)");

            if (attackSequence.GetStructureDamageDealt(id) >= modSettings.MinimumStructureDamageRequired)
            {
                LogDebug("Structure damage requires panic save");
                return true;
            }

            if ((attackSequence.GetArmorDamageDealt(id) + attackSequence.GetStructureDamageDealt(id)) /
                (previousArmor + previousStructure) *
                100 <= modSettings.MinimumDamagePercentageRequired)
            {
                LogDebug("Not enough damage");
                return false;
            }

            LogDebug("Total damage requires a panic save");
            return true;
        }

        public class Settings
        {
            public bool Debug = false;
            public bool EnableEjectPhrases;
            public bool FloatieSpam = false;
            public float EjectPhraseChance = 100;
            public bool ColorizeFloaties = true;

            // panic
            public bool PlayersCanPanic = true;
            public bool EnemiesCanPanic = true;
            public float MinimumDamagePercentageRequired = 10;
            public float MinimumStructureDamageRequired = 5;
            public bool OneChangePerTurn = false;
            public bool LosingLimbAlwaysPanics = false;
            public float UnsteadyModifier = 10;
            public float PilotHealthMaxModifier = 15;
            public float HeadMaxModifier = 15;
            public float CenterTorsoMaxModifier = 45;
            public float SideTorsoMaxModifier = 20;
            public float LeggedMaxModifier = 10;
            public float WeaponlessModifier = 10;
            public float AloneModifier = 10;
            public float UnsettledAimModifier = 1;
            public float StressedAimModifier = 1;
            public float StressedToHitModifier = -1;
            public float PanickedAimModifier = 2;
            public float PanickedToHitModifier = -2;
            public float MedianResolve = 50;
            public float ResolveMaxModifier = 10;
            public float DistractingModifier = 0;

            //deprecated public float MechHealthAlone = 50;
            public float MechHealthForCrit = 0.9f;
            public float CritOver = 70;
            public float UnsettledPanicModifier = 1f;
            public float StressedPanicModifier = 0.66f;
            public float PanickedPanicModifier = 0.33f;

            // Quirks
            public bool QuirksEnabled = true;
            public float BraveModifier = 5;
            public float DependableModifier = 5;

            // ejection
            public float MaxEjectChance = 50;
            public float EjectChanceMultiplier = 0.75f;

            // deprecated public bool ConsiderEjectingWhenAlone = false;
            public float BaseEjectionResist = 50;
            public float GutsEjectionResistPerPoint = 2;
            public float TacticsEjectionResistPerPoint = 0;
        }
    }
}
