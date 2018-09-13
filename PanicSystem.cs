using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BattleTech;
using Harmony;
using Newtonsoft.Json;
using static PanicSystem.Controller;
using static PanicSystem.Logger;
using static PanicSystem.MechChecks;

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
        private static string ejectPhraseListPath;

        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("com.BattleTech.PanicSystem");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            modDirectory = modDir;
            activeJsonPath = Path.Combine(modDir, "PanicSystem.json");
            storageJsonPath = Path.Combine(modDir, "PanicSystemStorage.json");
            FileLog.logPath = Path.Combine(modDir, "log.txt");
            try
            {
                PanicSystem.modSettings = JsonConvert.DeserializeObject<Settings>(modSettings);
                if (PanicSystem.modSettings.Debug)
                {
                    LogClear();
                }
            }
            catch (Exception e)
            {
                LogError(e);
                PanicSystem.modSettings = new Settings();
            }

            if (!PanicSystem.modSettings.EnableEjectPhrases)
            {
                return;
            }

            try
            {
                ejectPhraseListPath = Path.Combine(modDir, "phrases.txt");
                var reader = new StreamReader(ejectPhraseListPath);
                using (reader)
                {
                    while (!reader.EndOfStream)
                    {
                        ejectPhraseList.Add(reader.ReadLine());
                    }
                }
            }
            catch (Exception e)
            {
                LogError(e);
            }
        }

        /// <summary>
        ///     returning true implies they're on their last straw
        /// </summary>
        /// <param name="mech"></param>
        /// <returns></returns>
        private static bool AlwaysRollsForPanic(Mech mech)
        {
            if (!modSettings.ConsiderEjectingWhenAlone)
            {
                return false;
            }

            if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
            {
                return false;
            }

            var pilot = mech.GetPilot();
            if (pilot == null || pilot.LethalInjuries)
            {
                return false;
            }

            // one health left and a mech under MechHealthAlone percent
            if (pilot.Health - pilot.Injuries == 1 &&
                MechHealth(mech) <= modSettings.MechHealthAlone)
            {
                LogDebug("Last straw: Pilot and mech are nearly dead");
                return true;
            }

            // no allies and badly outmatched
            var enemyHealth = GetAllEnemiesHealth(mech);
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID) &&
                enemyHealth >= (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) * 3)
            {
                LogDebug("Last straw: Sole Survivor, hopeless situation");
                return true;
            }

            return false;
        }

        /// <summary>
        /// applies combat modifiers to tracked mechs based on panic status
        /// </summary>
        /// <param name="mech"></param>
        public static void ApplyPanicDebuff(Mech mech)
        {
            var index = GetPilotIndex(mech);
            if (trackedPilots[index].trackedMech != mech.GUID)
            {
                LogDebug("Pilot and mech mismatch; no status to change");
                return;
            }

            if (trackedPilots[index].pilotStatus != PanicStatus.Confident)
            {
                if (trackedPilots[index].pilotStatus == PanicStatus.Unsettled)
                {
                    LogDebug("Condition worsened: Stressed");
                    trackedPilots[index].pilotStatus = PanicStatus.Stressed;
                    mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                    mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                    mech.StatCollection.ModifyStat("Panic Attack: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, modSettings.StressedAimModifier);
                    mech.StatCollection.ModifyStat("Panic Attack: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, modSettings.StressedToHitModifier);
                }
                else if (trackedPilots[index].pilotStatus == PanicStatus.Stressed)
                {
                    LogDebug("Condition worsened: Panicked");
                    trackedPilots[index].pilotStatus = PanicStatus.Panicked;
                    mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                    mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                    mech.StatCollection.ModifyStat("Panic Attack: Panicking Aim!", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, modSettings.PanickedAimModifier);
                    mech.StatCollection.ModifyStat("Panic Attack: Panicking Defence!", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, modSettings.PanickedToHitModifier);
                }
            }
            else
            {
                LogDebug("Condition worsened: Unsetlled");
                trackedPilots[index].pilotStatus = PanicStatus.Unsettled;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, modSettings.UnsettledAttackModifier);
            }

            trackedPilots[index].panicWorsenedRecently = true;
        }

        /// <summary>
        ///     true implies panic is possible
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        private static bool CanPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
            {
                LogDebug($"{mech?.DisplayName} incapacitated by {attackSequence?.attacker?.DisplayName}");
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

        // 2.9 feature - scale the difficulty of panic levels being reached
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
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        public static float GetSavingThrow(Mech mech, AttackDirector.AttackSequence attackSequence = null)
        {
            var pilot = mech.GetPilot();
            var weapons = mech.Weapons;
            var gutsAndTacticsSum = mech.SkillGuts * modSettings.GutsEjectionResistPerPoint +
                                    mech.SkillTactics * modSettings.TacticsEjectionResistPerPoint;
            float totalMultiplier = 0;

            LogDebug(new string('-', 46));
            LogDebug($"{"Factors",-20} | {"Change",10} | {"Total",10}");
            LogDebug(new string('-', 46));

            LogDebug($"{$"Mech health {MechHealth(mech):#.##}%",-20} | {"",10} |");

            if (PercentPilot(pilot) < 1)
            {
                totalMultiplier += modSettings.PilotHealthMaxModifier * PercentPilot(pilot);
                LogDebug($"{"Pilot injuries",-20} | {modSettings.PilotHealthMaxModifier * PercentPilot(pilot),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (mech.IsUnsteady)
            {
                totalMultiplier += modSettings.UnsteadyModifier;
                LogDebug($"{"Unsteady",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
            }

            if (mech.IsFlaggedForKnockdown)
            {
                totalMultiplier += modSettings.UnsteadyModifier;
                LogDebug($"{"Knockdown",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
            }

            if (PercentHead(mech) < 1)
            {
                totalMultiplier += modSettings.HeadMaxModifier * PercentHead(mech);
                LogDebug($"{"Head",-20} | {modSettings.HeadMaxModifier * PercentHead(mech),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentCenterTorso(mech) < 1)
            {
                totalMultiplier += modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(mech));
                LogDebug($"{"CT",-20} | {modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentLeftTorso(mech) < 1)
            {
                totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(mech));
                LogDebug($"{"LT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentRightTorso(mech) < 1)
            {
                totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(mech));
                LogDebug($"{"RT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentLeftLeg(mech) < 1)
            {
                totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(mech));
                LogDebug($"{"LL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentRightLeg(mech) < 1)
            {
                totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentRightLeg(mech));
                LogDebug($"{"RL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentRightLeg(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel != ComponentDamageLevel.Functional || !w.HasAmmo)) // only fully unusable
            {
                if (UnityEngine.Random.Range(1, 5) == 0) // 20% chance of appearing
                {
                    SaySpamFloatie(mech, "NO WEAPONS!");
                }

                totalMultiplier += modSettings.WeaponlessModifier;
                LogDebug($"{"Weaponless",-20} | {modSettings.WeaponlessModifier,10} | {totalMultiplier,10:#.###}");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                if (UnityEngine.Random.Range(1, 5) == 0) // 20% chance of appearing
                {
                    SaySpamFloatie(mech, "NO ALLIES!");
                }

                totalMultiplier += modSettings.AloneModifier;
                LogDebug($"{"Alone",-20} | {modSettings.AloneModifier,10} | {totalMultiplier,10:#.###}");
            }

            totalMultiplier -= gutsAndTacticsSum;
            LogDebug($"{"Guts and Tactics",-20} | {$"-{gutsAndTacticsSum}",10} | {totalMultiplier,10:#.###}");

            var resolveModifier = modSettings.ResolveMaxModifier * (mech.Combat.LocalPlayerTeam.Morale - modSettings.MedianResolve) / modSettings.MedianResolve;
            totalMultiplier -= resolveModifier;
            LogDebug($"{$"Resolve {mech.Combat.LocalPlayerTeam.Morale}",-20} | {resolveModifier * -1,10:#.###} | {totalMultiplier,10:#.###}");

            return totalMultiplier;
        }

        public static bool SavedVsEject(Mech mech, float savingThrow, AttackDirector.AttackSequence attackSequence)
        {
            // TODO test
            if (modSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_drunk") &&
                mech.pilot.pilotDef.TimeoutRemaining > 0)
            {
                LogDebug("Drunkard - not ejecting");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                    (new ShowActorInfoSequence(mech, "..HIC!  I ain't.. ejettin'", FloatieMessage.MessageNature.PilotInjury, true)));
                return false;
            }

            // TODO test
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
            var roll = UnityEngine.Random.Range(1, 100);

            LogDebug(new string('-', 46));
            LogDebug($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            LogDebug(new string('-', 46));

            // will pass through if last straw is met to force an ejection roll
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

        public static bool SavedVsPanic(Mech mech, float savingThrow)
        {
            {
                if (modSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_brave"))
                {
                    savingThrow -= modSettings.BraveModifier;
                    LogDebug($"{"Bravery",-20} | {modSettings.BraveModifier,10} | {savingThrow,10:#.###}");
                }

                var index = GetPilotIndex(mech);
                savingThrow *= GetPanicModifier(trackedPilots[GetPilotIndex(mech)].pilotStatus);
                LogDebug($"{"Panic multiplier",-20} | {GetPanicModifier(trackedPilots[GetPilotIndex(mech)].pilotStatus),10} | {savingThrow,10:#.###}");

                savingThrow = (float) Math.Max(0f, Math.Round(savingThrow));
                if (!(savingThrow >= 1))
                {
                    LogDebug("Negative saving throw; skipping");
                    return false;
                }

                var roll = UnityEngine.Random.Range(1, 100);

                LogDebug(new string('-', 46));
                LogDebug($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
                LogDebug(new string('-', 46));

                SaySpamFloatie(mech, $"{$"SAVE:{savingThrow}",-6} {$"ROLL {roll}!",3}");

                // lower panic level
                if (roll == 100)
                {
                    LogDebug($"Critical success");
                    var status = trackedPilots[index].pilotStatus;

                    LogDebug($"{status} {(int) status}");
                    if ((int) status > 0) // don't lower below floor
                    {
                        status--;
                        trackedPilots[index].pilotStatus = status;
                    }

                    if ((int) trackedPilots[index].pilotStatus > 0) // prevent floatie if already at Confident
                    {
                        mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(mech, "CRIT SUCCESS!", FloatieMessage.MessageNature.Inspiration, false)));
                        SayStatusFloatie(mech, false);
                    }

                    return true;
                }

                // continue if roll wasn't 100
                if (roll >= savingThrow)
                {
                    SaySpamFloatie(mech, "PANIC SAVE!");
                    LogDebug("Successful panic save");
                    return true;
                }

                LogDebug("Failed panic save");
                SaySpamFloatie(mech, "SAVE FAIL!");
                ApplyPanicDebuff(mech);
                SayStatusFloatie(mech, false);

                // check for crit
                if (MechHealth(mech) <= modSettings.MechHealthForCrit &&
                    (roll == 1 || roll < (int) savingThrow - modSettings.CritOver))
                {
                    LogDebug("Critical failure on panic save");
                    // record status to see if it changes after
                    var status = trackedPilots[index].pilotStatus;
                    trackedPilots[index].pilotStatus = PanicStatus.Panicked;
                    mech.Combat.MessageCenter.PublishMessage(
                        new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(mech, "PANIC LEVEL CRITICAL!", FloatieMessage.MessageNature.CriticalHit, true)));

                    // show both floaties on a panic crit unless panicked already
                    if ((int) trackedPilots[index].pilotStatus != 3 && status != trackedPilots[index].pilotStatus)
                    {
                        SayStatusFloatie(mech, false);
                    }
                }
            }

            return false;
        }

        // method is called despite the setting, so it can be controlled in one place
        private static void SaySpamFloatie(Mech mech, string message)
        {
            if (!modSettings.FloatieSpam)
            {
                return;
            }

            mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                new ShowActorInfoSequence(mech, message, FloatieMessage.MessageNature.Neutral, false)));
        }

        private static void SayStatusFloatie(Mech mech, bool buff)
        {
            var index = GetPilotIndex(mech);
            var floatieString = $"{trackedPilots[index].pilotStatus.ToString()}";
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
            if (!CanPanic(mech, attackSequence))
            {
                return false;
            }

            return SufficientDamageWasDone(mech, attackSequence) || AlwaysRollsForPanic(mech);
        }

        public static bool SkipProcessingAttack(AttackStackSequence __instance, MessageCenterMessage message)
        {
            var attackCompleteMessage = message as AttackCompleteMessage;
            if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
            {
                return true;
            }

            if (!(__instance.directorSequences[0].target is Mech)) // can't do stuff with vehicles and buildings
            {
                return true;
            }

            return __instance.directorSequences[0].target?.GUID == null;
        }

        /// <summary>
        ///     returns true if 10% armor damage was incurred or any structure damage
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        private static bool SufficientDamageWasDone(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (attackSequence == null)
            {
                LogDebug("ERROR: attackSequence was null in damage check");
                return false;
            }

            if (!attackSequence.attackDidDamage)
            {
                LogDebug("No damage");
                return false;
            }

            var previousArmor = Patches.mechArmorBeforeAttack;
            var previousStructure = Patches.mechStructureBeforeAttack;

            LogDebug($"Damage >>> Armor: {attackSequence.attackArmorDamage} Structure: {attackSequence.attackStructureDamage} ({(attackSequence.attackArmorDamage + attackSequence.attackStructureDamage) / (previousArmor + previousStructure) * 100:#.##}%)");

            if (attackSequence.attackStructureDamage >= modSettings.MinimumStructureDamageRequired)
            {
                LogDebug($"Structure damage requires panic save");
                return true;
            }

            if ((attackSequence.attackArmorDamage + attackSequence.attackStructureDamage) /
                (previousArmor + previousStructure) *
                100 <= modSettings.MinimumDamagePercentageRequired)
            {
                LogDebug($"Not enough damage");
                return false;
            }

            LogDebug($"Total damage requires a panic save");
            return true;
        }

        public class Settings
        {
            public bool Debug = false;
            public bool EnableEjectPhrases = false;
            public bool FloatieSpam = false;
            public float EjectPhraseChance = 100;

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
            public float UnsettledAttackModifier = 1;
            public float StressedAimModifier = 1;
            public float StressedToHitModifier = -1;
            public float PanickedAimModifier = 2;
            public float PanickedToHitModifier = -2;
            public float MedianResolve = 50;
            public float ResolveMaxModifier = 10;
            public float MechHealthAlone = 50;
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
            public bool ConsiderEjectingWhenAlone = false;
            public float BaseEjectionResist = 50;
            public float GutsEjectionResistPerPoint = 2;
            public float TacticsEjectionResistPerPoint = 0;
        }
    }
}