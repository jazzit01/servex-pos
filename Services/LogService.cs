using System;
using System.IO;
using System.Text;

namespace ServeX.Services
{
    public class LogService
    {
        private static readonly object SyncRoot = new object();
        public static string LogFilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ServeX", "Logs", "servex.log");

        public void LogInfo(string message)
        {
            WriteEntry("INFO", message);
        }

        public void LogError(string message, Exception ex)
        {
            var fullMessage = ex == null
                ? message
                : string.Format("{0} - {1} | StackTrace: {2}", message, ex.Message, ex);

            WriteEntry("ERROR", fullMessage);
        }

        private static void WriteEntry(string type, string message)
        {
            try
            {
                var safeMessage = (message ?? string.Empty)
                    .Replace(Environment.NewLine, " ")
                    .Replace("\n", " ")
                    .Replace("\r", " ");

                var line = string.Format(
                    "[{0:yyyy-MM-dd HH:mm}] {1}: {2}{3}",
                    DateTime.Now,
                    type,
                    safeMessage,
                    Environment.NewLine);

                lock (SyncRoot)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));
                    File.AppendAllText(LogFilePath, line, Encoding.UTF8);
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("ServeX log write failed: {0}", exception);
            }
        }
    }
}
