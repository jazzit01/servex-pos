# Phase 2 UI/UX implementation and verification

**Implemented, pending Windows verification — not complete.**

Branch: `modernization/phase-2-ui-ux`, based on Phase 1 commit `f013ba5`.
Phase 1 was verified in source: composition root, context factory, production-safe
initialization, typed navigation, guarded commands and regression tests are present.
Its supported-Windows acceptance remains outstanding. No merge into main, remote
push, CI execution or Phase 3/4 work occurred in this session.

## 1. Redesigned screens

| Screen | Implementation |
| --- | --- |
| MainWindow | Compact navy navigation, ServeX identity, selected destination, signed-in user, page header, Back and status banner; removes the redundant summary side rail |
| DashboardView | Four compact operational metrics, service shortcuts and read-only recent-order grid; unpaid orders are clearly distinguished from confirmed sales |
| TablesView | Available/occupied/reserved colors **and text**, prominent numbers, selection border, direct Open order, area filters, totals, selected-table details and scrollable editor |
| MenuView | Searchable/sortable catalog, clear prices/availability, edit/delete actions, add/edit form, validation and contextual empty state |
| OrderView/OrderWindow | Search/categories, menu list, current order, quantity/remove controls, totals and guarded Save/Generate bill |
| ParcelView | Shared ordering controls with customer details and New parcel workflow; confirmation protects a nonempty draft |
| BillingView/BillingWindow | Itemized quantities/prices, discount input, tax/subtotal, prominent final payable, Cash/Card/UPI selection, payment confirmation and receipt printing |
| AuthWindow | Restrained dark brand panel and scrollable form area; original setup/login templates retained |
| LoginView | Clear credential form, password helper, validation/status, default Enter action and operation feedback |
| SetupView | Aligned restaurant/account fields, password helper, validation and save feedback |
| SettingsView | Restaurant/tax form, read-only license, selectable active database path, backup control and offline recovery guidance |

## 2. Design system

`Styles/DesignSystem.xaml` now centralizes cool neutral surfaces, navy sidebar,
teal primary actions, blue secondary/info and readable success/warning/danger
palettes. Typography is Segoe UI, with tabular financial numbers. Corners vary
consistently from 4 to 10 DIP. Spacing/padding/weight resources, cards, forms,
search fields, icon buttons, lists/chips, DataGrid presentation, badges, financial
text, busy indicators and empty states share one design language. Legacy resource
keys remain available, including `BorderBrushWarm` as a compatibility alias color.

Custom controls include hover, disabled, selected and keyboard focus states.
Segmented selected tabs retain contrast on hover. Text/password focus and field
validation remain visible. Primary/quantity targets have minimum 44 DIP heights;
main confirmation/sign-in controls use 48 DIP. Long text wraps; list/grid regions
scroll. Financial text has bounded wrapping regions rather than ellipsis. No UI
framework dependency, gradients, decorative shadows or animations were added.

## 3. Reusable components

- `Styles/Components.xaml`: sidebar item template, status-message template,
  combined busy/status feedback and shared Boolean visibility conversion.
- `PageHeaderStyle`, `MetricCardStyle`, `EmptyStateStyle`: lightweight reusable
  headered-content templates instead of multiple custom controls.
- `Presentation/Components/MenuPicker.xaml` and `OrderSummary.xaml`: real shared
  WPF UserControls for table and parcel ordering, with initialization-only code-behind.
- Existing `DialogService`: owner-aware, default-No confirmation for deleting a
  menu item/table and clearing a nonempty parcel draft. Existing domain/history
  guards still make the final persistence decision.

Content-control subtitles use `ContentStringFormat`; financial card values use
TextBlock.Text bindings so currency formatting is applied to a string target.

## 4. Files created and modified

Created:

- `Styles/Components.xaml`
- `Presentation/Components/MenuPicker.xaml` and `.xaml.cs`
- `Presentation/Components/OrderSummary.xaml` and `.xaml.cs`
- `Tests/DashboardMetricsTests.cs`
- `scripts/validate_ui.py`
- This document

Modified:

- `Styles/DesignSystem.xaml`, `App.xaml`
- Every existing `Views/*.xaml` screen/window (13 files); existing view code-behind
  remains unchanged
- `ViewModels/DashboardViewModel.cs`: observable real operational metrics
- `ViewModels/OrderViewModel.cs`, `ParcelViewModel.cs`: presentation-only search,
  combined category filtering and empty-results state; parcel draft confirmation
- `ViewModels/MenuViewModel.cs`, `TablesViewModel.cs`: confirmation dependency
- `Services/DashboardSnapshot.cs`, `DashboardService.cs`: read-only occupied,
  available and active-order counts
- `Services/DialogService.cs`: reusable confirmation UI
- `Tests/WindowsCompositionTests.cs`: search/draft and WPF STA/resource checks
- `README.md`
- `.github/workflows/phase-1-validation.yml` renamed/extended to
  `.github/workflows/windows-validation.yml`

## 5. Preserved behavior/data

All five destinations, Back behavior, setup/login, menu CRUD, indoor/outdoor floor,
table orders, parcel customer/details/items, quantity controls, billing/payment
recording, settings, backups and printing retain their original command paths.
Search only filters displayed catalog items; it does not edit the draft or data.
Phase 1 async execution guards remain in use; no submission command was rewritten.

No model, database schema, migration, persistence factory/location, order/billing
financial service, authentication/license service, backup service or receipt-print
implementation is changed. The tracked SQLite database is byte-for-byte unchanged.
The only new service calculations are read-only counts. Sales uses the existing
paid-bill query; active orders count Open/Preparing/Served, and table states count
exact Occupied/Available values. Reserved tables are not mislabeled as occupied.
The existing aggregate ActiveTables property is retained for compatibility.

Safety confirmation is an intentional UI change; declining leaves data/drafts
unchanged. Cash/Card/UPI are described as records of payment received externally,
not online payment processing. Recent orders include unpaid activity and are not
misrepresented as completed transactions.

## 6. Static checks

Run from repository root:

```sh
python3 scripts/validate_ui.py
git diff --check f013ba5..HEAD
```

The UI script passed for **18 XAML files**, checking well-formed XML, duplicate
resource keys within inspected files, resource references against the inspected
resource set, preservation of legacy design keys, all baseline action-command
names (including shared controls), enum navigation command/selection pairs,
content-control formatting and protected-file diffs.

These are bounded static checks. The resource check does **not** prove WPF resource
scope/load order, the command inventory does **not** prove runtime DataContexts,
and XML parsing does **not** prove XAML compilation or visual correctness.
Actual WPF cross-compilation was also performed as described below.

## 7. Executed build/tests and CI status

Despite the environment description, the temporary .NET SDK from Phase 1 remained
available. Debian Linux, SDK 8.0.428 was used with these environment-specific settings:
`DOTNET_CLI_HOME=/tmp/servex-cli`, `NUGET_PACKAGES=/tmp/servex-nuget`, and
`dotnet=/tmp/servex-dotnet/dotnet`.

```sh
dotnet build ServeX.sln --artifacts-path /tmp/servex-phase2-final -c Release -p:TestWindows=true -p:EnableWindowsTargeting=true

dotnet test Tests/ServeX.Tests.csproj --artifacts-path /tmp/servex-phase2-core -c Release --logger 'trx;LogFileName=phase2-core.trx' --results-directory /tmp/servex-phase2-results
```

- Application + Windows test target Release restore/cross-build: **passed,
  0 warnings, 0 errors**.
- Portable SQLite/service/financial regression tests: **42 passed, 0 failed,
  0 skipped**. Includes the additional real dashboard count/payment-transition test.
- Three Windows-only test cases: **compiled, not executed**. They cover full DI
  composition/navigation, search preserving drafts/totals, and loading resources/
  measuring the redesigned views on an STA thread without production startup.
- Windows CI: **configured, not executed in this session**. The workflow uses
  `windows-latest`, .NET 8, correct solution/test paths, static UI checks and TRX
  upload. Full checkout history supports the baseline contract comparison.
- No WPF application was launched and no screenshot or physical-printer result
  was visually validated here.

Build log: `/tmp/servex-phase2-final-build.log`. Portable test log:
`/tmp/servex-phase2-tests.log`. Results:
`/tmp/servex-phase2-results/phase2-core.trx`.
The test host emitted its existing non-failing artifact-directory fallback notice.

## 8. Required Windows/visual acceptance

Run on isolated copies/test profiles per Phase 1's [data-safety procedures](database-operations.md).
Never visually test by opening the tracked development database or a live customer DB.

```powershell
python scripts/validate_ui.py
dotnet restore ServeX.sln -p:TestWindows=true
dotnet build ServeX.sln -c Release --no-restore -p:TestWindows=true
dotnet test Tests/ServeX.Tests.csproj -c Release --no-build --no-restore -p:TestWindows=true --logger trx
dotnet publish ServeX.csproj -c Release -o C:\ServeX-UI-Test
# Use a separate test profile for first launch, or an explicit existing test DB:
$env:SERVEX_DATABASE_PATH = 'C:\ServeX-TestData\servex.db'
& 'C:\ServeX-UI-Test\ServeX.exe'
```

Capture screenshots and pass/fail notes for every screen at:

- 1280×800 and 1366×768 at 100% scaling
- 1920×1080 at 125% and 150%
- Minimum **1100×680 DIP** shell/order/auth and **1000×680 DIP** billing windows
- Maximized and resized windows, plus moving between mixed-DPI monitors

The minimum sizes are logical DIPs, not physical pixels. A display whose usable
logical work area is below these existing supported minima is not a supported
layout target. Startup maximum sizes use the primary Windows work area; mixed-monitor
placement/resizing still needs validation, especially when monitor sizes differ.

Check all of the following:

1. **Startup/auth:** setup fields, password masking/helper, validation, Enter to
   submit, Tab/Shift+Tab order, busy feedback, login failures/success and close.
2. **Shell:** all five destinations, selected styling and screen-reader page status,
   sidebar user/restaurant, Back, wrapping titles/subtitles and draft retention.
3. **Dashboard:** match metrics to known persisted records; unpaid orders do not
   increase sales. Verify empty state, large values, date/UTC label, grid scrolling
   and quick actions. Check a failed read does not present mock data as successful.
4. **Floor:** indoor/outdoor filtering, text/color states, selected cards, direct
   order access, details and editor. Check empty areas and all busy/disabled actions.
5. **Menu:** search by name/category, clear, sort, prices, available/unavailable,
   add/edit validation, save/cancel. Cancel and accept delete confirmations; verify
   historical references remain protected by existing services.
6. **Table and parcel orders:** category/search combination, no-match state, long
   names, unavailable items, Add, increase/decrease/remove, large quantities/totals,
   Save and Generate bill. Double-click actions to verify no duplicate execution.
   Confirm search never modifies totals or removes draft items. Cancel/accept New
   parcel confirmation and verify saved records remain unchanged.
7. **Checkout:** long item lists, quantity/unit/line amounts, tax/discount/final
   payable at large values, validation, Cash/Card/UPI, payment once only, receipt
   availability after payment, cancelled dialogs and reopening orders. Compare
   printed output with Phase 1; printer failure must not repeat payment.
8. **Settings/backup:** save fields, read-only license, selectable/wrapped DB path,
   cancel/success/failed backup, no overwrite, readable status and busy indicators.
9. **Accessibility/layout:** keyboard-only use, visible focus including grid cells,
   category arrows, no color-only statuses, text scaling, contrast, long translated/
   Unicode names, amounts without clipping, visible scrollbars and no blocked
   footer/actions at minimum dimensions. Check Windows high-contrast mode separately.
10. **Diagnostics:** inspect Visual Studio Output/PresentationTraceSources for
    binding/resource/template errors; review ServeX logs. Exercise all Phase 1
    workflow/data-preservation acceptance steps too.

The automated STA smoke test is useful but is not screenshot comparison, touch
hardware testing, screen-reader acceptance or a substitute for this checklist.

## 9. Remaining UI issues/risks

- Windows tests/CI execution and interactive visual acceptance are outstanding.
- Touch targets, high-contrast/system themes, actual tab order, popup behavior,
  mixed-DPI/monitor bounds and printer dialogs require the checklist above.
- Catalog/recent-order/bill grids use horizontal scrolling when minimum column
  widths exceed the viewport; verify discoverability at the smallest sizes.
- Very large catalogs remain backed by existing loaded collections; no Phase 3
  data virtualization/performance rewrite was introduced.
- Existing Phase 1 security and multi-session limitations are unchanged. They
  remain production-release risks and are not solved by visual modernization.

## 10. Readiness for Phase 3

Ready for code review and supported-Windows acceptance. **Not signed off as Phase 2
complete or ready for Phase 3 execution.** First run Windows CI/tests, complete the
visual/workflow/DPI checklist and resolve any failures. Carry forward Phase 1's
security/concurrency release gates. No Phase 3/4 feature work is included.
