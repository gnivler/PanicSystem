using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech.Save;
using Harmony;
using BattleTech;
using BattleTech.Save.SaveGameStructure;

namespace BasicPanic
{
    [HarmonyPatch(typeof(GameInstanceSave))]
    [HarmonyPatch(new Type[] { typeof(GameInstance), typeof(SaveReason) })]
    public static class GameInstanceSave_Constructor_Patch
    {
        static void Postfix(GameInstanceSave __instance)
        {
            Holder.SerializeStorageJson(__instance.InstanceGUID, __instance.SaveTime);
        }
    }

    [HarmonyPatch(typeof(GameInstanceSave), "Load")]
    public static class GameInstanceSave_Load_Patch
    {
        static void Prefix(GameInstanceSave __instance)
        {
            Holder.Resync(__instance.SaveTime);
        }
    }
}
