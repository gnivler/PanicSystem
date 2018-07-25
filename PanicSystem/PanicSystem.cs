using BattleTech;
using Harmony;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using static PanicSystem.Controller;
using static PanicSystem.Logger;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public class PanicSystem
    {
        internal static Settings ModSettings = new Settings();
        public static string ActiveJsonPath; //store current tracker here
        public static string StorageJsonPath; //store our meta trackers here
        public static string ModDirectory;
        public static bool KlutzEject = false;

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
                if (ModSettings.Debug || ModSettings.EnableDebug)
                {
                    Clear();
                }
            }
            catch
            {
                ModSettings = new Settings();
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

            Pilot pilot = mech.GetPilot();
            var weapons = mech.Weapons;
            int index = -1;
            float gutAndTacticsSum = mech.SkillGuts * ModSettings.GutsEjectionResistPerPoint + mech.SkillTactics * ModSettings.TacticsEjectionResistPerPoint;
            float panicModifiers = 0;

            index = GetTrackedPilotIndex(mech);

            if (LastStraw)
            {
                return true;
            }

            if (!WasEnoughDamageDone(mech, attackSequence))
            {
                return false;
            }

            Debug($"Collecting panic modifiers:");

            if (!CheckTrackedPilots(mech, ref index))
            {
                return false;
            }

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_brave"))
            {
                panicModifiers -= ModSettings.BraveModifier;
                Debug($"Bravery subtracts {ModSettings.BraveModifier}, modifier now at {panicModifiers:0.###}.");
            }

            if (PercentPilot(pilot) < 1)
            {
                panicModifiers += ModSettings.PilotHealthMaxModifier * (PercentPilot(pilot));
                Debug($"Pilot injuries add {ModSettings.PilotHealthMaxModifier * (PercentPilot(pilot)):0.###}, modifier now at {panicModifiers:0.###}.");
            }

            if (mech.IsUnsteady)
            {
                panicModifiers += ModSettings.UnsteadyModifier;
                Debug($"Unsteady adds {ModSettings.UnsteadyModifier}, modifier now at {panicModifiers:0.###}.");
            }

            if (mech.IsFlaggedForKnockdown && pilot.pilotDef.PilotTags.Contains("pilot_klutz"))
            {
                Debug($"Klutz!");
                if (RNG.Next(1, 101) == 13)
                {
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"WOOPS!", FloatieMessage.MessageNature.Death, true)));
                    Debug($"Very klutzy!");
                    KlutzEject = true;
                    return true;
                }

                panicModifiers += ModSettings.UnsteadyModifier;
                Debug($"Knockdown adds {ModSettings.UnsteadyModifier}, modifier now at {panicModifiers:0.###}.");
            }

            if (PercentHead(mech) < 1)
            {
                panicModifiers += ModSettings.HeadMaxModifier * (PercentHead(mech));
                Debug($"Head damage adds {ModSettings.HeadMaxModifier * (PercentHead(mech)):0.###}, modifier now at {panicModifiers:0.###}.");
            }

            if (PercentCenterTorso(mech) < 1)
            {
                panicModifiers += ModSettings.CenterTorsoMaxModifier * (PercentCenterTorso(mech));
                Debug($"CT damage adds {ModSettings.CenterTorsoMaxModifier * (PercentCenterTorso(mech)):0.###}, modifier now at {panicModifiers:0.###}.");
            }

            // these methods deal with missing limbs (0 modifiers get replaced with max modifiers)
            LeftTorso(mech, ref panicModifiers);
            RightTorso(mech, ref panicModifiers);
            LeftLeg(mech, ref panicModifiers);
            RightLeg(mech, ref panicModifiers);

            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed)) // only fully unusable
            {
                panicModifiers += ModSettings.WeaponlessModifier;
                Debug($"Weaponless adds {ModSettings.WeaponlessModifier}.");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                panicModifiers += ModSettings.AloneModifier;
                Debug($"Being alone adds {ModSettings.AloneModifier}, now {panicModifiers:0.###}.");
            }

            panicModifiers -= gutAndTacticsSum;
            Debug($"Guts and Tactics subtracts {gutAndTacticsSum}, modifier now at {panicModifiers:0.###}.");

            if (mech.team == mech.Combat.LocalPlayerTeam)
            {
                var moraleModifier = ModSettings.MoraleMaxModifier * (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / ModSettings.MedianMorale;
                panicModifiers += moraleModifier;
                Debug($"Curren morale {mech.Combat.LocalPlayerTeam.Morale} adds {moraleModifier:0.###}, modifier now at {panicModifiers:0.###}.");
            }

            panicModifiers = (float) Math.Max(0f, Math.Round(panicModifiers));
            Debug($"Roll to beat: {panicModifiers}");

            var rng = RNG.Next(1, 101);
            Debug($"Rolled: {rng}");

            if (rng < (int) panicModifiers)
            {
                Debug($"FAILED {panicModifiers}% PANIC SAVE!");
                ApplyPanicDebuff(mech, index);
            }
            else if (panicModifiers >= 0 && panicModifiers != 0)
            {
                Debug($"MADE {panicModifiers}% PANIC SAVE!");
            }

            return false;
        }

        private static void RightLeg(Mech mech, ref float panicModifiers)
        {
            if (mech.RightLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.LeggedMaxModifier;
                Debug($"RL damage adds {ModSettings.LeggedMaxModifier}, modifier now at {panicModifiers:0.###}.");
            }
            else if (PercentRightLeg(mech) < 1)
            {
                panicModifiers += ModSettings.LeggedMaxModifier * PercentRightLeg(mech);
                Debug($"RL damage adds {ModSettings.LeggedMaxModifier * PercentRightLeg(mech):0.###}, modifier now at {panicModifiers:0.###}.");
            }
        }

        private static void LeftLeg(Mech mech, ref float panicModifiers)
        {
            if (mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.LeggedMaxModifier;
                Debug($"LL destroyed, damage adds {ModSettings.LeggedMaxModifier}, modifier now at {panicModifiers:0.###}.");
            }
            else if (PercentLeftLeg(mech) < 1)
            {
                panicModifiers += ModSettings.LeggedMaxModifier * PercentLeftLeg(mech);
                Debug($"LL damage adds {ModSettings.LeggedMaxModifier * PercentLeftLeg(mech):0.###}, modifier now at {panicModifiers:0.###}.");
            }
        }

        private static void RightTorso(Mech mech, ref float panicModifiers)
        {
            if (mech.RightTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier;
                Debug($"RT destroyed, damage adds {ModSettings.SideTorsoMaxModifier}, modifier now at {panicModifiers:0.###}.");
            }
            else if (PercentRightTorso(mech) < 1)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech);
                Debug($"RT damage adds {ModSettings.SideTorsoMaxModifier * PercentRightTorso(mech):0.###}, modifier now at {panicModifiers:0.###}.");
            }
        }

        private static void LeftTorso(Mech mech, ref float panicModifiers)
        {
            if (mech.LeftTorsoDamageLevel == LocationDamageLevel.Destroyed)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier;
                Debug($"LT destroyed, damage adds {ModSettings.SideTorsoMaxModifier:0.###}, modifier now at {panicModifiers:0.###}.");
            }
            else if (PercentLeftTorso(mech) < 1)
            {
                panicModifiers += ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech);
                Debug($"LT damage adds {ModSettings.SideTorsoMaxModifier * PercentLeftTorso(mech):0.###}, modifier now at {panicModifiers:0.###}.");
            }
        }

        /// <summary>
        /// true implies punchin' out
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <param name="panicStarted"></param>
        /// <returns></returns>
        public static bool RollForEjectionResult(Mech mech, AttackDirector.AttackSequence attackSequence, bool panicStarted)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && !mech.HasHandledDeath))
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

            Pilot pilot = mech.GetPilot();
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
            Debug(new string(c: '-', count: 60));

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_drunk") && pilot.pilotDef.TimeoutRemaining > 0)
            {
                Debug("Drunkard - not ejecting!");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"..HIC!", FloatieMessage.MessageNature.Inspiration, true))); // hopefully this doesn't inspire the lance
                return false;
            }

            // pilot health
            float pilotHealthPercent = 1f - ((float) pilot.Injuries / pilot.Health);
            if (pilotHealthPercent < 1)
            {
                ejectModifiers += ModSettings.PilotHealthMaxModifier * (1f - pilotHealthPercent);
                Debug($"Pilot Health: {ejectModifiers}");
            }

            // unsteady
            if (mech.IsUnsteady)
            {
                ejectModifiers += ModSettings.UnsteadyModifier;
                Debug($"Unsteady: {ejectModifiers}");
            }

            // Head
            var headHealthPercent = (mech.HeadArmor + mech.HeadStructure) / (mech.GetMaxArmor(ArmorLocation.Head) + mech.GetMaxStructure(ChassisLocations.Head));
            if (headHealthPercent < 1)
            {
                ejectModifiers += ModSettings.HeadMaxModifier * (1f - headHealthPercent);
                Debug($"Head Damage: {ejectModifiers}");
            }

            // CT  
            var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure + mech.CenterTorsoRearArmor) / (mech.GetMaxArmor(ArmorLocation.CenterTorso) + mech.GetMaxStructure(ChassisLocations.CenterTorso));
            if (ctPercent < 1)
            {
                ejectModifiers += ModSettings.CenterTorsoMaxModifier * (1f - ctPercent);
                Debug($"CT Damage: {ejectModifiers}");
            }

            // LT/RT
            var ltStructurePercent = mech.LeftTorsoStructure / mech.GetMaxStructure(ChassisLocations.LeftTorso);
            if (ltStructurePercent < 1)
            {
                ejectModifiers += ModSettings.SideTorsoMaxModifier * (1f - ltStructurePercent);
            }

            Debug($"LT Damage: {ejectModifiers}");

            var rtStructurePercent = mech.RightTorsoStructure / mech.GetMaxStructure(ChassisLocations.RightTorso);
            if (rtStructurePercent < 1)
            {
                ejectModifiers += ModSettings.SideTorsoMaxModifier * (1f - rtStructurePercent);
                Debug($"RT Damage: {ejectModifiers}");
            }

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed))
            {
                ejectModifiers += ModSettings.WeaponlessModifier;
                Debug($"Weaponless: {ejectModifiers}");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m == mech as AbstractActor || m.IsDead))
            {
                ejectModifiers += ModSettings.AloneModifier;
                Debug($"Sole Survivor: {ejectModifiers}");
            }

            if (mech.team == mech.Combat.LocalPlayerTeam)
            {
                ejectModifiers -= (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / 8;
                Debug($"Morale: {ejectModifiers}");
            }

            if (ModSettings.QuirksEnabled && pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
            {
                ejectModifiers -= ModSettings.DependableModifier;
                Debug($"Dependable: {ejectModifiers}");
            }

            // calculate result
            ejectModifiers = Math.Max(0f, (ejectModifiers - ModSettings.BaseEjectionResist - (ModSettings.GutsEjectionResistPerPoint * guts) - (ModSettings.TacticsEjectionResistPerPoint * tactics)) * ModSettings.EjectChanceMultiplier);
            Debug($"After calculation: {ejectModifiers}");

            var rollToBeat = (float) Math.Round(ejectModifiers);
            Debug($"Final roll to beat: {rollToBeat}");

            // will pass through if last straw is met to force an ejection roll
            if (rollToBeat <= 0 && !IsLastStrawPanicking(mech, ref panicStarted))
            {
                //mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"RESISTED EJECTION!", FloatieMessage.MessageNature.Buff, true)));
                Debug($"RESISTED EJECTION!");
                return false;
            }

            // modify the roll based on existing pilot panic, and settings
            rollToBeat = (!panicStarted) ? Math.Min(rollToBeat, ModSettings.MaxEjectChance) : Math.Min(rollToBeat, ModSettings.MaxEjectChanceWhenEarlyEjectThresholdMet);

            var roll = RNG.Next(1, 101);
            Debug($"RollToBeat: {rollToBeat}");
            Debug($"Rolled: {roll}");
            Debug($"{rollToBeat}% EJECTION CHANCE!");

            //mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"{rollToBeat}% EJECTION CHANCE!", FloatieMessage.MessageNature.Debuff, true)));
            if (roll >= rollToBeat)
            {
                Debug($"AVOIDED!");
                //mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"AVOIDED!", FloatieMessage.MessageNature.Buff, true)));
                return false;
            }

            Debug($"FAILED SAVE: Punchin' Out!!");
            return true;
        }

        private static float GetAllEnemiesHealth(Mech mech)
        {
            var enemies = mech.Combat.GetAllEnemiesOf(mech);
            float enemiesHealth = 0f;
            foreach (var enemy in enemies)
            {
                enemiesHealth += enemy.SummaryArmorCurrent + enemy.SummaryStructureCurrent;
            }

            return enemiesHealth;
        }

        //TODO  overheating, ally destroyed

        private static float PercentPilot(Pilot pilot) => 1 - (float) pilot.Injuries / pilot.Health;

        private static float PercentRightTorso(Mech mech) =>
            1 -
            (mech.RightTorsoStructure + mech.RightTorsoFrontArmor + mech.RightTorsoRearArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.RightTorso) + mech.MaxArmorForLocation((int) ArmorLocation.RightTorso) + mech.MaxArmorForLocation((int) ArmorLocation.RightTorsoRear));

        private static float PercentLeftTorso(Mech mech) =>
            1 -
            (mech.LeftTorsoStructure + mech.LeftTorsoFrontArmor + mech.LeftTorsoRearArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.LeftTorso) + mech.MaxArmorForLocation((int) ArmorLocation.LeftTorso) + mech.MaxArmorForLocation((int) ArmorLocation.LeftTorsoRear));

        private static float PercentCenterTorso(Mech mech) =>
            1 -
            (mech.CenterTorsoStructure + mech.CenterTorsoFrontArmor + mech.CenterTorsoRearArmor) /
            (mech.MaxStructureForLocation((int) ChassisLocations.CenterTorso) + mech.MaxArmorForLocation((int) ArmorLocation.CenterTorso) + mech.MaxArmorForLocation((int) ArmorLocation.CenterTorsoRear));

        private static float PercentLeftLeg(Mech mech) => 1 - (mech.LeftLegStructure + mech.LeftLegArmor) / (mech.MaxStructureForLocation((int) ChassisLocations.LeftLeg) + mech.MaxArmorForLocation((int) ArmorLocation.LeftLeg));
        private static float PercentRightLeg(Mech mech) => 1 - (mech.RightLegStructure + mech.RightLegArmor) / (mech.MaxStructureForLocation((int) ChassisLocations.RightLeg) + mech.MaxArmorForLocation((int) ArmorLocation.RightLeg));
        private static float PercentHead(Mech mech) => 1 - (mech.HeadStructure + mech.HeadArmor) / (mech.MaxStructureForLocation((int) ChassisLocations.Head) + mech.MaxArmorForLocation((int) ArmorLocation.Head));

        private static bool CheckTrackedPilots(Mech mech, ref int index)
        {
            if (index < 0)
            {
                TrackedPilots.Add(new PanicTracker(mech)); //add a new tracker to tracked pilot, then we run it all over again;
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

        private static bool WasEnoughDamageDone(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (!attackSequence.attackDidDamage)
            {
                Debug($"No damage.");
                return false;
            }
            else
            {
                Debug($"Attack does {attackSequence.attackArmorDamage} damage, with {attackSequence.attackStructureDamage} to structure.");
            }

            if (attackSequence.attackStructureDamage > 0)
            {
                {
                    Debug($"{attackSequence.attackStructureDamage} structural damage causes a panic check.");
                    return true;
                }
            }

            /* + attackSequence.attackArmorDamage believe this isn't necessary because method is called in prefix*/
            if (attackSequence.attackArmorDamage / (mech.CurrentArmor) * 100 < ModSettings.MinimumArmourDamagePercentageRequired)
            {
                Debug($"Not enough armor damage ({attackSequence.attackArmorDamage}).");
                return false;
            }

            Debug($"{attackSequence.attackArmorDamage} damage attack causes a panic check.");
            return true;
        }

        /// <summary>
        /// true implies ejection is possible
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
            if ((guts == 10 && ModSettings.GutsTenAlwaysResists) || (ModSettings.PlayerCharacterAlwaysResists && pilot.IsPlayerCharacter))
            {
                return false;
            }

            // tactics 10 makes you immune, or combination of guts and tactics makes you immune.
            if ((tactics == 10 && ModSettings.TacticsTenAlwaysResists) || (gutsAndTacticsSum >= 10 && ModSettings.ComboTenAlwaysResists))
            {
                return false;
            }

            // pilots that cannot eject or be headshot shouldn't eject
            if (mech != null && !mech.CanBeHeadShot || !pilot.CanEject)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        ///  true implies panic is possible
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        private static bool CheckCanPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
            {
                Debug($"{mech.DisplayName} incapacitated by {attackSequence.attacker.DisplayName}.");
                return false;
            }

            if (attackSequence == null)
            {
                Debug($"No attack.");
                return false;
            }

            if (mech.team.IsLocalPlayer && !ModSettings.PlayerTeamCanPanic)
            {
                Debug($"Players can't panic.");
                return false;
            }

            if (!mech.team.IsLocalPlayer && !ModSettings.EnemiesCanPanic)
            {
                Debug($"AI can't panic.");
                return false;
            }

            return true;
        }

        //TODO add heat modifier
        //TODO check strucure mods
        //TODO extra modifiers for missing limbs on every roll?  same as damage?
        //TODO arms but how to deal with clip-ons

        /// <summary>
        /// applied combat modifiers to tracked mechs based on panic status
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
        /// returning true and ref here implies they're on their last straw
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="PanicStarted"></param>
        /// <returns></returns>
        public static bool IsLastStrawPanicking(Mech mech, ref bool PanicStarted)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
            {
                return false;
            }

            Pilot pilot = mech.GetPilot();
            int i = GetTrackedPilotIndex(mech);

            if (pilot != null && !pilot.LethalInjuries && pilot.Health - pilot.Injuries <= ModSettings.MinimumHealthToAlwaysEjectRoll)
            {
                Debug($"Last straw: Injuries.");
                //mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"INJURY PANIC!", FloatieMessage.MessageNature.Debuff, true)));
                PanicStarted = true;
                return true;
            }

            if (mech.Weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional) && ModSettings.ConsiderEjectingWithNoWeaps)
            {
                Debug($"Last straw: Weaponless.");
                //mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"WEAPONLESS PANIC!", FloatieMessage.MessageNature.Debuff, true)));
                PanicStarted = true;
                return true;
            }

            var enemyHealth = GetAllEnemiesHealth(mech);

            if (ModSettings.ConsiderEjectingWhenAlone && mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID) && enemyHealth >= (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) * 3)
            {
                Debug($"Last straw: Sole Survivor, hopeless situation.");
                //mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"HOPELESS SITUATION!", FloatieMessage.MessageNature.Debuff, true)));
                PanicStarted = true;
                return true;
            }

            if (i > -1)
            {
                if (TrackedPilots[i].TrackedMech == mech.GUID && TrackedPilots[i].PilotStatus == PanicStatus.Panicked)
                {
                    Debug($"Pilot is panicked!");
                    PanicStarted = true;
                    return true;
                }

                if (CanEjectBeforePanicked(mech, i))
                {
                    Debug($"Early ejection danger!");
                    PanicStarted = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// true implies this weight class of mech can eject before reaching Panicked
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="i"></param>
        /// <returns></returns>
        private static bool CanEjectBeforePanicked(Mech mech, int i)
        {
            if (TrackedPilots[i].TrackedMech == mech.GUID && mech.team.IsLocalPlayer)
            {
                if (mech.team.IsLocalPlayer)
                {
                    if (ModSettings.PlayerLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.LightMechEarlyEjecthreshold)
                        {
                            return true;
                        }
                    }

                    else if (ModSettings.PlayerMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.MediumMechEarlyEjectThreshold)
                        {
                            return true;
                        }
                    }

                    else if (ModSettings.PlayerHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.HeavyMechEarlyEjectThreshold)
                        {
                            return true;
                        }
                    }

                    else if (ModSettings.PlayerAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.AssaultMechEarlyEjectThreshold)
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    if (ModSettings.EnemyLightsConsiderEjectingEarly && mech.weightClass == WeightClass.LIGHT)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.LightMechEarlyEjecthreshold)
                        {
                            return true;
                        }
                    }

                    else if (ModSettings.EnemyMediumsConsiderEjectingEarly && mech.weightClass == WeightClass.MEDIUM)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.MediumMechEarlyEjectThreshold)
                        {
                            return true;
                        }
                    }

                    else if (ModSettings.EnemyHeaviesConsiderEjectingEarly && mech.weightClass == WeightClass.HEAVY)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.HeavyMechEarlyEjectThreshold)
                        {
                            return true;
                        }
                    }

                    else if (ModSettings.EnemyAssaultsConsiderEjectingEarly && mech.weightClass == WeightClass.ASSAULT)
                    {
                        if (TrackedPilots[i].PilotStatus >= ModSettings.AssaultMechEarlyEjectThreshold)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }

    public class Settings
    {
        public bool PlayerCharacterAlwaysResists = true;
        public bool PlayerTeamCanPanic = true;
        public bool EnemiesCanPanic = true;
        public bool Debug = false;
        public bool EnableDebug = false; // legacy compatibility

        //new mechanics for considering when to eject based on mech class
        public bool PlayerLightsConsiderEjectingEarly = false;
        public bool EnemyLightsConsiderEjectingEarly = true;
        public PanicStatus LightMechEarlyEjecthreshold = PanicStatus.Unsettled;

        public bool PlayerMediumsConsiderEjectingEarly = false;
        public bool EnemyMediumsConsiderEjectingEarly = false;
        public PanicStatus MediumMechEarlyEjectThreshold = PanicStatus.Stressed;

        public bool PlayerHeaviesConsiderEjectingEarly = false;
        public bool EnemyHeaviesConsiderEjectingEarly = false;
        public PanicStatus HeavyMechEarlyEjectThreshold = PanicStatus.Stressed;

        public bool PlayerAssaultsConsiderEjectingEarly = false;
        public bool EnemyAssaultsConsiderEjectingEarly = false;
        public PanicStatus AssaultMechEarlyEjectThreshold = PanicStatus.Stressed;

        public float MaxEjectChanceWhenEarlyEjectThresholdMet = 10;

        //minmum armour and structure damage
        public float MinimumArmourDamagePercentageRequired = 10; //if no structure damage, a Mech must lost a bit of its armour before it starts worrying
        public bool OneChangePerTurn = false;
        public bool LosingLimbAlwaysPanics = false;

        public float MedianMorale = 25;
        public float MoraleMaxModifier = 10;

        //tag effects
        public bool QuirksEnabled = false;
        public float BraveModifier = 5;
        public float DependableModifier = 5;

        //Unsettled debuffs
        //+1 difficulty to attacks

        //stressed debuffs
        //+2 difficulty to attacks
        //-1 difficulty to being hit
        public float UnsettledAttackModifier = 1;
        public float StressedAimModifier = 1;
        public float StressedToHitModifier = -1;
        public float PanickedAimModifier = 2;
        public float PanickedToHitModifier = -2;

        //ejection
        //+4 difficulty to attacks
        //-2 difficulty to being hit
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

        public float UnsteadyModifier = 10;
        public float PilotHealthMaxModifier = 15;
        public float HeadMaxModifier = 15;
        public float CenterTorsoMaxModifier = 45;
        public float SideTorsoMaxModifier = 20;
        public float LeggedMaxModifier = 10;
        public float WeaponlessModifier = 10;
        public float AloneModifier = 10;
    }
}