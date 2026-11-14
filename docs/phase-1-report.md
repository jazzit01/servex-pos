# Phase 1 implementation report

Branch: `modernization/phase-1-foundation`; baseline `cc64eba`. Main was not merged
or modified. Changes are local commits; no remote push or deployment was performed.

**Status:** implementation and available automated checks completed; Phase 1
acceptance remains pending supported-Windows execution and interactive verification.

## 1. Architecture audit

The [baseline audit](phase-1-audit.md) contains findings/severity. Critical startup
sample-order/occupancy insertion is removed. High-risk settings fallback, writable
storage selection and backup overwrite behavior are corrected. DI/service coupling,
integer navigation, async command reentry and error diagnostics are improved.
An additional historical table-area migration default issue is repaired safely.
Existing plaintext credentials, disabled licensing and unsupported concurrent
multi-session database access remain explicit production risks.

## 2. Changes implemented and files

| Files | Reason |
| --- | --- |
| `App.xaml.cs` | DI startup, migration retry/exit, explicit isolated demo action, preserved auth/main transition, instance guard, diagnostic hooks and disposal |
| `Application/CoreServices.cs`, `CompositionRoot.cs`, `PresentationFactory.cs` | Central core/UI registrations and factories for runtime user/order/callback arguments |
| `Infrastructure/Persistence/DatabaseLocation.cs`, `RuntimeDbContextFactory.cs` | Deterministic compatible storage selection, pinned path and short-lived EF contexts |
| `Data/ServeXDbContext.cs`, `ServeXDbContextFactory.cs` | Shared safe default/design-time location and escaped connection strings; mappings unchanged |
| `Services/DatabaseInitializationService.cs`, `DemoDataService.cs` | Forward migrations without production seeding, pre-change backups, narrow historic-area repair and explicit empty-demo seeding |
| `Services/DatabaseBackupService.cs` | WAL-safe staged/verified backup, no overwrite and restore-to-new-file API |
| `Services/ApplicationAccessService.cs`, `ApplicationSettingsService.cs` | Inject factory/logger; propagate settings failures and reject missing setup saves |
| `Services/MenuService.cs`, `TableService.cs` | Inject factory/logger; missing-item updates fail honestly; prevent historical table deletion |
| `Services/OrderService.cs`, `BillingService.cs` | Inject factory/logger, reject stale parcel writes, paid/cancelled billing and invalid payment enums; financial formulas unchanged |
| `Services/DashboardService.cs`, `WorkspaceSummaryService.cs` | Inject context factory; move shell EF queries to application service |
| `Services/OrderWindowService.cs`, `BillingWindowService.cs` | Inject runtime ViewModel/window factories; preserve dialog flow and ownership |
| `Services/LogService.cs`, `ReceiptPrintService.cs` | Writable diagnostic path/test log injection; inject logger and dispose PrintDocument without changing print content |
| `ViewModels/MainViewModel.cs` | Inject workspace graph, typed navigation, summary service, coalesced dependent refresh and event cleanup |
| `ViewModels/AuthWindowViewModel.cs` | Composition factories for setup/login; explicit blocked-startup failure |
| `ViewModels/AsyncRelayCommand.cs` | Awaitable guarded commands and reliable finally cleanup |
| `ViewModels/DashboardViewModel.cs`, `MenuViewModel.cs`, `TablesViewModel.cs`, `ParcelViewModel.cs`, `SettingsViewModel.cs`, `OrderViewModel.cs`, `BillingViewModel.cs`, `LoginViewModel.cs`, `SetupViewModel.cs` | Task-based commands/logging, injected dependencies, preserved binding names/statuses; fix skipped post-billing order reload |
| `Presentation/Navigation/WorkspaceDestination.cs`, `WorkspaceNavigation.cs`, `Views/MainWindow.xaml` | Shared named destinations, selected styling and dashboard Back semantics |
| `Tests/ServeX.Tests.csproj`, `GlobalUsings.cs`, `TestDatabase.cs`, `InitializationTests.cs`, `OrderBillingTests.cs`, `StartupAndCommandTests.cs`, `StorageBackupTests.cs`, `WindowsCompositionTests.cs` | Isolated SQLite regression cases plus Windows composition test target |
| `ServeX.csproj`, `ServeX.sln`, `.gitignore`, `.github/workflows/phase-1-validation.yml` | Explicit DI package, test project, generated-source exclusions, future artifact ignores and Windows CI configuration |
| `README.md`, `docs/phase-1-audit.md`, `architecture-decisions.md`, `database-operations.md`, `validation.md`, this report | Development/architecture/operations, decisions, evidence and acceptance instructions |

Models, all five migration files/snapshot, existing feature views/resources and
receipt layout remain unchanged. Previously tracked generated build files were
restored after validation; none are included in the implementation commits.

## 3. Database safety

- The tracked baseline database was inspected read-only and is **byte-for-byte
  unchanged**, verified by comparison with the baseline Git blob after validation.
- All automated tests use unique temporary SQLite files and test logs.
- Normal startup creates no sample items, rooms, tables, orders or bills.
- Existing installations retain their database path. Fresh installations use Local
  App Data; path pins/ambiguity checks prevent silent replacement of detected data.
- No new schema, reset, recreation, down migration or customer-file overwrite.
- Pending schema upgrades and exact empty-area repairs require verified backups.
  Area repair preserves every record/ID/status and changes only `Area = ''` to Indoor.
- Backup/restore stages SQLite snapshots, checks integrity and refuses existing
  destination files. Offline recovery keeps original databases and sidecars intact.

See [database operations](database-operations.md) for exact compatibility, profile
change, relocation and recovery procedures and discovery limitations.

## 4. Tests

Final clean Release portable run: **41 passed, 0 failed, 0 skipped**. Covers startup,
initialization, real migrations, data preservation, DI core, navigation/commands,
financial rounding, order edit/status transitions, billing persistence, parcel,
WAL backups/restores and failure scenarios.

The Windows target including its additional full composition case cross-compiles
cleanly. **No Windows tests, interactive checks or printer checks were executed**.
Windows CI is configured but has not run. The exact commands and full manual
workflow checklist are in [validation](validation.md).

## 5. Actual build status

```sh
dotnet restore ServeX.sln --artifacts-path /tmp/servex-final-artifacts -p:TestWindows=true -p:EnableWindowsTargeting=true
dotnet build ServeX.sln --artifacts-path /tmp/servex-final-artifacts -c Release --no-restore -p:TestWindows=true -p:EnableWindowsTargeting=true
```

Both succeeded on Linux with SDK 8.0.428. Application + Windows test target Release
cross-build: **0 warnings, 0 errors**. This is not a supported-Windows runtime result.
Baseline also built cleanly; introduced compiler issues caught during implementation
were corrected. Details/log paths and the non-failing test-host artifact-directory
notice are retained in validation.md.

## 6. Compatibility

All existing feature entry points remain: setup/login, Dashboard, Menu, Tables,
Parcel, Settings, order/billing windows, backup dialog and receipt printing.
Financial calculation expressions and EF schema are unchanged. Tests verify
persisted values/receipt data, and XAML compiles with updated enum bindings.

Intentional safety changes: no automatic demonstration data, no silent settings
fallback, no backup overwrite, no deletion of tables referenced by historical
orders, no charging cancelled/paid orders, no stale parcel duplication, explicit
failure on ambiguous/missing database selection, and controlled exit after unknown
UI exceptions. These are documented operational behavior changes, not removed POS
features. Physical printer output and interactive binding/dialog behavior still
need Windows acceptance; compatibility is not fully proven by cross-compilation.

## 7. Remaining issues, prioritized

1. **Release gate/high:** execute the supported Windows build/test target and every
   manual workflow/printer/storage failure check before customer rollout.
2. **High, pre-existing:** migrate plaintext passwords compatibly and implement
   an agreed license policy. The current bypass is retained, not endorsed for release.
3. **High, pre-existing:** single Windows session only; multi-session/terminal
   ordering/payment needs transactional concurrency design and tests. The instance
   guard does not cover other sessions/machines or external DB tools.
4. **Medium:** profile/install relocation requires explicit operator selection;
   no safe universal discovery of databases outside known locations is claimed.
5. **Medium:** monitor disk space, logs/backup retention and restore drills; there
   is no scheduled/off-device backup system or destructive restore UI.
6. **Medium/low, pre-existing:** consolidate duplicated financial rules only after
   agreement on rounding semantics; higher-precision inputs remain a documented
   risk. Historical polymorphic references remain schema-compatible.
7. **Low:** tracked generated artifacts/database remain in repository history;
   future cleanup requires an explicit data/repository retention decision.

## 8. Phase 2 readiness

The architectural foundation supports subsequent UI work without a framework
rewrite. **Do not mark Phase 1 complete or authorize production UI rollout yet:**
supported-Windows automated and interactive acceptance are outstanding. Phase 2
can proceed after those gates and triage of the existing security/concurrency
risks. No Phase 2 visual redesign is included here.
