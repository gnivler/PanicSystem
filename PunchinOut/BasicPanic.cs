using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BattleTech;
using Harmony;
using JetBrains.Annotations;
using Newtonsoft.Json;
using PunchinOut;
using System.IO;
using BattleTech.UI;

namespace BasicPanic
{
    [UsedImplicitly]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [HarmonyPatch(typeof(AttackStackSequence), "OnAttackComplete")]
    public static class AttackStackSequence_OnAttackComplete_Patch
    {
        public static void Prefix(AttackStackSequence __instance, MessageCenterMessage message)
        {
            AttackCompleteMessage attackCompleteMessage = message as AttackCompleteMessage;
            bool ShouldPanic = false;
            bool IsEarlyPanic = false;
            Mech mech = null;

            if (attackCompleteMessage == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
                return;


            if (__instance.directorSequences[0].target is Mech)
            {
                mech = __instance.directorSequences[0].target as Mech;
                ShouldPanic = RollHelpers.ShouldPanic(mech, attackCompleteMessage.attackSequence);


            }

            if(mech == null || mech.GUID == null || attackCompleteMessage == null)
            {
                return;
            }

            Holder.SerializeActiveJson();

            if (PanicHelpers.IsPanicking(mech, ref IsEarlyPanic) && BasicPanic.RollForEjectionResult(mech, attackCompleteMessage.attackSequence, IsEarlyPanic))
            {
                mech.EjectPilot(mech.GUID, attackCompleteMessage.stackItemUID, DeathMethod.PilotEjection, false);
            }
        }
    }

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

                if (Holder.TrackedPilots[index].pilotStatus == PanicStatus.Fatigued)
                {
                    Holder.TrackedPilots[index].pilotStatus = PanicStatus.Normal;
                }

                if (Holder.TrackedPilots[index].pilotStatus == PanicStatus.Stressed)
                {
                    Holder.TrackedPilots[index].pilotStatus = PanicStatus.Fatigued;
                }

                if (Holder.TrackedPilots[index].pilotStatus == PanicStatus.Panicked)
                {
                    Holder.TrackedPilots[index].pilotStatus = PanicStatus.Stressed;
                }

            }
            //reset panic values to account for panic level changes if we get this far, and we recovered.

            if (Holder.TrackedPilots[index].ChangedRecently)
            {
                Holder.TrackedPilots[index].ChangedRecently = false;
            }
            else if (Holder.TrackedPilots[index].pilotStatus != originalStatus)
            {

                __instance.StatCollection.ModifyStat<float>("Panic Turn Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                __instance.StatCollection.ModifyStat<float>("Panic Turn Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);


                if (Holder.TrackedPilots[index].pilotStatus == PanicStatus.Fatigued)
                {
                    __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Recovered To Fatigued!", FloatieMessage.MessageNature.Buff, true)));
                    __instance.StatCollection.ModifyStat<float>("Panic Turn: Fatigued Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.FatiguedAimModifier, -1, true);
                }


                else if (Holder.TrackedPilots[index].pilotStatus == PanicStatus.Stressed)
                {
                    __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Recovered To Stressed!", FloatieMessage.MessageNature.Buff, true)));
                    __instance.StatCollection.ModifyStat<float>("Panic Turn: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.StressedAimModifier, -1, true);
                    __instance.StatCollection.ModifyStat<float>("Panic Turn: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.StressedToHitModifier, -1, true);
                }

                else //now normal
                {
                    __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Recovered To Normal!", FloatieMessage.MessageNature.Buff, true)));
                }
            }

            Holder.SerializeActiveJson();
        }
    }

    [HarmonyPatch(typeof(BattleTech.GameInstance), "LaunchContract", new Type[] { typeof(Contract), typeof(string) })]
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
            int index = PanicHelpers.GetTrackedPilotIndex(__instance);

            if (BasicPanic.Settings.LosingLimbAlwaysPanics)
            {
                if (index < 0)
                {
                    Holder.TrackedPilots.Add(new PanicTracker(__instance)); //add a new tracker to tracked pilot, then we run it all over again;
                    index = PanicHelpers.GetTrackedPilotIndex(__instance);
                    if (index < 0)
                    {

                        return;
                    }

                }

                if (Holder.TrackedPilots[index].trackedMech != __instance.GUID)
                {
                    return;
                }

                if (Holder.TrackedPilots[index].trackedMech == __instance.GUID && Holder.TrackedPilots[index].ChangedRecently && BasicPanic.Settings.AlwaysGatedChanges)
                {
                    return;
                }
                RollHelpers.ApplyPanicDebuff(__instance, index);
            }
        }
    }

    public static class RollHelpers
    {
        public static bool ShouldPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
            {
                return false;
            }

            if(attackSequence == null)
            {
                return false;
            }

            if (!attackSequence.attackDidDamage) //no point in panicking over nothing
            {
                return false;
            }

            if(!attackSequence.attackDamagedStructure && !attackSequence.lowArmorStruck) // no structure damage and didn't strike low armour (unclear what lowArmorStruck is)
            {
                Logger.Logline($"attackDamagedStructure {attackSequence.attackDamagedStructure}, lowArmorStruck {attackSequence.lowArmorStruck}");
                float totalArmor = 0, maxArmor = 0;
                maxArmor = GetTotalMechArmour(mech, maxArmor);
                totalArmor = GetCurrentMechArmour(mech, totalArmor);
                float currentArmorPercent = totalArmor / maxArmor * 100;
                Logger.Logline($"maxArmor {maxArmor}, totalArmor {totalArmor}, currentArmorPercent { currentArmorPercent}");
                var settings = BasicPanic.Settings;

                var percentOfCurrentArmorDamaged = attackSequence.attackArmorDamage / currentArmorPercent;
                float mininumDamagePerecentRequired = settings.MinimumArmourDamagePercentageRequired;
                
                if (percentOfCurrentArmorDamaged <= 10) // (deprecated) basically if this equals to 100%, mech didn't lose enough armour
                {
                    return false;
                }
            }


            if (mech.team == mech.Combat.LocalPlayerTeam && !BasicPanic.Settings.PlayerTeamCanPanic)
            {
                return false;
            }
            else if (mech.team != mech.Combat.LocalPlayerTeam && !BasicPanic.Settings.EnemiesCanPanic)
            {
                return false;
            }

            int PanicRoll = 0;

            Pilot pilot = mech.GetPilot();
            var weapons = mech.Weapons;
            var guts = mech.SkillGuts;
            var tactics = mech.SkillTactics;
            var total = guts + tactics;
            int index = -1;

            index = PanicHelpers.GetTrackedPilotIndex(mech);
                
  
            float lowestRemaining = mech.CenterTorsoStructure + mech.CenterTorsoFrontArmor;
            float panicModifiers = 0;

            if (index < 0)
            {
                Holder.TrackedPilots.Add(new PanicTracker(mech)); //add a new tracker to tracked pilot, then we run it all over again;

                index = PanicHelpers.GetTrackedPilotIndex(mech);
                if(index < 0)
                {
                    
                    return false;
                }
 
            }
           
            if (Holder.TrackedPilots[index].trackedMech != mech.GUID)
                return false;

            if (Holder.TrackedPilots[index].trackedMech == mech.GUID && 
                Holder.TrackedPilots[index].ChangedRecently && BasicPanic.Settings.AlwaysGatedChanges)
            {
                return false;
            }

            // pilot health
            if (pilot != null)
            {
                float pilotHealthPercent = 1 - ((float)pilot.Injuries / pilot.Health);

                if (pilotHealthPercent < 1)
                {
                    panicModifiers += BasicPanic.Settings.PilotHealthMaxModifier * (1 - pilotHealthPercent);
                }
            }

            if (mech.IsUnsteady)
            {
                panicModifiers += BasicPanic.Settings.UnsteadyModifier;
            }

            // Head
            var headHealthPercent = (mech.HeadArmor + mech.HeadStructure) / (mech.GetMaxArmor(ArmorLocation.Head) + mech.GetMaxStructure(ChassisLocations.Head));
            if (headHealthPercent < 1)
            {
                panicModifiers += BasicPanic.Settings.HeadDamageMaxModifier * (1 - headHealthPercent);
            }

            // CT
            var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure) / (mech.GetMaxArmor(ArmorLocation.CenterTorso) + mech.GetMaxStructure(ChassisLocations.CenterTorso));
            if (ctPercent < 1)
            {
                panicModifiers += BasicPanic.Settings.CTDamageMaxModifier * (1 - ctPercent);
                lowestRemaining = Math.Min(mech.CenterTorsoStructure, lowestRemaining);
            }

            // side torsos
            var ltStructurePercent = mech.LeftTorsoStructure / mech.GetMaxStructure(ChassisLocations.LeftTorso);
            if (ltStructurePercent < 1)
            {
                panicModifiers += BasicPanic.Settings.SideTorsoInternalDamageMaxModifier * (1 - ltStructurePercent);
            }

            var rtStructurePercent = mech.RightTorsoStructure / mech.GetMaxStructure(ChassisLocations.RightTorso);
            if (rtStructurePercent < 1)
            {
                panicModifiers += BasicPanic.Settings.SideTorsoInternalDamageMaxModifier * (1 - rtStructurePercent);
            }

            // legs
            if (mech.RightLegDamageLevel == LocationDamageLevel.Destroyed || mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                float legPercent;

                if (mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
                {
                    legPercent = (mech.RightLegStructure + mech.RightLegArmor) / (mech.GetMaxStructure(ChassisLocations.RightLeg) + mech.GetMaxArmor(ArmorLocation.RightLeg));
                }
                else
                {
                    legPercent = (mech.LeftLegStructure + mech.LeftLegArmor) / (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
                }

                if (legPercent < 1)
                {
                    lowestRemaining = Math.Min(legPercent * (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg)), lowestRemaining);
                    panicModifiers += BasicPanic.Settings.LeggedMaxModifier * (1 - legPercent);
                }
            }

            // next shot could kill
            if (lowestRemaining <= attackSequence.cumulativeDamage)
            {
                panicModifiers += BasicPanic.Settings.NextShotLikeThatCouldKill;
            }

            // weaponless
            if (weapons.TrueForAll(w =>
                w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional))
            {
                panicModifiers += BasicPanic.Settings.WeaponlessModifier;
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                panicModifiers += BasicPanic.Settings.AloneModifier;
            }
            //straight up add guts, tactics, and morale to this as negative values
            panicModifiers -= total;
            if(mech.team == mech.Combat.LocalPlayerTeam)
            {
                MoraleConstantsDef moraleDef = mech.Combat.Constants.GetActiveMoraleDef(mech.Combat);
                panicModifiers -= Math.Max(mech.Combat.LocalPlayerTeam.Morale - moraleDef.CanUseInspireLevel, 0) / (float)2;
            }

            //reduce modifiers by 5 to account change to D20 roll instead of D100 roll, then min it t0 20 or modified floor
            panicModifiers /= 5;
            
            PanicRoll = PanicRoll + (int)panicModifiers;

            if ((total >= 20 || PanicRoll <= 0) && !BasicPanic.Settings.AtLeastOneChanceToPanic)
                return false;

            PanicRoll = Math.Min(PanicRoll, 20);

            if(PanicRoll < 0)
            {
                PanicRoll = 0; //make this have some kind of chance to happen
            }
            PanicRoll = UnityEngine.Random.Range(PanicRoll, 20); // actual roll
            //we get this far, we reduce total to under the max panic chance
            total = Math.Min(total, (int)BasicPanic.Settings.MaxPanicResistTotal);

            int rngRoll = UnityEngine.Random.Range(total, 20);

            if(rngRoll <= PanicRoll)
            {
                ApplyPanicDebuff(mech, index);
                return true;
            }
            mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Resisted Morale Check!", FloatieMessage.MessageNature.Buff, true)));
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
                Holder.TrackedPilots[index].pilotStatus = PanicStatus.Fatigued;


                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Fatigued Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.FatiguedAimModifier, -1, true);

            }
            else if (Holder.TrackedPilots[index].trackedMech == mech.GUID && Holder.TrackedPilots[index].pilotStatus == PanicStatus.Fatigued)
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Stressed!", FloatieMessage.MessageNature.Debuff, true)));
                Holder.TrackedPilots[index].pilotStatus = PanicStatus.Stressed;
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.StressedAimModifier, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.StressedToHitModifier, -1, true);
            }
            else if (Holder.TrackedPilots[index].trackedMech == mech.GUID && Holder.TrackedPilots[index].pilotStatus == PanicStatus.Stressed)
            {
                mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Panicked!", FloatieMessage.MessageNature.Debuff, true)));
                Holder.TrackedPilots[index].pilotStatus = PanicStatus.Panicked;

                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Panicking Aim!", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.PanickedAimModifier, -1, true);
                mech.StatCollection.ModifyStat<float>("Panic Attack: Panicking Defence!", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.PanickedToHitModifier, -1, true);
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

        //new mechanics for considering when to eject based on mech class
        public bool PlayerLightsConsiderEjectingEarly = false;
        public bool EnemyLightsConsiderEjectingEarly = true;
        public PanicStatus LightMechEarlyPanicThreshold = PanicStatus.Fatigued;

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
        public bool AlwaysGatedChanges = true;
        public float MaxPanicResistTotal = 15; //at least 20% chance to panic if you can't nullify the whole thing
        public bool LosingLimbAlwaysPanics = false;
        //fatigued debuffs
        //+1 difficulty to attacks
        public float FatiguedAimModifier = 1;

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
        public float MaxEjectChance = 50;

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

    public static class BasicPanic
    {
        internal static ModSettings Settings;

        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("io.github.RealityMachina.BasicPanic");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Holder.ModDirectory = Path.Combine(Path.GetDirectoryName(VersionManifestUtilities.MANIFEST_FILEPATH), @"..\..\..\Mods\BasicPanicSystem");
            Holder.ActiveJsonPath = Path.Combine(Holder.ModDirectory, "BasicPanicSystem.json");
            Holder.StorageJsonPath = Path.Combine(Holder.ModDirectory, "BasicPanicSystemStorage.json");
            try
            {
                Settings = JsonConvert.DeserializeObject<ModSettings>(modSettings);
            }
            catch (Exception)
            {
                Settings = new ModSettings();
            }
        }
        
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
            var weapons = mech.Weapons;
            var guts = mech.SkillGuts;
            var tactics = mech.SkillTactics;
            var total = guts + tactics;

            float lowestRemaining = mech.CenterTorsoStructure + mech.CenterTorsoFrontArmor;
            float ejectModifiers = 0;
            
            // guts 10 makes you immune, player character cannot be forced to eject
            if ((guts >= 10 && Settings.GutsTenAlwaysResists) || (pilot != null && pilot.IsPlayerCharacter && Settings.PlayerCharacterAlwaysResists))
                return false;

            // tactics 10 makes you immune, or combination of guts and tactics makes you immune.
            if ((tactics >= 10 && Settings.TacticsTenAlwaysResists) || (total >= 10 && Settings.ComboTenAlwaysResists))
                return false;

            // pilots that cannot eject or be headshot shouldn't eject
            if (!mech.CanBeHeadShot || (pilot != null && !pilot.CanEject))
                return false;

            // pilot health
            if (pilot != null)
            {
                float pilotHealthPercent = 1 - ((float)pilot.Injuries / pilot.Health);

                if (pilotHealthPercent < 1)
                {
                    ejectModifiers += Settings.PilotHealthMaxModifier * (1 - pilotHealthPercent);
                }
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
            var ctPercent = (mech.CenterTorsoFrontArmor + mech.CenterTorsoStructure) / (mech.GetMaxArmor(ArmorLocation.CenterTorso) + mech.GetMaxStructure(ChassisLocations.CenterTorso));
            if (ctPercent < 1)
            {
                ejectModifiers += Settings.CTDamageMaxModifier * (1 - ctPercent);
                lowestRemaining = Math.Min(mech.CenterTorsoStructure, lowestRemaining);
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
            if (mech.RightLegDamageLevel == LocationDamageLevel.Destroyed || mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
            {
                float legPercent;

                if (mech.LeftLegDamageLevel == LocationDamageLevel.Destroyed)
                {
                    legPercent = (mech.RightLegStructure + mech.RightLegArmor) / (mech.GetMaxStructure(ChassisLocations.RightLeg) + mech.GetMaxArmor(ArmorLocation.RightLeg));
                }
                else
                {
                    legPercent = (mech.LeftLegStructure + mech.LeftLegArmor) / (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg));
                }

                if (legPercent < 1)
                {
                    lowestRemaining = Math.Min(legPercent * (mech.GetMaxStructure(ChassisLocations.LeftLeg) + mech.GetMaxArmor(ArmorLocation.LeftLeg)), lowestRemaining);
                    ejectModifiers += Settings.LeggedMaxModifier * (1 - legPercent);
                }
            }

            // next shot could kill
            if (lowestRemaining <= attackSequence.cumulativeDamage)
            {
                ejectModifiers += Settings.NextShotLikeThatCouldKill;
            }
            
            // weaponless
            if (weapons.TrueForAll(w =>
                w.DamageLevel == ComponentDamageLevel.Destroyed || w.DamageLevel == ComponentDamageLevel.NonFunctional))
            {
                ejectModifiers += Settings.WeaponlessModifier;
            }

            // alone
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech as AbstractActor))
            {
                ejectModifiers += Settings.AloneModifier;
            }

            var modifiers = (ejectModifiers - Settings.BaseEjectionResist - (Settings.GutsEjectionResistPerPoint * guts) - (Settings.TacticsEjectionResistPerPoint * tactics) ) * 5;

            if (mech.team == mech.Combat.LocalPlayerTeam)
            {
                MoraleConstantsDef moraleDef = mech.Combat.Constants.GetActiveMoraleDef(mech.Combat);
               modifiers -= Math.Max(mech.Combat.LocalPlayerTeam.Morale - moraleDef.CanUseInspireLevel, 0);
            }

            if (modifiers < 0)
                return false;
            
            var rng = (new System.Random()).Next(100);
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
                ? new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Guts/Tactics Check Passed {Math.Floor(rollToBeat)}%", FloatieMessage.MessageNature.Buff, true))
                : new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Punchin' Out! {Math.Floor(rollToBeat)}%", FloatieMessage.MessageNature.Debuff, true)));

            return rng < rollToBeat;
        }
    }
}
