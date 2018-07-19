using BattleTech;
using BattleTech.UI;
using Harmony;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static PanicSystem.Holder;


// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class PanicSystem
    {
        internal static ModSettings Settings;
        public static string ActiveJsonPath; //store current tracker here
        public static string StorageJsonPath; //store our meta trackers here
        public static string ModDirectory;

        public static List<PanicTracker> TrackedPilots;
        public static List<MetaTracker> MetaTrackers;
        public static int CurrentIndex = -1;

        public static void Init(string modDir, string modSettings)
        {
            FileLog.Reset();
            FileLog.Log($"{DateTime.Now.ToLongTimeString()} Harmony init");
            Logger.Debug("Init()");
            var harmony = HarmonyInstance.Create("com.BattleTech.PanicSystem");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            ModDirectory = modDir;
            ActiveJsonPath = Path.Combine(modDir, "PanicSystem.json");
            StorageJsonPath = Path.Combine(modDir, "PanicSystemStorage.json");
            try
            {
                FileLog.Log($"Loading settings");
                Settings = JsonConvert.DeserializeObject<ModSettings>(modSettings);
            }
            catch (Exception)
            {
                Settings = new ModSettings();
            }
        }

        /// <summary>
        /// G returning anything true implies an ejection save will be required
        /// </summary>
        /// <param name="mech"></param>
        /// <param name="attackSequence"></param>
        /// <param name="panicStarted"></param>
        /// <returns></returns>
        public static bool RollForEjectionResult(Mech mech, AttackDirector.AttackSequence attackSequence,
            bool panicStarted)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && !mech.HasHandledDeath))
                return false;

            // knocked down mechs cannot eject
            if (mech.IsProne && Settings.KnockedDownCannotEject)
                return false;

            // have to do damage
            if (!attackSequence.attackDidDamage)
                return false;

            Pilot pilot = mech.GetPilot();
            if (pilot == null)
                return false;

            var weapons = mech.Weapons;
            var guts = mech.SkillGuts;
            var tactics = mech.SkillTactics;
            var total = guts + tactics;

            // guts 10 makes you immune, player character cannot be forced to eject
            if ((guts == 10 && Settings.GutsTenAlwaysResists) ||
                (Settings.PlayerCharacterAlwaysResists && pilot.IsPlayerCharacter))
                return false;

            // tactics 10 makes you immune, or combination of guts and tactics makes you immune.
            if ((tactics == 10 && Settings.TacticsTenAlwaysResists) ||
                (total >= 10 && Settings.ComboTenAlwaysResists))
                return false;

            // pilots that cannot eject or be headshot shouldn't eject
            if (!mech.CanBeHeadShot || !pilot.CanEject)
                return false;

            // start building ejectModifiers
            float lowestHealthLethalLocation = float.MaxValue;
            float ejectModifiers = 0;

            // pilot health
            float pilotHealthPercent = 1 - ((float) pilot.Injuries / pilot.Health);
            if (pilotHealthPercent < 1)
            {
                ejectModifiers += Settings.PilotHealthMaxModifier * (1 - pilotHealthPercent);
            }

            if (mech.IsUnsteady)
            {
                ejectModifiers += Settings.UnsteadyModifier;
            }

            // Head
            var headHealthPercent = (mech.HeadArmor + mech.HeadStructure) /
                                    (mech.GetMaxArmor(ArmorLocation.Head) +
                                     mech.GetMaxStructure(ChassisLocations.Head));
            if (headHealthPercent < 1)
            {
                ejectModifiers += Settings.HeadDamageMaxModifier * (1 - headHealthPercent);
            }

            // CT                                                                                   
            var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure + mech.CenterTorsoRearArmor) /
                            (mech.GetMaxArmor(ArmorLocation.CenterTorso) +
                             mech.GetMaxStructure(ChassisLocations.CenterTorso));
            if (ctPercent < 1)
            {
                ejectModifiers += Settings.CTDamageMaxModifier * (1 - ctPercent);
                lowestHealthLethalLocation = Math.Min(mech.CenterTorsoStructure, lowestHealthLethalLocation);
            }

            // side torsos
            var ltStructurePercent = mech.LeftTorsoStructure / mech.GetMaxStructure(ChassisLocations.LeftTorso);
            if (ltStructurePercent < 1)
            {
                ejectModifiers += Settings.SideTorsoInternalDamageMaxModifier * (1 - ltStructurePercent);
            }

            var rtStructurePercent = mech.RightTorsoStructure / mech.GetMaxStructure(ChassisLocations.RightTorso);
            if (rtStructurePercent < 1)
            {
                ejectModifiers += Settings.SideTorsoInternalDamageMaxModifier * (1 - rtStructurePercent);
            }

            // legs
            var legPercentRight = 1 - (mech.RightLegStructure + mech.RightLegArmor) /
                                  (mech.GetMaxStructure(ChassisLocations.RightLeg) +
                                   mech.GetMaxArmor(ArmorLocation.RightLeg));
            var legPercentLeft = 1 - (mech.LeftLegStructure + mech.LeftLegArmor) /
                                 (mech.GetMaxStructure(ChassisLocations.LeftLeg) +
                                  mech.GetMaxArmor(ArmorLocation.LeftLeg));
            if ((legPercentRight + legPercentLeft) < 2)
            {
                ejectModifiers += Settings.LeggedMaxModifier * (legPercentRight + legPercentLeft);
                var legCheck =
                    legPercentRight * (mech.GetMaxStructure(ChassisLocations.RightLeg) +
                                       mech.GetMaxArmor(ArmorLocation.RightLeg)) + legPercentLeft *
                    (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
                lowestHealthLethalLocation = Math.Min(legCheck, lowestHealthLethalLocation);
                lowestHealthLethalLocation = Math.Min(legCheck, lowestHealthLethalLocation);
            }

            // next shot like that could kill or leg
            if (lowestHealthLethalLocation <= attackSequence.cumulativeDamage)
            {
                ejectModifiers += Settings.NextShotLikeThatCouldKill;
            }

            // weaponless
            if (weapons.TrueForAll(w => w.DamageLevel == ComponentDamageLevel.Destroyed))
            {
                ejectModifiers += Settings.WeaponlessModifier;
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m == mech as AbstractActor || m.IsDead))
            {
                ejectModifiers += Settings.AloneModifier;
            }

            //dZ Because this is how it should be. Make this changeable. 
            var modifiers =
                (ejectModifiers - Settings.BaseEjectionResist - (Settings.GutsEjectionResistPerPoint * guts) -
                 (Settings.TacticsEjectionResistPerPoint * tactics)) * Settings.EjectChanceMultiplier;

            if (pilot.pilotDef.PilotTags.Contains("pilot_dependable"))
                modifiers -= Settings.DependableModifier;

            if (mech.team == mech.Combat.LocalPlayerTeam)
            {
                MoraleConstantsDef moraleDef = mech.Combat.Constants.GetActiveMoraleDef(mech.Combat);
                float medianMorale = 25;
                modifiers -= (mech.Combat.LocalPlayerTeam.Morale - medianMorale) / 2;
            }

            if (modifiers < 0)
            {
                return false;
            }

            var rng = (new Random()).Next(1, 101);
            float rollToBeat;
            if (!panicStarted)
            {
                rollToBeat = Math.Min(modifiers, Settings.MaxEjectChance);
            }
            else
            {
                rollToBeat = Math.Min(modifiers, Settings.MaxEjectChanceWhenEarly);
            }

            mech.Combat.MessageCenter.PublishMessage(!(rng < rollToBeat)
                ? new AddSequenceToStackMessage(new ShowActorInfoSequence(
                    mech, $"{Math.Floor(rollToBeat)}% save SUCCESS for Guts & Tactics",
                    FloatieMessage.MessageNature.Buff, true))
                : new AddSequenceToStackMessage(new ShowActorInfoSequence(
                    mech, $"{Math.Floor(rollToBeat)}% save FAILED: Punchin' Out!!", FloatieMessage.MessageNature.Debuff,
                    true)));

            return rng < rollToBeat;
        }
        public static class RollHelpers
        {
            public static bool ShouldPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
            {
                Logger.Debug($"------ START ------");

                if (!CheckCanPanic(mech, attackSequence))
                {
                    return false;
                }

                if (!CheckDamagePanic(mech, attackSequence))
                {
                    return false;
                }

                //dZ Get rid of garbage.
                Pilot pilot = mech.GetPilot();
                var weapons = mech.Weapons;
                var guts = mech.SkillGuts * Settings.GutsEjectionResistPerPoint;
                var tactics = mech.SkillTactics * Settings.TacticsEjectionResistPerPoint;
                var gutAndTacticsSum = guts + tactics;
                int index = -1;
                index = PanicHelpers.GetTrackedPilotIndex(mech);
                float lowestRemaining =
                    mech.CenterTorsoStructure + mech.CenterTorsoFrontArmor + mech.CenterTorsoRearArmor;
                float panicModifiers = 0;

                if (!CheckTrackedPilots(mech, ref index))
                {
                    return false;
                }

                panicModifiers = CheckPilotHealth(pilot, panicModifiers);
                panicModifiers = CheckMechUnsteady(mech, panicModifiers);
                panicModifiers = CheckHead(mech, panicModifiers);
                panicModifiers = CheckCT(mech, panicModifiers, ref lowestRemaining);
                panicModifiers = CheckLT(mech, panicModifiers);
                panicModifiers = CheckRT(mech, panicModifiers);
                panicModifiers = CheckLegs(mech, panicModifiers, ref lowestRemaining);
                panicModifiers = CheckFinalStraws(mech, attackSequence, lowestRemaining, panicModifiers, weapons);

                panicModifiers -= gutAndTacticsSum;

                if (pilot.pilotDef.PilotTags.Contains("pilot_brave"))
                    panicModifiers -= Settings.BraveModifier;


                Logger.Debug("Guts and Tactics");
                Logger.Debug(panicModifiers.ToString());
                if (mech.team == mech.Combat.LocalPlayerTeam)

                    //dZ - Inputtable morale is superior.
                {
                    float medianMorale = 25;
                    MoraleConstantsDef moraleDef = mech.Combat.Constants.GetActiveMoraleDef(mech.Combat);
                    panicModifiers -= (mech.Combat.LocalPlayerTeam.Morale - medianMorale) / 2;
                    Logger.Debug("Morale");
                    Logger.Debug(panicModifiers.ToString());
                }

                if ((panicModifiers < Settings.AtLeastOneChanceToPanicPercentage) && Settings.AtLeastOneChanceToPanic)
                {
                    panicModifiers = Settings.AtLeastOneChanceToPanicPercentage;
                    Logger.Debug("One Chance");
                    Logger.Debug(panicModifiers.ToString());
                }

                var rng = (new Random()).Next(1, 101);
                Logger.Debug("rng");
                Logger.Debug(rng.ToString());

                float rollToBeat;
                {
                    rollToBeat = Math.Min((int) panicModifiers, (int) Settings.MaxPanicResistTotal);
                    Logger.Debug("RollToBeat");
                    Logger.Debug(rollToBeat.ToString());
                }

                if (rng <= rollToBeat)

                {
                    Logger.Debug($"Failed panic save, debuffed!");
                    ApplyPanicDebuff(mech, index);
                    Logger.Debug(panicModifiers.ToString());
                    return true;
                }

                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                    new ShowActorInfoSequence(mech, $"Resisted Morale Check!", FloatieMessage.MessageNature.Buff,
                        true)));
                Logger.Debug($"No reason to panic.");
                Logger.Debug(panicModifiers.ToString());
                return false;
            }

            private static float CheckFinalStraws(Mech mech, AttackDirector.AttackSequence attackSequence,
                float lowestRemaining,
                float panicModifiers, List<Weapon> weapons)
            {
// next shot could kill or leg
                if (lowestRemaining <= attackSequence.cumulativeDamage)
                {
                    panicModifiers += Settings.NextShotLikeThatCouldKill;
                    Logger.Debug("Big Shot");
                    Logger.Debug(panicModifiers.ToString());
                }

                // weaponless
                if (weapons.TrueForAll(w =>
                    w.DamageLevel == ComponentDamageLevel.Destroyed ||
                    w.DamageLevel == ComponentDamageLevel.NonFunctional))
                {
                    panicModifiers += Settings.WeaponlessModifier;
                    Logger.Debug("Weaponless");
                    Logger.Debug(panicModifiers.ToString());
                }

                // alone
                if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
                {
                    panicModifiers += Settings.AloneModifier;
                    Logger.Debug("Alone");
                    Logger.Debug(panicModifiers.ToString());
                }

                return panicModifiers;
            }

            private static float CheckLegs(Mech mech, float panicModifiers, ref float lowestRemaining)
            {
                // dZ Check legs independently. Code here significantly improved.
                var legPercentRight = 1 - (mech.RightLegStructure + mech.RightLegArmor) /
                                      (mech.GetMaxStructure(ChassisLocations.RightLeg) +
                                       mech.GetMaxArmor(ArmorLocation.RightLeg));
                var legPercentLeft = 1 - (mech.LeftLegStructure + mech.LeftLegArmor) /
                                     (mech.GetMaxStructure(ChassisLocations.LeftLeg) +
                                      mech.GetMaxArmor(ArmorLocation.LeftLeg));
                if ((legPercentRight + legPercentLeft) < 2)
                {
                    panicModifiers += Settings.LeggedMaxModifier * (legPercentRight + legPercentLeft);
                    Logger.Debug("Legs");
                    Logger.Debug(panicModifiers.ToString());
                    var legCheck =
                        legPercentRight * (mech.GetMaxStructure(ChassisLocations.RightLeg) +
                                           mech.GetMaxArmor(ArmorLocation.RightLeg)) + legPercentLeft *
                        (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
                    lowestRemaining = Math.Min(legCheck, lowestRemaining);
                    lowestRemaining = Math.Min(legCheck, lowestRemaining);
                }

                return panicModifiers;
            }

            private static float CheckRT(Mech mech, float panicModifiers)
            {
                var rtStructurePercent = mech.RightTorsoStructure / mech.GetMaxStructure(ChassisLocations.RightTorso);
                if (rtStructurePercent < 1)
                {
                    panicModifiers += Settings.SideTorsoInternalDamageMaxModifier * (1 - rtStructurePercent);
                    Logger.Debug("RT");
                    Logger.Debug(panicModifiers.ToString());
                }

                return panicModifiers;
            }

            private static float CheckLT(Mech mech, float panicModifiers)
            {
                var ltStructurePercent = mech.LeftTorsoStructure / mech.GetMaxStructure(ChassisLocations.LeftTorso);
                if (ltStructurePercent < 1)
                {
                    panicModifiers += Settings.SideTorsoInternalDamageMaxModifier * (1 - ltStructurePercent);
                    Logger.Debug("LT");
                    Logger.Debug(panicModifiers.ToString());
                }

                return panicModifiers;
            }

            private static float CheckCT(Mech mech, float panicModifiers, ref float lowestRemaining)
            {
                var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure + mech.CenterTorsoRearArmor) /
                                (mech.GetMaxArmor(ArmorLocation.CenterTorso) +
                                 mech.GetMaxStructure(ChassisLocations.CenterTorso));
                if (ctPercent < 1)
                {
                    panicModifiers += Settings.CTDamageMaxModifier * (1 - ctPercent);
                    Logger.Debug("CT");
                    Logger.Debug(panicModifiers.ToString());
                    lowestRemaining = Math.Min(mech.CenterTorsoStructure, lowestRemaining);
                }

                return panicModifiers;
            }

            private static float CheckHead(Mech mech, float panicModifiers)
            {
                var headHealthPercent = (mech.HeadArmor + mech.HeadStructure) /
                                        (mech.GetMaxArmor(ArmorLocation.Head) +
                                         mech.GetMaxStructure(ChassisLocations.Head));
                if (headHealthPercent < 1)
                {
                    panicModifiers += Settings.HeadDamageMaxModifier * (1 - headHealthPercent);
                    Logger.Debug("Head Health");
                    Logger.Debug(panicModifiers.ToString());
                }

                return panicModifiers;
            }

            private static float CheckMechUnsteady(Mech mech, float panicModifiers)
            {
                if (mech.IsUnsteady)
                {
                    panicModifiers += Settings.UnsteadyModifier;
                    Logger.Debug("Unsteady");
                    Logger.Debug(panicModifiers.ToString());
                }

                return panicModifiers;
            }

            private static float CheckPilotHealth(Pilot pilot, float panicModifiers)
            {
                if (pilot != null)
                {
                    float pilotHealthPercent = 1 - ((float) pilot.Injuries / pilot.Health);

                    if (pilotHealthPercent < 1)
                    {
                        panicModifiers += Settings.PilotHealthMaxModifier * (1 - pilotHealthPercent);
                        Logger.Debug("Health");
                        Logger.Debug(panicModifiers.ToString());
                    }
                }

                return panicModifiers;
            }

            private static bool CheckTrackedPilots(Mech mech, ref int index)
            {
                if (index < 0)
                {
                    TrackedPilots.Add(
                        new PanicTracker(mech)); //add a new tracker to tracked pilot, then we run it all over again;
                    index = PanicHelpers.GetTrackedPilotIndex(mech);
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
                    TrackedPilots[index].ChangedRecently && Settings.AlwaysGatedChanges)
                {
                    return false;
                }

                return true;
            }

            private static bool CheckDamagePanic(Mech mech, AttackDirector.AttackSequence attackSequence)
            {
                if (!attackSequence.attackDidDamage)
                {
                    Logger.Debug($"No damage, no panic.");
                    return false;
                }

                if (attackSequence.attackStructureDamage == 0 ||
                    attackSequence.attackArmorDamage / (GetCurrentMechArmour(mech) + attackSequence.attackArmorDamage) *
                    100 <
                    Settings.MinimumArmourDamagePercentageRequired)
                {
                    Logger.Debug($"No structural damage and not enough armor damage. No panic.");
                    return false;
                }

                Logger.Debug($"Attack causes a panic check.");
                return true;
            }

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

                // credit to jo and thanks!
                if (mech.team.IsLocalPlayer && !Settings.PlayerTeamCanPanic)
                {
                    Logger.Debug($"Players can't panic.");
                    return false;
                }
                else if (!mech.team.IsLocalPlayer && !Settings.EnemiesCanPanic)
                {
                    Logger.Debug($"AI can't panic.");
                    return false;
                }

                return true;
            }

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

            public static void ApplyPanicDebuff(Mech mech, int index)
            {
                if (TrackedPilots[index].TrackedMech == mech.GUID &&
                    TrackedPilots[index].PilotStatus == PanicStatus.Confident)
                {
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(mech, $"Unsettled!", FloatieMessage.MessageNature.Debuff, true)));
                    TrackedPilots[index].PilotStatus = PanicStatus.Unsettled;
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier",
                        StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor",
                        StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Unsettled Aim", -1, "AccuracyModifier",
                        StatCollection.StatOperation.Float_Add, Settings.UnsettledAttackModifier, -1, true);

                }
                else if (TrackedPilots[index].TrackedMech == mech.GUID &&
                         TrackedPilots[index].PilotStatus == PanicStatus.Unsettled)
                {
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(mech, $"Stressed!", FloatieMessage.MessageNature.Debuff, true)));
                    TrackedPilots[index].PilotStatus = PanicStatus.Stressed;
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier",
                        StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor",
                        StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Stressed Aim", -1, "AccuracyModifier",
                        StatCollection.StatOperation.Float_Add, Settings.StressedAimModifier, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Stressed Defence", -1, "ToHitThisActor",
                        StatCollection.StatOperation.Float_Add, Settings.StressedToHitModifier, -1, true);
                }
                else if (TrackedPilots[index].TrackedMech == mech.GUID &&
                         TrackedPilots[index].PilotStatus == PanicStatus.Stressed)
                {
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(mech, $"Panicked!", FloatieMessage.MessageNature.Debuff, true)));
                    TrackedPilots[index].PilotStatus = PanicStatus.Panicked;
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier",
                        StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor",
                        StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Panicking Aim!", -1, "AccuracyModifier",
                        StatCollection.StatOperation.Float_Add, Settings.PanickedAimModifier, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Panicking Defence!", -1, "ToHitThisActor",
                        StatCollection.StatOperation.Float_Add, Settings.PanickedToHitModifier, -1, true);
                }
                else
                {
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                        new ShowActorInfoSequence(mech, $"Failed Panic Check!", FloatieMessage.MessageNature.Debuff,
                            true)));
                }

                TrackedPilots[index].ChangedRecently = true;
            }
        }

        public static class Logger
        {
            static readonly string FilePath = $"{ModDirectory}/Log.txt";

            public static void LogError(Exception ex)
            {
                using (var writer = new StreamWriter(FilePath, true))
                {
                    writer.WriteLine(
                        $"{DateTime.Now.ToShortTimeString()} Message: {ex.Message}\nStack Trace: {ex.StackTrace}");
                    writer.WriteLine(new string(c: '-', count: 80));
                }
            }

            public static void Debug(object line)
            {
                //if (!PanicSystem.Settings.DebugEnabled) return;
                using (var writer = new StreamWriter(FilePath, true))
                {
                    writer.WriteLine($"{DateTime.Now.ToShortTimeString()} {line}");
                }
            }
        }

        public class ModSettings
        {
            public bool PlayerCharacterAlwaysResists = true;
            public bool PlayerTeamCanPanic = true;
            public bool EnemiesCanPanic = true;
            public bool Debug = false;

            //new mechanics for considering when to eject based on mech class
            public bool PlayerLightsConsiderEjectingEarly = false;
            public bool EnemyLightsConsiderEjectingEarly = true;
            public PanicStatus LightMechEarlyPanicThreshold = PanicStatus.Unsettled;

            public bool PlayerMediumsConsiderEjectingEarly = false;
            public bool EnemyMediumsConsiderEjectingEarly = false;
            public PanicStatus MediumMechEarlyPanicThreshold = PanicStatus.Stressed;

            public bool PlayerHeaviesConsiderEjectingEarly = false;
            public bool EnemyHeaviesConsiderEjectingEarly = false;
            public PanicStatus HeavyMechEarlyPanicThreshold = PanicStatus.Stressed;

            public bool PlayerAssaultsConsiderEjectingEarly = false;
            public bool EnemyAssaultsConsiderEjectingEarly = false;
            public PanicStatus AssaultMechEarlyPanicThreshold = PanicStatus.Stressed;

            public float MaxEjectChanceWhenEarly = 10;

            //minmum armour and structure damage
            public float
                MinimumArmourDamagePercentageRequired =
                    10; //if no structure damage, a Mech must lost a bit of its armour before it starts worrying

            //general panic roll
            //rolls out of 20
            //max guts and tactics almost prevents any panicking (or being the player character, by default)
            public bool AtLeastOneChanceToPanic = true;
            public int AtLeastOneChanceToPanicPercentage = 10;
            public bool AlwaysGatedChanges = true;
            public float MaxPanicResistTotal = 15; //at least 20% chance to panic if you can't nullify the whole thing

            public bool LosingLimbAlwaysPanics = false;

            //Unsettled debuffs
            //+1 difficulty to attacks
            public float UnsettledAttackModifier = 1;
            public float BraveModifier = 5;
            public float DependableModifier = 5;

            //stressed debuffs
            //+2 difficulty to attacks
            //-1 difficulty to being hit

            public float StressedAimModifier = 2;

            public float StressedToHitModifier = -1;

            //ejection
            //+4 difficulty to attacks
            //-2 difficulty to being hit
            public float PanickedAimModifier = 4;
            public float PanickedToHitModifier = -2;
            public bool GutsTenAlwaysResists = false;
            public bool ComboTenAlwaysResists = false;
            public bool TacticsTenAlwaysResists = false;
            public int MinimumHealthToAlwaysEjectRoll = 1;
            public bool KnockedDownCannotEject = true;

            public bool ConsiderEjectingWithNoWeaps = true;
            public bool ConsiderEjectingWhenAlone = true;
            public float MaxEjectChance = 50;
            public float EjectChanceMultiplier = 5;

            public float BaseEjectionResist = 10;
            public float GutsEjectionResistPerPoint = 2;
            public float TacticsEjectionResistPerPoint = 1;
            public float UnsteadyModifier = 5;
            public float PilotHealthMaxModifier = 10;

            public float HeadDamageMaxModifier = 10;
            public float 
                CTDamageMaxModifier = 10;
            public float SideTorsoInternalDamageMaxModifier = 10;
            public float LeggedMaxModifier = 10;

            public float NextShotLikeThatCouldKill = 15;

            public float WeaponlessModifier = 15;
            public float AloneModifier = 20;
        }
    }
}