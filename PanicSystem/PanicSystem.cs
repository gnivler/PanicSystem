using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BattleTech;
using Harmony;
using Newtonsoft.Json;
using static PanicSystem.Controller;
//using static PanicSystem.Logger;
// ReSharper disable All

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public class PanicSystem
    {
        internal static Settings ModSettings = new Settings();
        public static string ActiveJsonPath; //store current tracker here
        public static string StorageJsonPath; //store our meta trackers here
        public static string ModDirectory;
        public static bool KlutzEject;

        public static Random RNG = new Random();

        // forces ejection saves every attack
        public static bool LastStraw = false;

        // makes saving throws harder
        public static bool PanicStarted = false;

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
                if (ModSettings.Debug) Logger.Clear();
            }
            catch
            {
                ModSettings = new Settings();
            }
        }

        /// <summary>
        ///     true implies a panic condition was met
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        public static bool ShouldPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (!CheckCanPanic(mech, attackSequence)) return false;

            var pilot = mech.GetPilot();
            var weapons = mech.Weapons;
            var index = -1;
            var gutAndTacticsSum = mech.SkillGuts * ModSettings.GutsEjectionResistPerPoint + mech.SkillTactics * ModSettings.TacticsEjectionResistPerPoint;
            float panicModifiers = 0;

            index = GetTrackedPilotIndex(mech);

            if (!CheckTrackedPilots(mech, ref index)) return false;

            if (LastStraw) return true;

            if (!WasEnoughDamageDone(mech, attackSequence)) return false;

            Logger.Debug($"Collecting panic modifiers:");

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_brave"))
            {
                panicModifiers -= ModSettings.BraveModifier;
                Logger.Debug($"Bravery adds -{ModSettings.BraveModifier}, modifier now at {panicModifiers:0.###}.");
            }

            if (PercentPilot(pilot) < 1)
            {
                panicModifiers += ModSettings.PilotHealthMaxModifier * PercentPilot(pilot);
                Logger.Debug($"Pilot injuries add {ModSettings.PilotHealthMaxModifier * PercentPilot(pilot):0.###}, modifier now at {panicModifiers:0.###}.");
            }

            if (mech.IsUnsteady)
            {
                panicModifiers += ModSettings.UnsteadyModifier;
                Logger.Debug($"Unsteady adds {ModSettings.UnsteadyModifier}, modifier now at {panicModifiers:0.###}.");
            }

            if (mech.IsFlaggedForKnockdown)
            {
                if (pilot.pilotDef.PilotTags.Contains("pilot_klutz"))
                {
                    Logger.Debug($"Klutz!");

                    if (RNG.Next(1, 101) == 13)
                    {
                        mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"WOOPS!", FloatieMessage.MessageNature.Death, true)));
                        Logger.Debug($"Very klutzy!");
                        KlutzEject = true;
                        return true;
                    }
                }

                panicModifiers += ModSettings.UnsteadyModifier;
                Logger.Debug($"Knockdown adds {ModSettings.UnsteadyModifier}, modifier now at {panicModifiers:0.###}.");
            }

            if (Math.Abs(PercentHead(mech)) != 0 && PercentHead(mech) < 1)
            {
                panicModifiers += ModSettings.HeadMaxModifier * PercentHead(mech);
                Logger.Debug($"Head damage adds {ModSettings.HeadMaxModifier * PercentHead(mech):0.###}, modifier now at {panicModifiers:0.###}.");
            }

            if (PercentCenterTorso(mech) != 0 && PercentCenterTorso(mech) < 1)
            {
                panicModifiers += ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech);
                Logger.Debug($"CT damage adds {ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech):0.###}, modifier now at {panicModifiers:0.###}.");
            }

            // these methods deal with missing limbs (0 modifiers get replaced with max modifiers)
            LeftTorso(mech, ref panicModifiers);
            RightTorso(mech, ref panicModifiers);
            LeftLeg(mech, ref panicModifiers);
            RightLeg(mech, ref panicModifiers);

            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed)) // only fully unusable
            {
                panicModifiers += ModSettings.WeaponlessModifier;
                Logger.Debug($"Weaponless adds {ModSettings.WeaponlessModifier}.");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                panicModifiers += ModSettings.AloneModifier;
                Logger.Debug($"Being alone adds {ModSettings.AloneModifier}, now {panicModifiers:0.###}.");
            }

            panicModifiers -= gutAndTacticsSum;
            Logger.Debug($"Guts and Tactics subtracts {gutAndTacticsSum}, modifier now at {panicModifiers:0.###}.");

            // if (mech.team == mech.Combat.LocalPlayerTeam)
            // {
            var moraleModifier = ModSettings.MoraleMaxModifier * (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / ModSettings.MedianMorale;
            panicModifiers += moraleModifier;
            Logger.Debug($"Curren morale {mech.Combat.LocalPlayerTeam.Morale} adds {moraleModifier:0.###}, modifier now at {panicModifiers:0.###}.");
            //}

            panicModifiers = (float) Math.Max(0f, Math.Round(panicModifiers));
            Logger.Debug($"Roll to beat: {panicModifiers}");

            var rng = RNG.Next(1, 101);
            Logger.Debug($"Rolled: {rng}");

            if (rng < (int) panicModifiers)
            {
                Logger.Debug($"FAILED {panicModifiers}% PANIC SAVE!");
                ApplyPanicDebuff(mech, index);
                return true;
            }

            if (panicModifiers >= 0 && panicModifiers != 0) Logger.Debug($"MADE {panicModifiers}% PANIC SAVE!");

            return false;
        }

        private static void RightLeg(Mech mech, ref float panicModifiers)
        {
            if (mech.RightLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.LeggedMaxModifier;
                Logger.Debug($"RL damage adds {ModSettings.LeggedMaxModifier}, modifier now at {panicModifiers:0.###}.");
            }
            else if (PercentRightLeg(mech) != 0 && PercentRightLeg(mech) < 1)
            {
                panicModifiers += ModSettings.LeggedMaxModifier * PercentRightLeg(mech);
                Logger.Debug($"RL damage adds {ModSettings.LeggedMaxModifier * PercentRightLeg(mech):0.###}, modifier now at {panicModifiers:0.###}.");
            }
        }

        private static void LeftLeg(Mech mech, ref float panicModifiers)
        {
            if (mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.LeggedMaxModifier;
                Logger.Debug($"LL destroyed, damage adds {ModSettings.LeggedMaxModifier}, modifier now at {panicModifiers:0.###}.");
            }
            else if (PercentLeftLeg(mech) != 0 && PercentLeftLeg(mech) < 1)
            {
                panicModifiers += ModSettings.LeggedMaxModifier * PercentLeftLeg(mech);
                Logger.Debug($"LL damage adds {ModSettings.LeggedMaxModifier * PercentLeftLeg(mech):0.###}, modifier now at {panicModifiers:0.###}.");
            }
        }

        private static void RightTorso(Mech mech, ref float panicModifiers)
        {
            if (mech.RightTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier;
                Logger.Debug($"RT destroyed, damage adds {ModSettings.SideTorsoMaxModifier}, modifier now at {panicModifiers:0.###}.");
            }
            else if (PercentRightTorso(mech) != 0 && PercentRightTorso(mech) < 1)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech);
                Logger.Debug($"RT damage adds {ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech):0.###}, modifier now at {panicModifiers:0.###}.");
            }
        }

        private static void LeftTorso(Mech mech, ref float panicModifiers)
        {
            if (mech.LeftTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier;
                Logger.Debug($"LT destroyed, damage adds {ModSettings.SideTorsoMaxModifier:0.###}, modifier now at {panicModifiers:0.###}.");
            }
            else if (PercentLeftTorso(mech) != 0 && PercentLeftTorso(mech) < 1)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech);
                Logger.Debug($"LT damage adds {ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech):0.###}, modifier now at {panicModifiers:0.###}.");
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
            Logger.Debug($"EJECTION CHANCE!");
            if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && !mech.HasHandledDeath) return false;

            // knocked down mechs cannot eject
            if (ModSettings.KnockedDownCannotEject && mech.IsProne) return false;

            if (!attackSequence.attackDidDamage) return false;

            var pilot = mech.GetPilot();
            if (pilot == null) return false;

            var weapons = mech.Weapons;
            var guts = mech.SkillGuts;
            var tactics = mech.SkillTactics;
            var gutsAndTacticsSum = guts + tactics;

            if (!CanEject(mech, guts, pilot, tactics, gutsAndTacticsSum)) return false;

            // start building ejectModifiers
            float ejectModifiers = 0;
            Logger.Debug($"Collecting ejection modifiers:");
            Logger.Debug(new string('-', 60));

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_drunk") && pilot.pilotDef.TimeoutRemaining > 0)
            {
                Logger.Debug("Drunkard - not ejecting!");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                    (new ShowActorInfoSequence(mech, $"..HIC!", FloatieMessage.MessageNature.Buff, true)));
                return false;
            }

            // pilot health
            if (PercentPilot(pilot) < 1)
            {
                ejectModifiers += ModSettings.PilotHealthMaxModifier * PercentPilot(pilot);
                Logger.Debug($"Pilot injury adds {ModSettings.PilotHealthMaxModifier * PercentPilot(pilot):0.###}, modifier now at {ejectModifiers:0.###}.");
            }

            // unsteady
            if (mech.IsUnsteady)
            {
                ejectModifiers += ModSettings.UnsteadyModifier;
                Logger.Debug($"Unsteady adds {ModSettings.UnsteadyModifier}, modifier now at {ejectModifiers:0.###}.");
            }

            // Head
            if (PercentHead(mech) != 0 && PercentHead(mech) < 1)
            {
                ejectModifiers += ModSettings.HeadMaxModifier * PercentHead(mech);
                Logger.Debug($"Head damage adds {ModSettings.HeadMaxModifier * PercentHead(mech):0.###}, modifier now at {ejectModifiers:0.###}.");
            }

            // CT  
            if (PercentCenterTorso(mech) != 0 && PercentCenterTorso(mech) < 1)
            {
                ejectModifiers += ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech);
                Logger.Debug($"CT damage adds {ModSettings.CenterTorsoMaxModifier * PercentCenterTorso(mech):0.###}, modifier now at {ejectModifiers:0.###}.");
            }

            // LT/RT
            if (PercentLeftTorso(mech) != 0 && PercentLeftTorso(mech) < 1)
            {
                ejectModifiers += ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech);
                Logger.Debug($"LT damage adds {ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech):0.###}, modifier now at {ejectModifiers:0.###}.");
            }

            if (PercentRightTorso(mech) != 0 && PercentRightTorso(mech) < 1)
            {
                ejectModifiers += ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech);
                Logger.Debug($"RT damage adds {ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech):0.###}, modifier now at {ejectModifiers:0.###}.");
            }

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed))
            {
                ejectModifiers += ModSettings.WeaponlessModifier;
                Logger.Debug($"Weaponless adds {ModSettings.WeaponlessModifier}, modifier now at {ejectModifiers:0.###}.");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m == mech as AbstractActor || m.IsDead))
            {
                ejectModifiers += ModSettings.AloneModifier;
                Logger.Debug($"Sole survivor adds {ModSettings.AloneModifier}, modifier now at {ejectModifiers:0.###}.");
            }

            //if (mech.team == mech.Combat.LocalPlayerTeam)
            //{
            var moraleModifier = ModSettings.MoraleMaxModifier * (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / ModSettings.MedianMorale * -1;
            ejectModifiers += moraleModifier;
            Logger.Debug($"Current morale {mech.Combat.LocalPlayerTeam.Morale} adds -{moraleModifier:0.###}, modifier now at {ejectModifiers:0.###}.");
            //}

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
            {
                ejectModifiers -= ModSettings.DependableModifier;
                Logger.Debug($"Dependable adds {ModSettings.DependableModifier}, modifier now at {ejectModifiers:0.###}.");
            }

            // calculate result
            ejectModifiers = Math.Max(0f, (ejectModifiers - ModSettings.BaseEjectionResist -
                                           ModSettings.GutsEjectionResistPerPoint * guts - ModSettings.TacticsEjectionResistPerPoint * tactics) * ModSettings.EjectChanceMultiplier);
            Logger.Debug($"After calculation: {ejectModifiers:0.###}");

            var rollToBeat = (float) Math.Round(ejectModifiers);

            // will pass through if last straw is met to force an ejection roll
            if (rollToBeat <= 0 && !IsLastStrawPanicking(mech, ref panicStarted))
            {
                Logger.Debug($"RESISTED EJECTION!");
                return false;
            }

            // modify the roll based on existing pilot panic, and settings
            rollToBeat = !panicStarted ? (int) Math.Min(rollToBeat, ModSettings.MaxEjectChance) : (int) Math.Min(rollToBeat, ModSettings.MaxEjectChanceWhenEarly);

            var roll = RNG.Next(1, 101);
            Logger.Debug($"RollToBeat: {rollToBeat}");
            Logger.Debug($"Rolled: {roll}");

            if (roll >= rollToBeat)
            {
                Logger.Debug($"AVOIDED!");
                return false;
            }

            Logger.Debug($"FAILED SAVE: Punchin' Out!!");
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
                if (index < 0) return false;
            }

            if (TrackedPilots[index].TrackedMech != mech.GUID) return false;

            if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].ChangedRecently && ModSettings.OneChangePerTurn) return false;

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
                Logger.Debug($"No damage.");
                return false;
            }

            Logger.Debug($"Attack does {attackSequence.attackArmorDamage} damage, and {attackSequence.attackStructureDamage} to structure.");

            if (attackSequence.attackStructureDamage > 0)
            {
                Logger.Debug($"{attackSequence.attackStructureDamage} structural damage causes a panic check.");
                return true;
            }

            /* + attackSequence.attackArmorDamage believe this isn't necessary because method is called in prefix*/
            if (attackSequence.attackArmorDamage / mech.CurrentArmor * 100 < ModSettings.MinimumArmourDamagePercentageRequired)
            {
                Logger.Debug($"Not enough armor damage ({attackSequence.attackArmorDamage}).");
                return false;
            }

            Logger.Debug($"{attackSequence.attackArmorDamage} damage attack causes a panic check.");
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
                Logger.Debug($"{mech.DisplayName} incapacitated by {attackSequence.attacker.DisplayName}.");
                return false;
            }

            if (attackSequence == null)
            {
                Logger.Debug($"No attack.");
                return false;
            }

            if (mech.team.IsLocalPlayer && !ModSettings.PlayerTeamCanPanic)
            {
                Logger.Debug($"Players can't panic.");
                return false;
            }

            if (!mech.team.IsLocalPlayer && !ModSettings.EnemiesCanPanic)
            {
                Logger.Debug($"AI can't panic.");
                return false;
            }

            return true;
        }

        //TODO add heat modifier
        //TODO check strucure mods
        //TODO extra modifiers for missing limbs on every roll?  same as damage?
        //TODO arms but how to deal with clip-ons

        /// <summary>
        ///     applied combat modifiers to tracked mechs based on panic status
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="index"></param>
        public static void ApplyPanicDebuff(Mech mech, int index)
        {
            if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].PilotStatus == PanicStatus.Confident)
            {
                Logger.Debug("UNSETTLED!");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"UNSETTLED!", FloatieMessage.MessageNature.Debuff, true)));
                TrackedPilots[index].PilotStatus = PanicStatus.Unsettled;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Unsettled Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.UnsettledAttackModifier);
            }
            else if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].PilotStatus == PanicStatus.Unsettled)
            {
                Logger.Debug("STRESSED!");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"STRESSED!", FloatieMessage.MessageNature.Debuff, true)));
                TrackedPilots[index].PilotStatus = PanicStatus.Stressed;
                mech.StatCollection.ModifyStat("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);
                mech.StatCollection.ModifyStat("Panic Attack: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, ModSettings.StressedAimModifier);
                mech.StatCollection.ModifyStat("Panic Attack: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, ModSettings.StressedToHitModifier);
            }
            else if (TrackedPilots[index].TrackedMech == mech.GUID && TrackedPilots[index].PilotStatus == PanicStatus.Stressed)
            {
                Logger.Debug("PANICKED!");
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
        /// <param name="PanicStarted"></param>
        /// <returns></returns>
        public static bool IsLastStrawPanicking(Mech mech, ref bool PanicStarted)
        {
            if (mech == null || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath) return false;

            var pilot = mech.GetPilot();
            var i = GetTrackedPilotIndex(mech);

            if (pilot != null && !pilot.LethalInjuries && pilot.Health - pilot.Injuries <= ModSettings.MinimumHealthToAlwaysEjectRoll)
            {
                Logger.Debug($"Last straw: Injuries.");
                PanicStarted = true;
                return true;
            }

            if (ModSettings.ConsiderEjectingWithNoWeaps && mech.Weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed))
            {
                Logger.Debug($"Last straw: Weaponless.");
                PanicStarted = true;
                return true;
            }

            var enemyHealth = GetAllEnemiesHealth(mech);

            if (ModSettings.ConsiderEjectingWhenAlone && mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID) &&
                enemyHealth >= (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) * 3) // deliberately simple for better or worse (3-to-1 health)
            {
                Logger.Debug($"Last straw: Sole Survivor, hopeless situation.");
                PanicStarted = true;
                return true;
            }

            if (i > -1)
            {
                if (TrackedPilots[i].TrackedMech == mech.GUID && TrackedPilots[i].PilotStatus == PanicStatus.Panicked)
                {
                    Logger.Debug($"Pilot is panicked!");
                    PanicStarted = true;
                    return true;
                }

                if (CanEjectBeforePanicked(mech, i))
                {
                    Logger.Debug($"Early ejection danger!");
                    PanicStarted = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     true implies this weight class of mech can eject before reaching Panicked
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
                    Logger.Debug($"Considering player mech {mech.VariantName}");
                    if (ModSettings.PlayerLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        Logger.Debug($"Settings can eject early");
                        if (TrackedPilots[i].PilotStatus >= ModSettings.LightMechEarlyEjecthreshold)
                        {
                            Logger.Debug($"Pilot can eject early");
                            return true;
                        }
                    }

                    if (ModSettings.PlayerMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        Logger.Debug($"Medium - bad");
                        if (TrackedPilots[i].PilotStatus >= ModSettings.MediumMechEarlyEjectThreshold) return true;
                    }

                    if (ModSettings.PlayerHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        Logger.Debug($"Heavy - bad");
                        if (TrackedPilots[i].PilotStatus >= ModSettings.HeavyMechEarlyEjectThreshold) return true;
                    }

                    if (ModSettings.PlayerAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        Logger.Debug($"Assault - bad");
                        if (TrackedPilots[i].PilotStatus >= ModSettings.AssaultMechEarlyEjectThreshold) return true;
                    }

                    return false;
                }

                Logger.Debug($"Considering enemy mech {mech.VariantName}");
                if (ModSettings.EnemyLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                {
                    Logger.Debug($"Settings can eject early");
                    if (TrackedPilots[i].PilotStatus >= ModSettings.LightMechEarlyEjecthreshold)
                    {
                        Logger.Debug($"Pilot can eject early");
                        return true;
                    }
                }

                if (ModSettings.EnemyMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                {
                    Logger.Debug($"Medium - bad");
                    if (TrackedPilots[i].PilotStatus >= ModSettings.MediumMechEarlyEjectThreshold) return true;
                }

                if (ModSettings.EnemyHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                {
                    Logger.Debug($"Heavy - bad");
                    if (TrackedPilots[i].PilotStatus >= ModSettings.HeavyMechEarlyEjectThreshold) return true;
                }

                if (ModSettings.EnemyAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                {
                    Logger.Debug($"Assault - bad");
                    if (TrackedPilots[i].PilotStatus >= ModSettings.AssaultMechEarlyEjectThreshold) return true;
                }
            }

            return false;
        }
    }

    public class Settings
    {
        public float AloneModifier = 10;
        public PanicStatus AssaultMechEarlyEjectThreshold = PanicStatus.Stressed;

        public float BaseEjectionResist = 50;
        public float BraveModifier = 5;
        public float CenterTorsoMaxModifier = 45;
        public bool ComboTenAlwaysResists = false;
        public bool ConsiderEjectingWhenAlone = false;

        public bool ConsiderEjectingWithNoWeaps = false;
        public bool Debug = false;
        public float DependableModifier = 5;
        public float EjectChanceMultiplier = 1;
        public bool EnemiesCanPanic = true;
        public bool EnemyAssaultsConsiderEjectingEarly = false;
        public bool EnemyHeaviesConsiderEjectingEarly = false;
        public bool EnemyLightsConsiderEjectingEarly = true;
        public bool EnemyMediumsConsiderEjectingEarly = false;
        public float GutsEjectionResistPerPoint = 2;
        public bool GutsTenAlwaysResists = false;
        public float HeadMaxModifier = 15;
        public PanicStatus HeavyMechEarlyEjectThreshold = PanicStatus.Stressed;
        public bool KnockedDownCannotEject = false;
        public float LeggedMaxModifier = 10;
        public PanicStatus LightMechEarlyEjecthreshold = PanicStatus.Unsettled;
        public bool LosingLimbAlwaysPanics = false;
        public float MaxEjectChance = 50;

        public float MaxEjectChanceWhenEarly = 10;

        public float MedianMorale = 25;
        public PanicStatus MediumMechEarlyEjectThreshold = PanicStatus.Stressed;

        //minmum armour and structure damage
        public float MinimumArmourDamagePercentageRequired = 10; //if no structure damage, a Mech must lost a bit of its armour before it starts worrying

        //ejection
        //+4 difficulty to attacks
        //-2 difficulty to being hit
        public int MinimumHealthToAlwaysEjectRoll = 1;
        public float MoraleMaxModifier = 10;
        public bool OneChangePerTurn = false;
        public float PanickedAimModifier = 2;
        public float PanickedToHitModifier = -2;
        public float PilotHealthMaxModifier = 15;

        public bool PlayerAssaultsConsiderEjectingEarly = false;
        public bool PlayerCharacterAlwaysResists = true;

        public bool PlayerHeaviesConsiderEjectingEarly = false;

        //new mechanics for considering when to eject based on mech class
        public bool PlayerLightsConsiderEjectingEarly = false;

        public bool PlayerMediumsConsiderEjectingEarly = false;
        public bool PlayerTeamCanPanic = true;

        //tag effects
        public bool QuirksEnabled = false;
        public float SideTorsoMaxModifier = 20;
        public float StressedAimModifier = 1;
        public float StressedToHitModifier = -1;
        public float TacticsEjectionResistPerPoint = 0;
        public bool TacticsTenAlwaysResists = false;

        //Unsettled debuffs
        //+1 difficulty to attacks

        //stressed debuffs
        //+2 difficulty to attacks
        //-1 difficulty to being hit
        public float UnsettledAttackModifier = 1;

        public float UnsteadyModifier = 10;
        public float WeaponlessModifier = 10;
    }
}