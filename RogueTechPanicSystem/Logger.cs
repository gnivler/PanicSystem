using System;
using System.IO;

namespace RogueTechPanicSystem
{
    // code 'borrowed' from Morphyum
    public static class Logger
    {
        static string filePath = $"{Holder.ModDirectory}/Log.txt";
        public static void LogError(Exception ex)
        {
            using (StreamWriter writer = new StreamWriter(filePath, true))
            {
                writer.WriteLine("Message :" + ex.Message + "<br/>" + Environment.NewLine + "StackTrace :" + ex.StackTrace +
                   "" + Environment.NewLine + "Date :" + DateTime.Now);
                writer.WriteLine(Environment.NewLine + "-----------------------------------------------------------------------------" + Environment.NewLine);
            }
        }

        public static void Debug(object line)
        {
            // idea 'borrowed' from jo
            if (!RogueTechPanicSystem.Settings.DebugEnabled) return;
            using (StreamWriter writer = new StreamWriter(filePath, true))
            {
                writer.WriteLine($"{DateTime.Now.ToShortTimeString()} {line}");
            }
        }
    }
}