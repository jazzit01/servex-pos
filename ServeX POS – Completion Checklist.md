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

## P0 – Security and data safety

Six problems should be fixed before the app touches real customers or real sales data.

- [ ] **Hash passwords.** `User.Password` is stored and compared as plain text (`ApplicationAccessService`). Store a salted hash (PBKDF2 or BCrypt), verify by hash, and migrate the existing admin row.
- [ ] **Stop seeding demo data on fresh installs.** `DatabaseInitializationService` adds Classic Burger, Caesar Salad, tables T1/T2, rooms R101/R102 and a fake open order for T1 before setup runs. This inflates order counts and leaves T1 occupied. Seed only in a debug build or behind an explicit "load sample data" option.
- [ ] **Move the database out of the install folder.** `servex.db` lives in `AppContext.BaseDirectory/Data`, so it is lost on reinstall and unwritable under Program Files. Use `%LocalAppData%\ServeX` and migrate any existing file on first start.
- [ ] **Move and cap the log file.** `logs.txt` also sits in the install folder and never rotates. Write it next to the database and roll it by size or day.
- [ ] **Replace the license stub.** `LicenseService.IsValid` always returns true. Either implement real validation or remove the license field and screen text until it is real.
- [ ] **Stop showing raw exception text to staff.** The global handler prints `e.Exception.Message`. Show a plain message, log the detail, and also handle exceptions outside the UI thread (`AppDomain` and `TaskScheduler` handlers).

## Orders and billing workflow

Orders can be created and billed, but several states and controls the data model promises are never reachable from the screens.

- [ ] **P1 – Cancel order.** `OrderStatus.Cancelled` exists, but no screen or command cancels an order. Add a Cancel button with a confirmation, free the table, and keep the order for history.
- [ ] **P1 – Set "Paid" after payment.** `BillingService.ConfirmPaymentAsync` sets the status to `Billed` and never uses `Paid`. Decide on one meaning for each, then update the dashboard and guards to match.
- [ ] **P1 – Block billing of cancelled or already-paid orders.** The current guard only checks `Billed` and `PaidAt`.
- [ ] **P1 – Release the table after a closed bill.** The table goes straight to Available. `TableStatus.Cleaning` is never set, so add a "mark clean" step if you want it.
- [ ] **P2 – Kitchen flow.** `Preparing` and `Served` are never set. Add a send-to-kitchen action, a kitchen ticket print, and a mark-served control, or delete the unused statuses.
- [ ] **P2 – Merge items instead of overwriting.** Saving a table or parcel order deletes all items and re-adds them. Two terminals saving at once will overwrite each other. Add a row version or lock the order while it is open.
- [ ] **P2 – Read prices from the menu on save.** `OrderSaveItem.Price` comes from the screen. Re-read the price from the database when the order is saved.
- [ ] **P2 – Discount options.** Only a flat amount is supported. Add percentage discounts and a reason field, with a manager check above a limit.
- [ ] **P2 – Payment details.** Card and UPI record only the method. Add an optional reference number, and split payment across methods if needed.
- [ ] **P2 – Table transfer and bill split.** Moving an order to another table, merging two tables and splitting a bill are not available.

## Tables, Menu and Rooms

Table and menu screens work for adding and editing, but deletes are unsafe or unhelpful, and Rooms have no screen at all.

- [ ] **P0 – Block deleting a table that has an open order.** `TableService.DeleteTableAsync` removes the table without checking. `Order.ReferenceId` has no foreign key, so the order is orphaned and its bill then labels itself "Parcel". Refuse the delete and say why.
- [ ] **P1 – Archive menu items instead of failing to delete.** Items that were ever ordered cannot be deleted (foreign key is `Restrict`), and staff only see a generic error. Add an archived flag, hide archived items from ordering, and keep them in old bills.
- [ ] **P1 – Menu validation.** Check for duplicate item names within a category, and a price above zero.
- [ ] **P1 – Reserved tables.** `Reserved` can only be set by hand in the edit panel. Add a reserve and release action on the floor view, and block new orders on a reserved table unless confirmed.
- [ ] **P2 – Rooms.** The model, seed rows and header count exist, but there is no Rooms screen, no room orders, and the dashboard prints "Room #id". Either build them (screen, room status, room orders, billing to room) or remove the model, seed rows and count.
- [ ] **P2 – Category management.** Categories are free text on each item. Add a category list so spelling stays consistent.
- [ ] **P2 – Clean up `TableService.UpdateTableAsync`.** It repeats the same normalisation several times and has an unreachable early return. Collapse it into one validation step.

## Users, Settings and Login

There is one admin account and no way to leave the session, change a password or add staff.

- [ ] **P1 – Logout / switch user.** The sidebar shows who is signed in but has no sign-out button. Add one that returns to the login screen without restarting.
- [ ] **P1 – Change password.** Add it to Settings, asking for the current password first.
- [ ] **P1 – Manage users and roles.** Add staff accounts with at least Admin and Cashier roles. Limit Settings, Menu edits, deletes, discounts and backup to Admin.
- [ ] **P1 – Login hardening.** Add a short lockout or delay after repeated failures, and make the username check case-insensitive in the database rather than with `ToLower()` in the query.
- [ ] **P1 – Edit all restaurant details.** `UpdateSettingsAsync` only saves name, phone and tax. Address and license key cannot be changed after setup. Add address, and show the license state.
- [ ] **P2 – Tax settings.** One flat percentage applies to everything. Add a tax label (GST, VAT), an optional tax number for receipts, and a way to mark items as tax-exempt.
- [ ] **P2 – Warn when tax changes.** Open orders are re-priced at the new rate when billed. Show a note, or lock the rate on the order when it is first saved.
- [ ] **P2 – Currency and locale.** Amounts are shown with `N2` and no currency symbol. Add a currency setting and apply it everywhere, including receipts.

## Receipts and printing

Printing works through the Windows print dialog path, but the receipt does not carry the restaurant's own details.

- [ ] **P1 – Use the real restaurant header.** `ReceiptPrintService` prints the fixed text "SERVEX" and "Simple Receipt". Print the restaurant name, address and phone from Settings, plus the tax number if one is added.
- [ ] **P1 – Show tax rate and currency.** Print the tax percentage next to the tax line and use the configured currency symbol.
- [ ] **P1 – Reprint a past bill.** Receipts can only be printed from the billing screen right after payment. Add a reprint action from order history.
- [ ] **P1 – Handle printer problems.** Show a clear message when no printer is installed, the printer is offline, or the user cancels. Let the user choose a printer instead of always using the default.
- [ ] **P2 – Long item names and paper width.** Item names are drawn on one line at a fixed width. Wrap long names so they do not run off an 80 mm roll, and make width configurable for 58 mm printers.
- [ ] **P2 – Kitchen ticket.** A separate printout of new items per order, with table and time, goes with the kitchen flow in the orders section.
- [ ] **P2 – Print preview or PDF.** Offer an on-screen preview and a save-as-PDF option for testing without a printer.

## Backup, restore and data location

Backup uses SQLite's own copy routine, which is safe while the app runs. Nothing exists yet to bring a backup back.

- [ ] **P0 – Restore from backup.** Add a Restore action in Settings: pick a file, confirm, validate that it is a ServeX database, close open connections, replace the live file, and restart the app. Keep a copy of the replaced database first.
- [ ] **P1 – Automatic backups.** Create a dated backup on every start or close and keep the last N, in a folder outside the install directory.
- [ ] **P1 – Backup location and result.** Remember the last folder used, and show the saved path and file size after a successful backup.
- [ ] **P1 – Migration safety.** `MigrateAsync` runs on every start. Back up the database automatically before applying a new migration, and show a readable message if it fails.
- [ ] **P2 – Export data.** Export orders, bills and menu to CSV or Excel for the accountant.

## Dashboard, history and reports

The dashboard shows today's revenue, active tables, today's orders, parcel count and the last five orders. There is no way to look further back.

- [ ] **P1 – Order history.** A screen listing all orders with search and filters (date range, type, status, payment method), opening any bill read-only, with reprint.
- [ ] **P1 – Fix "Active tables".** The dashboard counts every table that is not Available, so Reserved tables are counted as active. Count only Occupied, and show Reserved separately.
- [ ] **P1 – Daily summary and end-of-day close.** Totals by payment method, tax collected, discounts given, cancelled orders, and a printable end-of-day report.
- [ ] **P2 – Sales reports.** Revenue by day, week and month, best-selling items, and sales by category.
- [ ] **P2 – Open orders list.** Show unpaid orders on the dashboard so none are left behind at closing.
- [ ] **P2 – Time zones.** Times are stored in UTC and converted for display in some places and not others. Convert consistently everywhere dates are shown.

## Packaging and release

The project builds a Windows executable, but nothing yet turns it into something a restaurant can install and keep running.

- [ ] **P1 – Installer.** Produce a per-user installer (MSIX or Inno Setup) that installs the app and creates a shortcut, with data kept in `%LocalAppData%\ServeX` so updates never touch it.
- [ ] **P1 – Self-contained publish.** Publish for win-x64 with the .NET runtime included, so the till PC needs nothing installed first.
- [ ] **P1 – Version number.** Set an assembly version in the project file and show it in Settings, so support knows which build is running.
- [ ] **P1 – App icon and title.** The project file sets no application icon. Add one and use the restaurant name in window titles.
- [ ] **P1 – First-run check.** On start, confirm the data folder is writable and a printer exists, and say clearly if not.
- [ ] **P2 – Update path.** Decide how new versions reach the till (installer download or auto-update), and make sure the pre-migration backup runs on update.
- [ ] **P2 – Support bundle.** A button that zips the log and a database copy so problems can be sent for diagnosis.

## Repo hygiene, tests and code quality

The repository holds 1,280 tracked files, most of them build output, and it has no tests.

- [ ] **P0 – Add a `.gitignore` and untrack build output.** `bin/`, `obj/`, `.codex-build/`, `.dotnet/` and `.nuget/` are committed, including DLLs and the local `servex.db` (a live copy with its -wal and -shm files, plus `logs.txt`, is tracked under `bin/Debug`). Remove them from the index and ignore them. That database likely holds the admin password in plain text, so open it, change that password, and purge the files from git history.
- [ ] **P1 – Write the README.** It is currently one line. Cover what the app does, requirements, how to build and run, where data and logs live, and how to back up.
- [ ] **P1 – Add a test project.** Start with the billing maths (subtotal, tax, discount limits, rounding), order save rules, table delete guard, and login. Use an in-memory SQLite database.
- [ ] **P1 – Share one tax and rounding routine.** The tax lookup is copied across `OrderService`, `BillingService`, `ApplicationSettingsService` and the seed code. Put it in one place so a rate change cannot drift.
- [ ] **P1 – Share one database access pattern.** Every service creates its own `ServeXDbContext` and its own `LogService`. Pass them in so services can be tested.
- [ ] **P1 – Turn on nullable reference types.** The models use `string` and navigation properties that can be null with no annotations. Enable them to catch null bugs at build time.
- [ ] **P2 – Build in CI.** A Windows build and test run on every push.
- [ ] **P2 – Git history.** Only one commit was visible in this shallow clone. Keep commits small and described so progress can be tracked from the log.

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
- [ ] **Back to Dashboard:** hidden on the dashboard, visible elsewhere, returns to the dashboard.
- [ ] **Header counts:** Menu items, tables, rooms and orders change after adding or deleting.
- [ ] **Logout:** *gap* – not available yet.

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
- [ ] **Delete an unused item:** it disappears. **Delete an ordered item:** a readable message appears and the item stays (*gap* – archive option).

### Tables

- [ ] **Indoor / Outdoor filter:** only that area's tables show, and the counts update.
- [ ] **Select table:** highlights it and shows its details and available actions.
- [ ] **Add table:** unique number required; a duplicate (any case) is refused; area and status are saved.
- [ ] **Edit table / Edit selected:** both open the same panel with the table's values; saving updates the floor.
- [ ] **Open order (floor card and selected panel):** opens the order window for that table; an occupied table loads its saved items.
- [ ] **Delete table / Delete selected:** an empty table is removed; a table with an open order is refused (*gap*).
- [ ] **Cancel:** closes the panel without saving.

### Table order window

- [ ] **Category filter:** shows only that category; "All" shows everything.
- [ ] **Add item:** adds a line; adding the same item again increases its quantity.
- [ ] **Increase / Decrease quantity:** totals update; decreasing from 1 removes the line or stops at 1 (confirm which is intended).
- [ ] **Remove item:** removes the line and updates totals.
- [ ] **Save order:** an empty order is refused; a valid one marks the table Occupied; reopening shows the same items.
- [ ] **Generate bill:** saves the order, then opens billing for it.
- [ ] **Totals:** subtotal, tax at the configured percentage, and total match a hand calculation.

### Parcel

- [ ] **Customer name:** blank saves as "Atithi".
- [ ] **Add, increase, decrease, remove item:** same behaviour as the table order window.
- [ ] **Save order:** creates a parcel order with no table; it can be reopened and edited until billed.
- [ ] **Generate bill:** opens billing for the parcel.
- [ ] **New parcel:** clears the draft and starts a fresh order without touching saved ones.

### Billing

- [ ] **Discount:** negative is refused; above the bill total is refused; a valid amount reduces the total.
- [ ] **Payment method (Cash, Card, UPI):** the selected method is saved and shown on the receipt.
- [ ] **Confirm payment:** marks the order billed, frees the table, and blocks a second payment on the same order.
- [ ] **Print receipt:** disabled until payment is confirmed; prints the items, tax, discount, total and method.
- [ ] **Closing the window without paying:** the order stays open and the table stays Occupied.

### Settings

- [ ] **Save settings:** empty name or phone is refused; tax outside 0–100 is refused; valid changes appear in the header and on the next bill.
- [ ] **Backup database:** the save dialog opens, the file is created, and it opens as a valid database; choosing the live database path is refused.
- [ ] **Restore, change password, address, license:** *gap* – not available yet.

### Whole-app checks

- [ ] **Restart:** all data survives a close and reopen.
- [ ] **No printer:** printing shows a clear message rather than crashing.
- [ ] **Window sizes:** every screen is usable at the smallest supported window size and at full screen.
- [ ] **Log file:** errors are written and the user sees a short message, not a stack trace.
