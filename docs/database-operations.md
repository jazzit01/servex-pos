# Database storage, migration, backup and recovery

## Selection policy

1. `SERVEX_DATABASE_PATH`, when explicitly configured, must be an absolute path to
   an **existing** file. Invalid values fail closed. This is an operator override;
   keep it set for each launch until the permanent path pin is updated offline.
2. Otherwise the per-user `database-location.txt` pin is authoritative. If the
   pinned database is missing, startup stops. The pin records the legacy source
   after relocation so the original can remain available for recovery.
3. An executable-relative `Data/servex.db` is copied to Local App Data using
   SQLite’s verified backup operation before the pin is updated. If an unrelated
   local database also exists, startup stops for operator selection.
4. Without either existing database, fresh storage is created under
   `%LOCALAPPDATA%\ServeX\Data\servex.db`, then pinned. A zero-length reserved file
   permits retry if first-launch migration fails. It is not a replacement for any
   detected existing database.

The DI composition root resolves once and every runtime service uses that same
path. The parameterless/design-time context uses the same policy. Contexts use
SQLite connection-string builders, including for paths containing semicolons.
The Settings screen displays the active database path. Runtime logs are stored
beside that database.

Close older ServeX installations before starting the upgraded version. The
original legacy database remains available for recovery while the upgraded app
writes to the pinned copy. If verification or pinning fails, the original
database remains in place and startup reports the failure.
A missing pin itself cannot identify a database under another Windows profile or
another installation directory. Operators must inventory those locations before
using a new profile/install folder. Do not interpret a fresh setup screen as proof
that an old installation had no data.

## Moving an existing installation safely

1. In the existing working installation, record Settings' active database path.
   Create a new backup from Settings to a **new, unused** file name on a separate
   protected drive. Confirm success and preserve that original backup.
2. Close ServeX in every Windows session. Preserve the entire original database
   directory, including any `-wal`/`-shm` files. Do not copy only the main file while
   another process is writing. Do not run old/new installations simultaneously.
3. Choose a new writable destination, preferably
   `%LOCALAPPDATA%\ServeX\Data\servex.db`. It must not exist. Restore from the
   verified standalone backup to this new file (see below). Keep all old files.
4. Launch a copied/new executable with the selected path explicitly set:
   `$env:SERVEX_DATABASE_PATH = 'C:\absolute\path\servex.db'`.
   Verify the restaurant, user login, menu, floor, historical orders and bill totals.
5. With ServeX closed, save the chosen absolute existing path as the complete content
   of `%LOCALAPPDATA%\ServeX\database-location.txt`. Keep the old pin as a separately
   named backup. If the executable still has a different legacy database beside it,
   retain the explicit environment override, or move the executable into a fresh
   directory without a second legacy DB. Do not delete the old database to bypass
   ambiguity. Launch again and confirm the displayed path and historical counts.
6. Retain the original installation and backups until acceptance is complete.

Changing the user/profile changes Local App Data and the pin. Explicitly select the
existing database; a new user cannot infer where another user's data resides.

## Forward migrations

`DatabaseInitializationService` checks pending migrations. Existing nonempty files
receive a SQLite online backup under `<active database directory>\Backups` before
any forward migration. Filenames include UTC time and a GUID. Backup failure
stops the change. Migration exceptions are logged; startup offers Yes to retry or
No to exit. Correct permissions/disk/locking issues before retrying.

The historic `AddTableAreaSupport` migration fills old rows with `Area = ''`, which
EF cannot read as an enum. Initialization repairs **only** that exact legacy value
to `Indoor`, after backing up. It does not modify table status or IDs and is safe
to repeat. Other unknown enum values are not silently rewritten by initialization.
No migration files/schema are changed. Never use `EnsureDeleted`, `EnsureCreated`,
a down migration or `dotnet ef database update 0` on a customer installation.

## Backups

Use **Settings → Backup Database**. The implementation uses SQLite's online backup
API to capture committed pages including WAL, writes a uniquely named staging file,
closes connections, runs `PRAGMA integrity_check` and checks core ServeX tables,
then moves the staged file to the chosen name without overwrite. Failures propagate
to the existing status message and logs. An existing destination is refused even
if a file-picker overwrite prompt was accepted; choose a new filename.

Backups include credentials and customer data. Store them under the same access
controls as the original DB, retain off-device copies and periodically exercise
restore on isolated copies. Monitor free space; automatic pre-migration backups
are not a complete scheduled backup/retention strategy.

## Restore from Settings

Choose **Settings → Restore from backup**, select a ServeX backup, and confirm
the restart. ServeX validates the copy and prepares its schema before closing.
At startup it puts a safety snapshot named `before-restore-*.db` in the active
database’s `Backups` folder, then applies the staged copy. Review restored orders,
settings, and balances before entering new sales.

If startup cannot finish, keep the safety snapshot and the staged
`*.restore-pending.db` file for recovery. The app reports the error and offers a
retry before opening any workspace.

Normal startup also keeps the seven most recent daily snapshots per database
under `%LOCALAPPDATA%\ServeX\AutomaticBackups`. Retention applies only to these
automatic files.

## Offline restore (never overwrites live data)

Close every ServeX process first. Preserve the current database directory intact.
`DatabaseBackupService.RestoreToNewFileAsync(backupPath, newDatabasePath)` provides
an operator/tooling API for integrity-checked restore to a **new** path. It is
covered by temporary-database tests. Use this procedure when normal startup is unavailable or when you need to validate a copy without replacing the active database.

For an operator without a custom tool, Settings-generated backups are standalone
SQLite files: copy the backup to a new filename while ServeX is closed, then use
the SQLite CLI (if installed) to inspect the copy:

```powershell
[System.IO.File]::Copy('D:\Backups\servex-backup.db', 'C:\Recovery\servex-restored.db', $false)
sqlite3 'C:\Recovery\servex-restored.db' 'PRAGMA integrity_check;'
# Must report exactly: ok
$env:SERVEX_DATABASE_PATH = 'C:\Recovery\servex-restored.db'
& 'C:\ServeX\ServeX.exe'
```

The copy operation refuses an existing destination.

Compare expected records/totals and the active path before any new sales. A restored
older schema will receive a new backup before forward migration. Keep the damaged
original and initial backup for investigation; never replace/delete them as part
of recovery. If integrity fails or the file is not a ServeX database, stop and seek
support. Never erase migration history to force startup.
