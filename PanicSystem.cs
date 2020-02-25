using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Harmony;
using Newtonsoft.Json;
using PanicSystem.Components;
using PanicSystem.Components.IRBTModUtilsCustomDialog;
using static PanicSystem.Logger;

// ReSharper disable InconsistentNaming

// HUGE thanks to RealityMachina and mpstark for their work, outstanding.
namespace PanicSystem
{
    public static class PanicSystem
    {
        internal static Settings modSettings = new Settings();
        internal static string activeJsonPath; //store current tracker here
        internal static string storageJsonPath; //store our meta trackers here
        internal static string modDirectory;
        internal static List<string> ejectPhraseList = new List<string>();
        internal static HarmonyInstance harmony;
        internal static readonly Dictionary<PanicStatus, string> panicStates = new Dictionary<PanicStatus, string>();

        public static void Init(string modDir, string settings)
        {
            harmony = HarmonyInstance.Create("com.BattleTech.PanicSystem");
            modDirectory = modDir;
            activeJsonPath = Path.Combine(modDir, "PanicSystem.json");
            storageJsonPath = Path.Combine(modDir, "PanicSystemStorage.json");
            try
            {
                modSettings = JsonConvert.DeserializeObject<Settings>(settings);
            }
            catch (Exception ex)
            {
                LogDebug(ex);
                modSettings = new Settings();
            }

            // thank you Frosty IRBTModUtils CustomDialog
            // https://github.com/IceRaptor/IRBTModUtils
            // Try to determine the battletech directory
            string fileName = Process.GetCurrentProcess().MainModule.FileName;
            string btDir = Path.GetDirectoryName(fileName);
            //LogDebug($"BT File is: {fileName} with btDir: {btDir}");
            if (Coordinator.CallSigns == null) {
                string filePath = Path.Combine(btDir, modSettings.Dialogue.CallsignsPath);
                //LogDebug($"Reading files from {filePath}");
                try {
                    Coordinator.CallSigns = File.ReadAllLines(filePath).ToList();
                } catch (Exception e) {
                    LogDebug("Failed to read callsigns from BT directory!");
                    LogDebug(e);
                    Coordinator.CallSigns = new List<string> { "Alpha", "Beta", "Gamma" };
                }
                //LogDebug($"Callsign count is: {Coordinator.CallSigns.Count}");
            }
            
            panicStates.Add(PanicStatus.Confident, modSettings.PanicStates[0]);
            panicStates.Add(PanicStatus.Unsettled, modSettings.PanicStates[1]);
            panicStates.Add(PanicStatus.Stressed, modSettings.PanicStates[2]);
            panicStates.Add(PanicStatus.Panicked, modSettings.PanicStates[3]);
            harmony.PatchAll();
            Helpers.SetupEjectPhrases(modDir);
        }
    }
}
