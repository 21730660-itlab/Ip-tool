# IP Monitor for Windows (native C# app)

A native Windows version of IP Monitor (C#, .NET 8, WPF). It uses the **same database file**
(`ip-monitor-db.json`) and the **same accounts** as the web version, so both can be used on the same data.

## Phase 1 (this version)
- Sign in / create account (same pre-shared key and passwords as the web version)
- Open or create the database file; every change is saved to it automatically
- If another program (or the web version) changes the file, the app warns you instead of overwriting it
- Dashboard, Sites, IPs & networks (with hosts, next free IP), Devices (MikroTik ports, bridges, IP addresses,
  wireless; other brands), Connections (wired / wireless with link subnet), VLANs, History, Users and permissions
- Device IP addresses are listed as hosts of their network automatically; a new prefix creates its network
- Ping hosts and devices, open a MikroTik in WinBox, open a device's web page
- Backup copy, restore, CSV export for Excel (networks & hosts, devices, connections, devices with passwords)
- Light and dark mode

Later phases: site map, PDF / Excel reports, configuration backups, subnet calculator.

## Layout
- `src/IpMonitor.Core` — data model, IP maths, accounts, all rules (no Windows code, testable anywhere)
- `src/IpMonitor.App` — the Windows app (WPF, windows built in code)
- `tests/IpMonitor.Check` — automatic checks: `dotnet run --project tests/IpMonitor.Check [ip-monitor-db.json]`

## Build the .exe
```
dotnet publish src/IpMonitor.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o out
```
The result is one file, `out/IP-Monitor.exe`, that runs on Windows 10 and 11 without installing .NET.
Preferences (last database file, theme, WinBox location) are kept in `%APPDATA%\IPMonitor\settings.json`.

## Build the installer (IP-Monitor-Setup.exe)
Needs NSIS 3 (`makensis`, also available on Linux).
```
dotnet publish src/IpMonitor.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o out-fdd
mkdir out-setup && cd installer && makensis IP-Monitor-Setup.nsi
```
`out-setup/IP-Monitor-Setup.exe` (about 0.35 MB) installs for the current user without administrator rights
(`%LOCALAPPDATA%\Programs\IP Monitor`), adds Start menu and desktop shortcuts and an entry in
Settings > Apps, and downloads and installs the .NET 8 Desktop Runtime when it is missing.
The uninstaller never deletes the database file. `makensis -DOFFLINE` builds a large setup with .NET built in
(from the self-contained build in `out`).
