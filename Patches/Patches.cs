using BattleTech;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using Harmony;
using static PanicSystem.Controller;
using static PanicSystem.PanicSystem;
using static PanicSystem.Logger;

// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem.Patches
{
    public static class Patches
    {
        public static void ManualPatching()
        {
            //var targetMethod = AccessTools.Method(typeof(Mech), "CheckForHeatDamage");
            //var transpiler = SymbolExtensions.GetMethodInfo(() => Mech_CheckForHeatDamage_Patch.Transpiler(null));
            //var postfix = SymbolExtensions.GetMethodInfo(() => Mech_CheckForHeatDamage_Patch.Postfix());
            //harmony.Patch(targetMethod, null, new HarmonyMethod(postfix), new HarmonyMethod(transpiler));
        }

        // patch works to determine how much heat damage was done by overheating... which isn't really required
        // multiply a local variable by 7 and aggregate it on global `static float heatDamage`
        //public class Mech_CheckForHeatDamage_Patch
        //{
        //    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        //    {
        //        var codes = instructions.ToList();
        //        var heatDamageField = AccessTools.Field(typeof(Patches), nameof(heatDamage));
        //        var log = SymbolExtensions.GetMethodInfo(() => LogDebug(null));
        //
        //        var newStack = new List<CodeInstruction>
        //        {
        //            new CodeInstruction(OpCodes.Ldloc_0), // push float (2)
        //            new CodeInstruction(OpCodes.Ldc_R4, 7f), // push float (7)
        //            new CodeInstruction(OpCodes.Mul), // multiply   (14)
        //            new CodeInstruction(OpCodes.Ldsfld, heatDamageField), // push float (0)
        //            new CodeInstruction(OpCodes.Add), // add        (14)
        //            new CodeInstruction(OpCodes.Stsfld, heatDamageField), // store result
        //        };
        //
        //        codes.InsertRange(codes.Count - 1, newStack);
        //        return codes.AsEnumerable();
        //    }
        //
        //    public static void Postfix() => LogDebug($"heatDamage: {heatDamage}");
        //}
        //
    }
}
