using System;
using System.IO;
using System.Text;

namespace PanicSystem
{
    public static class Logger
    {
        private static StringBuilder sb = new StringBuilder();
        private static string LogFilePath => $"{PanicSystem.modDirectory}/log.txt";
        private const string Version = "v3.0";

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
            if (!PanicSystem.modSettings.Debug) return;
            sb.Append(line + "\n");
        }

        public static void FlushLogBuffer()
        {
            if (sb.Length == 0)
            {
                return;
            }

            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine(sb.ToString());
            }

            sb.Length = 0;
        }

        public static void Clear()
        {
            using (var writer = new StreamWriter(LogFilePath, false))
            {
                writer.WriteLine($"{DateTime.Now.ToLongTimeString()} PanicSystem {Version}");
            }
        }
    }
}