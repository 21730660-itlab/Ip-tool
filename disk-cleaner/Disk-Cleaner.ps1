<#
.SYNOPSIS
    Disk Cleaner - frees space on Windows by removing temporary files,
    caches and the contents of the Recycle Bin.

.DESCRIPTION
    Run "Disk-Cleaner.bat" to start it with a menu (it asks for admin rights
    so it can also clean the Windows folders). Files that are in use by an
    open program are skipped, never forced.

    Nothing personal is touched: documents, downloads, browser passwords,
    cookies and history are all left alone.

.PARAMETER Mode
    Run without the menu:
      Scan  - only show how much space can be freed
      Quick - temp folders + Recycle Bin
      Full  - Quick + update cache, browser caches, error reports, etc.
      Deep  - Full + Windows component store cleanup (slow, needs admin)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Disk-Cleaner.ps1 -Mode Full
#>
[CmdletBinding()]
param(
    [ValidateSet('Scan', 'Quick', 'Full', 'Deep')]
    [string]$Mode
)

$ErrorActionPreference = 'Continue'
$IsAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$LogFile = Join-Path $PSScriptRoot 'cleanup-log.txt'

# ---------------------------------------------------------------- helpers

function Format-Size([double]$Bytes) {
    if ($Bytes -ge 1GB) { return '{0:N2} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N1} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N0} KB' -f ($Bytes / 1KB) }
    return '{0:N0} B' -f $Bytes
}

function Write-Log([string]$Text) {
    try { Add-Content -Path $LogFile -Value ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Text) } catch { }
}

# Folders that must never be emptied, even if a pattern resolves to them by mistake.
$Protected = @(
    $env:SystemDrive + '\', $env:windir, "$env:windir\System32", $env:ProgramFiles,
    ${env:ProgramFiles(x86)}, $env:ProgramData, $env:USERPROFILE, $env:APPDATA,
    $env:LOCALAPPDATA, "$env:SystemDrive\Users"
) | Where-Object { $_ } | ForEach-Object { $_.TrimEnd('\').ToLowerInvariant() }

function Test-SafePath([string]$Path) {
    $p = $Path.TrimEnd('\').ToLowerInvariant()
    return ($p.Length -gt 3) -and ($Protected -notcontains $p)
}

# Expands wildcard patterns into the real folders / files they point at.
function Resolve-Targets([string[]]$Patterns) {
    foreach ($pattern in $Patterns) {
        if (-not $pattern) { continue }
        Get-Item -Path $pattern -Force -ErrorAction SilentlyContinue |
            Where-Object { Test-SafePath $_.FullName }
    }
}

function Get-ItemsSize($Items) {
    $total = 0
    foreach ($item in $Items) {
        if ($item.PSIsContainer) {
            $sum = (Get-ChildItem -LiteralPath $item.FullName -Recurse -Force -File -ErrorAction SilentlyContinue |
                    Measure-Object -Property Length -Sum).Sum
            if ($sum) { $total += $sum }
        } elseif (Test-Path -LiteralPath $item.FullName) {
            $total += $item.Length
        }
    }
    return [double]$total
}

function Get-TargetSize($Target) {
    if ($Target.SizeOf) { return [double](& $Target.SizeOf) }
    $items = @(Resolve-Targets $Target.Folders) + @(Resolve-Targets $Target.Files)
    return Get-ItemsSize $items
}

# Empties folders (keeping the folder itself) and deletes matching files.
# Anything locked by a running program is silently skipped.
function Remove-TargetContent($Target) {
    foreach ($folder in Resolve-Targets $Target.Folders) {
        if (-not $folder.PSIsContainer) { continue }
        Get-ChildItem -LiteralPath $folder.FullName -Force -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    foreach ($file in Resolve-Targets $Target.Files) {
        if ($file.PSIsContainer) { continue }
        Remove-Item -LiteralPath $file.FullName -Force -ErrorAction SilentlyContinue
    }
}

function Get-FreeSpace {
    $d = Get-CimInstance Win32_LogicalDisk -Filter ("DeviceID='{0}'" -f $env:SystemDrive) -ErrorAction SilentlyContinue
    if ($d) { return [double]$d.FreeSpace } else { return 0 }
}

# ---------------------------------------------------------------- what gets cleaned

$Local = $env:LOCALAPPDATA
$Win   = $env:windir

$Targets = @(
    @{ Name = 'Your temporary files';        Level = 1; Admin = $false
       Folders = @($env:TEMP) }

    @{ Name = 'Windows temporary files';     Level = 1; Admin = $true
       Folders = @("$Win\Temp") }

    @{ Name = 'Recycle Bin (all drives)';    Level = 1; Admin = $false
       SizeOf = {
           $sum = 0
           foreach ($drive in Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue) {
               $bin = Join-Path $drive.Root '$Recycle.Bin'
               if (Test-Path -LiteralPath $bin) {
                   $s = (Get-ChildItem -LiteralPath $bin -Recurse -Force -File -ErrorAction SilentlyContinue |
                         Measure-Object -Property Length -Sum).Sum
                   if ($s) { $sum += $s }
               }
           }
           $sum
       }
       Clean = {
           if (Get-Command Clear-RecycleBin -ErrorAction SilentlyContinue) {
               Clear-RecycleBin -Force -ErrorAction SilentlyContinue
           } else {
               (New-Object -ComObject Shell.Application).Namespace(0xA).Items() |
                   ForEach-Object { Remove-Item -LiteralPath $_.Path -Recurse -Force -ErrorAction SilentlyContinue }
           }
       } }

    @{ Name = 'Windows Update download cache'; Level = 2; Admin = $true
       Folders = @("$Win\SoftwareDistribution\Download")
       Services = @('wuauserv', 'bits') }

    @{ Name = 'Delivery Optimization cache'; Level = 2; Admin = $true
       Folders = @("$Win\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")
       Clean = {
           if (Get-Command Delete-DeliveryOptimizationCache -ErrorAction SilentlyContinue) {
               Delete-DeliveryOptimizationCache -Force -ErrorAction SilentlyContinue | Out-Null
           }
       } }

    @{ Name = 'Thumbnail cache';             Level = 2; Admin = $false
       Files = @("$Local\Microsoft\Windows\Explorer\thumbcache_*.db",
                 "$Local\Microsoft\Windows\Explorer\iconcache_*.db") }

    @{ Name = 'Internet Explorer / Windows web cache'; Level = 2; Admin = $false
       Folders = @("$Local\Microsoft\Windows\INetCache") }

    @{ Name = 'Error reports and crash dumps'; Level = 2; Admin = $false
       Folders = @("$Local\Microsoft\Windows\WER", "$Local\CrashDumps",
                   "$env:ProgramData\Microsoft\Windows\WER\ReportArchive",
                   "$env:ProgramData\Microsoft\Windows\WER\ReportQueue",
                   "$Win\Minidump")
       Files   = @("$Win\MEMORY.DMP") }

    @{ Name = 'Graphics (DirectX) shader cache'; Level = 2; Admin = $false
       Folders = @("$Local\D3DSCache", "$Local\NVIDIA\DXCache", "$Local\NVIDIA\GLCache", "$Local\AMD\DxCache") }

    @{ Name = 'Google Chrome cache';         Level = 2; Admin = $false
       Folders = @("$Local\Google\Chrome\User Data\*\Cache",
                   "$Local\Google\Chrome\User Data\*\Code Cache",
                   "$Local\Google\Chrome\User Data\*\GPUCache") }

    @{ Name = 'Microsoft Edge cache';        Level = 2; Admin = $false
       Folders = @("$Local\Microsoft\Edge\User Data\*\Cache",
                   "$Local\Microsoft\Edge\User Data\*\Code Cache",
                   "$Local\Microsoft\Edge\User Data\*\GPUCache") }

    @{ Name = 'Mozilla Firefox cache';       Level = 2; Admin = $false
       Folders = @("$Local\Mozilla\Firefox\Profiles\*\cache2") }

    @{ Name = 'Windows component store (old updates) - slow'; Level = 3; Admin = $true
       SizeOf = { 0 }
       Clean = {
           Write-Host '      Running DISM, this can take 5-20 minutes...' -ForegroundColor DarkGray
           & dism.exe /Online /Cleanup-Image /StartComponentCleanup /Quiet /NoRestart | Out-Null
       } }
)

# ---------------------------------------------------------------- run

function Invoke-Cleanup([int]$Level, [switch]$ScanOnly) {
    $chosen = $Targets | Where-Object { $_.Level -le $Level }
    $freeBefore = Get-FreeSpace
    $totalFound = 0
    $totalFreed = 0

    Write-Host ''
    if ($ScanOnly) { Write-Host '  Scanning (nothing will be deleted)...' -ForegroundColor Cyan }
    else           { Write-Host '  Cleaning...' -ForegroundColor Cyan }
    Write-Host ''

    foreach ($t in $chosen) {
        $label = '  {0,-48}' -f $t.Name
        if ($t.Admin -and -not $IsAdmin) {
            Write-Host $label -NoNewline
            Write-Host 'skipped (needs admin)' -ForegroundColor DarkYellow
            continue
        }
        Write-Host $label -NoNewline

        $before = Get-TargetSize $t
        $totalFound += $before

        if ($ScanOnly) {
            if ($t.SizeOf -and $t.Level -eq 3) { Write-Host 'size shown by Windows only' -ForegroundColor DarkGray }
            else { Write-Host (Format-Size $before) -ForegroundColor White }
            continue
        }

        $stopped = @()
        foreach ($svc in @($t.Services)) {
            if (-not $svc) { continue }
            $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
            if ($s -and $s.Status -eq 'Running') {
                Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
                $stopped += $svc
            }
        }

        if ($t.Clean) { & $t.Clean }
        Remove-TargetContent $t

        foreach ($svc in $stopped) { Start-Service -Name $svc -ErrorAction SilentlyContinue }

        $after = Get-TargetSize $t
        $freed = [Math]::Max(0, $before - $after)
        $totalFreed += $freed
        Write-Host ('freed ' + (Format-Size $freed)) -ForegroundColor Green
        Write-Log ("{0}: freed {1}" -f $t.Name, (Format-Size $freed))
    }

    Write-Host ''
    Write-Host '  ------------------------------------------------------------------'
    if ($ScanOnly) {
        Write-Host ('  Space that can be freed: ' + (Format-Size $totalFound)) -ForegroundColor Cyan
    } else {
        $freeAfter = Get-FreeSpace
        Write-Host ('  Total cleaned:           ' + (Format-Size $totalFreed)) -ForegroundColor Green
        Write-Host ('  Free space on {0}  before: {1}   now: {2}' -f $env:SystemDrive,
                    (Format-Size $freeBefore), (Format-Size $freeAfter)) -ForegroundColor Green
        Write-Host '  Some files may remain because a program is still using them.' -ForegroundColor DarkGray
        Write-Log ("Total freed {0}. Free space now {1}" -f (Format-Size $totalFreed), (Format-Size $freeAfter))
    }
    Write-Host ''
}

function Show-Header {
    Clear-Host
    Write-Host ''
    Write-Host '  ==================================================================' -ForegroundColor Cyan
    Write-Host '                         DISK CLEANER                               ' -ForegroundColor Cyan
    Write-Host '  ==================================================================' -ForegroundColor Cyan
    Write-Host ('  Free space on {0}  {1}' -f $env:SystemDrive, (Format-Size (Get-FreeSpace)))
    if ($IsAdmin) { Write-Host '  Running as administrator - all items can be cleaned.' -ForegroundColor Green }
    else          { Write-Host '  Not running as administrator - Windows folders will be skipped.' -ForegroundColor Yellow }
    Write-Host ''
}

if ($Mode) {
    Write-Log "Started with -Mode $Mode"
    switch ($Mode) {
        'Scan'  { Invoke-Cleanup -Level 3 -ScanOnly }
        'Quick' { Invoke-Cleanup -Level 1 }
        'Full'  { Invoke-Cleanup -Level 2 }
        'Deep'  { Invoke-Cleanup -Level 3 }
    }
    return
}

while ($true) {
    Show-Header
    Write-Host '   1  Scan       - see how much space can be freed (deletes nothing)'
    Write-Host '   2  Quick      - temp files + empty the Recycle Bin'
    Write-Host '   3  Full       - Quick + update cache, browser caches, error reports'
    Write-Host '   4  Deep       - Full + remove old Windows update files (slow)'
    Write-Host '   Q  Quit'
    Write-Host ''
    Write-Host '  Tip: close your browsers first so their caches can be cleaned.' -ForegroundColor DarkGray
    Write-Host ''
    $choice = (Read-Host '  Choose an option').Trim().ToUpperInvariant()

    $level = 0
    switch ($choice) {
        '1' { Invoke-Cleanup -Level 3 -ScanOnly }
        '2' { $level = 1 }
        '3' { $level = 2 }
        '4' { $level = 3 }
        'Q' { return }
        default { $level = -1 }
    }
    if ($level -lt 0) { continue }

    if ($level -gt 0) {
        Write-Host ''
        Write-Host '  The Recycle Bin will be emptied. Deleted files cannot be recovered.' -ForegroundColor Yellow
        $ok = (Read-Host '  Continue? (Y/N)').Trim().ToUpperInvariant()
        if ($ok -eq 'Y') {
            Write-Log "Started cleanup level $level"
            Invoke-Cleanup -Level $level
        }
    }
    Read-Host '  Press Enter to go back to the menu' | Out-Null
}
