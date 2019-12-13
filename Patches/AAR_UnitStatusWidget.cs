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

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(AAR_UnitStatusWidget), "FillInData")]
    public static class AAR_UnitStatusWidget_FillInData_Patch
    {
        private static int? mechEjections;
        private static int? vehicleEjections;

        public static void Prefix(UnitResult ___UnitData)
        {
            try
            {
                // get the total and decrement it globally
                mechEjections = ___UnitData.pilot.StatCollection.GetStatistic("MechsEjected")?.Value<int>();
                Log($"{___UnitData.pilot.Callsign} MechsEjected {mechEjections}");
                vehicleEjections = ___UnitData.pilot.StatCollection.GetStatistic("VehiclesEjected")?.Value<int>();
                Log($"{___UnitData.pilot.Callsign} vehicleEjections {vehicleEjections}");
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        // subtract ejection kills to limit the number of regular kill stamps drawn
        // then draw red ones in Postfix
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            try
            {
                // subtract our value right as the getter comes back
                // makes the game draw fewer normal stamps
                var index = codes.FindIndex(x => x.operand is MethodInfo info &&
                                                 info == AccessTools.Method(typeof(Pilot), "get_MechsKilled"));

                var newStack = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(AAR_UnitStatusWidget), "UnitData")),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AARIcons), nameof(AARIcons.GetEjectionCount))),
                    new CodeInstruction(OpCodes.Sub)
                };
                codes.InsertRange(index + 1, newStack);
            }
            catch (Exception ex)
            {
                Log(ex);
            }

            return codes.AsEnumerable();
        }

        public static void Postfix(UnitResult ___UnitData, RectTransform ___KillGridParent)
        {
            try
            {
                // weird loop
                for (var x = 0; x < mechEjections--; x++)
                {
                    Log("Adding stamp");
                    AARIcons.AddEjectedMech(___KillGridParent);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
    }
}
