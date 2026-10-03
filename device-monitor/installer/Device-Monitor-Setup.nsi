; Device Monitor - installer (NSIS 3)
; Build:  makensis Device-Monitor-Setup.nsi             -> small setup, installs the .NET 8 Desktop Runtime if missing
;         makensis -DOFFLINE Device-Monitor-Setup.nsi   -> full setup with .NET built in (no download needed)
; The app must be published first (see ../README.md): framework-dependent to ../out-fdd, self-contained to ../out.

Unicode true
Target amd64-unicode
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!define APP      "Device Monitor"
!define EXE      "Device-Monitor.exe"
!define VERSION  "1.2.0"
!define REGKEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeviceMonitor"
!define RUNKEY   "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUNTIME_URL  "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
!define RUNTIME_PAGE "https://dotnet.microsoft.com/download/dotnet/8.0"

!ifdef OFFLINE
  !define SRC "..\out"
  OutFile "..\out-setup\Device-Monitor-Setup-Full.exe"
!else
  !define SRC "..\out-fdd"
  OutFile "..\out-setup\Device-Monitor-Setup.exe"
!endif

Name "${APP}"
Caption "${APP} ${VERSION} Setup"
BrandingText "${APP} ${VERSION}"
; per-user install: no administrator rights needed
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\Device Monitor"
InstallDirRegKey HKCU "${REGKEY}" "InstallLocation"
SetCompressor /SOLID lzma
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APP}"
VIAddVersionKey "FileDescription" "${APP} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "${APP}"

!define MUI_ICON   "..\src\DeviceMonitor.App\app.ico"
!define MUI_UNICON "..\src\DeviceMonitor.App\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEFINISHPAGE_BITMAP "welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "welcome.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "header.bmp"
!define MUI_WELCOMEPAGE_TITLE "Install ${APP}"
!define MUI_WELCOMEPAGE_TEXT "This installs ${APP} ${VERSION} on this computer.$\r$\n$\r$\n${APP} pings your MikroTik routers and other network devices on the interval you choose, shows their status with live graphs, warns you with a pop-up when a device turns OFF or comes back ON, and saves every event in a log file.$\r$\n$\r$\nNo administrator rights are needed. Click Next to continue."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Open ${APP} now"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${APP} needs 64-bit Windows 10 or 11." /SD IDOK
    Abort
  ${EndIf}
FunctionEnd

!ifndef OFFLINE
; Is the .NET 8 Desktop Runtime (x64) installed?  Result in $0: 1 = yes
Function HasRuntime
  StrCpy $0 0
  FindFirst $1 $2 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\8.*"
  ${If} $2 != ""
    StrCpy $0 1
  ${EndIf}
  FindClose $1
FunctionEnd

Section "-.NET 8 Desktop Runtime" SecRuntime
  Call HasRuntime
  ${If} $0 == 1
    DetailPrint ".NET 8 Desktop Runtime is already installed."
    Return
  ${EndIf}
  MessageBox MB_YESNO|MB_ICONQUESTION "${APP} needs Microsoft .NET 8 Desktop Runtime (free, about 55 MB), which is not on this computer yet.$\r$\n$\r$\nDownload and install it now?$\r$\n(Windows may ask for permission to install it.)" /SD IDNO IDYES download
    MessageBox MB_ICONINFORMATION "${APP} will be installed, but it will only open after you install .NET 8 Desktop Runtime (x64) from:$\r$\n${RUNTIME_PAGE}$\r$\n$\r$\nTip: the Full setup (Device-Monitor-Setup-Full.exe) has .NET built in." /SD IDOK
    Return
  download:
  DetailPrint "Downloading .NET 8 Desktop Runtime..."
  nsExec::ExecToLog `powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol='Tls12'; $$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing -Uri '${RUNTIME_URL}' -OutFile '$TEMP\windowsdesktop-runtime-8-x64.exe'"`
  Pop $1
  ${IfNot} ${FileExists} "$TEMP\windowsdesktop-runtime-8-x64.exe"
    MessageBox MB_ICONEXCLAMATION "The download did not work (no internet?). Install .NET 8 Desktop Runtime (x64) yourself from the page that opens now, then start ${APP}." /SD IDOK
    ExecShell "open" "${RUNTIME_PAGE}"
    Return
  ${EndIf}
  DetailPrint "Installing .NET 8 Desktop Runtime..."
  ExecWait '"$TEMP\windowsdesktop-runtime-8-x64.exe" /install /passive /norestart' $1
  Delete "$TEMP\windowsdesktop-runtime-8-x64.exe"
  Call HasRuntime
  ${If} $0 != 1
    MessageBox MB_ICONEXCLAMATION ".NET 8 Desktop Runtime was not installed (code $1). Install it from the page that opens now, then start ${APP}." /SD IDOK
    ExecShell "open" "${RUNTIME_PAGE}"
  ${EndIf}
SectionEnd
!endif

Section "Device Monitor" SecApp
  SectionIn RO
  SetOutPath "$INSTDIR"
  ; close a running copy so the file can be replaced
  nsExec::Exec 'taskkill /IM "${EXE}"'
  Sleep 800
  File "${SRC}\${EXE}"
  File /oname=Device-Monitor.ico "..\src\DeviceMonitor.App\app.ico"
  File /oname=README.txt "README-install.txt"
  File /oname=devices-example.csv "devices-example.csv"
  WriteUninstaller "$INSTDIR\Uninstall Device Monitor.exe"

  CreateDirectory "$SMPROGRAMS\${APP}"
  CreateShortcut "$SMPROGRAMS\${APP}\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\Device-Monitor.ico"
  CreateShortcut "$SMPROGRAMS\${APP}\Log files.lnk" "$DOCUMENTS\Device Monitor\Logs"
  CreateShortcut "$SMPROGRAMS\${APP}\Uninstall ${APP}.lnk" "$INSTDIR\Uninstall Device Monitor.exe"
  CreateDirectory "$DOCUMENTS\Device Monitor\Logs"

  ; Apps & features entry
  WriteRegStr HKCU "${REGKEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${REGKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${REGKEY}" "Publisher" "${APP}"
  WriteRegStr HKCU "${REGKEY}" "DisplayIcon" "$INSTDIR\Device-Monitor.ico"
  WriteRegStr HKCU "${REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REGKEY}" "UninstallString" '"$INSTDIR\Uninstall Device Monitor.exe"'
  WriteRegStr HKCU "${REGKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall Device Monitor.exe" /S'
  WriteRegDWORD HKCU "${REGKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${REGKEY}" "NoRepair" 1
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\Device-Monitor.ico"
SectionEnd

Section "Start with Windows (monitor in the background)" SecAutostart
  WriteRegStr HKCU "${RUNKEY}" "DeviceMonitor" '"$INSTDIR\${EXE}" --minimized'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} "The Device Monitor program (required)."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Put a Device Monitor icon on the desktop."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAutostart} "Start Device Monitor in the notification area when you sign in, so your devices are always watched. Can be changed later in Settings."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  nsExec::Exec 'taskkill /IM "${EXE}"'
  Sleep 800
  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\Device-Monitor.ico"
  Delete "$INSTDIR\README.txt"
  Delete "$INSTDIR\devices-example.csv"
  Delete "$INSTDIR\Uninstall Device Monitor.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APP}\${APP}.lnk"
  Delete "$SMPROGRAMS\${APP}\Log files.lnk"
  Delete "$SMPROGRAMS\${APP}\Uninstall ${APP}.lnk"
  RMDir "$SMPROGRAMS\${APP}"
  Delete "$DESKTOP\${APP}.lnk"
  DeleteRegValue HKCU "${RUNKEY}" "DeviceMonitor"
  DeleteRegKey HKCU "${REGKEY}"
  ; the device list (%APPDATA%\DeviceMonitor) and the log files (Documents\Device Monitor\Logs) are kept
SectionEnd
