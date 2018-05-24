using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech.Save;
using Harmony;

namespace BasicPanic
{
    [HarmonyPatch(typeof(GameInstanceSave), "GameInstanceSave")]
    public static class GameInstanceSave_Constructor_Patch
    {
        static void Postfix(GameInstanceSave __instance)
        {
            Holder.SerializeStorageJson(__instance.InstanceGUID);
        }
    }

    [HarmonyPatch(typeof(GameInstanceSave), "PostDeserialization")]
    public static class GameInstanceSave_PostDeserialization_Patch
    {
        static void Prefix(GameInstanceSave __instance)
        {
            Holder.Resync(__instance.SaveTime);
        }
    }
}
