# Phase 1 architectural decisions

1. **Incremental boundaries, one runtime project.** Preserve namespaces, models,
   views, XAML resources, migrations and printing. Add an Application composition
   layer, Infrastructure persistence and Presentation navigation instead of moving
   every existing file. The separate test project enables real regression checks.
2. **Context factory instead of scoped desktop contexts.** Desktop windows have no
   natural HTTP-style scope. Singleton stateless services inject EF's
   `IDbContextFactory<ServeXDbContext>` and dispose contexts per operation. Runtime
   user/order arguments are supplied at the composition boundary, not looked up
   from containers inside ViewModels. Main owns/disposes event subscriptions.
3. **Storage compatibility before relocation.** Retain an existing executable-local
   database and pin its location. Fresh installs use Local App Data. Missing or
   ambiguous selections stop startup. Explicit recovery paths must already exist.
   Operator-managed relocation avoids copying a live WAL database incorrectly.
4. **Migrations only at normal initialization.** No production sample rows, reset,
   schema recreation or new migration. Existing files are backed up before changes.
   Repair only the historical empty-area default to the intended Indoor value.
   Demonstration data is an explicit Debug action on a different database.
5. **Task-based commands, unchanged bindings.** `AsyncRelayCommand.ExecuteAsync` is
   awaitable and prevents duplicate execution; `ICommand.Execute` is the sole async
   void adapter. Existing cross-command IsBusy guards remain. Operation catches log
   diagnostics and retain user status bindings. Unknown UI errors close the process.
6. **Typed navigation, stable workspaces.** Destinations are an enum shared by XAML
   command parameters/triggers and navigation state. Main retains workspace VMs;
   repeat clicks do not reload the selected destination. Table entry refreshes floor
   data for operational freshness. Back keeps the existing dashboard behavior.
   Source-workspace changes update dependent overview/parcel data, preserving other
   editors and settings drafts; overlapping refresh requests are coalesced.
7. **Financial compatibility.** Preserve tax/discount formulas and receipt content.
   Regression tests lock rounding/totals, persisted bills, duplicate payment rejection
   and table release. New guards reject cancelled/paid orders and invalid payment
   methods; stale parcel IDs fail instead of creating duplicate orders. Concurrent
   multi-session writes remain outside this phase's single-session guarantee.
8. **Portable tests plus Windows composition tests.** Portable tests compile actual
   platform-neutral source against SQLite, enabling validation on Linux without
   WPF stubs. `TestWindows=true` references the real application and exercises its
   composition factories/workspace VMs as well. Cross-compilation is not Windows
   runtime acceptance; CI/manual gates remain explicit.
