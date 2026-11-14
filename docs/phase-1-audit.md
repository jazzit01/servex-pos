# Phase 1 baseline audit

Baseline: `cc64eba`, branch `modernization/phase-1-foundation`. Initial working tree clean.

Reviewed startup/authentication, all service and ViewModel responsibilities, model mappings,
window code-behind, XAML bindings/resources and all five EF migrations/snapshot. The application
is one .NET 8 WPF project. Views are thin; services already own most persistence work.

| Severity | Baseline finding | Phase 1 disposition |
| --- | --- | --- |
| Critical | Normal startup inserts demo menu, tables, rooms, order and bill, and occupies a table whenever corresponding tables are empty | Remove startup seeding; explicit isolated demo action only |
| High | Database/log paths under executable directory can be unwritable; moving executable can silently create another database | Preserve legacy database, persist selected path, detect ambiguity; writable fresh storage |
| High | Backup writes directly over destination with no integrity verification | Refuse overwrite; stage SQLite online backup, verify, then publish |
| High | Settings query errors return plausible defaults | Propagate failure, block startup/login on read errors |
| High | User passwords stored in plaintext; license validator always returns true | Existing compatibility retained; separate security remediation required before production rollout |
| High | Payment/order writes lack multi-process concurrency protection | Single-instance application guard; multi-terminal concurrency remains unsupported |
| Medium | MainViewModel constructs service graph and queries EF | Central composition root, factory contexts, summary service |
| Medium | Async lambdas passed to Action-based commands become async void; repeated actions can interleave | Task-based commands, execution guard and logged exception boundary |
| Medium | Order screen reload inside busy billing action returns immediately | Reload after busy scope exits |
| Medium | Navigation uses integer constants across VM and XAML | Typed destination/state; preserve cached workspace VMs and dashboard back behavior |
| Medium | Unhandled UI exceptions are all marked handled, including unknown state after failures | Log and stop after unexpected UI failure; recoverable operations retain local status handling |
| Medium | Duplicate tax/subtotal logic in order, parcel, billing and VMs | Keep formulas stable; regression coverage before future consolidation |
| Medium | Table/room references are polymorphic integer IDs without FKs; deletion can orphan history | Historical table deletion guard; schema unchanged |
| Low | Repository tracks build output, NuGet artifacts, executable and a SQLite database | Preserve tracked files; exclude generated paths from compile and future artifacts from Git |

## Data and migrations

Legacy database: `<executable directory>/Data/servex.db`. A tracked copy exists at
`bin/Debug/net8.0-windows/Data/servex.db`; it contains 2 orders, 2 bills, settings and a user.
Only read-only schema/count inspection was performed. Never run tests against it or commit its contents.
Original backup uses SQLite's online BackupDatabase API, suitable for WAL, but can overwrite files.
No restore UI exists; offline restore must be documented and tested with isolated copies.

Five forward migrations create tables and add billing fields, settings/users, table area,
and parcel customer name. No forward migration deletes records; down migrations are destructive
and must not be used on customer installations. No schema change is planned for Phase 1.

## Validation environment

Debian Linux; no Windows desktop/runtime. Temporary SDK 8.0.428 installed under `/tmp/servex-dotnet`.
Windows execution and printer verification cannot be performed here. Baseline restore/build
results are recorded in `validation.md` after execution; environment errors are kept distinct
from source compiler errors. Existing generated files and database must remain unchanged.
