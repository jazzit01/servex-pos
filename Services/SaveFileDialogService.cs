using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ServeX.Services
{
    [SupportedOSPlatform("windows")]
    public class SaveFileDialogService
    {
        private static string BackupLocationFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ServeX", "last-backup-folder.txt");

        public void RememberBackupFolder(string backupPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupLocationFile));
            File.WriteAllText(BackupLocationFile, Path.GetDirectoryName(backupPath));
        }

        private static string GetBackupFolder()
        {
            try
            {
                if (File.Exists(BackupLocationFile))
                {
                    var path = File.ReadAllText(BackupLocationFile).Trim();
                    if (Path.IsPathFullyQualified(path) && Directory.Exists(path)) return path;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        public string PickDatabaseRestoreSource()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a ServeX backup",
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                CheckFileExists = true,
                InitialDirectory = GetBackupFolder()
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string PickDatabaseBackupPath(string suggestedFileName)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Backup Database",
                FileName = suggestedFileName,
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                DefaultExt = ".db",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = GetBackupFolder()
            };

            return dialog.ShowDialog() == true
                ? dialog.FileName
                : null;
        }
    }
}
