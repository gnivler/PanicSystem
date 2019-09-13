using System.IO;
using Harmony;
using static PanicSystem.PanicSystem;

namespace PanicSystem
{
    public class Logger
    {
        private static string LogFilePath => Path.Combine(modDirectory, "log.txt");

        public static void LogReport(object input)
        {
            if (!modSettings.Debug) return;
            File.WriteAllText(LogFilePath, input.ToString());
        }

        internal static void Log(object input)
        {
            FileLog.Log($"[PanicSystem] {input}");
        }
    }
}