# Phase 1 validation record

Environment: Debian Linux, .NET SDK **8.0.428**, runtime 8.0.34, MSBuild 17.11.71.
No Windows runtime/desktop or physical printer was available. The managed SDK was
installed temporarily under `/tmp/servex-dotnet`; no SDK is committed to the repo.
For commands below, `dotnet` was `/tmp/servex-dotnet/dotnet`, with
`DOTNET_CLI_HOME=/tmp/servex-cli` and `NUGET_PACKAGES=/tmp/servex-nuget`.
Runtime/package checks required the environment's permitted expanded execution
access. Initial sandbox bootstrap failures (read-only CLI home and runtime mutex)
were environmental and resolved before the baseline compiler check.

## Baseline, before application edits

At `cc64eba`:

```sh
dotnet restore ServeX.csproj -p:EnableWindowsTargeting=true --force
dotnet build ServeX.csproj -p:EnableWindowsTargeting=true --no-restore
```

Both succeeded. Build: **0 warnings, 0 errors**. No pre-existing source compiler
errors were observed. Logs: `/tmp/servex-baseline-restore.log` and
`/tmp/servex-baseline-build.log`.

## Final clean validation

```sh
dotnet restore ServeX.sln --artifacts-path /tmp/servex-final-artifacts -p:TestWindows=true -p:EnableWindowsTargeting=true
dotnet build ServeX.sln --artifacts-path /tmp/servex-final-artifacts -c Release --no-restore -p:TestWindows=true -p:EnableWindowsTargeting=true

dotnet test Tests/ServeX.Tests.csproj --artifacts-path /tmp/servex-core-final -c Release --logger 'trx;LogFileName=phase1-core-release.trx' --results-directory /tmp/servex-test-results
```

- Clean solution restore: **passed**.
- Application + Windows test target Release cross-build: **passed, 0 warnings, 0 errors**.
- Portable Release suite: **41 passed, 0 failed, 0 skipped**.
- Windows composition suite: **cross-compiled; not executed**. One additional
  Windows-only case exercises the full container, runtime ViewModel factories,
  cached workspaces, navigation/back and Setup/Login/Blocked ViewModel decisions.
- Interactive WPF, authentication windows, dialogs, binding diagnostics, printer
  and Windows installation permissions: **not executed**.
- Windows CI workflow: **configured, not run in this session**.

Earlier incremental checks caught two introduced build issues: WPF has no
RetryCancel MessageBox enum, and WPF test implicit usings omit System.IO. Both
were corrected; the final builds above are clean. The test host emits a
`VSTEST_ARTIFACTS_DIRECTORY` informational fallback notice; it does not represent
an application compiler warning or failed case. Earlier repeated TRX runs also
reported overwriting their own temporary test result; the final run uses a new name.

Logs: `/tmp/servex-final-restore.log`, `/tmp/servex-final-build.log`,
`/tmp/servex-final-tests.log`. Structured results:
`/tmp/servex-test-results/phase1-core-release.trx`.

## Automated coverage

| Area | Cases exercised |
| --- | --- |
| Initialization | Empty first launch, repeat launch, existing customer catalog/orders/bills/settings/users/statuses, empty existing catalog, corrupt database |
| Upgrade | Five real migrations, backup of pre-upgrade schema, row preservation, legacy empty Area repair, repeated repair no-op, backup failure blocks upgrade |
| Demo | Explicit seeding only, repeat no-op, customer database unchanged, no orders/bills/occupancy |
| Startup/auth | Setup vs Login, setup transaction, repeat setup rejected, valid/invalid/empty login, read errors propagate |
| DI | Core registrations validate; factory creates independent contexts at selected path |
| Navigation/commands | All five typed destinations, notifications, invalid destination, repeated selection, dashboard Back, async reentry, disabled execution, exception/finally behavior |
| Financial/order | Four subtotal/tax rounding cases, active-order edit identity/items, three payment methods, persisted discount/total/status/table release, duplicate billing, invalid discounts/payment method, Paid/Cancelled guards, stale parcel edits |
| Workflows | Parcel save/edit/bill/receipt data, menu FK restriction, table history deletion restriction, transaction rollback on invalid menu FK |
| Storage/backup | Local App Data, legacy pin, moved executable, missing pinned file, ambiguity/explicit path validation, live WAL backup, restore to new file, corrupt source, overwrite/self/missing-source rejection |

## Required Windows manual acceptance (still outstanding)

Use an isolated Windows user/profile and a **copy** of representative data, never
production. Publish to a new directory so tracked development `bin/Data` is not
mistaken for the test database. Record paths, row counts, known order/bill totals,
application version and pass/fail evidence before and after each test.

1. Run the Windows commands in README including `-p:TestWindows=true`; expect the
   portable cases plus the Windows-only composition test to pass. Publish with
   `dotnet publish ServeX.csproj -c Release -o C:\ServeX-Phase1-Test`.
2. **First launch:** with a new test profile and no DB, launch the published exe.
   Expect setup, no sample catalog, tables, rooms, orders or bills. Complete setup;
   verify a second setup cannot replace it. Try incorrect credentials, then correct
   credentials; expect inline error then the normal main shell. Restart; expect
   login and identical settings/customer counts.
3. **Legacy installation:** place a representative SQLite backup at the test exe's
   `Data\servex.db`. Use a clean test profile or an explicit existing path override.
   Confirm the active path in Settings, existing login, menu, rooms/table statuses,
   order IDs, billed totals/payment timestamps and migration history. For an older
   schema, confirm a readable pre-migration backup exists and no data disappears.
4. **Navigation:** visit Dashboard, Menu, Tables, Parcel, Settings; confirm selected
   button styling, title/icon/content and Back to Dashboard. Reclick the selected
   destination; confirm no reload/reset. Start an unsaved parcel/settings edit,
   navigate away/back and confirm it persists. Watch Visual Studio Output for WPF
   binding errors. Exercise dashboard shortcuts as well as sidebar buttons.
5. **Menu/floor:** add/edit/search/filter an item; toggle availability. Add indoor
   and outdoor tables, check duplicate-number validation, edit and switch filters.
   A referenced menu item/table must refuse deletion while history stays intact.
6. **Table order:** open an available table; add, increase/decrease/remove items,
   save, close/reopen and verify quantities and totals. Generate bill, cancel the
   billing dialog, reopen and pay. Confirm the order screen reloads, the table
   becomes available, and floor/dashboard/history refresh correctly.
7. **Parcel:** add items, enter customer, save/edit, generate bill, confirm payment;
   verify receipt customer label, unchanged floor status and fresh next-parcel draft.
   Cancel dialogs and verify the saved order remains available. Rapidly double-click
   save/generate/payment and confirm no duplicate operation/charge.
8. **Billing:** compare known subtotal, tax, discount and total examples from tests;
   exercise Cash/Card/UPI. Negative/excess discounts must fail without changing
   payment state. Retry payment on a billed/paid order and try a cancelled order;
   expect rejection. Verify persisted totals after restart.
9. **Printing:** print/reprint through a configured 80mm printer or Windows test
   printer. Compare prior receipt content, item quantities, financial values and
   layout. Disconnect/default an invalid printer; expect a logged inline error,
   no repeated payment and successful reprint after printer recovery.
10. **Settings/backup/restore:** save restaurant/phone/tax; verify title and next
    order tax. Cancel a backup dialog, create a new backup during normal activity,
    reject an existing name/live DB destination, and test a denied destination.
    Restore to a new file offline using database-operations.md, select it explicitly,
    restart and compare all expected records/totals. Retain every original file.
11. **Failure recovery/storage:** use isolated corrupt/missing/ambiguous DB copies,
    read-only legacy folder, blocked Backups directory and unavailable disk path.
    Expect an actionable startup failure/retry/exit, log diagnostics, and no empty
    fallback, setup reset or original-file overwrite. Restore valid access and retry.
    Move the exe while retaining its pinned DB; verify data continuity. Test another
    Windows user only with explicit database selection per the documented procedure.
12. **Shutdown/errors:** open a second instance in the same session; expect refusal.
    Close auth and main windows; expect process exit/released DB contexts. Confirm
    logs are writable under Local App Data. In a development debugger, exercise an
    unexpected dispatcher exception; expect diagnostic logging and controlled exit.

Supported-Windows build/test execution and this interactive checklist are release
and Phase 1 completion gates. Linux cross-builds alone do not satisfy them.
