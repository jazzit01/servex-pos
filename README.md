# ServeX POS 0.3.1 for Windows x64

Close ServeX, then download and run `ServeX-0.3.1-win-x64-setup.exe`. It includes
the .NET desktop runtime and creates a ServeX POS shortcut in your Start menu.
The app and installer use the supplied ServeX icons.

- New installations start with five indoor tables, five outdoor tables, and
  three rooms after first-run setup.
- For an existing installation, an administrator can choose **Tables → Add
  starter layout**. It fills empty areas without replacing existing tables or
  rooms.
- Menu categories now offer starter suggestions, and **Save & add another**
  keeps the menu form open for bulk entry. A table tile opens its order directly.

The installer is unsigned. Check the SHA-256 value in `SHA256SUMS.txt`. The
installer was compiled and its payload tested. Interactive installation and the
desktop UI still need Windows verification.

The app installs under `%LOCALAPPDATA%\Programs\ServeX`. Its database remains
separate under `%LOCALAPPDATA%\ServeX`.
