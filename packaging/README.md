# Windows installation and updates

On Windows with the .NET 8 SDK and Inno Setup 6.3 or later, run
`powershell -File .\packaging\Build-Installer.ps1` from the repository root.
The per-user x64 installer is written to `artifacts\installer` and adds a
ServeX POS shortcut to the Start menu. Verify a clean install and an upgrade
with test data before distributing an installer.

Release maintainers publish the tested installer for each version to GitHub
Releases. To update, close ServeX, create a backup from Settings, and run the
new installer as the same Windows user. The installer updates the app under
`%LOCALAPPDATA%\Programs\ServeX`. Data remains in `%LOCALAPPDATA%\ServeX`.
ServeX verifies a database backup before applying any pending migrations.
After launch, verify the version in Settings and review recent orders.

Use the [database recovery procedure](../docs/database-operations.md) if an
update fails after a database migration. Restoring an earlier executable alone
does not revert database changes.
