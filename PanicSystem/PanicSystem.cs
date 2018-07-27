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

        // I put in shitty global bools because it was easiest at the time, sorry!
        // forces ejection saves every attack
        public static bool LastStraw;

        // makes saving throws harder
        public static bool PanicStarted;

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

                Debug("Phrase list:");
                foreach (var phrase in KnockDownPhraseList)
                {
                    Debug(phrase);
                }
            }
            catch (Exception e)
            {
                Error(e);
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
            if (!CheckCanPanic(mech, attackSequence))
            {
                return false;
            }

            var pilot = mech.GetPilot();
            var weapons = mech.Weapons;
            var gutsAndTacticsSum = mech.SkillGuts * ModSettings.GutsEjectionResistPerPoint + mech.SkillTactics * ModSettings.TacticsEjectionResistPerPoint;
            float panicModifiers = 0;
// TODO make sure this isn't fucked
            var index = GetTrackedPilotIndex(mech);

            if (!CheckTrackedPilots(mech, ref index))
            {
                return false;
            }

            if (LastStraw)
            {
                return true;
            }

            if (!WasEnoughDamageDone(mech, attackSequence))
            {
                return false;
            }

            Debug($"Collecting panic modifiers:");


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
                    Debug($"Klutz!");

                    if (Rng.Next(1, 101) == 13)
                    {
                        mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                            (new ShowActorInfoSequence(mech, $"WOOPS!", FloatieMessage.MessageNature.Debuff, true)));
                        Debug($"Very klutzy!");
                        KlutzEject = true;
                        return true;
                    }
                }
                else if (ModSettings.EnableKnockDownPhrases)
                {
                    var message = KnockDownPhraseList[Rng.Next(0, KnockDownPhraseList.Count)];
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                        (new ShowActorInfoSequence(mech, message, FloatieMessage.MessageNature.Debuff, true)));
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
            LeftTorso(mech, ref panicModifiers);
            RightTorso(mech, ref panicModifiers);
            LeftLeg(mech, ref panicModifiers);
            RightLeg(mech, ref panicModifiers);

            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed)) // only fully unusable
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

            var roll = Rng.Next(1, 101);
            Debug($"Rolled {roll}");

            if (roll < (int) panicModifiers)
            {
                Debug($"Failed saving throw");
                ApplyPanicDebuff(mech, index);
                // ReSharper disable once InconsistentNaming
                var i = GetTrackedPilotIndex(mech);
                if (CanEjectBeforePanicked(mech, i))
                {
                    return true;
                }
            }

            Debug($"Made panic saving throw");
            return false;
        }

        private static void RightLeg(Mech mech, ref float panicModifiers)
        {
            if (mech.RightLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.LeggedMaxModifier;
                Debug($"RL destroyed, adds {ModSettings.LeggedMaxModifier}, now {panicModifiers:0.###}");
            }
            else
            {
                panicModifiers += ModSettings.LeggedMaxModifier * PercentRightLeg(mech);
                Debug($"RL damage adds {ModSettings.LeggedMaxModifier * PercentRightLeg(mech):0.###}, now {panicModifiers:0.###}");
            }
        }

        private static void LeftLeg(Mech mech, ref float panicModifiers)
        {
            if (mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.LeggedMaxModifier;
                Debug($"LL destroyed, adds {ModSettings.LeggedMaxModifier}, now {panicModifiers:0.###}");
            }
            else
            {
                panicModifiers += ModSettings.LeggedMaxModifier * PercentLeftLeg(mech);
                Debug($"LL damage adds {ModSettings.LeggedMaxModifier * PercentLeftLeg(mech):0.###}, now {panicModifiers:0.###}");
            }
        }

        private static void RightTorso(Mech mech, ref float panicModifiers)
        {
            if (mech.RightTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier;
                Debug($"RT destroyed, adds {ModSettings.SideTorsoMaxModifier}, now {panicModifiers:0.###}");
            }
            else
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech);
                Debug($"RT damage adds {ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech):0.###}, now {panicModifiers:0.###}");
            }
        }

        private static void LeftTorso(Mech mech, ref float panicModifiers)
        {
            if (mech.LeftTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier;
                Debug($"LT destroyed, adds {ModSettings.SideTorsoMaxModifier:0.###}, now {panicModifiers:0.###}");
            }
            else if (PercentLeftTorso(mech) < 1)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech);
                Debug($"LT damage adds {ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech):0.###}, now {panicModifiers:0.###}");
            }
        }

        /// <summary>
        ///     true implies punchin' out
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <param name="panicStarted"></param>
        /// <returns></returns>
        public static bool RollForEjectionResult(Mech mech, AttackDirector.AttackSequence attackSequence, bool panicStarted)
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

            if (!CanEject(mech, guts, pilot, tactics, gutsAndTacticsSum))
            {
                return false;
            }

            // start building ejectModifiers
            float ejectModifiers = 0;
            Debug($"Collecting ejection modifiers:");
            Debug(new string('-', 60));

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_drunk") && pilot.pilotDef.TimeoutRemaining > 0)
            {
                Debug("Drunkard - not ejecting");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                    (new ShowActorInfoSequence(mech, $"..HIC!", FloatieMessage.MessageNature.Buff, true)));
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

            // LT/RT
            if (PercentLeftTorso(mech) < 1)
            {
                ejectModifiers += ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech);
                Debug($"LT damage adds {ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech):0.###}, now {ejectModifiers:0.###}");
            }

            if (PercentRightTorso(mech) < 1)
            {
                ejectModifiers += ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech);
                Debug($"RT damage adds {ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech):0.###}, now {ejectModifiers:0.###}");
            }

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed))
            {
                ejectModifiers += ModSettings.WeaponlessModifier;
                Debug($"Weaponless adds {ModSettings.WeaponlessModifier}, now {ejectModifiers:0.###}");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m == mech as AbstractActor || m.IsDead))
            {
                ejectModifiers += ModSettings.AloneModifier;
                Debug($"Sole survivor adds {ModSettings.AloneModifier}, now {ejectModifiers:0.###}");
            }

            var moraleModifier = ModSettings.MoraleMaxModifier * (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / ModSettings.MedianMorale;
            ejectModifiers -= moraleModifier;
            Debug($"Current morale {mech.Combat.LocalPlayerTeam.Morale} subtracts {moraleModifier:0.###}, now {ejectModifiers:0.###}");

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
            {
                ejectModifiers -= ModSettings.DependableModifier;
                Debug($"Dependable pilot tag subtracts {ModSettings.DependableModifier}, now {ejectModifiers:0.###}");
            }

            // calculate result
            ejectModifiers = Math.Max(0f, (ejectModifiers - ModSettings.BaseEjectionResist -
                                           ModSettings.GutsEjectionResistPerPoint * guts - ModSettings.TacticsEjectionResistPerPoint * tactics) * ModSettings.EjectChanceMultiplier);
            Debug($"After calculation: {ejectModifiers:0.###}");

            var savingThrow = (float) Math.Round(ejectModifiers);

            // will pass through if last straw is met to force an ejection roll
            if (savingThrow <= 0 && !IsLastStrawPanicking(mech, ref panicStarted))
            {
                Debug($"Resisted ejection");
                return false;
            }

            // modify the roll based on existing pilot panic, and settings
            savingThrow = !panicStarted ? (int) Math.Min(savingThrow, ModSettings.MaxEjectChance) : (int) Math.Min(savingThrow, ModSettings.MaxEjectChanceWhenEarly);

            var roll = Rng.Next(1, 101);
            Debug($"Saving throw: {savingThrow}");
            Debug($"Rolled {roll}");

            if (roll >= savingThrow)
            {
                Debug($"Made ejection saving throw");
                return false;
            }

            Debug($"Failed ejection saving throw: Punchin' Out!!");
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
        private static bool CheckTrackedPilots(Mech mech, ref int index)
        {
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
                Debug($"No damage");
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
        ///     true implies ejection is possible
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="guts"></param>
        /// <param name="pilot"></param>
        /// <param name="tactics"></param>
        /// <param name="gutsAndTacticsSum"></param>
        /// <returns></returns>
        private static bool CanEject(Mech mech, int guts, Pilot pilot, int tactics, int gutsAndTacticsSum)
        {
            // guts 10 makes you immune, player character cannot be forced to eject
            if (guts == 10 && ModSettings.GutsTenAlwaysResists || ModSettings.PlayerCharacterAlwaysResists && pilot.IsPlayerCharacter) return false;

            // tactics 10 makes you immune, or combination of guts and tactics makes you immune.
            if (tactics == 10 && ModSettings.TacticsTenAlwaysResists || gutsAndTacticsSum >= 10 && ModSettings.ComboTenAlwaysResists) return false;

            // pilots that cannot eject or be headshot shouldn't eject
            if (mech != null && !mech.CanBeHeadShot || !pilot.CanEject) return false;

            return true;
        }

        /// <summary>
        ///     true implies panic is possible
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        private static bool CheckCanPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
            {
                Debug($"{mech?.DisplayName} incapacitated by {attackSequence.attacker.DisplayName}");
                return false;
            }

            if (attackSequence == null)
            {
                Debug($"No attack");
                return false;
            }

            if (mech.team.IsLocalPlayer && !ModSettings.PlayersCanPanic)
            {
                Debug($"Players can't panic");
                return false;
            }

            if (!mech.team.IsLocalPlayer && !ModSettings.EnemiesCanPanic)
            {
                Debug($"AI can't panic");
                return false;
            }

            return true;
        }

        /// <summary>
        /// applies combat modifiers to tracked mechs based on panic status
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="index"></param>
        public static void ApplyPanicDebuff(Mech mech, int index)
        {
            if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].PilotStatus == PanicStatus.Confident)
            {
                Debug("UNSETTLED!");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"UNSETTLED!", FloatieMessage.MessageNature.Debuff, true)));
                TrackedPilots[index].PilotStatus = PanicStatus.Unsettled;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.UnsettledAttackModifier);
            }
            else if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].PilotStatus == PanicStatus.Unsettled)
            {
                Debug("STRESSED!");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"STRESSED!", FloatieMessage.MessageNature.Debuff, true)));
                TrackedPilots[index].PilotStatus = PanicStatus.Stressed;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.StressedAimModifier);
                mech.StatCollection.ModifyStat("Panic Attack: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, ModSettings.StressedToHitModifier);
            }
            else if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].PilotStatus == PanicStatus.Stressed)
            {
                Debug("PANICKED!");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"PANICKED!", FloatieMessage.MessageNature.Debuff, true)));
                TrackedPilots[index].PilotStatus = PanicStatus.Panicked;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Panicking Aim!", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.PanickedAimModifier);
                mech.StatCollection.ModifyStat("Panic Attack: Panicking Defence!", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, ModSettings.PanickedToHitModifier);
            }

            TrackedPilots[index].ChangedRecently = true;
        }

        /// <summary>
        ///     returning true and ref true here implies they're on their last straw
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="panicStarted"></param>
        /// <returns></returns>
        public static bool IsLastStrawPanicking(Mech mech, ref bool panicStarted)
        {
            if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath) return false;

            var pilot = mech.GetPilot();
            var i = GetTrackedPilotIndex(mech);

            if (pilot != null && !pilot.LethalInjuries && pilot.Health - pilot.Injuries <= ModSettings.MinimumHealthToAlwaysEjectRoll)
            {
                Debug($"Last straw: Injuries");
                panicStarted = true;
                return true;
            }

            if (ModSettings.ConsiderEjectingWithNoWeaps && mech.Weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed))
            {
                Debug($"Last straw: Weaponless");
                panicStarted = true;
                return true;
            }

            var enemyHealth = GetAllEnemiesHealth(mech);

            if (ModSettings.ConsiderEjectingWhenAlone && mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID) &&
                enemyHealth >= (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) * 3) // deliberately simple for better or worse (3-to-1 health)
            {
                Debug($"Last straw: Sole Survivor, hopeless situation");
                panicStarted = true;
                return true;
            }

            if (i > -1)
            {
                if (TrackedPilots[i].TrackedMech == mech.GUID && TrackedPilots[i].PilotStatus == PanicStatus.Panicked)
                {
                    Debug($"Pilot is panicked!");
                    panicStarted = true;
                    return true;
                }

                if (CanEjectBeforePanicked(mech, i))
                {
                    Debug($"Early ejection danger!");
                    panicStarted = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///  true implies this weight class of mech can eject before reaching Panicked
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="i"></param>
        /// <returns></returns>
        private static bool CanEjectBeforePanicked(Mech mech, int i)
        {
            if (TrackedPilots[i].TrackedMech == mech.GUID)
            {
                if (mech.team.IsLocalPlayer)
                {
                    Debug($"Considering player {mech.DisplayName} for early panic");
                    if (ModSettings.EjectEarlyPlayerLight && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.EjectThresholdLight)
                        {
                            Debug($"Pilot can eject early");
                            return true;
                        }
                    }

                    if (ModSettings.EjectEarlyPlayerMedium && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.EjectThresholdMedium)
                        {
                            return true;
                        }
                    }

                    if (ModSettings.EjectEarlyPlayerHeavy && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.EjectThresholdHeavy)
                        {
                            return true;
                        }
                    }

                    if (ModSettings.EjectEarlyPlayerAssault && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.EjectThresholdAssault)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                Debug($"Considering enemy {mech.DisplayName} for early panic");
                if (ModSettings.EjectEarlyEnemyLight && mech.weightClass == WeightClass.LIGHT)
                {
                    if (TrackedPilots[i].PilotStatus >= ModSettings.EjectThresholdLight)
                    {
                        Debug($"Pilot can eject early");
                        return true;
                    }
                }

                if (ModSettings.EjectEarlyEnemyMedium && mech.weightClass == WeightClass.MEDIUM)
                {
                    if (TrackedPilots[i].PilotStatus >= ModSettings.EjectThresholdMedium)
                    {
                        return true;
                    }
                }

                if (ModSettings.EjectEarlyEnemyHeavy && mech.weightClass == WeightClass.HEAVY)
                {
                    if (TrackedPilots[i].PilotStatus >= ModSettings.EjectThresholdHeavy)
                    {
                        return true;
                    }
                }

                if (ModSettings.EjectEarlyEnemyAssault && mech.weightClass == WeightClass.ASSAULT)
                {
                    if (TrackedPilots[i].PilotStatus >= ModSettings.EjectThresholdAssault)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public class Settings
    {
        public bool Debug = false;
        public bool EnableKnockDownPhrases = false;

        // panic
        public bool PlayerCharacterAlwaysResists = true;
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
        public float MedianMorale = 25;
        public float MoraleMaxModifier = 10;

        // Quirks
        public bool QuirksEnabled = false;
        public float BraveModifier = 5;
        public float DependableModifier = 5;

        // ejection
        public bool EjectEarlyPlayerLight = true;
        public bool EjectEarlyEnemyLight = true;
        public PanicStatus EjectThresholdLight = PanicStatus.Stressed;

        public bool EjectEarlyPlayerMedium = false;
        public bool EjectEarlyEnemyMedium = false;
        public PanicStatus EjectThresholdMedium = PanicStatus.Stressed;

        public bool EjectEarlyPlayerHeavy = false;
        public bool EjectEarlyEnemyHeavy = false;
        public PanicStatus EjectThresholdHeavy = PanicStatus.Stressed;

        public bool EjectEarlyPlayerAssault = false;
        public bool EjectEarlyEnemyAssault = false;
        public PanicStatus EjectThresholdAssault = PanicStatus.Stressed;
        public int MinimumHealthToAlwaysEjectRoll = 1;
        public float MaxEjectChance = 50;
        public float EjectChanceMultiplier = 1;
        public bool GutsTenAlwaysResists = false;
        public bool ComboTenAlwaysResists = false;
        public bool TacticsTenAlwaysResists = false;
        public bool KnockedDownCannotEject = false;
        public bool ConsiderEjectingWithNoWeaps = false;
        public bool ConsiderEjectingWhenAlone = false;
        public float BaseEjectionResist = 50;
        public float GutsEjectionResistPerPoint = 2;
        public float TacticsEjectionResistPerPoint = 0;
        public float MaxEjectChanceWhenEarly = 10;
    }
}