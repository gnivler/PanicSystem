using System;
using System.IO;
using System.Text;

namespace PanicSystem
{
    public static class Logger
    {
        private static StringBuilder SB = new StringBuilder();
        private static string LogFilePath => $"{PanicSystem.ModDirectory}/log.txt";
        public static void Error(Exception ex)
        {
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine($"Message: {ex.Message}");
                writer.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        public static void Debug(string line)
        {
            if (!PanicSystem.ModSettings.Debug) return;
            SB.Append(line + "\n");
        }

        public static void FlushLog()
        {
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine(SB.ToString());
            }
            SB = new StringBuilder();
        }


        public static void Clear()
        {
            using (var writer = new StreamWriter(LogFilePath, false))
            {
                writer.WriteLine($"{DateTime.Now.ToLongTimeString()} Init");
            }
        }
    }
}