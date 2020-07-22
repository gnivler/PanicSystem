using System;
using System.IO;
using Harmony;
using static PanicSystem.PanicSystem;

// ReSharper disable ClassNeverInstantiated.Global

namespace PanicSystem
{
    public class Logger
    {
        private static string LogFilePath => Path.Combine(modDirectory, "log.txt");

        public static void LogReport(object line)
        {
            if (modSettings.CombatLog)
            {
                using (var writer = new StreamWriter(LogFilePath, true))
                {
                    writer.WriteLine($"{line}");
                }
            }
        }

        internal static void LogDebug(object input)
        {
            /*if (modSettings.CombatLog)
            {
                try
                {
                    using (var writer = new StreamWriter(LogFilePath, true))
                    {
                        writer.WriteLine($" {input ?? "null"}");
                    }
                }
                catch (Exception ) { }
            }*/

            if (modSettings.Debug)
            {
                FileLog.Log($"[PanicSystem] {input ?? "null"}");
            }
        }
    }
}
