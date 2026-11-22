; Build through Build-Installer.ps1 after selecting a trusted Inno Setup install.
#ifndef PublishDirectory
  #error PublishDirectory is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif

[Setup]
AppId=ServeX.POS.Desktop
AppName=ServeX POS
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\ServeX
DefaultGroupName=ServeX
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=ServeX-{#AppVersion}-win-x64-setup
SetupIconFile=..\Assets\ServeX.ico
UninstallDisplayIcon={app}\ServeX.exe

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{userprograms}\ServeX POS"; Filename: "{app}\ServeX.exe"
