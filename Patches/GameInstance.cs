using BattleTech;
using BattleTech.Save;
using Harmony;
using static PanicSystem.Components.Controller;

// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    [HarmonyPatch(typeof(GameInstance), "LaunchContract", typeof(Contract), typeof(string))]
    public static class GameInstance_LaunchContract_Patch
    {
        // reset on new contracts
        private static void Postfix() => Reset();
    }

    [HarmonyPatch(typeof(GameInstance), "Load")]
    public static class GameInstance_Load_Patch
    {
        private static void Prefix(GameInstanceSave save) => Resync(save.SaveTime);
    }
}
