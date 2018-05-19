using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BattleTech;
using Harmony;
using JetBrains.Annotations;
using Newtonsoft.Json;
using System.Collections.Generic;
using PunchinOut;

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
            Mech mech = null;
            if (attackCompleteMessage  == null || attackCompleteMessage.stackItemUID != __instance.SequenceGUID)
                return;

            if (__instance.directorSequences[0].target is Mech)
            {
                ShouldPanic = RollHelpers.ShouldPanic(__instance.directorSequences[0].target as Mech, attackCompleteMessage.attackSequence);
                mech = __instance.directorSequences[0].target as Mech;
                
            }

            if (PanicHelpers.IsPanicking(mech) && BasicPanic.RollForEjectionResult(mech, attackCompleteMessage.attackSequence))
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
                return;

            bool FoundPilot = false;
            Pilot pilot = mech.GetPilot();
            int index = 0;

            if (pilot == null)
                return;

            index = PanicHelpers.GetTrackedPilotIndex(pilot);
            if (index > 0)
            {
                FoundPilot = true;
            }


            if (!FoundPilot)
            {
                Holder.TrackedPilots.Add(new PanicTracker(pilot)); //add a new tracker to tracked pilot, then we run it all over again;

                index = PanicHelpers.GetTrackedPilotIndex(pilot);
                if(index > 0)
                {
                    FoundPilot = true;
                }
            }

            if(FoundPilot && !Holder.TrackedPilots[index].ChangedRecently)
            {
                if(Holder.TrackedPilots[index].pilotStatus == PanicStatus.Fatigued)
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
            else
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
                    __instance.StatCollection.ModifyStat<float>("Panic Turn: Panicking Aim!", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.PanickedAimModifier, -1, true);
                    __instance.StatCollection.ModifyStat<float>("Panic Turn: Panicking Defence!", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.PanickedToHitModifier, -1, true);
                }
            }
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

    public static class PanicHelpers
    {
        public static bool IsPanicking(Mech mech)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
                return false;

            var pilot = mech.GetPilot();

            if (pilot == null)
                return false;

            int i = GetTrackedPilotIndex(pilot);

            if(i > 0)
            {
                if (Holder.TrackedPilots[i].trackedPilot == pilot.GUID &&
                    Holder.TrackedPilots[i].pilotStatus == PanicStatus.Panicked)
                {
                    return true;
                }
            }

            

            return false;
        }

        public static int GetTrackedPilotIndex(Pilot pilot)
        {
            for (int i = 0; i < Holder.TrackedPilots.Count; i++)
            {

                if (Holder.TrackedPilots[i].trackedPilot == pilot.GUID)
                {
                    return i;
                }
            }

            return -1;
        }

    }

    public static class RollHelpers
    {
        public static bool ShouldPanic(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && mech.HasHandledDeath))
                    return false;

            int PanicRoll = UnityEngine.Random.Range(0, 20); // initial roll

            if (!attackSequence.attackDidDamage) //no point in panicking over nothing
                return false;

            var pilot = mech.GetPilot();
            var weapons = mech.Weapons;
            var guts = mech.SkillGuts;
            var tactics = mech.SkillTactics;
            var total = guts + tactics;
            var index = PanicHelpers.GetTrackedPilotIndex(pilot);
            float lowestRemaining = mech.CenterTorsoStructure + mech.CenterTorsoFrontArmor;
            float panicModifiers = 0;

            if (Holder.TrackedPilots[index].trackedPilot != pilot.GUID)
                return false;

            if(Holder.TrackedPilots[index].trackedPilot == pilot.GUID && 
                Holder.TrackedPilots[index].ChangedRecently && BasicPanic.Settings.AlwaysGatedChanges)
            {
                return false;
            }

            // pilot health
            if (pilot != null)
            {
                var pilotHealthPercent = 1 - (pilot.Injuries / pilot.Health);

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
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech))
            {
                panicModifiers += BasicPanic.Settings.AloneModifier;
            }
            //reduce modifiers by five to account change to D20 roll instead of D100 roll, then min it t0 20 or modified floor

            panicModifiers /= 5;

            PanicRoll = PanicRoll + (int)panicModifiers;

            PanicRoll = Math.Min(PanicRoll, 20);

            if (total >= 20 && !BasicPanic.Settings.AtLeastOneChanceToPanic)
                return false;

            //we get this far, we reduce total to under the max panic chance
            total = Math.Min(total, 20 - 1);

            int rngRoll = UnityEngine.Random.Range(total, 20);

            if(rngRoll < PanicRoll)
            {

                if (Holder.TrackedPilots[index].trackedPilot == pilot.GUID && Holder.TrackedPilots[index].pilotStatus == PanicStatus.Normal)
                {
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Fatigued!", FloatieMessage.MessageNature.Debuff, true)));
                    Holder.TrackedPilots[index].pilotStatus = PanicStatus.Fatigued;
                    Holder.TrackedPilots[index].ChangedRecently = true;

                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Fatigued Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.FatiguedAimModifier, -1, true);

                }
                else if (Holder.TrackedPilots[index].trackedPilot == pilot.GUID && Holder.TrackedPilots[index].pilotStatus == PanicStatus.Fatigued)
                {
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Stressed!", FloatieMessage.MessageNature.Debuff, true)));
                    Holder.TrackedPilots[index].pilotStatus = PanicStatus.Stressed;
                    Holder.TrackedPilots[index].ChangedRecently = true;
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Stressed Aim", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.StressedAimModifier, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Stressed Defence", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.StressedToHitModifier, -1, true);
                }
                else if (Holder.TrackedPilots[index].trackedPilot == pilot.GUID && Holder.TrackedPilots[index].pilotStatus == PanicStatus.Stressed)
                {
                    mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Panicked!", FloatieMessage.MessageNature.Debuff, true)));
                    Holder.TrackedPilots[index].pilotStatus = PanicStatus.Panicked;
                    Holder.TrackedPilots[index].ChangedRecently = true;
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Accuracy", -1, "AccuracyModifier", StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack Reset: Mech To Hit", -1, "ToHitThisActor", StatCollection.StatOperation.Set, 0f, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Panicking Aim!", -1, "AccuracyModifier", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.PanickedAimModifier, -1, true);
                    mech.StatCollection.ModifyStat<float>("Panic Attack: Panicking Defence!", -1, "ToHitThisActor", StatCollection.StatOperation.Float_Add, BasicPanic.Settings.PanickedToHitModifier, -1, true);
                }
                return true;
            }
            mech.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Resisted Panic Check!", FloatieMessage.MessageNature.Buff, true)));
            return false;
        }

    }
    internal class ModSettings
    {
        public bool PlayerCharacterAlwaysResists = true;


        //general panic roll
        //rolls out of 20
        //max guts and tactics almost prevents any panicking (or being the player character, by default)
        public bool AtLeastOneChanceToPanic = true;
        public bool AlwaysGatedChanges = true;

        //fatigued debuffs
        //-5 to aim
        public float FatiguedAimModifier = -5;

        //stressed debuffs
        //-10 to aim
        //+5 to being hit

        public float StressedAimModifier = -10;
        public float StressedToHitModifier = 5;
        //ejection
        //-15 to aim
        //+5 to being hit
        public float PanickedAimModifier = -15;
        public float PanickedToHitModifier = 5;
        public bool GutsTenAlwaysResists = true;
        public bool ComboTenAlwaysResists = false;
        public bool TacticsTenAlwaysResists = true;

        public bool KnockedDownCannotEject = true;

        public float MaxEjectChance = 50;

        public float BaseEjectionResist = 10;
        public float GutsEjectionResistPerPoint = 2;
        public float TacticsEjectionResistPerPoint = 1;
        public float UnsteadyModifier = 3;
        public float PilotHealthMaxModifier = 5;

        public float HeadDamageMaxModifier = 5;
        public float CTDamageMaxModifier = 10;
        public float SideTorsoInternalDamageMaxModifier = 5;
        public float LeggedMaxModifier = 10;

        public float NextShotLikeThatCouldKill = 10;
        
        public float WeaponlessModifier = 10;
        public float AloneModifier = 10;
    }
    public static class Holder
    {
        public static List<PanicTracker> TrackedPilots;

        public static void Reset()
        {
            TrackedPilots = new List<PanicTracker>();
        }
        
    }

    public static class BasicPanic
    {
        internal static ModSettings Settings;

        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("io.github.RealityMachina.BasicPanic");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            try
            {
                Settings = JsonConvert.DeserializeObject<ModSettings>(modSettings);
            }
            catch (Exception)
            {
                Settings = new ModSettings();
            }
        }
        
        public static bool RollForEjectionResult(Mech mech, AttackDirector.AttackSequence attackSequence)
        {
            if (mech == null || mech.IsDead || (mech.IsFlaggedForDeath && !mech.HasHandledDeath))
                return false;

            // knocked down mechs cannot eject
            if (mech.IsProne && Settings.KnockedDownCannotEject)
                return false;

            // have to do damage
            if (!attackSequence.attackDidDamage)
                return false;

            var pilot = mech.GetPilot();
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
                var pilotHealthPercent = 1 - (pilot.Injuries / pilot.Health);

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
            if (mech.Combat.GetAllAlliesOf(mech).TrueForAll(m => m.IsDead || m == mech))
            {
                ejectModifiers += Settings.AloneModifier;
            }

            var modifiers = (ejectModifiers - Settings.BaseEjectionResist - (Settings.GutsEjectionResistPerPoint * guts) - (Settings.TacticsEjectionResistPerPoint * tactics) ) * 5;

            if (modifiers < 0)
                return false;
            
            var rng = (new System.Random()).Next(100);
            var rollToBeat = Math.Min(modifiers, Settings.MaxEjectChance);

            mech.Combat.MessageCenter.PublishMessage(!(rng < rollToBeat)
                ? new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Guts/Tactics Check Passed {Math.Floor(rollToBeat)}%", FloatieMessage.MessageNature.Buff, true))
                : new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, $"Punchin' Out! {Math.Floor(rollToBeat)}%", FloatieMessage.MessageNature.Debuff, true)));

            return rng < rollToBeat;
        }
    }
}
