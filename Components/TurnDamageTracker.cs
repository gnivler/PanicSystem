using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using Newtonsoft.Json;
using static PanicSystem.PanicSystem;
using static PanicSystem.Logger;
using static PanicSystem.Helpers;

namespace PanicSystem.Components
{
    public static class TurnDamageTracker
    {
        private static Dictionary<string, float> turnStartArmor = new Dictionary<string, float>();
        private static Dictionary<string, float> turnStartStructure = new Dictionary<string, float>();
        private static Dictionary<string, int> turnExternalHeatAccumulator = new Dictionary<string, int>();
        private static List<AbstractActor> activationVictims=new List<AbstractActor>();
        private static List<string> ejectedActors = new List<string>();

        private static AbstractActor attacker=null;
        internal static void newTurnFor(AbstractActor actor)
        {
            if (actor != attacker)
            {
                LogReport($"new Turn Activation for {actor.Nickname} - {actor.DisplayName} - {actor.GUID}");
                attacker = actor;
            }
            else
            {
                LogReport($"new Turn Re-Activation for {actor.Nickname}");
            }
        }

        internal static void batchDamageDuringActivation(AbstractActor actor, float damage, float directStructureDamage, int heatdamage)
        {
            if (!(actor is Mech) && !(actor is Vehicle))
                return;
            if (!activationVictims.Contains(actor))
            {
                activationVictims.Add(actor);
                LogReport($"{actor.DisplayName}|{actor.Nickname}|{actor.GUID} added to victims. [{activationVictims.Count}]");
            }
            if (!turnExternalHeatAccumulator.ContainsKey(actor.GUID))
            {//got here without a new turn for defender use max values for struc/armor
                firstTurnFor(actor);
            }
            //acumulate heat
            turnExternalHeatAccumulator[actor.GUID] = turnExternalHeatAccumulator[actor.GUID] + heatdamage;
            //no need to accumulate armor/structure, we noted values on turn activation start
        }

        internal static void completedTurnFor(AbstractActor actor)
        {
            if (attacker != null)
            {
                LogReport($"completed Turn Activation for {actor.Nickname} - {actor.DisplayName} - {actor.GUID} -victims [{activationVictims.Count}]");
                foreach (AbstractActor v in activationVictims)
                {
                    DamageHandler.ProcessBatchedTurnDamage(v);
                }
                activationVictims.Clear();
            }
            attacker = null;

            if(modSettings.OneChangePerTurn)
                resetDamageTrackerFor(actor);
        }

        internal static void resetDamageTrackerFor(AbstractActor actor)
        {
            if(actor==null)
                return;

            turnExternalHeatAccumulator[actor.GUID] = 0;//external heat 0 start of activation
            if (actor is Mech mech)
            {
                turnStartStructure[actor.GUID] = mech.RightTorsoStructure + mech.LeftTorsoStructure + mech.CenterTorsoStructure + mech.LeftLegStructure + mech.RightLegStructure + mech.HeadStructure;

                turnStartArmor[actor.GUID] = mech.RightTorsoFrontArmor + mech.RightTorsoRearArmor + mech.LeftTorsoFrontArmor + mech.LeftTorsoRearArmor +
                     mech.CenterTorsoFrontArmor + mech.CenterTorsoRearArmor + mech.LeftLegArmor + mech.RightLegArmor + mech.HeadArmor;
            }
            else if (actor is Vehicle v)
            {
                turnStartStructure[actor.GUID] = v.LeftSideStructure + v.RightSideStructure + v.FrontStructure + v.RearStructure + v.TurretStructure;

                turnStartArmor[actor.GUID] = v.LeftSideArmor + v.RightSideArmor + v.FrontArmor + v.RearArmor + v.TurretArmor;
            }
            else
            {
                LogReport("Not mech or vehicle");
                turnStartStructure[actor.GUID] = 0;
                turnStartArmor[actor.GUID] = 0;
            }

            LogReport($"Damage Levels reset for {actor.Nickname} - {actor.DisplayName} - {actor.GUID} -H:{turnExternalHeatAccumulator[actor.GUID]} A:{turnStartArmor[actor.GUID]} S:{turnStartStructure[actor.GUID]}");

        }


        internal static void hintAttackComplete( string reason)
        {
            if (attacker != null)
            {
                LogReport($"attack was completed for {attacker.Nickname} - {attacker.DisplayName} - {attacker.GUID} ({reason}) -victims [{activationVictims.Count}]");
                foreach (AbstractActor actor in activationVictims)
                {
                    DamageHandler.ProcessBatchedTurnDamage(actor);
                }
                activationVictims.Clear();
            }
        }

        internal static AbstractActor attackActor()
        {
            return attacker;
        }

        internal static void firstTurnFor(AbstractActor actor)
        {
            LogReport($"first Turn for {actor.Nickname}  - {actor.DisplayName} - {actor.GUID}");
            turnExternalHeatAccumulator[actor.GUID]=0;//external heat 0 start of turn
            if (actor is Mech mech)
            {
                turnStartStructure[actor.GUID]= MaxStructureForLocation(mech, (int)ChassisLocations.RightTorso) + MaxStructureForLocation(mech, (int)ChassisLocations.LeftTorso) + MaxStructureForLocation(mech, (int)ChassisLocations.CenterTorso)
                    + MaxStructureForLocation(mech, (int)ChassisLocations.LeftLeg) + MaxStructureForLocation(mech, (int)ChassisLocations.RightLeg) + MaxStructureForLocation(mech, (int)ChassisLocations.Head);

                turnStartArmor[actor.GUID]= MaxArmorForLocation(mech, (int)ArmorLocation.RightTorso) + MaxArmorForLocation(mech, (int)ArmorLocation.RightTorsoRear)
             + MaxArmorForLocation(mech, (int)ArmorLocation.LeftTorso) + MaxArmorForLocation(mech, (int)ArmorLocation.LeftTorsoRear)
             + MaxArmorForLocation(mech, (int)ArmorLocation.CenterTorso) + MaxArmorForLocation(mech, (int)ArmorLocation.CenterTorsoRear)
             + MaxArmorForLocation(mech, (int)ArmorLocation.LeftLeg) + MaxArmorForLocation(mech, (int)ArmorLocation.RightLeg)
             + MaxArmorForLocation(mech, (int)ArmorLocation.Head);
            }
            else if (actor is Vehicle v)
            {
                turnStartStructure[actor.GUID]= MaxStructureForLocation(v, (int)VehicleChassisLocations.Left) + MaxStructureForLocation(v, (int)VehicleChassisLocations.Right)+ MaxStructureForLocation(v, (int)VehicleChassisLocations.Front)+ MaxStructureForLocation(v, (int)VehicleChassisLocations.Rear)+ MaxStructureForLocation(v, (int)VehicleChassisLocations.Turret);

                turnStartArmor[actor.GUID]= MaxArmorForLocation(v, (int)VehicleChassisLocations.Left) + MaxArmorForLocation(v, (int)VehicleChassisLocations.Right) + MaxArmorForLocation(v, (int)VehicleChassisLocations.Front) + MaxArmorForLocation(v, (int)VehicleChassisLocations.Rear) + MaxArmorForLocation(v, (int)VehicleChassisLocations.Turret);
            }
            else
            {
                LogReport("Not mech or vehicle");
                turnStartStructure[actor.GUID]= 0;
                turnStartArmor[actor.GUID]=0;
            }


        }

        internal static bool EjectionAlreadyCounted(AbstractActor a)
        {
            if (!ejectedActors.Contains(a.GUID))
            {
                LogReport($"{a.Nickname} - {a.DisplayName} - {a.GUID} ejection !");
                ejectedActors.Add(a.GUID);
                return false;
            }
            LogReport($"{a.Nickname} - {a.DisplayName} - {a.GUID} ejection already counted! DUPLICATE CAUGHT!!!");
            return true;
        }

        internal static void Reset()
        {
            LogReport($"Turn Damage Tracker Reset - new Mission");
            turnStartArmor = new Dictionary<string, float>();
            turnStartStructure = new Dictionary<string, float>();
            turnExternalHeatAccumulator = new Dictionary<string, int>();
            attacker = null;
            activationVictims = new List<AbstractActor>();
            ejectedActors = new List<string>();
            Patches.VehicleRepresentation.hookPDF();
        }

        internal static void DamageDuringTurn(AbstractActor actor,out float armorDamage,out float structureDamage,out float previousArmor,out float previousStructure,out int heatdamage)
        {
            if (!turnExternalHeatAccumulator.ContainsKey(actor.GUID))
            {//got here without a new turn for actor use max values for struc/armor
                firstTurnFor(actor);
            }

            //return accumulated heat
            heatdamage = turnExternalHeatAccumulator[actor.GUID];
            //return armor/structure at start of turn
            previousArmor = turnStartArmor[actor.GUID];
            previousStructure = turnStartStructure[actor.GUID];
            if (actor is Mech mech)
            {
                structureDamage = previousStructure - (mech.RightTorsoStructure + mech.LeftTorsoStructure + mech.CenterTorsoStructure + mech.LeftLegStructure + mech.RightLegStructure + mech.HeadStructure);
                armorDamage = previousArmor - (mech.RightTorsoFrontArmor + mech.RightTorsoRearArmor + mech.LeftTorsoFrontArmor + mech.LeftTorsoRearArmor +
                     mech.CenterTorsoFrontArmor + mech.CenterTorsoRearArmor + mech.LeftLegArmor + mech.RightLegArmor + mech.HeadArmor);
            }
            else if (actor is Vehicle v)
            {
                structureDamage = previousStructure - (v.LeftSideStructure + v.RightSideStructure + v.FrontStructure + v.RearStructure + v.TurretStructure);
                armorDamage = previousArmor - (v.LeftSideArmor + v.RightSideArmor + v.FrontArmor + v.RearArmor + v.TurretArmor);
            }
            else
            {
                structureDamage = 0;
                armorDamage = 0;
                LogReport("Not mech or vehicle");
            }
        }
    }
}
