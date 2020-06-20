using Harmony;
using System.Reflection;
using PanicSystem;
using static PanicSystem.Logger;

namespace PanicSystem.Patches
{
    public class VehicleRepresentation
    {
        private static MethodInfo originalPDF = null;
        private static MethodInfo prefixPDF = null;
        private static bool supressDeathFloatie = false;

        public static void hookPDF()
        {
            if (originalPDF == null)
            {
                LogReport($"hookPDF");
                originalPDF = AccessTools.Method(typeof(BattleTech.VehicleRepresentation), "PlayDeathFloatie");
                prefixPDF = AccessTools.Method(typeof(BattleTech.VehicleRepresentation), nameof(PrefixDeathFloatie));
                PanicSystem.harmony.Patch(originalPDF, new HarmonyMethod(prefixPDF));
                //PanicSystem.harmony.Unpatch(originalPDF, HarmonyPatchType.Prefix);
            }
        }

        public static void supressDeathFloatieOnce()
        {
            supressDeathFloatie = true;
        }

        internal static bool PrefixDeathFloatie()
        {
            if (supressDeathFloatie)
            {
                supressDeathFloatie = false;
                return false;
            }
            return true;
        }
    }
}
