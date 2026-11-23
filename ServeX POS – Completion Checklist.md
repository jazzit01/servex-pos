# ServeX POS – Completion Checklist

Nov 20, 2026 · @Ejaz

## Status at a glance

ServeX is a working dine-in and parcel POS, roughly 60–70% of the way to a shippable product. That figure is an estimate from reading the code; the app was not built or run. Tick items as they are finished. **P0** = fix before real customers, **P1** = needed for daily use, **P2** = polish.

Already built:

- First-run setup and login
- Menu and table management (indoor/outdoor), add, edit, delete
- Table orders and parcel orders, with tax, discount and cash/card/UPI
- 80 mm receipt printing, dashboard summary, database backup

Reviewed: repo `jazzit01/servex-pos`, one visible commit, about 10,300 lines of C# and XAML (WPF, .NET 8, SQLite).

## Implementation notes

The original findings below describe the project at the time of the audit. The Phase 2 baseline now supports explicit Debug demo data, selects Local App Data for new installations, backs up existing databases before migrations, and blocks deleting tables with order history. It also includes a README, tests, and Windows CI configuration. Legacy database relocation now keeps the original file as a recovery copy.

This pass moves table orders and billing into the main workspace, caps the log with one rotated copy, and makes startup and dashboard errors staff-friendly. The navigation test cases below reflect the new flow. Windows interactive verification remains open.

## P0 – Security and data safety

Six problems should be fixed before the app touches real customers or real sales data.

- [x] **Hash passwords.** New credentials use salted PBKDF2-SHA256. Startup upgrades existing accounts before login and preserves their passwords.
- [x] **Stop seeding demo data on fresh installs.** Sample records are available only through explicit Debug demo mode.
- [x] **Move the database out of the install folder.** Startup copies a verified legacy database to Local App Data and pins it, preserving the original. An explicit recovery path still overrides selection.
- [x] **Move and cap the log file.** `logs.txt` also sits in the install folder and never rotates. Runtime logs now live next to the active database and rotate at 2 MiB with one previous file retained.
- [x] **Remove the license stub.** Setup and Settings no longer ask for or display a key. Existing database values are preserved.
- [x] **Stop showing raw exception text to staff.** Startup, dashboard, and global error messages display staff-facing guidance. Detailed errors go to the log, including failures caught by the `AppDomain` and `TaskScheduler` handlers.

## Orders and billing workflow

Orders can be created and billed, but several states and controls the data model promises are never reachable from the screens.

- [x] **P1 – Cancel order.** Saved table and parcel orders have a confirmed Cancel action. Cancellation keeps history, releases the table, and prevents billing.
- [x] **P1 – Set "Paid" after payment.** New payments use Paid. Legacy Billed records with a payment timestamp still display as Paid in the dashboard.
- [x] **P1 – Block billing of cancelled or already-paid orders.** The service rejects cancelled orders and orders already marked billed or paid.
- [x] **P1 – Release the table after a closed bill.** A paid table becomes Cleaning. The floor marks it available again when staff choose Mark clean.
- [x] **P2 – Kitchen flow.** Saved table, room and parcel orders can send new items to the kitchen and be marked served.
- [x] **P2 – Merge items instead of overwriting.** The app preserves matching lines and uses a revision check to reject stale table and parcel edits.
- [x] **P2 – Read prices from the menu on save.** Saves use catalog prices and reject new unavailable items. Existing archived lines retain their historical prices.
- [x] **P2 – Discount options.** Checkout supports amount and percentage discounts with a reason. Discounts above 20% require the signed-in administrator’s password.
- [x] **P2 – Payment details.** Checkout records optional Card and UPI references and splits a bill across two payment allocations.
- [x] **P2 – Table transfer and bill split.** A saved order can move to another table or merge into its open order. Checkout splits the amount between two payments.

## Tables, Menu and Rooms

Table and menu screens work for adding and editing, but deletes are unsafe or unhelpful, and Rooms have no screen at all.

- [x] **Block deleting a table that has an open order.** The service refuses deletion for any table with order history.
- [x] **P1 – Archive menu items instead of failing to delete.** Archived items disappear from the catalog and order picker and remain readable in historical orders.
- [x] **P1 – Menu validation.** Add and edit reject duplicate names within a category and prices that round to zero or less.
- [x] **P1 – Reserved tables.** Reserve and release are floor actions. Starting an order on a reservation requires confirmation.
- [x] **P2 – Rooms.** Rooms have a workspace for numbers, status and room orders. Billing and receipts use the room number.
- [x] **P2 – Category management.** The menu editor offers existing categories and uses their saved spelling for case-insensitive matches.
- [x] **P2 – Clean up `TableService.UpdateTableAsync`.** Table updates apply the shared validation and normalization once.

## Users, Settings and Login

There is one admin account and no way to leave the session, change a password or add staff.

- [x] **P1 – Logout / switch user.** Sign out returns to the login window within the same process.
- [x] **P1 – Change password.** Settings verifies the current password and confirms the new one before saving.
- [x] **P1 – Manage users and roles.** Administrators can add Admin or Cashier accounts. Management, deletion, discounts and manual backups have role checks.
- [x] **P1 – Login hardening.** SQLite uses a case-insensitive username match. Five consecutive failures pause sign-in for 15 seconds.
- [x] **P1 – Edit all restaurant details.** Settings saves name, address, phone, and tax. The inactive license feature was removed.
- [x] **P2 – Tax settings.** Settings offer a tax label and registration number, and the menu supports tax-exempt items.
- [x] **P2 – Warn when tax changes.** Settings explains that saved open orders use the current tax rate when billed.
- [x] **P2 – Currency and locale.** Settings select a currency and locale. Money is formatted in the workspace, reports and receipts.

## Receipts and printing

Printing works through the Windows print dialog path, but the receipt does not carry the restaurant's own details.

- [x] **P1 – Use the real restaurant header.** Receipts use the saved restaurant name, address, and phone.
- [x] **P1 – Show tax rate and currency.** Receipts show the recorded tax rate and currency for new bills. Legacy receipts show the saved tax amount with no inferred rate.
- [x] **P1 – Reprint a past bill.** History opens orders in a read-only billing workspace and offers receipt reprints for payments.
- [x] **P1 – Handle printer problems.** Ask staff to select a Windows printer and handle cancellation, unavailable printers, and offline printers. Physical printer acceptance remains open.
- [x] **P2 – Long item names and paper width.** Receipt text wraps within the selected 58 mm or 80 mm paper size.
- [x] **P2 – Kitchen ticket.** Kitchen tickets print the location, time, and quantities added since the previous ticket.
- [x] **P2 – Print preview or PDF.** Billing provides an on-screen receipt preview and PDF through the Windows print picker when Microsoft Print to PDF is installed.

## Backup, restore and data location

Backup and restore use SQLite’s native copy operation. Restore stages a verified copy and applies it on restart before operations resume.

- [x] **P0 – Restore from backup.** Settings stages a verified ServeX backup and restarts the app. Startup keeps a safety copy before replacing active data. The data operation is covered by SQLite tests. Windows file picker and restart still need a hands-on check.
- [x] **P1 – Automatic backups.** Normal startup creates one backup per day in Local App Data and retains the seven most recent dated backups per database.
- [x] **P1 – Backup location and result.** Settings remembers the last successful backup folder and shows the saved path and file size.
- [x] **P1 – Migration safety.** Startup creates an integrity-checked backup before applying pending migrations to an existing database.
- [x] **P2 – Export data.** Settings exports separate orders, bills, and menu CSV files in one ZIP.

## Dashboard, history and reports

The dashboard shows today's revenue, active tables, today's orders, parcel count and the last five orders. There is no way to look further back.

- [x] **P1 – Order history.** History has date, order/reference, type, status and payment filters, plus read-only details and reprints.
- [x] **P1 – Fix "Active tables".** Occupied and reserved tables have separate counts.
- [x] **P1 – Daily summary and end-of-day close.** The sales summary shows payment breakdown, tax, discounts and cancellations and prints a single-day end-of-day report.
- [x] **P2 – Sales reports.** Reports group payments by day, week or month and show best-selling items and category amounts.
- [x] **P2 – Open orders list.** The dashboard lists unpaid active orders separately.
- [x] **P2 – Time zones.** The dashboard and receipts display local time. Exports label timestamps as UTC.

## Packaging and release

The project builds a Windows executable, but nothing yet turns it into something a restaurant can install and keep running.

- [ ] **P1 – Installer.** Per-user Inno Setup configuration and build script are ready. The native installer must still be compiled and tested on Windows, including installation, upgrade, and preservation of the Local App Data database.
- [x] **P1 – Self-contained publish.** The Windows x64 publish profile includes the .NET desktop runtime. Launch on Windows is still part of acceptance testing.
- [x] **P1 – Version number.** The project declares its version and Settings displays it.
- [x] **P1 – App icon and title.** The app embeds a ServeX icon and uses the restaurant name in sign-in and workspace titles.
- [x] **P1 – First-run check.** Startup verifies database-folder write access and setup warns if there is no Windows printer.
- [x] **P2 – Update path.** The Windows packaging guide documents installer-based updates and pre-migration backup.
- [x] **P2 – Support bundle.** Settings creates a local ZIP with a verified database copy and available logs after confirmation.

## Repo hygiene, tests and code quality

The repository holds 1,280 tracked files, most of them build output, and it has no tests.

- [ ] **P0 – Remove historical local database and build files.** The active tree ignores and no longer tracks these files. The reviewed procedure covers credential rotation and published-history removal. Published history remains intact pending separate authorization.
- [x] **P1 – Write the README.** It covers requirements, build, run, storage, backups, and publishing.
- [x] **P1 – Add a test project.** Real SQLite tests cover initialization, login, orders, billing, and recoverability using isolated temporary databases.
- [x] **P1 – Share one tax and rounding routine.** Order, billing, settings, and their previews use shared lookup and rounding code.
- [x] **P1 – Share one database access pattern.** Services use an injected runtime context factory and logger.
- [x] **P1 – Turn on nullable reference types.** Nullable reference checking is enabled on entity models. Optional text and navigation properties are annotated.
- [x] **P2 – Build in CI.** GitHub Actions is configured for Windows build and tests on pushes and pull requests.
- [x] **P2 – Git history.** Project changes are tracked in descriptive commits.

Installer source and a build helper are in `packaging/`. Installer compilation and clean/upgrade acceptance require Windows. The requested [Git history cleanup procedure](docs/history-cleanup-review.md) is ready for review. Published history has been preserved.

## Control-by-control test list

Every command bound in the screens is listed below with the result to expect. Run it on a clean install, and tick each line only after you have seen it work. Lines marked *gap* depend on a fix above.

### Setup and login

- [ ] **Save setup:** empty restaurant, address, phone, username or password is refused with a clear message; valid data opens the login screen or main window.
- [ ] **Save setup twice:** a second attempt after setup is refused ("already configured").
- [ ] **Login, correct password:** main window opens showing the signed-in username.
- [ ] **Login, wrong or empty password:** stays on login with an error; the username is not case-sensitive.
- [ ] **Login after restart:** goes straight to login, not setup.

### Main window and navigation

- [ ] **Sidebar: Dashboard, Menu, Tables, Parcel, Settings:** each opens its own screen and the title and subtitle update.
- [ ] **Back and Dashboard:** on an order or bill, Back returns to the previous screen. Dashboard is available in the sidebar. On top-level screens, Back to dashboard returns to the dashboard.
- [ ] **Header counts:** Menu items, tables, rooms and orders change after adding or deleting.
- [ ] **Logout:** confirm signed-out users return to the login window.

### Dashboard

- [ ] **New table order:** opens the order window for the first available table; with no free table, shows a message instead of failing.
- [ ] **Go to Tables / Go to Menu:** open the matching screens.
- [ ] **Figures:** today's revenue, active tables, today's orders and parcels match what was billed; the last five orders show the right table or parcel label.

### Menu

- [ ] **Add item:** opens an empty panel; save with name, category and price adds it to the list and to the order screens.
- [ ] **Add item, invalid:** empty name, empty category, zero or negative price is refused.
- [ ] **Edit item:** loads the item, saving updates the list and any open order screen.
- [ ] **Availability toggle:** an unavailable item shows disabled and cannot be added to a table or parcel order.
- [ ] **Search and Clear search:** the list filters as you type; Clear restores it.
- [ ] **Cancel:** closes the panel and discards changes.
- [ ] **Archive an item:** it disappears from the menu and new orders but remains on existing bills.

### Tables

- [ ] **Indoor / Outdoor filter:** only that area's tables show, and the counts update.
- [ ] **Select table:** highlights it and shows its details and available actions.
- [ ] **Add table:** unique number required; a duplicate (any case) is refused; area and status are saved.
- [ ] **Edit table / Edit selected:** both open the same panel with the table's values; saving updates the floor.
- [ ] **Open order (floor card and selected panel):** opens the order workspace for that table; an occupied table loads its saved items.
- [ ] **Delete table / Delete selected:** an empty table is removed; a table with an open order is refused.
- [ ] **Cancel:** closes the panel without saving.

### Table order workspace

- [ ] **Category filter:** shows only that category; "All" shows everything.
- [ ] **Add item:** adds a line; adding the same item again increases its quantity.
- [ ] **Increase / Decrease quantity:** totals update; decreasing from 1 removes the line or stops at 1 (confirm which is intended).
- [ ] **Remove item:** removes the line and updates totals.
- [ ] **Save order:** an empty order is refused; a valid one marks the table Occupied; reopening shows the same items.
- [ ] **Generate bill:** saves the order, then opens billing in the same main window.
- [ ] **Totals:** subtotal, tax at the configured percentage, and total match a hand calculation.

### Parcel

- [ ] **Customer name:** blank is stored without a customer name and the receipt uses “Parcel”.
- [ ] **Add, increase, decrease, remove item:** same behaviour as the table order workspace.
- [ ] **Save order:** creates a parcel order with no table; it can be reopened and edited until billed.
- [ ] **Generate bill:** opens billing for the parcel.
- [ ] **New parcel:** clears the draft and starts a fresh order without touching saved ones.

### Billing

- [ ] **Discount:** negative is refused; above the bill total is refused; a valid amount reduces the total.
- [ ] **Payment method (Cash, Card, UPI):** the selected method is saved and shown on the receipt.
- [ ] **Confirm payment:** marks the order paid, puts a dining table into Cleaning, and blocks a second payment on the same order.
- [ ] **Print receipt:** disabled until payment is confirmed; prints the items, tax, discount, total and method.
- [ ] **Going back without paying:** the order stays open and the table stays Occupied.

### Settings

- [ ] **Save settings:** empty name or phone is refused; tax outside 0–100 is refused; valid changes appear in the header and on the next bill.
- [ ] **Backup database:** the save dialog opens, the file is created, and it opens as a valid database; choosing the live database path is refused.
- [ ] **Restore, change password, and address:** verify the backup restart, current-password check, and updated address on receipts.

### Whole-app checks

- [ ] **Restart:** all data survives a close and reopen.
- [ ] **No printer:** printing shows a clear message rather than crashing.
- [ ] **Window sizes:** every screen is usable at the smallest supported window size and at full screen.
- [ ] **Log file:** errors are written and the user sees a short message, not a stack trace.
