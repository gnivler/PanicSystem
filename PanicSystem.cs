using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using BattleTech;
using BattleTech.UI;
using Harmony;
using Newtonsoft.Json;
using static PanicSystem.Controller;
using static PanicSystem.Logger;
using static PanicSystem.MechChecks;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.

// 2.8 reworked design by don Zappo, balancing and testing by ganimal, and coding by gnivler
namespace PanicSystem
{
    public static class PanicSystem
    {
        internal static Settings ModSettings = new Settings();
        internal static string ActiveJsonPath; //store current tracker here
        internal static string StorageJsonPath; //store our meta trackers here
        internal static string ModDirectory;
        internal static List<string> EjectPhraseList = new List<string>();
        internal static string EjectPhraseListPath;

        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("com.BattleTech.PanicSystem");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            ModDirectory = modDir;
            ActiveJsonPath = Path.Combine(modDir, "PanicSystem.json");
            StorageJsonPath = Path.Combine(modDir, "PanicSystemStorage.json");
            try
            {
                ModSettings = JsonConvert.DeserializeObject<Settings>(modSettings);
                if (ModSettings.Debug)
                {
                    Clear();
                }
            }
            catch (Exception e)
            {
                Error(e);
                ModSettings = new Settings();
            }

            if (!ModSettings.EnableEjectPhrases)
            {
                return;
            }

            try
            {
                EjectPhraseListPath = Path.Combine(modDir, "phrases.txt");
                var reader = new StreamReader(EjectPhraseListPath);
                using (reader)
                {
                    while (!reader.EndOfStream)
                    {
                        EjectPhraseList.Add(reader.ReadLine());
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
        public static bool AlwaysRollsForPanic(Mech mech)
        {
            if (!ModSettings.ConsiderEjectingWhenAlone)
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
                MechHealth(mech) <= ModSettings.MechHealthAlone)
            {
                Debug("Last straw: Pilot and mech are nearly dead");
                return true;
            }

            // no allies and badly outmatched
            var enemyHealth = GetAllEnemiesHealth(mech);
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID) &&
                enemyHealth >= (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) * 3) // deliberately simple for better or worse (3-to-1 health)
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
            if (TrackedPilots[index].TrackedMech != mech.GUID)
            {
                Debug("Pilot and mech mismatch");
                return;
            }

            if (TrackedPilots[index].PilotStatus == PanicStatus.Confident)
            {
                Debug("Condition change: Unsetlled");
                TrackedPilots[index].PilotStatus = PanicStatus.Unsettled;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.UnsettledAttackModifier);
            }
            else if (TrackedPilots[index].PilotStatus == PanicStatus.Unsettled)
            {
                Debug("Condition change: Stressed");
                TrackedPilots[index].PilotStatus = PanicStatus.Stressed;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.StressedAimModifier);
                mech.StatCollection.ModifyStat("Panic Attack: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, ModSettings.StressedToHitModifier);
            }
            else if (TrackedPilots[index].PilotStatus == PanicStatus.Stressed)
            {
                Debug("Condition change: Panicked");
                TrackedPilots[index].PilotStatus = PanicStatus.Panicked;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Panicking Aim!", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.PanickedAimModifier);
                mech.StatCollection.ModifyStat("Panic Attack: Panicking Defence!", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, ModSettings.PanickedToHitModifier);
            }

            TrackedPilots[index].PanicWorsenedRecently = true;
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
                mech.team.IsLocalPlayer && !ModSettings.PlayersCanPanic ||
                !mech.team.IsLocalPlayer && !ModSettings.EnemiesCanPanic)
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
        public static bool CheckTrackedPilots(Mech mech)
        {
            var index = GetTrackedPilotIndex(mech);
            if (index < 0)
            {
                // add a new tracker to tracked pilot, then we run it all over again
                TrackedPilots.Add(new PanicTracker(mech));
                index = GetTrackedPilotIndex(mech);
                if (index < 0)
                {
                    return false;
                }
            }

            if (TrackedPilots[index].TrackedMech != mech.GUID)
            {
                return false;
            }

            if (TrackedPilots[index].TrackedMech == mech.GUID &&
                TrackedPilots[index].PanicWorsenedRecently &&
                ModSettings.OneChangePerTurn)
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
                    return ModSettings.UnsettledPanicModifier;
                }
                case PanicStatus.Stressed:
                {
                    return ModSettings.StressedPanicModifier;
                }
                case PanicStatus.Panicked:
                {
                    return ModSettings.PanickedPanicModifier;
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
            var gutsAndTacticsSum = mech.SkillGuts * ModSettings.GutsEjectionResistPerPoint +
                                    mech.SkillTactics * ModSettings.TacticsEjectionResistPerPoint;
            float totalMultiplier = 0;

            Debug($"{"Factors",-20} | {"Change",10} | {"Total",10}");
            Debug(new string(c: '-', count: 46));

            if (PercentPilot(pilot) < 1)
            {
                totalMultiplier += ModSettings.PilotHealthMaxModifier * (1 - PercentPilot(pilot));
                Debug($"{"Pilot injuries",-20} | {ModSettings.PilotHealthMaxModifier * (1 - PercentPilot(pilot)),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (mech.IsUnsteady)
            {
                totalMultiplier += ModSettings.UnsteadyModifier;
                Debug($"{"Unsteady",-20} | {ModSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
            }

            if (mech.IsFlaggedForKnockdown)
            {
                totalMultiplier += ModSettings.UnsteadyModifier;
                Debug($"{"Knockdown",-20} | {ModSettings.UnsteadyModifier,10} | {totalMultiplier,10:#.###}");
            }

            if (PercentHead(mech) < 1)
            {
                totalMultiplier += ModSettings.HeadMaxModifier * PercentHead(mech);
                Debug($"{"Head",-20} | {ModSettings.HeadMaxModifier * PercentHead(mech),10:#.###} | {totalMultiplier,10:#.###}");
            }

            if (PercentCenterTorso(mech) < 1)
            {
                totalMultiplier += ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech);
                Debug($"{"CT",-20} | {ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech),10:#.###} | {totalMultiplier,10:#.###}");
            }

            // these methods deal with missing limbs (0 modifiers get replaced with max modifiers)
            EvalLeftTorso(mech, ref totalMultiplier);
            EvalRightTorso(mech, ref totalMultiplier);
            EvalLeftLeg(mech, ref totalMultiplier);
            EvalRightLeg(mech, ref totalMultiplier);

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel != ComponentDamageLevel.Functional || !w.HasAmmo)) // only fully unusable
            {
                if (UnityEngine.Random.Range(1, 5) == 0) // 20% chance of appearing
                {
                    SaySpamFloatie(mech, "NO WEAPONS!", FloatieMessage.MessageNature.Death);
                }

                totalMultiplier += ModSettings.WeaponlessModifier;
                Debug($"{"Weaponless",-20} | {ModSettings.WeaponlessModifier,10} | {totalMultiplier,10:#.###}");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                if (UnityEngine.Random.Range(1, 5) == 0) // 20% chance of appearing
                {
                    SaySpamFloatie(mech, "NO ALLIES!", FloatieMessage.MessageNature.Death);
                }

                totalMultiplier += ModSettings.AloneModifier;
                Debug($"{"Alone",-20} | {ModSettings.AloneModifier,10} | {totalMultiplier,10:#.###}");
            }

            totalMultiplier -= gutsAndTacticsSum;
            Debug($"{"Guts and Tactics",-20} | {$"-{gutsAndTacticsSum}",10} | {totalMultiplier,10:#.###}");

            var moraleModifier = ModSettings.MoraleMaxModifier * (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / ModSettings.MedianMorale;
            totalMultiplier -= moraleModifier;
            Debug($"{$"Morale {mech.Combat.LocalPlayerTeam.Morale}",-20} | {moraleModifier * -1,10:#.###} | {totalMultiplier,10:#.###}");

            return totalMultiplier;
        }

        public static bool SavedVsEject(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            // TODO test
            if (ModSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_drunk") &&
                mech.pilot.pilotDef.TimeoutRemaining > 0)
            {
                Debug("Drunkard - not ejecting");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                    (new ShowActorInfoSequence(mech, "..HIC!  I ain't.. ejettin'", FloatieMessage.MessageNature.PilotInjury, true)));
                FlushLog();
                return false;
            }

            var savingThrow = GetSavingThrow(mech, attackSequence);

            // TODO test
            if (ModSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
            {
                savingThrow -= ModSettings.DependableModifier;
                Debug($"{"Dependable",-20} | {ModSettings.DependableModifier,10} | {savingThrow,10:#.###}");
            }

            // calculate result
            savingThrow = Math.Max(0f, (savingThrow - ModSettings.BaseEjectionResist) * ModSettings.EjectChanceMultiplier);
            savingThrow = (float) Math.Round(savingThrow);
            var roll = UnityEngine.Random.Range(1, 100);

            Debug(new string(c: '-', count: 46));
            Debug($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            Debug(new string(c: '-', count: 46));
            Debug($"{"Mech health",-20}{MechHealth(mech),25:#.##}%");

            // will pass through if last straw is met to force an ejection roll
            if (savingThrow <= 0)
            {
                SaySpamFloatie(mech, "EJECT RESIST!", FloatieMessage.MessageNature.Dodge);
                Debug("Resisted ejection");
                FlushLog();
                return true;
            }

            // cap the saving throw by the setting
            savingThrow = (int) Math.Min(savingThrow, ModSettings.MaxEjectChance);

            Debug($"Saving throw: {savingThrow} Roll {roll}");
            SaySpamFloatie(mech, $"SAVE: {savingThrow}  ROLL: {roll}!", FloatieMessage.MessageNature.ArmorDamage);
            if (roll >= savingThrow)
            {
                Debug("Made ejection save");
                SaySpamFloatie(mech, "EJECT SAVE!", FloatieMessage.MessageNature.Dodge);
                SaySpamFloatie(mech, $"MECH HEALTH {MechHealth(mech):#.#}%", FloatieMessage.MessageNature.Neutral);
                FlushLog();
                return true;
            }

            Debug("Failed ejection save: Punchin\' Out!!");
            return false;
        }

        public static bool SavedVsPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            var savingThrow = GetSavingThrow(mech, attackSequence);

            // TODO test
            if (ModSettings.QuirksEnabled && mech.pilot.pilotDef.PilotTags.Contains("pilot_brave"))
            {
                savingThrow -= ModSettings.BraveModifier;
                Debug($"{"Bravery",-20} | {ModSettings.BraveModifier,10} | {savingThrow,10:#.###}");
            }

            savingThrow *= GetPanicModifier(TrackedPilots[GetTrackedPilotIndex(mech)].PilotStatus);
            Debug($"{"Panic multiplier",-20} | {GetPanicModifier(TrackedPilots[GetTrackedPilotIndex(mech)].PilotStatus),10} | {savingThrow,10:#.###}");

            savingThrow = (float) Math.Max(0f, Math.Round(savingThrow));
            if (!(savingThrow >= 1))
            {
                Debug("Negative saving throw");
                FlushLog();
                return false;
            }

            var roll = UnityEngine.Random.Range(1, 100);

            Debug(new string(c: '-', count: 46));
            //Debug(string.Format("{0,-20} | {1,-5}{2,5} | {3,10}", "Saving throw", $"{savingThrow:###}", $"{roll,5}" ,"Roll"));
            Debug($"{"Saving throw",-20} | {savingThrow,-5:###}{roll,5} | {"Roll",10}");
            Debug(new string(c: '-', count: 46));
            //Debug(string.Format("{0,-20}{1,25}", "Mech health", $"{MechHealth(mech):#.##}"));
            Debug($"{"Mech health",-20}{MechHealth(mech),25:#.##}%");

            var index = GetTrackedPilotIndex(mech);
            if (index == -1)
            {
                CheckTrackedPilots(mech);
            }

            SaySpamFloatie(mech, $"{$"SAVE:{savingThrow}",-6} {$"ROLL {roll}!",3}", FloatieMessage.MessageNature.ArmorDamage);

            if (roll == 100)
            {
                Debug($"Critical success");
                var status = TrackedPilots[index].PilotStatus;

                Debug($"{status} {(int) status}");
                if ((int) status > 0) // don't lower below floor
                {
                    status--;
                    TrackedPilots[index].PilotStatus = status;
                }

                if ((int) TrackedPilots[index].PilotStatus > 0) // prevent floatie if already at Confident
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
                SaySpamFloatie(mech, "PANIC SAVE!", FloatieMessage.MessageNature.Dodge);
                Debug("Made panic save");
                return true;
            }

            Debug("Failed panic save");
            SaySpamFloatie(mech, "SAVE FAIL!", FloatieMessage.MessageNature.Debuff);
            ApplyPanicDebuff(mech);
            SayStatusFloatie(mech, false);

            // check for crit
            if (MechHealth(mech) <= ModSettings.MechHealthForCrit &&
                (roll == 1 || roll < (int) savingThrow - ModSettings.CritOver))
            {
                Debug("Critical failure on panic save");
                var status = TrackedPilots[index].PilotStatus; // record status to see if it changes after
                TrackedPilots[index].PilotStatus = PanicStatus.Panicked;
                mech.Combat.MessageCenter.PublishMessage(
                    new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(mech, "PANIC LEVEL CRITICAL!", FloatieMessage.MessageNature.CriticalHit, false)));

                // show both floaties on a panic crit unless panicked already
                if ((int) status != 3 && status != TrackedPilots[index].PilotStatus)
                {
                    SayStatusFloatie(mech, false);
                }
            }
            FlushLog();
            return false;
        }

        public static void SayStatusFloatie(Mech mech, bool buff)
        {
            var nature = buff ? FloatieMessage.MessageNature.Buff : FloatieMessage.MessageNature.Debuff;

            var index = GetTrackedPilotIndex(mech);
            var floatieString = $"{TrackedPilots[index].PilotStatus.ToString()}";
            if (buff)
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, floatieString, FloatieMessage.MessageNature.Buff, false)));
            }
            else
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, floatieString, FloatieMessage.MessageNature.Debuff, false)));
            }
        }

        // method is called despite the setting, so it can be controlled in one place
        public static void SaySpamFloatie(Mech mech, string message, FloatieMessage.MessageNature nature)
        {
            if (!ModSettings.FloatieSpam)
            {
                return;
            }

            mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                new ShowActorInfoSequence(mech, message, FloatieMessage.MessageNature.NotSet, false)));
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

            if (attackSequence.attackStructureDamage >= ModSettings.MinimumStructureDamageRequired)
            {
                Debug($"{attackSequence.attackStructureDamage} structure damage requires panic throw");
                return true;
            }

            if (attackSequence.attackArmorDamage / mech.CurrentArmor * 100 <= ModSettings.MinimumArmorDamagePercentageRequired)
            {
                Debug($"Not enough damage");
                return false;
            }

            Debug($"Damage requires a panic throw");
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
            public float MinimumArmorDamagePercentageRequired = 10;
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
            public float MedianMorale = 50;
            public float MoraleMaxModifier = 10;
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
            public float EjectChanceMultiplier = 1;
            public bool KnockedDownCannotEject = false;
            public bool ConsiderEjectingWhenAlone = false;
            public float BaseEjectionResist = 50;
            public float GutsEjectionResistPerPoint = 2;
            public float TacticsEjectionResistPerPoint = 0;
        }
    }
}