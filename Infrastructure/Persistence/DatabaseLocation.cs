using System;
using System.IO;
using ServeX.Services;

namespace ServeX.Infrastructure.Persistence
{
    public sealed class DatabaseSelectionException : InvalidOperationException
    {
        public DatabaseSelectionException(string message) : base(message) { }
    }

    // Resolve once at composition time so every service uses the same database.
    public sealed class DatabaseLocation
    {
        public DatabaseLocation(string filePath) { FilePath = Path.GetFullPath(filePath); }
        public string FilePath { get; }

        public static DatabaseLocation Resolve(string executableDirectory, string localDataDirectory, string explicitPath = null)
        {
            var legacy = Path.GetFullPath(Path.Combine(executableDirectory, "Data", "servex.db"));
            var directory = Path.Combine(localDataDirectory, "ServeX");
            var pin = Path.Combine(directory, "database-location.txt");
            var fresh = Path.GetFullPath(Path.Combine(directory, "Data", "servex.db"));

            // An explicit path is an operator recovery/compatibility selection, never a request
            // to create an empty replacement for a missing customer database.
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                if (!Path.IsPathFullyQualified(explicitPath) || !File.Exists(explicitPath))
                    throw new DatabaseSelectionException("SERVEX_DATABASE_PATH must identify an existing database by absolute path.");
                return new DatabaseLocation(explicitPath);
            }

            if (File.Exists(pin))
            {
                var lines = File.ReadAllLines(pin);
                var selected = lines.Length > 0 ? lines[0].Trim() : string.Empty;
                var migratedFrom = lines.Length > 1 ? lines[1].Trim() : string.Empty;
                if (!Path.IsPathFullyQualified(selected) || !File.Exists(selected))
                    throw new DatabaseSelectionException("ServeX could not find the previously selected database. Reconnect the drive or restore the database to its previous location. If it was moved, ask your administrator to set SERVEX_DATABASE_PATH to the existing database file.");
                if (File.Exists(legacy) && SamePath(selected, legacy) && !SamePath(selected, fresh))
                    return RelocateLegacy(selected, fresh, pin, replacePin: true);
                if (File.Exists(legacy) && !SamePath(selected, legacy) &&
                    (!Path.IsPathFullyQualified(migratedFrom) || !SamePath(migratedFrom, legacy)))
                    throw new DatabaseSelectionException("Multiple ServeX databases were found. Select the correct existing database with SERVEX_DATABASE_PATH before continuing.");
                return new DatabaseLocation(selected);
            }

            if (File.Exists(legacy) && File.Exists(fresh) && !SamePath(legacy, fresh))
                throw new DatabaseSelectionException("Both legacy and Local App Data databases exist. Select the correct database with SERVEX_DATABASE_PATH.");

            if (File.Exists(legacy) && !SamePath(legacy, fresh))
                return RelocateLegacy(legacy, fresh, pin, replacePin: false);

            var path = fresh;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Directory.CreateDirectory(directory);
            // Reserve the fresh file before pinning it. A failed first migration can safely retry.
            if (!File.Exists(path))
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            using (var writer = new StreamWriter(new FileStream(pin, FileMode.CreateNew, FileAccess.Write, FileShare.None)))
                writer.Write(path);
            return new DatabaseLocation(path);
        }

        private static DatabaseLocation RelocateLegacy(string legacy, string fresh, string pin, bool replacePin)
        {
            if (File.Exists(fresh))
                throw new DatabaseSelectionException("Both legacy and Local App Data databases exist. Select the correct database before relocating.");

            // Use a distinct destination so an interruption before the atomic pin update
            // leaves the original selected and a subsequent startup can safely retry.
            var destination = Path.Combine(Path.GetDirectoryName(fresh), $"servex-migrated-{Guid.NewGuid():N}.db");
            DatabaseBackupService.CopyVerifiedDatabase(legacy, destination);

            var temporaryPin = pin + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllLines(temporaryPin, new[] { destination, legacy });
                File.Move(temporaryPin, pin, replacePin);
            }
            finally
            {
                if (File.Exists(temporaryPin)) File.Delete(temporaryPin);
            }

            return new DatabaseLocation(destination);
        }

        private static bool SamePath(string first, string second) => string.Equals(
            Path.GetFullPath(first), Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
