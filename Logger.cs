using System;
using System.IO;
using System.Text;
using Stream = RenderHeads.Media.AVProVideo.Stream;

namespace PanicSystem
{
    public static class Logger
    {
        private static StringBuilder SB = new StringBuilder();
        private static string LogFilePath => $"{PanicSystem.ModDirectory}/log.txt";
        private static StreamWriter sw = new StreamWriter(LogFilePath, true);

        public static void Error(Exception ex)
        {
            using (sw)
            {
                sw.WriteLine($"Message: {ex.Message}");
                sw.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        public static void Debug(string line)
        {
            if (!PanicSystem.ModSettings.Debug) return;
            SB.Append(line + "\n");
        }

        public static void FlushLog()
        {
            if (SB.Length == 0) return;
            using (sw)
            {
                sw.WriteLine(SB.ToString());
            }

            SB.Length = 0;
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