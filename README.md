# ServeX POS

ServeX is an existing restaurant/hotel Windows point-of-sale application built with
.NET 8, WPF/XAML, MVVM, EF Core 8 and SQLite. It includes setup/login, dashboard,
menu management, indoor/outdoor tables, table orders, parcel orders, billing,
receipt printing, settings and database backups. Phase 1 preserves these workflows
and the existing database schema. Phase 2 applies a consistent Segoe UI, teal and
navy interface on that foundation without changing financial or persistence rules.

## Development

Use Windows 10/11, the .NET 8 SDK and optionally Visual Studio 2022 with the
**.NET desktop development** workload. Clone `jazzit01/servex-pos` and work on a
feature branch. Do not start development builds against a customer database.

```powershell
dotnet restore ServeX.sln -p:TestWindows=true
dotnet build ServeX.sln -c Release --no-restore -p:TestWindows=true
dotnet test Tests/ServeX.Tests.csproj -c Release --no-build --no-restore -p:TestWindows=true
dotnet run --project ServeX.csproj
```

Build output and local databases are ignored. For an isolated first-launch check,
use the explicit Debug demo action:

```powershell
dotnet run --project ServeX.csproj -- --demo
```

`--demo` selects `%LOCALAPPDATA%\ServeX\Demo\servex-demo.db`, applies migrations,
and seeds only an entirely empty demo database with optional catalog/table/room
examples. It creates no orders, bills or occupied tables. Normal startup never
resolves the demo seeder. Release builds reject `--demo`. Existing customer/demo
records are not cleared by either mode.

## Architecture and DI

- `App.xaml.cs` owns startup, authentication-to-main-window transition, a Windows
  session instance guard, exception boundaries, and container disposal.
- `Application/CompositionRoot.cs` registers presentation dependencies;
  `Application/CoreServices.cs` registers platform-neutral services and persistence.
- `Application/PresentationFactory.cs` supplies runtime arguments (user, settings,
  callbacks); registered delegates supply order/billing ViewModels and windows.
  Container lookups are confined to composition/startup boundaries.
- Stateless application services, logging, the context factory, and UI services are
  application singletons. Workspace ViewModels and windows are transient and the
  main shell retains one of each workspace ViewModel. Order/billing ViewModels are
  created per dialog. Each persistence operation creates and disposes its own EF
  context; no singleton owns a live context.
- `Presentation/Navigation` owns typed destinations and dashboard-back behavior.
  Existing XAML templates/resources are retained, with enum navigation bindings.
- `Infrastructure/Persistence` selects storage and creates contexts. Existing
  `Data`, `Models`, `Services`, `ViewModels`, `Views` and `Styles` folders retain
  compatible namespaces. Services own database work; `WorkspaceSummaryService`
  removes direct EF access from the main ViewModel.
- `AsyncRelayCommand` provides an awaitable command API and prevents reentry.
  Expected operation failures are logged and presented through existing status
  bindings. Unexpected UI failures are logged and close the process safely.

The target Presentation/Application/Domain/Infrastructure boundaries are logical
and incremental. A wholesale file move or additional runtime projects would add
regression risk without improving this phase. See [architectural decisions](docs/architecture-decisions.md).

## Database safety

Fresh installations use `%LOCALAPPDATA%\ServeX\Data\servex.db`. Existing
`<executable directory>\Data\servex.db` installations are copied into Local App Data
on startup using SQLite's backup operation. ServeX verifies and pins the new
copy while keeping the original as a recovery copy.
The selected path is pinned in `%LOCALAPPDATA%\ServeX\database-location.txt`.
A missing pinned file, conflicting legacy/local files, inaccessible storage, or
corrupt database stops startup; no replacement database is selected. Runtime logs
are written beside the active database and rotate at 2 MiB with one retained
copy. Failures before database selection use the Local App Data fallback log.
See the
[storage, migration and recovery procedure](docs/database-operations.md) before
moving an installation or restoring a backup.

Initialization applies the **forward** EF migrations. Before changing
an existing schema it creates an integrity-checked backup beside the selected
DB in `Backups`. A failed backup blocks migration. The historical empty table-area
default is repaired to `Indoor` after a backup; IDs, statuses and relationships stay
intact. Startup preserves existing users and business records.

Normal startup keeps the seven most recent daily backups per database under
`%LOCALAPPDATA%\ServeX\AutomaticBackups`. In Settings, **Restore from backup** stages a verified copy and restarts ServeX.
Startup saves the current database in its `Backups` folder before applying the
restore. Check the restored orders and settings before resuming service.

## Build a Windows package

Publish a self-contained x64 build to a fresh folder:

```powershell
dotnet publish ServeX.csproj -c Release -r win-x64 -p:PublishProfile=WindowsX64 -o artifacts/ServeX-win-x64
```

Copy the entire output folder to a Windows x64 PC and run `ServeX.exe`. The
output includes the .NET desktop runtime. The per-user Inno Setup configuration,
build script, and upgrade procedure are in the [packaging guide](packaging/README.md).

## Tests and verification

```powershell
# Real SQLite/migrations/services + full WPF composition/ViewModel tests on Windows
dotnet test Tests/ServeX.Tests.csproj -p:TestWindows=true

# Platform-neutral production source + real SQLite tests on Windows/Linux
dotnet test Tests/ServeX.Tests.csproj

# Linux cross-build only; this cannot run WPF
dotnet build ServeX.csproj -p:EnableWindowsTargeting=true
```

All test databases and logs use unique temporary directories. The portable target
links the actual platform-neutral production source and migration files. The
Windows target references the application assembly and also tests full composition,
auth ViewModel factories, cached workspaces and selected/back navigation. The
Windows CI workflow is configured in `.github/workflows/windows-validation.yml`.
See [actual results and the required Windows acceptance checklist](docs/validation.md).

## Day-to-day workflows

First-run setup adds five indoor tables, five outdoor tables, and three available
rooms. An administrator can use **Tables → Add starter layout** to fill empty
areas on an existing installation. Menu categories include starter suggestions
and any custom categories already saved.


- The dining floor supports reservations, order transfers and merges, and a
  **Mark clean** step after payment. Room service and parcel orders use the same
  billing and kitchen actions. Kitchen tickets include quantities added since
  the last ticket.
- Billing records external payments. Staff can enter Card or UPI references,
  split a bill between two payments, and preview or print a receipt. To save a
  PDF, select Microsoft Print to PDF in the Windows print picker if installed.
- **History and sales** includes filtered order history, paid receipt reprints,
  unpaid-order visibility on the dashboard, and printable daily and sales reports.
- Administrators can add Cashier and Admin accounts in Settings. Cashiers can
  record standard payments but cannot edit the menu, apply discounts, delete
  tables, or manage backups. Discounts above 20% require administrator confirmation.
- Settings controls currency and locale, tax details, and 58/80 mm receipt width.
  It also provides CSV exports and a separately confirmed support bundle. Share
  support bundles only with trusted recipients because they contain a database
  copy and logs.

## Release verification

The checklist records the implementation status and the Windows control-by-control
acceptance cases. Windows interactive authentication, navigation, DPI, printing,
and installer upgrade tests require a Windows machine. Portable tests cover SQLite
migrations, orders, billing, authentication, backups, and reports.

Startup upgrades legacy passwords to salted PBKDF2 hashes. Restrict access to
pre-upgrade database backups because they may contain the original credentials.
Use a single Windows account and a local database per installation. Stale revisions
are rejected when orders are edited or paid, but a network-shared SQLite database
and multi-session deployment are outside the supported configuration.

See the [completion checklist](ServeX%20POS%20%E2%80%93%20Completion%20Checklist.md)
and the [history cleanup review](docs/history-cleanup-review.md).
