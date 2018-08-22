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
            try
            {
                PanicSystem.modSettings = JsonConvert.DeserializeObject<Settings>(modSettings);
                if (PanicSystem.modSettings.Debug)
                {
                    Clear();
                }
            }
            catch (Exception e)
            {
                Error(e);
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
                Error(e);
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
                Debug("Last straw: Pilot and mech are nearly dead");
                return true;
            }

            // no allies and badly outmatched
            var enemyHealth = GetAllEnemiesHealth(mech);
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID) &&
                enemyHealth >= (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) * 3)
            {
                Debug("Last straw: Sole Survivor, hopeless situation");
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
            var index = GetTrackedPilotIndex(mech);
            if (trackedPilots[index].trackedMech != mech.GUID)
            {
                Debug("Pilot and mech mismatch; no status to change");
                return;
            }

            if (trackedPilots[index].pilotStatus != PanicStatus.Confident)
            {
                if (trackedPilots[index].pilotStatus == PanicStatus.Unsettled)
                {
                    Debug("Condition worsened: Stressed");
                    trackedPilots[index].pilotStatus = PanicStatus.Stressed;
                    mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                    mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                    mech.StatCollection.ModifyStat("Panic Attack: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, modSettings.StressedAimModifier);
                    mech.StatCollection.ModifyStat("Panic Attack: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, modSettings.StressedToHitModifier);
                }
                else if (trackedPilots[index].pilotStatus == PanicStatus.Stressed)
                {
                    Debug("Condition worsened: Panicked");
                    trackedPilots[index].pilotStatus = PanicStatus.Panicked;
                    mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                    mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                    mech.StatCollection.ModifyStat("Panic Attack: Panicking Aim!", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, modSettings.PanickedAimModifier);
                    mech.StatCollection.ModifyStat("Panic Attack: Panicking Defence!", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, modSettings.PanickedToHitModifier);
                }
            }
            else
            {
                Debug("Condition worsened: Unsetlled");
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
                Debug($"{mech?.DisplayName} incapacitated by {attackSequence?.attacker?.DisplayName}");
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
        ///     true implies the pilot and mech are properly tracked
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        private static void CheckTrackedPilots(Mech mech)
        {
            var index = GetTrackedPilotIndex(mech);
            if (index < 0)
            {
                // add a new tracker to tracked pilot, then we run it all over again
                trackedPilots.Add(new PanicTracker(mech));
                index = GetTrackedPilotIndex(mech);
                if (index < 0)
                {
                    return;
                }
            }

            if (trackedPilots[index].trackedMech != mech.GUID)
            {
                return;
            }

            if (trackedPilots[index].trackedMech == mech.GUID &&
                trackedPilots[index].panicWorsenedRecently &&
                modSettings.OneChangePerTurn)
            {
                return;
            }

            return;
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

            Debug(new string('-', 46));
            Debug($"{"Factors",-20} | {"Change",10} | {"Total",10}");
            Debug(new string('-', 46));

            Debug($"{$"Mech health {MechHealth(mech):#.##}%",-20} | {"",10} |");

            if (PercentPilot(pilot) < 1)
            {
                totalMultiplier += modSettings.PilotHealthMaxModifier * PercentPilot(pilot);
                Debug($"{"Pilot injuries",-20} | {modSettings.PilotHealthMaxModifier * PercentPilot(pilot),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (mech.IsUnsteady)
            {
                totalMultiplier += modSettings.UnsteadyModifier;
                Debug($"{"Unsteady",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
            }

            if (mech.IsFlaggedForKnockdown)
            {
                totalMultiplier += modSettings.UnsteadyModifier;
                Debug($"{"Knockdown",-20} | {modSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
            }

            if (PercentHead(mech) < 1)
            {
                totalMultiplier += modSettings.HeadMaxModifier * PercentHead(mech);
                Debug($"{"Head",-20} | {modSettings.HeadMaxModifier * PercentHead(mech),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentCenterTorso(mech) < 1)
            {
                totalMultiplier += modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(mech));
                Debug($"{"CT",-20} | {modSettings.CenterTorsoMaxModifier * (1 - PercentCenterTorso(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentLeftTorso(mech) < 1)
            {
                totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(mech));
                Debug($"{"LT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentLeftTorso(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentRightTorso(mech) < 1)
            {
                totalMultiplier += modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(mech));
                Debug($"{"RT",-20} | {modSettings.SideTorsoMaxModifier * (1 - PercentRightTorso(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentLeftLeg(mech) < 1)
            {
                totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(mech));
                Debug($"{"LL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentLeftLeg(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentRightLeg(mech) < 1)
            {
                totalMultiplier += modSettings.LeggedMaxModifier * (1 - PercentRightLeg(mech));
                Debug($"{"RL",-20} | {modSettings.LeggedMaxModifier * (1 - PercentRightLeg(mech)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel != ComponentDamageLevel.Functional || !w.HasAmmo)) // only fully unusable
            {
                if (UnityEngine.Random.Range(1, 5) == 0) // 20% chance of appearing
                {
                    SaySpamFloatie(mech, "NO WEAPONS!");
                }

                totalMultiplier += modSettings.WeaponlessModifier;
                Debug($"{"Weaponless",-20} | {modSettings.WeaponlessModifier,10} | {totalMultiplier,10:#.###}");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                if (UnityEngine.Random.Range(1, 5) == 0) // 20% chance of appearing
                {
                    SaySpamFloatie(mech, "NO ALLIES!");
                }

                totalMultiplier += modSettings.AloneModifier;
                Debug($"{"Alone",-20} | {modSettings.AloneModifier,10} | {totalMultiplier,10:#.###}");
            }

            totalMultiplier -= gutsAndTacticsSum;
            Debug($"{"Guts and Tactics",-20} | {$"-{gutsAndTacticsSum}",10} | {totalMultiplier,10:#.###}");

            var resolveModifier = modSettings.ResolveMaxModifier * (mech.Combat.LocalPlayerTeam.Morale - modSettings.MedianResolve) / modSettings.MedianResolve;
            totalMultiplier -= resolveModifier;
            Debug($"{$"Resolve {mech.Combat.LocalPlayerTeam.Morale}",-20} | {resolveModifier * -1,10:#.###} | {totalMultiplier,10:#.###}");

            return totalMultiplier;
        }

        public static bool SavedVsEject(Mech mech, float savingThrow, AttackDirector.AttackSequence attackSequence)
        {
            // TODO test
            if (modSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_drunk") &&
                mech.pilot.pilotDef.TimeoutRemaining > 0)
            {
                Debug("Drunkard - not ejecting");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                    (new ShowActorInfoSequence(mech, "..HIC!  I ain't.. ejettin'", FloatieMessage.MessageNature.PilotInjury, true)));
                FlushLogBuffer();
                return false;
            }

            // TODO test
            if (modSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
            {
                savingThrow -= modSettings.DependableModifier;
                Debug($"{"Dependable",-20} | {modSettings.DependableModifier,10} | {savingThrow,10:#.###}");
            }

            // calculate result
            savingThrow = Math.Max(0f, savingThrow - modSettings.BaseEjectionResist);
            Debug($"{"Base ejection resist",-20} | {modSettings.BaseEjectionResist,10} | {savingThrow,10:#.###}");

            savingThrow = (float) Math.Round(savingThrow);
            Debug($"{"Eject multiplier",-20} | {modSettings.EjectChanceMultiplier,10} | {savingThrow,10:#.###}");
            var roll = UnityEngine.Random.Range(1, 100);

            Debug(new string('-', 46));
            Debug($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            Debug(new string('-', 46));

            // will pass through if last straw is met to force an ejection roll
            if (savingThrow <= 0)
            {
                Debug("Negative saving throw; skipping");
                SaySpamFloatie(mech, "EJECT RESIST!");
                FlushLogBuffer();
                return true;
            }

            // cap the saving throw by the setting
            savingThrow = (int) Math.Min(savingThrow, modSettings.MaxEjectChance);

            SaySpamFloatie(mech, $"SAVE: {savingThrow}  ROLL: {roll}!");
            if (roll >= savingThrow)
            {
                Debug("Successful ejection save");
                SaySpamFloatie(mech, $"EJECT SAVE! HEALTH: {MechHealth(mech):#.#}%");
                FlushLogBuffer();
                return true;
            }

            Debug("Failed ejection save: Punchin\' Out!!");
            return false;
        }

        public static bool SavedVsPanic(Mech mech, float savingThrow, AttackDirector.AttackSequence attackSequence)
        {
            // TODO test
            if (modSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_brave"))
            {
                savingThrow -= modSettings.BraveModifier;
                Debug($"{"Bravery",-20} | {modSettings.BraveModifier,10} | {savingThrow,10:#.###}");
            }

            savingThrow *= GetPanicModifier(trackedPilots[GetTrackedPilotIndex(mech)].pilotStatus);
            Debug($"{"Panic multiplier",-20} | {GetPanicModifier(trackedPilots[GetTrackedPilotIndex(mech)].pilotStatus),10} | {savingThrow,10:#.###}");

            savingThrow = (float) Math.Max(0f, Math.Round(savingThrow));
            if (!(savingThrow >= 1))
            {
                Debug("Negative saving throw; skipping");
                FlushLogBuffer();
                return false;
            }

            var roll = UnityEngine.Random.Range(1, 100);

            Debug(new string('-', 46));
            Debug($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            Debug(new string('-', 46));

            var index = GetTrackedPilotIndex(mech);
            if (index == -1)
            {
                CheckTrackedPilots(mech);
            }

            SaySpamFloatie(mech, $"{$"SAVE:{savingThrow}",-6} {$"ROLL {roll}!",3}");

            if (roll == 100)
            {
                Debug($"Critical success");
                var status = trackedPilots[index].pilotStatus;

                Debug($"{status} {(int) status}");
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
                Debug("Successful panic save");
                return true;
            }

            Debug("Failed panic save");
            SaySpamFloatie(mech, "SAVE FAIL!");
            ApplyPanicDebuff(mech);
            SayStatusFloatie(mech, false);

            // check for crit
            if (MechHealth(mech) <= modSettings.MechHealthForCrit &&
                (roll == 1 || roll < (int) savingThrow - modSettings.CritOver))
            {
                Debug("Critical failure on panic save");
                var status = trackedPilots[index].pilotStatus; // record status to see if it changes after
                trackedPilots[index].pilotStatus = PanicStatus.Panicked;
                mech.Combat.MessageCenter.PublishMessage(
                    new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(mech, "PANIC LEVEL CRITICAL!", FloatieMessage.MessageNature.CriticalHit, true)));

                // show both floaties on a panic crit unless panicked already
                if ((int) status != 3 && status != trackedPilots[index].pilotStatus)
                {
                    SayStatusFloatie(mech, false);
                }
            }

            FlushLogBuffer();
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
            var index = GetTrackedPilotIndex(mech);
            var floatieString = $"{trackedPilots[index].pilotStatus.ToString()}";
            if (buff)
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, floatieString, FloatieMessage.MessageNature.Inspiration, true)));
            }
            else
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, floatieString, FloatieMessage.MessageNature.CriticalHit, true)));
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
            if (!attackSequence.attackDidDamage)
            {
                Debug("No damage");
                return false;
            }

            Debug($"Damage >>> Armor: {attackSequence.attackArmorDamage} Structure: {attackSequence.attackStructureDamage}");

            if (attackSequence.attackStructureDamage >= modSettings.MinimumStructureDamageRequired)
            {
                Debug($"Structure damage requires panic save");
                return true;
            }

            if (attackSequence.attackArmorDamage / mech.StartingArmor * 100 <= modSettings.MinimumDamagePercentageRequired) 

            if ((attackSequence.attackArmorDamage + attackSequence.attackStructureDamage) /
                (mech.StartingArmor + mech.StartingStructure) *
                100 <= modSettings.MinimumDamagePercentageRequired)
            {
                Debug($"Not enough damage");
                return false;
            }

            Debug($"Total damage requires a panic save");
            return true;
        }

        public class Settings
        {
            public bool Debug = false;
            public bool EnableEjectPhrases = false;
            public bool FloatieSpam = false;

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