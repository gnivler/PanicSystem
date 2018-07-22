using System;
using System.IO;

namespace PanicSystem
{
    public static class Logger
    {
        private static string LogFilePath => $"{PanicSystem.ModDirectory}/log.txt";

        public static void Error(Exception ex)
        {
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine($"Message: {ex.Message}");
                writer.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        public static void Debug(String line)
        {
            if (!PanicSystem.ModSettings.Debug) return;
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine(line);
            }
        }
    }
}