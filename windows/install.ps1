# IP Monitor for Windows - installer
# Installs IP Monitor as a desktop app for the current Windows user (no admin rights needed):
#   - program files in  %LOCALAPPDATA%\Programs\IP Monitor
#   - your database in  Documents\IP Monitor\ip-monitor-db.json  (never overwritten if it already exists)
#   - Start menu and Desktop shortcuts with the IP Monitor icon
#   - an entry in Settings > Apps so it can be uninstalled like any other program
# The app opens in its own window (no tabs, no address bar) using Microsoft Edge, which is part of Windows 10 and 11.

$ErrorActionPreference = 'Stop'
$src     = Split-Path -Parent $MyInvocation.MyCommand.Path
$appName = 'IP Monitor'
$version = '1.0'
$dest    = Join-Path $env:LOCALAPPDATA "Programs\$appName"
$dataDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) $appName
$winProfile = Join-Path $dest 'window-profile'

function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }

Say ''
Say '  IP Monitor - Windows setup' 'Cyan'
Say '  --------------------------' 'Cyan'

# 1. a Chromium browser engine to run the app window (Edge first, Chrome as a fallback)
$engine = @(
  "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
  "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
  "$env:LOCALAPPDATA\Microsoft\Edge\Application\msedge.exe",
  "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
  "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
  "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $engine) {
  Say '  Microsoft Edge was not found. Install Edge (or Google Chrome) and run setup again.' 'Red'
  exit 1
}
Say "  Window engine: $engine"

# 2. program files
foreach ($f in 'IP-Monitor.html', 'IP-Monitor.ico') {
  if (-not (Test-Path (Join-Path $src $f))) { Say "  Missing $f next to this installer. Unzip the whole folder first." 'Red'; exit 1 }
}
New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($f in 'IP-Monitor.html', 'IP-Monitor.ico', 'uninstall.ps1', 'winbox-link-setup.cmd') {
  $p = Join-Path $src $f
  if (Test-Path $p) { Copy-Item $p $dest -Force; try { Unblock-File (Join-Path $dest $f) } catch {} }
}
Say "  Program installed in: $dest"

# 3. the database: copied once, never overwritten
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$db = Join-Path $dataDir 'ip-monitor-db.json'
if (Test-Path $db) {
  Say "  Database kept as it is: $db" 'Yellow'
} elseif (Test-Path (Join-Path $src 'ip-monitor-db.json')) {
  Copy-Item (Join-Path $src 'ip-monitor-db.json') $db
  try { Unblock-File $db } catch {}
  Say "  Database placed in: $db"
} else {
  Say "  No database file in the package. Create one from inside the app (Backup > Database file)." 'Yellow'
}

# 4. shortcuts: the app in its own window with its own taskbar icon
$page = [Uri]::new((Join-Path $dest 'IP-Monitor.html')).AbsoluteUri
$launchArgs = "--app=`"$page`" --user-data-dir=`"$winProfile`" --no-first-run --no-default-browser-check --window-size=1440,920"
$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk"
$desktop   = Join-Path ([Environment]::GetFolderPath('Desktop')) "$appName.lnk"
foreach ($lnk in $startMenu, $desktop) {
  $s = $shell.CreateShortcut($lnk)
  $s.TargetPath       = $engine
  $s.Arguments        = $launchArgs
  $s.WorkingDirectory = $dest
  $s.IconLocation     = (Join-Path $dest 'IP-Monitor.ico') + ',0'
  $s.Description      = 'IP Monitor - network and IP address management'
  $s.Save()
}
Say '  Shortcuts added to the Start menu and the Desktop.'

# 5. Settings > Apps entry, so it uninstalls like any other program
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\IPMonitor'
New-Item -Path $key -Force | Out-Null
$values = @{
  DisplayName     = $appName
  DisplayVersion  = $version
  Publisher       = 'IP Monitor'
  DisplayIcon     = (Join-Path $dest 'IP-Monitor.ico')
  InstallLocation = $dest
  UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $dest 'uninstall.ps1')`""
  NoModify        = 1
  NoRepair        = 1
}
foreach ($k in $values.Keys) { Set-ItemProperty -Path $key -Name $k -Value $values[$k] }

# 6. optional: "Open in WinBox" links
$winbox = Join-Path $dest 'winbox-link-setup.cmd'
if (Test-Path $winbox) {
  $ans = Read-Host '  Also set up "Open in WinBox" (log in to MikroTik devices with one click)? [Y/N]'
  if ($ans -match '^[Yy]') { & cmd.exe /c "`"$winbox`"" }
}

Say ''
Say '  IP Monitor is installed.' 'Green'
Say '  First time: in the app press "Open database file" and choose' 'Green'
Say "    $db" 'Green'
Say '  and allow editing. After that every change is saved into it automatically.' 'Green'
Say ''
$open = Read-Host '  Open IP Monitor now? [Y/N]'
if ($open -match '^[Yy]') { Start-Process -FilePath $engine -ArgumentList $launchArgs }
