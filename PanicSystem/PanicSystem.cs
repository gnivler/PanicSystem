using BattleTech;
using Harmony;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static PanicSystem.Controller;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class PanicSystem
    {
        internal static Settings ModSettings = new Settings();
        public static string ActiveJsonPath; //store current tracker here
        public static string StorageJsonPath; //store our meta trackers here
        public static string ModDirectory;

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
                    Logger.Clear();
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

            index = GetTrackedPilotIndex(mech);
            float panicModifiers = 0;

            if (LastStraw)
            {
                return true;
            }

            if (!CheckPanicFromDamage(mech, attackSequence))
            {
                return false;
            }

            Logger.Debug($"Collecting panic modifiers:");
            if (!CheckTrackedPilots(mech, ref index))
            {
                return false;
            }
            
            if (pilot.pilotDef.PilotTags.Contains("pilot_brave"))
            {
                panicModifiers -= ModSettings.BraveModifier;
                Logger.Debug($"bravery: {panicModifiers}");
            }

            GetPilotHealthModifier(pilot, ref panicModifiers);
            Logger.Debug($"pilot: {panicModifiers}");

            GetUnsteadyModifier(mech, ref panicModifiers);
            Logger.Debug($"unsteady: {panicModifiers}");

            GetHeadModifier(mech, ref panicModifiers);
            Logger.Debug($"head: {panicModifiers}");

            GetCTModifier(mech, ref panicModifiers);
            Logger.Debug($"CT: {panicModifiers}");

            GetLTModifier(mech, ref panicModifiers);
            Logger.Debug($"LT: {panicModifiers}");

            CheckRT(mech, ref panicModifiers);
            Logger.Debug($"RT: {panicModifiers}");

            GetLegModifier(mech, ref panicModifiers);
            Logger.Debug($"Legs: {panicModifiers}");

            CheckLastStraws(mech, ref panicModifiers, weapons);
            Logger.Debug($"LastStraw: {panicModifiers}");

            panicModifiers -= gutAndTacticsSum;
            Logger.Debug($"Guts and Tactics: {panicModifiers} ({mech.SkillGuts}x{ModSettings.GutsEjectionResistPerPoint} + {mech.SkillTactics}x{ModSettings.TacticsEjectionResistPerPoint})");

            if (mech.team == mech.Combat.LocalPlayerTeam)
            {
                var moraleModifier = (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / 8;
                panicModifiers -= moraleModifier;
                Logger.Debug($"Morale: {panicModifiers}");
            }

            panicModifiers = (float) Math.Max(0f, Math.Round(panicModifiers));
            Logger.Debug($"Roll to beat: {panicModifiers}");

            var rng = new Random().Next(1, 101);
            Logger.Debug($"Rolled: {rng}");

            if (rng < (int) panicModifiers)
            {
                Logger.Debug($"FAILED {panicModifiers}% PANIC SAVE!");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"FAILED {panicModifiers}% PANIC SAVE!", FloatieMessage.MessageNature.Debuff, true)));

                ApplyPanicDebuff(mech, index);
            }
            else if (panicModifiers >= 0)
            {
                if (panicModifiers != 0)
                {
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"MADE {panicModifiers}% PANIC SAVE!", FloatieMessage.MessageNature.Buff, true)));
                }
                Logger.Debug($"MADE {panicModifiers}% PANIC SAVE!");
            }

            return false;
        }

        /// <summary>
        /// true implies an ejection condition was met and ejections occurs
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

            if (!attackSequence.attackDidDamage)
            {
                return false;
            }

            // knocked down mechs cannot eject
            if (mech.IsProne && ModSettings.KnockedDownCannotEject)
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

            if (!CheckCantEject(mech, guts, pilot, tactics, gutsAndTacticsSum))
            {
                return false;
            }

            // start building ejectModifiers
            float ejectModifiers = 0;
            Logger.Debug($"Collecting ejection modifiers:");
            Logger.Debug(new string(c: '-', count: 80));

            if (pilot.pilotDef.PilotTags.Contains("pilot_drunk"))
            {
                Logger.Debug("Drunkard - not ejecting!");
                return false;
            }

            // pilot health
            float pilotHealthPercent = 1f - ((float) pilot.Injuries / pilot.Health);
            if (pilotHealthPercent < 1)
            {
                ejectModifiers += ModSettings.PilotHealthMaxModifier * (1f - pilotHealthPercent);
                Logger.Debug($"Pilot Health: {ejectModifiers}");
            }

            // unsteady
            if (mech.IsUnsteady)
            {
                ejectModifiers += ModSettings.UnsteadyModifier;
                Logger.Debug($"Unsteady: {ejectModifiers}");
            }

            // Head
            var headHealthPercent = (mech.HeadArmor + mech.HeadStructure) / (mech.GetMaxArmor(ArmorLocation.Head) + mech.GetMaxStructure(ChassisLocations.Head));
            if (headHealthPercent < 1)
            {
                ejectModifiers += ModSettings.HeadDamageMaxModifier * (1f - headHealthPercent);
                Logger.Debug($"Head Damage: {ejectModifiers}");
            }

            // CT  
            var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure + mech.CenterTorsoRearArmor) / (mech.GetMaxArmor(ArmorLocation.CenterTorso) + mech.GetMaxStructure(ChassisLocations.CenterTorso));
            if (ctPercent < 1)
            {
                ejectModifiers += ModSettings.CTDamageMaxModifier * (1f - ctPercent);
                Logger.Debug($"CT Damage: {ejectModifiers}");
            }

            // LT/RT
            var ltStructurePercent = mech.LeftTorsoStructure / mech.GetMaxStructure(ChassisLocations.LeftTorso);
            if (ltStructurePercent < 1)
            {
                ejectModifiers += ModSettings.SideTorsoInternalDamageMaxModifier * (1f - ltStructurePercent);
            }

            Logger.Debug($"LT Damage: {ejectModifiers}");

            var rtStructurePercent = mech.RightTorsoStructure / mech.GetMaxStructure(ChassisLocations.RightTorso);
            if (rtStructurePercent < 1)
            {
                ejectModifiers += ModSettings.SideTorsoInternalDamageMaxModifier * (1f - rtStructurePercent);
                Logger.Debug($"RT Damage: {ejectModifiers}");
            }

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed))
            {
                ejectModifiers += ModSettings.WeaponlessModifier;
                Logger.Debug($"Weaponless: {ejectModifiers}");
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m == mech as AbstractActor || m.IsDead))
            {
                ejectModifiers += ModSettings.AloneModifier;
                Logger.Debug($"Sole Survivor: {ejectModifiers}");
            }

            if (mech.team == mech.Combat.LocalPlayerTeam)
            {
                ejectModifiers -= (mech.Combat.LocalPlayerTeam.Morale - ModSettings.MedianMorale) / 2;
                Logger.Debug($"Morale: {ejectModifiers}");
            }
            
            if (pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
            {
                ejectModifiers -= ModSettings.DependableModifier;
                Logger.Debug($"Dependable: {ejectModifiers}");
            }

            //dZ Because this is how it should be. Make this changeable. 
            ejectModifiers = Math.Max(0f, (ejectModifiers - ModSettings.BaseEjectionResist - (ModSettings.GutsEjectionResistPerPoint * guts) - (ModSettings.TacticsEjectionResistPerPoint * tactics)) * ModSettings.EjectChanceMultiplier);
            Logger.Debug($"After calculation: {ejectModifiers}");

            var rollToBeat = (float) Math.Round(ejectModifiers);
            Logger.Debug($"Final roll to beat: {rollToBeat}");


            // passes through if last straw is met to force an ejection roll
            if (rollToBeat <= 0 && !IsLastStrawPanicking(mech, ref panicStarted))
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"RESISTED EJECTION!", FloatieMessage.MessageNature.Buff, true)));
                Logger.Debug($"RESISTED EJECTION!");
                return false;
            }

            if (!panicStarted)
            {
                rollToBeat = Math.Min(rollToBeat, ModSettings.MaxEjectChance);
            }
            else
            {
                rollToBeat = Math.Min(rollToBeat, ModSettings.MaxEjectChanceWhenEarlyEjectThresholdMet);
            }

            var roll = RNG.Next(1, 101);
            Logger.Debug($"RollToBeat: {rollToBeat}");
            Logger.Debug($"Rolled: {roll}");
            Logger.Debug($"{rollToBeat}% EJECTION CHANCE!");

            mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"{rollToBeat}% EJECTION CHANCE!", FloatieMessage.MessageNature.Debuff, true)));
            if (roll >= rollToBeat)
            {
                Logger.Debug($"AVOIDED!");
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"AVOIDED!", FloatieMessage.MessageNature.Buff, true)));
                return false;
            }

            Logger.Debug($"FAILED SAVE: Punchin' Out!!");
            return true;
            //mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"FAILED SAVE: Punchin' Out!!", FloatieMessage.MessageNature.Debuff, true)));   
        }

        private static void Eject()
        {
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

        /// <summary>
        /// returns modifiers
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="panicModifiers"></param>
        /// <param name="weapons"></param>
        /// <returns></returns>
        private static void CheckLastStraws(Mech mech, ref float panicModifiers, List<Weapon> weapons)
        {
            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional))
            {
                panicModifiers += ModSettings.WeaponlessModifier;
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                panicModifiers += ModSettings.AloneModifier;
            }
        }

        /// <summary>
        /// returns modifiers and updates lowestHealthLethalLocation
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="panicModifiers"></param>
        /// <returns></returns>
        private static void GetLegModifier(Mech mech, ref float panicModifiers)
        {
            // dZ Check legs independently. Code here significantly improved.  Handles missing legs
            var legPercentRight = 1 - (mech.RightLegStructure + mech.RightLegArmor) / (mech.GetMaxStructure(ChassisLocations.RightLeg) + mech.GetMaxArmor(ArmorLocation.RightLeg));
            var legPercentLeft = 1 - (mech.LeftLegStructure + mech.LeftLegArmor) / (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
            if (legPercentRight + legPercentLeft < 2)
            {
                panicModifiers += ModSettings.LeggedMaxModifier * (legPercentRight + legPercentLeft);
            }
        }

        /// <summary>
        /// returns modifiers
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="panicModifiers"></param>
        /// <returns></returns>
        private static void CheckRT(Mech mech, ref float panicModifiers)
        {
            var rtStructurePercent = mech.RightTorsoStructure / mech.GetMaxStructure(ChassisLocations.RightTorso);
            if (rtStructurePercent < 1)
            {
                panicModifiers += ModSettings.SideTorsoInternalDamageMaxModifier * (1 - rtStructurePercent);
            }
        }

        /// <summary>
        /// returns modifiers
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="panicModifiers"></param>
        /// <returns></returns>
        private static void GetLTModifier(Mech mech, ref float panicModifiers)
        {
            var ltStructurePercent = mech.LeftTorsoStructure / mech.GetMaxStructure(ChassisLocations.LeftTorso);
            if (ltStructurePercent < 1)
            {
                panicModifiers += ModSettings.SideTorsoInternalDamageMaxModifier * (1 - ltStructurePercent);
            }
        }

        /// <summary>
        /// returns modifiers and updates lowestHealthLethalLocation
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="panicModifiers"></param>
        /// <returns></returns>
        private static void GetCTModifier(Mech mech, ref float panicModifiers)
        {
            var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure + mech.CenterTorsoRearArmor) / (mech.GetMaxArmor(ArmorLocation.CenterTorso) + mech.GetMaxStructure(ChassisLocations.CenterTorso));
            if (ctPercent < 1)
            {
                panicModifiers += ModSettings.CTDamageMaxModifier * (1 - ctPercent);
            }
        }

        /// <summary>
        /// returns modifiers
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="panicModifiers"></param>
        /// <returns></returns>
        private static void GetHeadModifier(Mech mech, ref float panicModifiers)
        {
            var headHealthPercent = (mech.HeadArmor + mech.HeadStructure) / (mech.GetMaxArmor(ArmorLocation.Head) + mech.GetMaxStructure(ChassisLocations.Head));
            if (headHealthPercent < 1)
            {
                panicModifiers += ModSettings.HeadDamageMaxModifier * (1 - headHealthPercent);
            }
        }

        /// <summary>
        /// returns modifiers
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="panicModifiers"></param>
        /// <returns></returns>
        private static void GetUnsteadyModifier(Mech mech, ref float panicModifiers)
        {
            if (mech.IsUnsteady)
            {
                panicModifiers += ModSettings.UnsteadyModifier;
            }
        }

        /// <summary>
        /// returns modifiers
        /// </summary>
        /// <param name="pilot"></param>
        /// <param name="panicModifiers"></param>
        /// <returns></returns>
        private static void GetPilotHealthModifier(Pilot pilot, ref float panicModifiers)
        {
            if (pilot != null)
            {
                float pilotHealthPercent = 1f - (float) pilot.Injuries / pilot.Health;
                if (pilotHealthPercent < 1)
                {
                    panicModifiers += ModSettings.PilotHealthMaxModifier * (1 - pilotHealthPercent);
                }
            }
        }

        /// <summary>
        /// true implies the pilot is tracked
        /// </summary>
        /// <param name="mech"></param>
        /// <returns></returns>
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

        /// <summary>
        /// returns modifiers
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <returns></returns>
        private static bool CheckPanicFromDamage(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (!attackSequence.attackDidDamage)
            {
                Logger.Debug($"No damage.");
                return false;
            }
            else
            {
                Logger.Debug($"Attack does {attackSequence.attackArmorDamage} armor and {attackSequence.attackStructureDamage} structure.");
            }

            if (attackSequence.attackStructureDamage > 0)
            {
                Logger.Debug($"{attackSequence.attackStructureDamage} structural damage causes a panic check.");
                return true;
            }

            if (attackSequence.attackArmorDamage / (GetCurrentMechArmour(mech) + attackSequence.attackArmorDamage) * 100 < ModSettings.MinimumArmourDamagePercentageRequired)
            {
                Logger.Debug($"Not enough armor damage ({attackSequence.attackArmorDamage}).");
                return false;
            }

            Logger.Debug($"{attackSequence.attackArmorDamage} damage attack causes a panic check.");
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
        private static bool CheckCantEject(Mech mech, int guts, Pilot pilot, int tactics, int gutsAndTacticsSum)
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

        /// <summary>
        /// sums armour from all locations
        /// </summary>
        /// <param name="mech"></param>
        /// <returns></returns>
        private static float GetCurrentMechArmour(Mech mech)
        {
            float totalArmor = 0;
            totalArmor += mech.GetCurrentArmor(ArmorLocation.Head);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.CenterTorso);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.CenterTorsoRear);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.LeftTorso);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.LeftTorsoRear);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.RightTorso);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.RightTorsoRear);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.RightArm);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.LeftArm);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.RightLeg);
            totalArmor += mech.GetCurrentArmor(ArmorLocation.LeftLeg);
            return totalArmor;
        }

        private static float GetCurrentMechStructure(Mech mech)
        {
            float totalStructure = 0;
            totalStructure += mech.GetCurrentStructure(ChassisLocations.Head);
            totalStructure += mech.GetCurrentStructure(ChassisLocations.CenterTorso);
            totalStructure += mech.GetCurrentStructure(ChassisLocations.LeftTorso);
            totalStructure += mech.GetCurrentStructure(ChassisLocations.RightTorso);
            totalStructure += mech.GetCurrentStructure(ChassisLocations.RightArm);
            totalStructure += mech.GetCurrentStructure(ChassisLocations.LeftArm);
            totalStructure += mech.GetCurrentStructure(ChassisLocations.RightLeg);
            totalStructure += mech.GetCurrentStructure(ChassisLocations.LeftLeg);
            return totalStructure;
        }

        /// <summary>
        /// not in use
        /// </summary>
        /// <param name="mech"></param>
        /// <returns></returns>
        private static float GetTotalMechArmour(Mech mech)
        {
            float maxArmor = 0;
            maxArmor += mech.GetMaxArmor(ArmorLocation.CenterTorso);
            maxArmor += mech.GetMaxArmor(ArmorLocation.LeftArm);
            maxArmor += mech.GetMaxArmor(ArmorLocation.CenterTorsoRear);
            maxArmor += mech.GetMaxArmor(ArmorLocation.Head);
            maxArmor += mech.GetMaxArmor(ArmorLocation.LeftTorso);
            maxArmor += mech.GetMaxArmor(ArmorLocation.RightTorso);
            maxArmor += mech.GetMaxArmor(ArmorLocation.RightTorsoRear);
            maxArmor += mech.GetMaxArmor(ArmorLocation.LeftTorsoRear);
            maxArmor += mech.GetMaxArmor(ArmorLocation.RightArm);
            maxArmor += mech.GetMaxArmor(ArmorLocation.LeftLeg);
            maxArmor += mech.GetMaxArmor(ArmorLocation.RightLeg);
            return maxArmor;
        }

        /// <summary>
        /// applied combat modifiers to tracked mechs based on panic status
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
                Logger.Debug("STRESSED!!");
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
                Logger.Debug($"Last straw health.");
                PanicStarted = true;
                return true;
            }

            if (mech.Weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional) && ModSettings.ConsiderEjectingWithNoWeaps)
            {
                Logger.Debug($"Last straw weapons.");
                PanicStarted = true;
                return true;
            }

            var enemyHealth = GetAllEnemiesHealth(mech);

            if (ModSettings.ConsiderEjectingWhenAlone && mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m.GUID == mech.GUID) && enemyHealth >= (mech.SummaryArmorCurrent + mech.SummaryStructureCurrent) * 2)
            {
                Logger.Debug($"Last straw sole survivor.");
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
        public bool EnableDebug = false;  // legacy compatibility

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
        public float HeadDamageMaxModifier = 15;
        public float CTDamageMaxModifier = 35;
        public float SideTorsoInternalDamageMaxModifier = 25;
        public float LeggedMaxModifier = 10;
        public float WeaponlessModifier = 10;
        public float AloneModifier = 10;
    }
}