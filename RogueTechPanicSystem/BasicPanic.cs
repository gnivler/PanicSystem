using BattleTech;
using BattleTech.UI;
using Harmony;
using JetBrains.Annotations;
using Newtonsoft.Json;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using static RogueTechPanicSystem.RogueTechPanicSystem;

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace RogueTechPanicSystem
{
    [UsedImplicitly]
    public static class RogueTechPanicSystem
    {
        internal static ModSettings Settings;

        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("de.group.RogueTechPanicSystem");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Holder.ModDirectory = modDir;
            Holder.ActiveJsonPath = Path.Combine(modDir, "RogueTechPanicSystem.json");
            Holder.StorageJsonPath = Path.Combine(modDir, "RogueTechPanicSystemStorage.json");
            try
            {
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
        /// <param name="IsEarlyPanic"></param>
        /// <returns></returns>
        public static bool RollForEjectionResult(Mech mech, AttackDirector.AttackSequence attackSequence, bool IsEarlyPanic)
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
            float pilotHealthPercent = 1 - ((float)pilot.Injuries / pilot.Health);
            if (pilotHealthPercent < 1)
            {
                ejectModifiers += Settings.PilotHealthMaxModifier * (1 - pilotHealthPercent);
            }

            if (mech.IsUnsteady)
            {
                ejectModifiers += Settings.UnsteadyModifier;
            }

            // Head
            var headHealthPercent = (mech.HeadArmor + mech.HeadStructure) / (mech.GetMaxArmor(ArmorLocation.Head) + mech.GetMaxStructure(ChassisLocations.Head));
            if (headHealthPercent < 1)
            {
                ejectModifiers += Settings.HeadDamageMaxModifier * (1 - headHealthPercent);
            }

            // CT                                                                                   
            var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure + mech.CenterTorsoRearArmor) / (mech.GetMaxArmor(ArmorLocation.CenterTorso) + mech.GetMaxStructure(ChassisLocations.CenterTorso));
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
            var LegPercentRight = 1 - (mech.RightLegStructure + mech.RightLegArmor) / (mech.GetMaxStructure(ChassisLocations.RightLeg) + mech.GetMaxArmor(ArmorLocation.RightLeg));
            var LegPercentLeft = 1 - (mech.LeftLegStructure + mech.LeftLegArmor) / (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
            if ((LegPercentRight + LegPercentLeft) < 2)
            {
                ejectModifiers += Settings.LeggedMaxModifier * (LegPercentRight + LegPercentLeft);
                var LegCheck = LegPercentRight * (mech.GetMaxStructure(ChassisLocations.RightLeg) + mech.GetMaxArmor(ArmorLocation.RightLeg)) + LegPercentLeft * (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
                lowestHealthLethalLocation = Math.Min(LegCheck, lowestHealthLethalLocation);
                lowestHealthLethalLocation = Math.Min(LegCheck, lowestHealthLethalLocation);
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
            var modifiers = (ejectModifiers - Settings.BaseEjectionResist - (Settings.GutsEjectionResistPerPoint * guts) - (Settings.TacticsEjectionResistPerPoint * tactics)) * RogueTechPanicSystem.Settings.EjectChanceMultiplier;
            if (mech.team == mech.Combat.LocalPlayerTeam)
            {
                MoraleConstantsDef moraleDef = mech.Combat.Constants.GetActiveMoraleDef(mech.Combat);
                float medianMorale = 25;
                modifiers -= (mech.Combat.LocalPlayerTeam.Morale - medianMorale)/2;
            }
            if (modifiers < 0)
            {
                return false;
            }

            var rng = (new Random()).Next(1, 101);
            float rollToBeat;
            if (!IsEarlyPanic)
            {
                rollToBeat = Math.Min(modifiers, Settings.MaxEjectChance);
            }
            else
            {
                rollToBeat = Math.Min(modifiers, Settings.MaxEjectChanceWhenEarly);
            }

            mech.Combat.MessageCenter.PublishMessage(!(rng < rollToBeat)
                ? new AddSequenceToStackMessage(new ShowActorInfoSequence(
                    mech, $"{Math.Floor(rollToBeat)}% save SUCCESS for Guts & Tactics ", FloatieMessage.MessageNature.Buff, true))
                : new AddSequenceToStackMessage(new ShowActorInfoSequence(
                    mech, $"{Math.Floor(rollToBeat)}% save FAILED: Punchin' Out!!", FloatieMessage.MessageNature.Debuff, true)));

            return rng < rollToBeat;
        }
    }

    [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete", null)]
    public static class AttackStackSequence_OnAttackComplete_Patch
    {
        public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
        {
            AttackCompleteMessage attackCompleteMessage = message as AttackCompleteMessage;
            bool hasReasonToPanic = false;
            Mech mech = null;
            if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
            {
                return;
            }
            if (__instance.directorSequences[0].target is Mech)
            {
                mech = __instance.directorSequences[0].target as Mech;
                RollHelpers.ShouldPanic(mech, attackCompleteMessage.attackSequence);
            }
            if (mech == null || mech.GUID == null || attackCompleteMessage == null)
            {
                return;
            }
            Holder.SerializeActiveJson();
            if (PanicHelpers.IsPanicking(mech, ref hasReasonToPanic) && RollForEjectionResult(mech, attackCompleteMessage.attackSequence, hasReasonToPanic))
            {
                mech.EjectPilot(mech.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
            }
        }
    }

    //[HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
    //public static class AttackStackSequence_OnAttackComplete_Patch
    //{
    //    public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
    //    {
    //        AttackCompleteMessage attackCompleteMessage = message as AttackCompleteMessage;
    //        bool hasReasonToPanic = false;
    //        bool panicStarted = false;
    //        Mech mech = null;
    //        if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
    //        {
    //            return;
    //        }
    //        if (__instance.directorSequences[0].target is Mech)
    //        {
    //            mech = __instance.directorSequences[0].target as Mech;
    //            hasReasonToPanic = RollHelpers.ShouldPanic(mech, attackCompleteMessage.attackSequence);
    //        }
    //        if (mech == null || mech.GUID == null || attackCompleteMessage == null)
    //        {
    //            return;
    //        }
    //        Holder.SerializeActiveJson();
    //        if (PanicHelpers.IsPanicking(mech, ref panicStarted) && RollForEjectionResult(mech, attackCompleteMessage.attackSequence, panicStarted))
    //        {
    //            mech.EjectPilot(mech.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
    //        }
    //    }
    //}

    [HarmonyPatch(typeof(AbstractActor), "OnNewRound")]
    public static class AbstractActor_BeginNewRound_Patch
    {
        public static void Prefix(AbstractActor __instance)
        {
            if (!(__instance is Mech mech) || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
            {
                return;
            }

            bool FoundPilot = false;
            Pilot pilot = mech.GetPilot();
            int index = -1;

            if (pilot == null)
            {
                return;
            }

            index = PanicHelpers.GetTrackedPilotIndex(mech);
            if (index > -1)
            {
                FoundPilot = true;
            }

            if (!FoundPilot)
            {
                PanicTracker panicTracker = new PanicTracker(mech);
                Holder.TrackedPilots.Add(panicTracker); //add a new tracker to tracked pilot, then we run it all over again;;
                index = PanicHelpers.GetTrackedPilotIndex(mech);
                if (index > -1)
                {
                    FoundPilot = true;
                }
                else
                {
                    return;
                }
            }

            PanicStatus originalStatus = Holder.TrackedPilots[index].pilotStatus;
            if (FoundPilot && !Holder.TrackedPilots[index].ChangedRecently)
            {
                switch (Holder.TrackedPilots[index].pilotStatus)
                {
                    case PanicStatus.Unsettled:
                        Holder.TrackedPilots[index].pilotStatus = PanicStatus.Normal;
                        break;
                    case PanicStatus.Stressed:
                        Holder.TrackedPilots[index].pilotStatus = PanicStatus.Unsettled;
                        break;
                    case PanicStatus.Panicked:
                        Holder.TrackedPilots[index].pilotStatus = PanicStatus.Stressed;
                        break;
                    default:
                        break;
                }

            }

            //reset panic values to account for panic level changes if we get this far, and we recovered.
            if (Holder.TrackedPilots[index].ChangedRecently)
            {
                Holder.TrackedPilots[index].ChangedRecently = false;
            }
            else if (Holder.TrackedPilots[index].pilotStatus != originalStatus)
            {
                __instance.StatCollection.ModifyStat("Panic Turn Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f);
                __instance.StatCollection.ModifyStat("Panic Turn Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f);

                if (Holder.TrackedPilots[index].pilotStatus == PanicStatus.Unsettled)
                {
                    __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                                                                   new ShowActorInfoSequence(mech, $"Unsettled",
                                                                   FloatieMessage.MessageNature.Buff, true)));
                    __instance.StatCollection.ModifyStat("Panic Turn: Unsettled Aim", -1,
                                                         "AccuracyModifier", StatCollection.StatOperation.Float_Add,
                                                         RogueTechPanicSystem.Settings.UnsettledAttackModifier, -1, true);
                }

                else if (Holder.TrackedPilots[index].pilotStatus == PanicStatus.Stressed)
                {
                    __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                                                                  (new ShowActorInfoSequence(mech, $"Stressed",
                                                                  FloatieMessage.MessageNature.Buff, true)));
                    __instance.StatCollection.ModifyStat("Panic Turn: Stressed Aim", -1, "AccuracyModifier", 
                                                         StatCollection.StatOperation.Float_Add,
                                                         RogueTechPanicSystem.Settings.StressedAimModifier);
                    __instance.StatCollection.ModifyStat("Panic Turn: Stressed Defence", -1, "ToHitThisActor", 
                                                         StatCollection.StatOperation.Float_Add,
                                                         RogueTechPanicSystem.Settings.StressedToHitModifier);
                }

                else //now normal
                {
                    __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage
                                                                  (new ShowActorInfoSequence(mech, "Confident", FloatieMessage.MessageNature.Buff, true)));
                }
            }
            Holder.SerializeActiveJson();
        }
    }

    [HarmonyPatch(typeof(GameInstance), "LaunchContract")]
    public static class BattleTech_GameInstance_LaunchContract_Patch
    {
        static void Postfix()
        {
            // reset on new contracts
            Holder.Reset();
        }
    }

    [HarmonyPatch(typeof(AAR_SalvageScreen), "OnCompleted")]
    public static class Battletech_SalvageScreen_Patch
    {
        static void Postfix()
        {
            Holder.Reset(); //don't keep data we don't need after a mission
        }
    }

    [HarmonyPatch(typeof(Mech), "OnLocationDestroyed")]
    public static class Battletech_Mech_LocationDestroyed_Patch
    {
        static void Postfix(Mech __instance)
        {
            if (__instance == null || __instance.IsDead || (__instance.IsFlaggedForDeath && __instance.HasHandledDeath))
            {
                return;
            }

            // G  TODO refactor or improve
            int index = PanicHelpers.GetTrackedPilotIndex(__instance);
            if (RogueTechPanicSystem.Settings.LosingLimbAlwaysPanics)
            {
                if (Holder.TrackedPilots[index].trackedMech != __instance.GUID)
                {
                    return;
                }

                if (Holder.TrackedPilots[index].trackedMech == __instance.GUID &&
                    Holder.TrackedPilots[index].ChangedRecently &&
                    RogueTechPanicSystem.Settings.AlwaysGatedChanges)
                {
                    return;
                }

                if (index < 0)
                {
                    Holder.TrackedPilots.Add(new PanicTracker(__instance)); //add a new tracker to tracked pilot, then we run it all over again;
                    index = PanicHelpers.GetTrackedPilotIndex(__instance);
                    if (index < 0)  // G  Why does this matter?
                    {
                        return;
                    }
                }
                RollHelpers.ApplyPanicDebuff(__instance, index);
            }
        }
    }

    public static class RollHelpers
    {
        public static bool ShouldPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            Logger.Debug($"------ START ------");
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
            if (mech.team.IsLocalPlayer && !RogueTechPanicSystem.Settings.PlayerTeamCanPanic)
            {
                Logger.Debug($"Players can't panic.");
                return false;
            }
            else if (!mech.team.IsLocalPlayer && !RogueTechPanicSystem.Settings.EnemiesCanPanic)
            {
                Logger.Debug($"AI can't panic.");
                return false;
            }
            if (!attackSequence.attackDidDamage)
            {
                Logger.Debug($"No damage.");
                return false;
            }
            if (attackSequence.attackStructureDamage > 0)
            {
                Logger.Debug($"{mech.DisplayName} suffers structure damage from {attackSequence.attacker.DisplayName}.");
                return true;
            }
            else
            {
                var settings = RogueTechPanicSystem.Settings;
                float mininumDamagePercentRequired = settings.MinimumArmourDamagePercentageRequired;  // default is 10%
                float totalArmor = 0, maxArmor = 0;
                maxArmor = GetTotalMechArmour(mech, maxArmor);
                totalArmor = GetCurrentMechArmour(mech, totalArmor);
                float currentArmorPercent = totalArmor / maxArmor * 100;
                float percentOfCurrentArmorDamaged = attackSequence.attackArmorDamage / currentArmorPercent;
                Logger.Debug($"{attackSequence.attacker.DisplayName} attacking {mech.DisplayName} for {attackSequence.attackArmorDamage} to armour.");
                Logger.Debug($"{mech.DisplayName} has {currentArmorPercent.ToString("0.0")}% armor ({totalArmor}/{maxArmor}).  The attack does {(attackSequence.attackArmorDamage / totalArmor * 100).ToString("0.0")}% damage.");
                if (attackSequence.attackArmorDamage / (totalArmor + attackSequence.attackArmorDamage) * 100 >= mininumDamagePercentRequired)
                {
                    Logger.Debug($"Big hit causes panic.");
                    return true;
                }
            }

            Logger.Debug($"Attack yields no reason to panic.  Considering other factors...");

            //dZ Get rid of garbage.
            Pilot pilot = mech.GetPilot();
            var weapons = mech.Weapons;
            var guts = mech.SkillGuts * RogueTechPanicSystem.Settings.GutsEjectionResistPerPoint;
            var tactics = mech.SkillTactics * RogueTechPanicSystem.Settings.TacticsEjectionResistPerPoint;
            var total = guts + tactics;
            int index = -1;
            index = PanicHelpers.GetTrackedPilotIndex(mech);
            float lowestRemaining = mech.CenterTorsoStructure + mech.CenterTorsoFrontArmor + mech.CenterTorsoRearArmor;
            float panicModifiers = 0;

            if (index < 0)
            {
                Holder.TrackedPilots.Add(new PanicTracker(mech)); //add a new tracker to tracked pilot, then we run it all over again;
                index = PanicHelpers.GetTrackedPilotIndex(mech);
                if (index < 0)
                {
                    return false;
                }
            }
            if (Holder.TrackedPilots[index].trackedMech != mech.GUID)
            {
                return false;
            }
            if (Holder.TrackedPilots[index].trackedMech == mech.GUID &&
                Holder.TrackedPilots[index].ChangedRecently && RogueTechPanicSystem.Settings.AlwaysGatedChanges)
            {
                return false;
            }
            // pilot health
            if (pilot != null)
            {
                float pilotHealthPercent = 1 - ((float)pilot.Injuries / pilot.Health);

                if (pilotHealthPercent < 1)
                {
                    panicModifiers += RogueTechPanicSystem.Settings.PilotHealthMaxModifier * (1 - pilotHealthPercent);
                }
            }
            if (mech.IsUnsteady)
            {
                panicModifiers += RogueTechPanicSystem.Settings.UnsteadyModifier;
            }
            // Head
            var headHealthPercent = (mech.HeadArmor + mech.HeadStructure) / (mech.GetMaxArmor(ArmorLocation.Head) + mech.GetMaxStructure(ChassisLocations.Head));
            if (headHealthPercent < 1)
            {
                panicModifiers += RogueTechPanicSystem.Settings.HeadDamageMaxModifier * (1 - headHealthPercent);
            }
            // CT
            var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure + mech.CenterTorsoRearArmor) / (mech.GetMaxArmor(ArmorLocation.CenterTorso) + mech.GetMaxStructure(ChassisLocations.CenterTorso));
            if (ctPercent < 1)
            {
                panicModifiers += RogueTechPanicSystem.Settings.CTDamageMaxModifier * (1 - ctPercent);
                lowestRemaining = Math.Min(mech.CenterTorsoStructure, lowestRemaining);
            }
            // side torsos
            var ltStructurePercent = mech.LeftTorsoStructure / mech.GetMaxStructure(ChassisLocations.LeftTorso);
            if (ltStructurePercent < 1)
            {
                panicModifiers += RogueTechPanicSystem.Settings.SideTorsoInternalDamageMaxModifier * (1 - ltStructurePercent);
            }
            var rtStructurePercent = mech.RightTorsoStructure / mech.GetMaxStructure(ChassisLocations.RightTorso);
            if (rtStructurePercent < 1)
            {
                panicModifiers += RogueTechPanicSystem.Settings.SideTorsoInternalDamageMaxModifier * (1 - rtStructurePercent);
            }

            // dZ Check legs independently. Code here significantly improved.
            var LegPercentRight = 1 - (mech.RightLegStructure + mech.RightLegArmor) / (mech.GetMaxStructure(ChassisLocations.RightLeg) + mech.GetMaxArmor(ArmorLocation.RightLeg));
            var LegPercentLeft = 1 - (mech.LeftLegStructure + mech.LeftLegArmor) / (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
            if ((LegPercentRight + LegPercentLeft) < 2)
            {
                panicModifiers += RogueTechPanicSystem.Settings.LeggedMaxModifier * (LegPercentRight + LegPercentLeft);
                var LegCheck = LegPercentRight * (mech.GetMaxStructure(ChassisLocations.RightLeg) + mech.GetMaxArmor(ArmorLocation.RightLeg)) + LegPercentLeft * (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
                lowestRemaining = Math.Min(LegCheck, lowestRemaining);
                lowestRemaining = Math.Min(LegCheck, lowestRemaining);
            }

            // next shot could kill or leg
            if (lowestRemaining <= attackSequence.cumulativeDamage)
            {
                panicModifiers += RogueTechPanicSystem.Settings.NextShotLikeThatCouldKill;
            }

            // weaponless
            if (weapons.TrueForAll(w =>
                w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional))
            {
                panicModifiers += RogueTechPanicSystem.Settings.WeaponlessModifier;
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                panicModifiers += RogueTechPanicSystem.Settings.AloneModifier;
            }
            //straight up add guts, tactics, and morale to this as negative values
            panicModifiers -= total;
            if (mech.team == mech.Combat.LocalPlayerTeam)

            //dZ - Inputtable morale is superior.
            {
                float medianMorale = 25;
                MoraleConstantsDef moraleDef = mech.Combat.Constants.GetActiveMoraleDef(mech.Combat);
                panicModifiers -= (mech.Combat.LocalPlayerTeam.Morale - medianMorale) / 2;
            }
            
            if ((panicModifiers <= 0) && !RogueTechPanicSystem.Settings.AtLeastOneChanceToPanic)
            {
                return false;
            }
            else if (panicModifiers <= 0)
            {
                panicModifiers = RogueTechPanicSystem.Settings.AtLeastOneChanceToPanicPercentage;
            }

            var rng = (new Random()).Next(1, 101);

            float rollToBeat;
            {
                rollToBeat = Math.Min((int)panicModifiers, (int)RogueTechPanicSystem.Settings.MaxPanicResistTotal);
            }

            if (rng <= rollToBeat)
            {
                Logger.Debug($"Failed panic save, debuffed!");
                ApplyPanicDebuff(mech, index);
                return true;
            }
            mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Resisted Morale Check!", FloatieMessage.MessageNature.Buff, true)));
            Logger.Debug($"No reason to panic.");
            return false;
        }

        private static float GetCurrentMechArmour(Mech mech, float totalArmor)
        {
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

        private static float GetTotalMechArmour(Mech mech, float maxArmor)
        {
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
            if (Holder.TrackedPilots[index].trackedMech == mech.GUID && Holder.TrackedPilots[index].pilotStatus == PanicStatus.Normal)
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Fatigued!", FloatieMessage.MessageNature.Debuff, true)));
                Holder.TrackedPilots[index].pilotStatus = PanicStatus.Unsettled;
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Fatigued Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, RogueTechPanicSystem.Settings.UnsettledAttackModifier, -1, true);

            }
            else if (Holder.TrackedPilots[index].trackedMech == mech.GUID && Holder.TrackedPilots[index].pilotStatus == PanicStatus.Unsettled)
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Stressed!", FloatieMessage.MessageNature.Debuff, true)));
                Holder.TrackedPilots[index].pilotStatus = PanicStatus.Stressed;
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, RogueTechPanicSystem.Settings.StressedAimModifier, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, RogueTechPanicSystem.Settings.StressedToHitModifier, -1, true);
            }
            else if (Holder.TrackedPilots[index].trackedMech == mech.GUID && Holder.TrackedPilots[index].pilotStatus == PanicStatus.Stressed)
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Panicked!", FloatieMessage.MessageNature.Debuff, true)));
                Holder.TrackedPilots[index].pilotStatus = PanicStatus.Panicked;
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Panicking Aim!", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, RogueTechPanicSystem.Settings.PanickedAimModifier, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Panicking Defence!", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, RogueTechPanicSystem.Settings.PanickedToHitModifier, -1, true);
            }
            else
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Failed Panic Check!", FloatieMessage.MessageNature.Debuff, true)));
            }
            Holder.TrackedPilots[index].ChangedRecently = true;
        }
    }

    internal class ModSettings
    {
        public bool PlayerCharacterAlwaysResists = true;
        public bool PlayerTeamCanPanic = true;
        public bool EnemiesCanPanic = true;
        public bool DebugEnabled = false;

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
        public float MinimumArmourDamagePercentageRequired = 10; //if no structure damage, a Mech must lost a bit of its armour before it starts worrying

        //general panic roll
        //rolls out of 20
        //max guts and tactics almost prevents any panicking (or being the player character, by default)
        public bool AtLeastOneChanceToPanic = true;
        public int AtLeastOneChanceToPanicPercentage = 10;
        public bool AlwaysGatedChanges = true;
        public float MaxPanicResistTotal = 15; //at least 20% chance to panic if you can't nullify the whole thing
        public bool LosingLimbAlwaysPanics = false;
        //fatigued debuffs
        //+1 difficulty to attacks
        public float UnsettledAttackModifier = 1;

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
        public float CTDamageMaxModifier = 10;
        public float SideTorsoInternalDamageMaxModifier = 10;
        public float LeggedMaxModifier = 10;

        public float NextShotLikeThatCouldKill = 15;

        public float WeaponlessModifier = 15;
        public float AloneModifier = 20;
    }
}