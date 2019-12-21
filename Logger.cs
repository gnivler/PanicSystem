using System;
using System.IO;
using System.Reflection;
using Harmony;
using static PanicSystem.PanicSystem;

// ReSharper disable ClassNeverInstantiated.Global

namespace PanicSystem
{
    public class Logger
    {
        private static string LogFilePath => Path.Combine(modDirectory, "log.txt");

        private static readonly string Version = ((AssemblyFileVersionAttribute) Attribute.GetCustomAttribute(
            Assembly.GetExecutingAssembly(), typeof(AssemblyFileVersionAttribute), false)).Version;

        public static void LogReport(object line)
        {
            if (!modSettings.CombatLog)
            {
                return;
            }

            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine($"{line}");
            }
        }

        public static void LogClear()
        {
            using (var writer = new StreamWriter(LogFilePath, false))
            {
                writer.WriteLine($"{DateTime.Now.ToLongTimeString()} PanicSystem v{Version}");
            }
        }

        internal static void LogDebug(object input)
        {
            FileLog.Log($"[PanicSystem] {input}");
        }
    }
}
