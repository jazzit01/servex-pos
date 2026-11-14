# ServeX POS

ServeX is an existing restaurant/hotel Windows point-of-sale application built with
.NET 8, WPF/XAML, MVVM, EF Core 8 and SQLite. It includes setup/login, dashboard,
menu management, indoor/outdoor tables, table orders, parcel orders, billing,
receipt printing, settings and database backups. Phase 1 preserves these workflows
and the existing database schema; it does not redesign the UI.

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

The repository historically tracks binaries and a SQLite database under `bin/`.
These are retained for compatibility and data preservation. A normal Debug run from
this checkout can detect that database as a legacy installation. For an isolated
first-launch check, publish to a **new directory** and use a separate Windows test
user/profile, or use the explicit Debug demo action:

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
`<executable directory>\Data\servex.db` installations stay at their original path.
The selected path is pinned in `%LOCALAPPDATA%\ServeX\database-location.txt`.
A missing pinned file, conflicting legacy/local files, inaccessible storage, or
corrupt database stops startup; no replacement database is selected. See the
[storage, migration and recovery procedure](docs/database-operations.md) before
moving an installation or restoring a backup.

Initialization applies the five existing **forward** EF migrations. Before changing
an existing schema it creates an integrity-checked backup beside the selected
DB in `Backups`. A failed backup blocks migration. The historical empty table-area
default is repaired to `Indoor` after a backup; IDs, statuses and relationships stay
intact. No new schema migration, database reset, deletion or recreation is used.

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
Windows CI workflow is configured in `.github/workflows/phase-1-validation.yml`.
See [actual results and the required Windows acceptance checklist](docs/validation.md).

## Known limitations and Phase 1 status

- Windows interactive authentication, bindings, dialogs, all POS workflows and
  physical receipt printing require the documented manual acceptance pass.
- Legacy passwords remain plaintext and license validation remains disabled.
  These existing security issues need a separate compatible credential/license
  migration before production deployment; Phase 1 does not hardcode new credentials.
- Only one process per Windows session is guarded. Shared-database/multi-session
  or multi-terminal concurrency is unsupported; payment concurrency is not solved
  by this phase. Use a single Windows account/session per installation.
- Restore is an offline operation; there is no new destructive restore UI.
- There is no cross-user automatic legacy discovery. When changing Windows user
  profiles or moving the executable, explicitly identify the existing DB first.
- Existing tax/discount rounding formulas and receipt format are unchanged. Catalog
  prices use two decimals; arbitrary higher-precision service inputs can expose a
  pre-existing difference between line-price rounding and aggregate order rounding.
- Logs and automatic backups need operator retention/storage monitoring.

Phase 1 code is implemented on `modernization/phase-1-foundation`. Completion and
Phase 2 rollout remain gated on supported Windows execution and manual acceptance.
See the [audit](docs/phase-1-audit.md) and [implementation report](docs/phase-1-report.md).
