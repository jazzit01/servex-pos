Unicode true
!include "MUI2.nsh"
!include "x64.nsh"

Name "ServeX POS 0.3.1"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\ServeX"
RequestExecutionLevel user
SetCompressor /SOLID lzma

!define MUI_ICON "${APPICON}"
!define MUI_UNICON "${APPICON}"
!define MUI_FINISHPAGE_RUN "$INSTDIR\ServeX.exe"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "ServeX requires 64-bit Windows."
    Abort
  ${EndIf}
  SetShellVarContext current
  SetRegView 64
FunctionEnd

Section "ServeX POS"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH}\*.*"
  CreateShortCut "$SMPROGRAMS\ServeX POS.lnk" "$INSTDIR\ServeX.exe"
  WriteUninstaller "$INSTDIR\Uninstall ServeX.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ServeXPOS" "DisplayName" "ServeX POS"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ServeXPOS" "DisplayVersion" "0.3.1"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ServeXPOS" "UninstallString" '"$INSTDIR\Uninstall ServeX.exe"'
SectionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
FunctionEnd

Section "Uninstall"
  !include "${UNINSTALLFILES}"
  Delete "$INSTDIR\Uninstall ServeX.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\ServeX POS.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ServeXPOS"
SectionEnd
