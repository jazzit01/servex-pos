using System;
using System.IO;
using System.Text;

namespace ServeX.Services
{
    public class LogService
    {
        private const long MaximumLogBytes = 2 * 1024 * 1024;
        private static readonly object SyncRoot = new object();
        private static string DefaultLogPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ServeX", "Logs", "servex.log");

        public string FilePath { get; }
        public LogService(string logFilePath = null) { FilePath = logFilePath ?? DefaultLogPath; }

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

        private void WriteEntry(string type, string message)
        {
            try
            {
                var safeMessage = (message ?? string.Empty)
                    .Replace(Environment.NewLine, " ")
                    .Replace("\n", " ")
                    .Replace("\r", " ");

                const int maximumEntryCharacters = 128 * 1024;
                if (safeMessage.Length > maximumEntryCharacters)
                    safeMessage = safeMessage.Substring(0, maximumEntryCharacters) + " [truncated]";

                var line = string.Format(
                    "[{0:yyyy-MM-dd HH:mm}] {1}: {2}{3}",
                    DateTime.Now,
                    type,
                    safeMessage,
                    Environment.NewLine);

                lock (SyncRoot)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length + Encoding.UTF8.GetByteCount(line) > MaximumLogBytes)
                    {
                        File.Move(FilePath, FilePath + ".1", overwrite: true);
                    }
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("ServeX log write failed: {0}", exception);
            }
        }
    }
}
