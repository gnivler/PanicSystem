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

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class PanicSystem
    {
        internal static Settings ModSettings = new Settings();
        internal static string ActiveJsonPath; //store current tracker here
        internal static string StorageJsonPath; //store our meta trackers here
        internal static string ModDirectory;
        internal static bool KlutzEject;
        internal static readonly Random Rng = new Random();
        internal static List<string> KnockDownPhraseList = new List<string>();
        internal static string KnockDownPhraseListPath;

        // I put in shitty global bool because it was easiest at the time, sorry!
        // forces ejection saves every attack that cause any damage
        public static bool LastStraw;

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

            if (!ModSettings.EnableKnockDownPhrases)
            {
                return;
            }

            try
            {
                KnockDownPhraseListPath = Path.Combine(modDir, "phrases.txt");
                var reader = new StreamReader(KnockDownPhraseListPath);
                using (reader)
                {
                    while (!reader.EndOfStream)
                    {
                        KnockDownPhraseList.Add(reader.ReadLine());
                    }
                }
            }
            catch (Exception e)
            {
                Error(e);
            }
        }

        private static void EvalRightLeg(Mech mech, ref float modifiers)
        {
            if (mech.RightLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                modifiers += ModSettings.LeggedMaxModifier;
                Debug($"RL destroyed, adds {ModSettings.LeggedMaxModifier}, now {modifiers:0.###}");
            }
            else
            {
                modifiers += ModSettings.LeggedMaxModifier * PercentRightLeg(mech);
                Debug($"RL damage adds {ModSettings.LeggedMaxModifier * PercentRightLeg(mech):0.###}, now {modifiers:0.###}");
            }
        }

        private static void EvalLeftLeg(Mech mech, ref float modifiers)
        {
            if (mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                modifiers += ModSettings.LeggedMaxModifier;
                Debug($"LL destroyed, adds {ModSettings.LeggedMaxModifier}, now {modifiers:0.###}");
            }
            else
            {
                modifiers += ModSettings.LeggedMaxModifier * PercentLeftLeg(mech);
                Debug($"LL damage adds {ModSettings.LeggedMaxModifier * PercentLeftLeg(mech):0.###}, now {modifiers:0.###}");
            }
        }

        private static void EvalRightTorso(Mech mech, ref float modifiers)
        {
            if (mech.RightTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                modifiers += ModSettings.SideTorsoMaxModifier;
                Debug($"RT destroyed, adds {ModSettings.SideTorsoMaxModifier}, now {modifiers:0.###}");
            }
            else
            {
                modifiers += ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech);
                Debug($"RT damage adds {ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech):0.###}, now {modifiers:0.###}");
            }
        }

        private static void EvalLeftTorso(Mech mech, ref float modifiers)
        {
            if (mech.LeftTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                modifiers += ModSettings.SideTorsoMaxModifier;
                Debug($"LT destroyed, adds {ModSettings.SideTorsoMaxModifier:0.###}, now {modifiers:0.###}");
            }
            else if (PercentLeftTorso(mech) < 1)
            {
                modifiers += ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech);
                Debug($"LT damage adds {ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech):0.###}, now {modifiers:0.###}");
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
/*
            if (!CheckTrackedPilots(mech))
            {
                return false;
            }
*/
            if (WasEnoughDamageDone(mech, attackSequence) || MetLastStraw(mech))
            {
                return true;
            }

            return false;
        }

        public static bool FailedPanicSave(Mech mech)
        {
            var pilot = mech.GetPilot();
            var weapons = mech.Weapons;
            var gutsAndTacticsSum = mech.SkillGuts * ModSettings.GutsEjectionResistPerPoint +
                                    mech.SkillTactics * ModSettings.TacticsEjectionResistPerPoint;
            float panicModifiers = 0;
            Debug("Collecting panic modifiers:");

            if (PercentPilot(pilot) < 1)
            {
                panicModifiers += ModSettings.PilotHealthMaxModifier * PercentPilot(pilot);
                Debug($"Pilot injuries add {ModSettings.PilotHealthMaxModifier * PercentPilot(pilot):0.###}, now {panicModifiers:0.###}");
            }

            if (mech.IsUnsteady)
            {
                panicModifiers += ModSettings.UnsteadyModifier;
                Debug($"Unsteady adds {ModSettings.UnsteadyModifier}, now {panicModifiers:0.###}");
            }

            if (mech.IsFlaggedForKnockdown)
            {
                if (pilot.pilotDef.PilotTags.Contains("pilot_klutz"))
                {
                    Debug("Klutz!");

                    if (Rng.Next(1, 101) == 13)
                    {
                        mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                            (new ShowActorInfoSequence(mech, "WOOPS!", FloatieMessage.MessageNature.Debuff, false)));
                        Debug("Very klutzy!");
                        KlutzEject = true;
                        return true;
                    }
                }
                else if (ModSettings.EnableKnockDownPhrases)
                {
                    var message = KnockDownPhraseList[Rng.Next(0, KnockDownPhraseList.Count - 1)];
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                        (new ShowActorInfoSequence(mech, message, FloatieMessage.MessageNature.Debuff, false)));
                }

                panicModifiers += ModSettings.UnsteadyModifier;
                Debug($"Knockdown adds {ModSettings.UnsteadyModifier}, now {panicModifiers:0.###}");
            }

            if (PercentHead(mech) < 1)
            {
                panicModifiers += ModSettings.HeadMaxModifier * PercentHead(mech);
                Debug($"Head damage adds {ModSettings.HeadMaxModifier * PercentHead(mech):0.###}, now {panicModifiers:0.###}");
            }

            if (PercentCenterTorso(mech) < 1)
            {
                panicModifiers += ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech);
                Debug($"CT damage adds {ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech):0.###}, now {panicModifiers:0.###}");
            }

            // these methods deal with missing limbs (0 modifiers get replaced with max modifiers)
            EvalLeftTorso(mech, ref panicModifiers);
            EvalRightTorso(mech, ref panicModifiers);
            EvalLeftLeg(mech, ref panicModifiers);
            EvalRightLeg(mech, ref panicModifiers);

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel != ComponentDamageLevel.Functional || !w.HasAmmo)) // only fully unusable
            {
                panicModifiers += ModSettings.WeaponlessModifier;
                Debug($"Weaponless adds {ModSettings.WeaponlessModifier}");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                panicModifiers += ModSettings.AloneModifier;
                Debug($"Being alone adds {ModSettings.AloneModifier}, now {panicModifiers:0.###}");
            }

            panicModifiers -= gutsAndTacticsSum;
            Debug($"Guts and Tactics subtracts {gutsAndTacticsSum}, now {panicModifiers:0.###}");

            var moraleModifier = ModSettings.MoraleMaxModifier * (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / ModSettings.MedianMorale;
            panicModifiers -= moraleModifier;
            Debug($"Current morale {mech.Combat.LocalPlayerTeam.Morale} subtracts {moraleModifier:0.###}, now {panicModifiers:0.###}");

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_brave"))
            {
                panicModifiers -= ModSettings.BraveModifier;

                Debug($"Bravery subtracts {ModSettings.BraveModifier}, now {panicModifiers:0.###}");
            }

            panicModifiers = (float) Math.Max(0f, Math.Round(panicModifiers));
            Debug($"Saving throw: {panicModifiers}");

            var index = GetTrackedPilotIndex(mech);
            var roll = Rng.Next(1, 101);
            Debug($"Rolled {roll}");

            if (roll < (int) panicModifiers)
            {
                Debug("Failed panic save");
                ApplyPanicDebuff(mech);
                if (roll == 1 || roll < (int) panicModifiers - ModSettings.CritOver &&
                    (MechHealth(mech) <= ModSettings.MechHealthForCrit))
                {
                    Debug("Critical failure on panic save");
                    ApplyPanicDebuff(mech);
                    mech.Combat.MessageCenter.PublishMessage(
                        new AddSequenceToStackMessage(
                            new ShowActorInfoSequence(mech, "PANIC CRIT!", FloatieMessage.MessageNature.Debuff, false)));
                }

                ShowStatusFloatie(mech, "FAILED! ");
                return true;
            }

            if (roll == 100)
            {
                Debug($"Critical success: Index {index}");
                if (TrackedPilots[index].PilotStatus != PanicStatus.Panicked &&
                    (int) TrackedPilots[index].PilotStatus > 0) // don't lower below floor
                {
                    var status = (int) TrackedPilots[index].PilotStatus;
                    status--;
                    TrackedPilots[index].PilotStatus = (PanicStatus) status;
                }

                ShowStatusFloatie(mech);
                return false;
            }

            Debug("Made panic save");
            return false;
        }

        public static void ShowStatusFloatie(Mech mech, string prefix = "")
        {
            var index = GetTrackedPilotIndex(mech);
            var floatieString = $"{TrackedPilots[index].PilotStatus.ToString()}";
            mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, floatieString, FloatieMessage.MessageNature.Debuff, false)));
        }

        /// <summary>
        ///     true implies punchin' out
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        public static bool FailedEjectSave(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && !mech.HasHandledDeath)
            {
                return false;
            }

            // knocked down mechs cannot eject
            if (ModSettings.KnockedDownCannotEject && mech.IsProne)
            {
                return false;
            }

            if (!attackSequence.attackDidDamage)
            {
                return false;
            }

            var pilot = mech.GetPilot();
            if (pilot == null)
            {
                return false;
            }

            var weapons = mech.Weapons;
            var guts = mech.SkillGuts;
            var tactics = mech.SkillTactics;
            var gutsAndTacticsSum = guts + tactics;

            if (!mech.CanBeHeadShot || !pilot.CanEject)
            {
                return false;
            }

            // start building ejectModifiers
            float ejectModifiers = 0;
            Debug("Collecting ejection modifiers:");
            Debug(new string('-', 60));

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_drunk") && pilot.pilotDef.TimeoutRemaining > 0)
            {
                Debug("Drunkard - not ejecting");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                    (new ShowActorInfoSequence(mech, "..HIC!", FloatieMessage.MessageNature.Buff, true)));
                return false;
            }

            // pilot health
            if (PercentPilot(pilot) < 1)
            {
                ejectModifiers += ModSettings.PilotHealthMaxModifier * PercentPilot(pilot);
                Debug($"Pilot injury adds {ModSettings.PilotHealthMaxModifier * PercentPilot(pilot):0.###}, now {ejectModifiers:0.###}");
            }

            // unsteady
            if (mech.IsUnsteady)
            {
                ejectModifiers += ModSettings.UnsteadyModifier;
                Debug($"Unsteady adds {ModSettings.UnsteadyModifier}, now {ejectModifiers:0.###}");
            }

            if (mech.IsFlaggedForKnockdown)
            {
                if (pilot.pilotDef.PilotTags.Contains("pilot_klutz"))
                {
                    Debug("Klutz!");

                    if (Rng.Next(1, 101) == 13)
                    {
                        mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                            (new ShowActorInfoSequence(mech, "WOOPS!", FloatieMessage.MessageNature.Debuff, false)));
                        Debug("Very klutzy!");
                        KlutzEject = true;
                        return true;
                    }
                }
                else if (ModSettings.EnableKnockDownPhrases)
                {
                    var message = KnockDownPhraseList[Rng.Next(0, KnockDownPhraseList.Count - 1)];
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                        (new ShowActorInfoSequence(mech, message, FloatieMessage.MessageNature.Debuff, false)));
                }

                ejectModifiers += ModSettings.UnsteadyModifier;
                Debug($"Knockdown adds {ModSettings.UnsteadyModifier}, now {ejectModifiers:0.###}");
            }

            // Head
            if (PercentHead(mech) < 1)
            {
                ejectModifiers += ModSettings.HeadMaxModifier * PercentHead(mech);
                Debug($"Head damage adds {ModSettings.HeadMaxModifier * PercentHead(mech):0.###}, now {ejectModifiers:0.###}");
            }

            // CT  
            if (PercentCenterTorso(mech) < 1)
            {
                ejectModifiers += ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech);
                Debug($"CT damage adds {ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech):0.###}, now {ejectModifiers:0.###}");
            }

            // these methods deal with missing limbs (0 modifiers get replaced with max modifiers)
            EvalLeftTorso(mech, ref ejectModifiers);
            EvalRightTorso(mech, ref ejectModifiers);
            EvalLeftLeg(mech, ref ejectModifiers);
            EvalRightLeg(mech, ref ejectModifiers);

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed || !w.HasAmmo))
            {
                ejectModifiers += ModSettings.WeaponlessModifier;
                Debug($"Weaponless adds {ModSettings.WeaponlessModifier}, now {ejectModifiers:0.###}");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                ejectModifiers += ModSettings.AloneModifier;
                Debug($"Sole survivor adds {ModSettings.AloneModifier}, now {ejectModifiers:0.###}");
            }

            ejectModifiers -= gutsAndTacticsSum;
            Debug($"Guts and Tactics subtracts {gutsAndTacticsSum}, now {ejectModifiers:0.###}");

            var moraleModifier = ModSettings.MoraleMaxModifier * (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / ModSettings.MedianMorale;
            ejectModifiers -= moraleModifier;
            Debug($"Current morale {mech.Combat.LocalPlayerTeam.Morale} subtracts {moraleModifier:0.###}, now {ejectModifiers:0.###}");

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
            {
                ejectModifiers -= ModSettings.DependableModifier;
                Debug($"Dependable pilot tag subtracts {ModSettings.DependableModifier}, now {ejectModifiers:0.###}");
            }

            // calculate result
            ejectModifiers = Math.Max(0f, (ejectModifiers -
                                           ModSettings.BaseEjectionResist -
                                           ModSettings.GutsEjectionResistPerPoint * guts -
                                           ModSettings.TacticsEjectionResistPerPoint * tactics) *
                                          ModSettings.EjectChanceMultiplier);
            Debug($"After calculation: {ejectModifiers:0.###}");

            var savingThrow = (float) Math.Round(ejectModifiers);

            // will pass through if last straw is met to force an ejection roll
            if (savingThrow <= 0)
            {
                Debug("Resisted ejection");
                return false;
            }

            // modify the roll based on existing pilot panic, and settings
            savingThrow = (int) Math.Min(savingThrow, ModSettings.MaxEjectChance);

            var roll = Rng.Next(1, 101);
            Debug($"Saving throw: {savingThrow}");
            Debug($"Rolled {roll}");

            if (roll >= savingThrow)
            {
                Debug("Made ejection save");
                return false;
            }

            Debug("Failed ejection save: Punchin\' Out!!");
            return true;
        }

        /// <summary>
        ///     returns the sum of all enemy armour and structure
        /// </summary>
        /// <param name="mech"></param>
        /// <returns></returns>
        private static float GetAllEnemiesHealth(Mech mech)
        {
            var enemies = mech.Combat.GetAllEnemiesOf(mech);
            var enemiesHealth = 0f;

            enemiesHealth += enemies.Select(e => e.SummaryArmorCurrent + e.SummaryStructureCurrent).Sum();
            return enemiesHealth;
        }

        // these methods all produce straight percentages
        private static float PercentPilot(Pilot pilot)
        {
            return 1 - (float) pilot.Injuries / pilot.Health;
        }

        private static float PercentRightTorso(Mech mech)
        {
            return 1 -
                   (mech.RightTorsoStructure + mech.RightTorsoFrontArmor + mech.RightTorsoRearArmor) /
                   (mech.MaxStructureForLocation((int) ChassisLocations.RightTorso) + mech.MaxArmorForLocation((int) ArmorLocation.RightTorso) + mech.MaxArmorForLocation((int) ArmorLocation.RightTorsoRear));
        }

        private static float PercentLeftTorso(Mech mech)
        {
            return 1 -
                   (mech.LeftTorsoStructure + mech.LeftTorsoFrontArmor + mech.LeftTorsoRearArmor) /
                   (mech.MaxStructureForLocation((int) ChassisLocations.LeftTorso) + mech.MaxArmorForLocation((int) ArmorLocation.LeftTorso) + mech.MaxArmorForLocation((int) ArmorLocation.LeftTorsoRear));
        }

        private static float PercentCenterTorso(Mech mech)
        {
            return 1 -
                   (mech.CenterTorsoStructure + mech.CenterTorsoFrontArmor + mech.CenterTorsoRearArmor) /
                   (mech.MaxStructureForLocation((int) ChassisLocations.CenterTorso) + mech.MaxArmorForLocation((int) ArmorLocation.CenterTorso) + mech.MaxArmorForLocation((int) ArmorLocation.CenterTorsoRear));
        }

        private static float PercentLeftLeg(Mech mech)
        {
            return 1 - (mech.LeftLegStructure + mech.LeftLegArmor) / (mech.MaxStructureForLocation((int) ChassisLocations.LeftLeg) + mech.MaxArmorForLocation((int) ArmorLocation.LeftLeg));
        }

        private static float PercentRightLeg(Mech mech)
        {
            return 1 - (mech.RightLegStructure + mech.RightLegArmor) / (mech.MaxStructureForLocation((int) ChassisLocations.RightLeg) + mech.MaxArmorForLocation((int) ArmorLocation.RightLeg));
        }

        private static float PercentHead(Mech mech)
        {
            return 1 - (mech.HeadStructure + mech.HeadArmor) / (mech.MaxStructureForLocation((int) ChassisLocations.Head) + mech.MaxArmorForLocation((int) ArmorLocation.Head));
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
                TrackedPilots.Add(new PanicTracker(mech)); // add a new tracker to tracked pilot, then we run it all over again
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

            if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].ChangedRecently && ModSettings.OneChangePerTurn)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        ///     returns true if 10% armour damage was incurred or any structure damage
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        private static bool WasEnoughDamageDone(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (!attackSequence.attackDidDamage)
            {
                Debug("No damage");
                return false;
            }

            Debug($"Attack does {attackSequence.attackArmorDamage} damage, and {attackSequence.attackStructureDamage} to structure");

            if (attackSequence.attackStructureDamage > 0)
            {
                Debug($"{attackSequence.attackStructureDamage} structural damage causes a panic check");
                return true;
            }

            /* + attackSequence.attackArmorDamage believe this isn't necessary because method is called in prefix*/
            if (attackSequence.attackArmorDamage / mech.CurrentArmor * 100 < ModSettings.MinimumArmourDamagePercentageRequired)
            {
                Debug($"Not enough armor damage ({attackSequence.attackArmorDamage})");
                return false;
            }

            Debug($"{attackSequence.attackArmorDamage} damage attack causes a panic check");
            return true;
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
                Debug("UNSETTLED!");
                TrackedPilots[index].PilotStatus = PanicStatus.Unsettled;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.UnsettledAttackModifier);
            }
            else if (TrackedPilots[index].PilotStatus == PanicStatus.Unsettled)
            {
                Debug("STRESSED!");
                TrackedPilots[index].PilotStatus = PanicStatus.Stressed;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.StressedAimModifier);
                mech.StatCollection.ModifyStat("Panic Attack: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, ModSettings.StressedToHitModifier);
            }
            else if (TrackedPilots[index].PilotStatus == PanicStatus.Stressed)
            {
                Debug("PANICKED!");
                TrackedPilots[index].PilotStatus = PanicStatus.Panicked;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Panicking Aim!", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.PanickedAimModifier);
                mech.StatCollection.ModifyStat("Panic Attack: Panicking Defence!", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, ModSettings.PanickedToHitModifier);
            }

            TrackedPilots[index].ChangedRecently = true;
        }

        public static float MechHealth(Mech mech) =>
            (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) /
            (mech.SummaryArmorMax + mech.SummaryStructureMax);

        /// <summary>
        ///     returning true implies they're on their last straw
        /// </summary>
        /// <param name="mech"></param>
        /// <returns></returns>
        public static bool MetLastStraw(Mech mech)
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

            var enemyHealth = GetAllEnemiesHealth(mech);
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID) &&
                enemyHealth >= (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) * 3) // deliberately simple for better or worse (3-to-1 health)
            {
                Debug("Last straw: Sole Survivor, hopeless situation");
                return true;
            }

            return false;
        }

        public class Settings
        {
            public bool Debug = false;
            public bool EnableKnockDownPhrases = false;

            // panic
            public bool PlayersCanPanic = true;
            public bool EnemiesCanPanic = true;
            public float MinimumArmourDamagePercentageRequired = 10;
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
            public float MechHealthForCrit = 90;
            public float CritOver = 70;

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