# ServeX POS 0.3.0 for Windows x64

Download and run `ServeX-0.3.0-win-x64-setup.exe`. It includes the .NET desktop
runtime and creates a ServeX POS shortcut in your Start menu. Installation is
per Windows user. ServeX opens first-time setup if it finds no existing database.

The installer is unsigned. Its SHA-256 checksum is listed in `SHA256SUMS.txt`.
The installer was compiled and its payload tested, including verification that
no database files are in the payload. Interactive installation requires Windows.

App files go to `%LOCALAPPDATA%\Programs\ServeX`. The application stores its
database separately in `%LOCALAPPDATA%\ServeX`. Uninstalling the application
leaves the data folder in place.
