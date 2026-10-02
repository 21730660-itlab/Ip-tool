; IP Monitor for Windows - installer (NSIS 3)
; Build:  makensis IP-Monitor-Setup.nsi                 -> small setup, installs the .NET 8 Desktop Runtime if missing
;         makensis -DOFFLINE IP-Monitor-Setup.nsi       -> full setup with .NET built in (no download needed)
; The app must be published first (see ../README.md): framework-dependent to ../out-fdd, self-contained to ../out.

Unicode true
Target amd64-unicode
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!define APP      "IP Monitor"
!define EXE      "IP-Monitor.exe"
!define VERSION  "1.0.0"
!define REGKEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\IPMonitor"
!define RUNTIME_URL  "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
!define RUNTIME_PAGE "https://dotnet.microsoft.com/download/dotnet/8.0"

!ifdef OFFLINE
  !define SRC "..\out"
  OutFile "..\out-setup\IP-Monitor-Setup-Full.exe"
!else
  !define SRC "..\out-fdd"
  OutFile "..\out-setup\IP-Monitor-Setup.exe"
!endif

Name "${APP}"
Caption "${APP} ${VERSION} Setup"
BrandingText "${APP} ${VERSION}"
; per-user install: no administrator rights needed
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\IP Monitor"
InstallDirRegKey HKCU "${REGKEY}" "InstallLocation"
SetCompressor /SOLID lzma
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APP}"
VIAddVersionKey "FileDescription" "${APP} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "${APP}"

!define MUI_ICON   "..\src\IpMonitor.App\app.ico"
!define MUI_UNICON "..\src\IpMonitor.App\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEFINISHPAGE_BITMAP "welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "welcome.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "header.bmp"
!define MUI_WELCOMEPAGE_TITLE "Install ${APP}"
!define MUI_WELCOMEPAGE_TEXT "This installs ${APP} ${VERSION} for Windows on this computer.$\r$\n$\r$\nIt works with your existing ip-monitor-db.json database file (the same file and accounts as the web version). Your database is never changed or removed by this setup.$\r$\n$\r$\nClick Next to continue."
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
    MessageBox MB_ICONINFORMATION "${APP} will be installed, but it will only open after you install .NET 8 Desktop Runtime (x64) from:$\r$\n${RUNTIME_PAGE}" /SD IDOK
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

Section "IP Monitor" SecApp
  SectionIn RO
  SetOutPath "$INSTDIR"
  ; close a running copy so the file can be replaced
  nsExec::Exec 'taskkill /IM "${EXE}"'
  Sleep 500
  File "${SRC}\${EXE}"
  File /oname=IP-Monitor.ico "..\src\IpMonitor.App\app.ico"
  File /oname=README.txt "README-install.txt"
  WriteUninstaller "$INSTDIR\Uninstall IP Monitor.exe"

  CreateDirectory "$SMPROGRAMS\${APP}"
  CreateShortcut "$SMPROGRAMS\${APP}\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\IP-Monitor.ico"
  CreateShortcut "$SMPROGRAMS\${APP}\Uninstall ${APP}.lnk" "$INSTDIR\Uninstall IP Monitor.exe"

  ; Apps & features entry
  WriteRegStr HKCU "${REGKEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${REGKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${REGKEY}" "Publisher" "${APP}"
  WriteRegStr HKCU "${REGKEY}" "DisplayIcon" "$INSTDIR\IP-Monitor.ico"
  WriteRegStr HKCU "${REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REGKEY}" "UninstallString" '"$INSTDIR\Uninstall IP Monitor.exe"'
  WriteRegStr HKCU "${REGKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall IP Monitor.exe" /S'
  WriteRegDWORD HKCU "${REGKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${REGKEY}" "NoRepair" 1
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\IP-Monitor.ico"
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} "The IP Monitor program (required)."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Put an IP Monitor icon on the desktop."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  nsExec::Exec 'taskkill /IM "${EXE}"'
  Sleep 500
  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\IP-Monitor.ico"
  Delete "$INSTDIR\README.txt"
  Delete "$INSTDIR\Uninstall IP Monitor.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APP}\${APP}.lnk"
  Delete "$SMPROGRAMS\${APP}\Uninstall ${APP}.lnk"
  RMDir "$SMPROGRAMS\${APP}"
  Delete "$DESKTOP\${APP}.lnk"
  DeleteRegKey HKCU "${REGKEY}"
  ; the database file and %APPDATA%\IPMonitor (preferences) are kept on purpose
SectionEnd
