using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ServeX.Services
{
    [SupportedOSPlatform("windows")]
    public class SaveFileDialogService
    {
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
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            return dialog.ShowDialog() == true
                ? dialog.FileName
                : null;
        }
    }
}
