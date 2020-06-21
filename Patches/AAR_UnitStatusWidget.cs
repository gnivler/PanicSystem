using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using BattleTech.UI;
using Harmony;
using PanicSystem.Components;
using UnityEngine;
using static PanicSystem.Logger;
using static PanicSystem.PanicSystem;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(AAR_UnitStatusWidget), "FillInData")]
    public static class AAR_UnitStatusWidget_FillInData_Patch
    {
        private static int mechEjections;
        private static int vehicleEjections;

        public static void Prefix(UnitResult ___UnitData)
        {
            try
            {
                mechEjections = 0;
                vehicleEjections = 0;
                // get the total and decrement it globally
                var MechsEjected = ___UnitData.pilot.StatCollection.GetStatistic("MechsEjected");
                if (MechsEjected != null)
                {
                    mechEjections = MechsEjected.Value<int>();
                    LogDebug($"{___UnitData.pilot.Callsign} MechsEjected {mechEjections}");
                }

                var VehiclesEjected = ___UnitData.pilot.StatCollection.GetStatistic("VehiclesEjected");
                if (VehiclesEjected != null)
                {
                    vehicleEjections = VehiclesEjected.Value<int>();
                    LogDebug($"{___UnitData.pilot.Callsign} vehicleEjections {vehicleEjections}");
                }
            }
            catch (Exception ex)
            {
                LogDebug(ex);
            }
        }

        // subtract ejection kills to reduce the number of regular kill stamps drawn
        // then draw red ones to replace them in Postfix
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            try
            {
                // subtract our value right as the getter comes back
                // makes the game draw fewer normal stamps
                if (modSettings.VehiclesCanPanic)
                {
                    var vehicleIndex = codes.FindIndex(x => x.operand is MethodInfo info &&
                                                            info == AccessTools.Method(typeof(Pilot), "get_OthersKilled"));

                    var vehicleStack = new List<CodeInstruction>
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(AAR_UnitStatusWidget), "UnitData")),
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AARIcons), "GetVehicleEjectionCount")),
                        new CodeInstruction(OpCodes.Sub)
                    };

                    codes.InsertRange(vehicleIndex + 1, vehicleStack);
                }

                var mechIndex = codes.FindIndex(x => x.operand is MethodInfo info &&
                                                     info == AccessTools.Method(typeof(Pilot), "get_MechsKilled"));

                var mechStack = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(AAR_UnitStatusWidget), "UnitData")),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AARIcons), "GetMechEjectionCount")),
                    new CodeInstruction(OpCodes.Sub)
                };
                codes.InsertRange(mechIndex + 1, mechStack);
            }
            catch (Exception ex)
            {
                LogDebug(ex);
            }

            return codes.AsEnumerable();
        }

        public static void Postfix(UnitResult ___UnitData, RectTransform ___KillGridParent)
        {
            try
            {
                var statCollection = ___UnitData.pilot.StatCollection;
                if (modSettings.VehiclesCanPanic)
                {
                    for (var x = 0; x < vehicleEjections; x++)
                    {
                        LogDebug($"{___UnitData.pilot.Callsign} vehicleEjections {x}/{vehicleEjections}");
                        AARIcons.AddEjectedVehicle(___KillGridParent);
                    }

                    statCollection.Set("VehiclesEjected", 0);
                }

                // weird loop
                for (var x = 0; x < mechEjections; x++)
                {
                    LogDebug($"{___UnitData.pilot.Callsign} mechsEjections {x}/{mechEjections}");
                    AARIcons.AddEjectedMech(___KillGridParent);
                }

                statCollection.Set("MechsEjected", 0);
            }
            catch (Exception ex)
            {
                LogDebug(ex);
            }
        }
    }
}
