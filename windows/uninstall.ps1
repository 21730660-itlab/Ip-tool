# IP Monitor for Windows - uninstaller
# Removes the program, its shortcuts and its Settings > Apps entry.
# Your database (Documents\IP Monitor\ip-monitor-db.json) is NOT deleted.

$appName = 'IP Monitor'
$dest    = Join-Path $env:LOCALAPPDATA "Programs\$appName"
$dataDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) $appName

$ans = Read-Host "Uninstall $appName? Your database in $dataDir is kept. [Y/N]"
if ($ans -notmatch '^[Yy]') { exit 0 }

foreach ($lnk in (Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk"),
                 (Join-Path ([Environment]::GetFolderPath('Desktop')) "$appName.lnk")) {
  if (Test-Path $lnk) { Remove-Item $lnk -Force }
}
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\IPMonitor' -Recurse -Force -ErrorAction SilentlyContinue

# the uninstaller runs from inside the program folder, so the folder is removed a moment after it exits
Start-Process -WindowStyle Hidden -FilePath cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$dest`""
Write-Host "$appName was removed. Your database is still in $dataDir" -ForegroundColor Green
Start-Sleep -Seconds 2
