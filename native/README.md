# IP Monitor for Windows (native C# app)

A native Windows version of IP Monitor (C#, .NET 8, WPF). It uses the **same database file**
(`ip-monitor-db.json`) and the **same accounts** as the web version, so both can be used on the same data.

## Phase 1 (this version)
- Sign in / create account (same pre-shared key and passwords as the web version)
- Open or create the database file; every change is saved to it automatically
- If another program (or the web version) changes the file, the app warns you instead of overwriting it
- Dashboard like the web version: health score, 20 health checks (critical / warning / info) with the items
  behind each one, quick find, key figures, busiest subnets, weakest wireless links, inventory, address space,
  sites and recent activity
- Live monitoring: every device with an IP is pinged (every 30 s to 10 min); a pop-up (with sound) appears in the
  corner of the screen when a device stops answering and when it is back; UP / DOWN badges on the site map;
  every event is written to an up/down log (CSV, opens in Excel) next to the database: <database>-uptime-log.csv
- Wireless connections: frequency (MHz), band (2.4 / 5 / 60 GHz) and distance (km) are typed in on each
  wireless connection; the station / access point role and IP stay on each device
- MikroTik model: a searchable list of about 100 MikroTik models (type “sq”, “lhg”, “4011”…); picking one fills in
  the device type and its ports
- History can be exported as a PDF file (the current site and search filter are applied)
- Copy a device's username or password (device form, Devices page, site map); a copied password is removed
  from the clipboard after 60 seconds, and read-only users cannot copy passwords
- Passwords can be shown or hidden (eye button)
- Site map: click a device for WinBox, its web page, ping, config backups or edit (right-click menu, double-click edits)
- Site map: every site with its devices, cables and wireless links; drag to arrange (positions shared with the web
  version), zoom and pan, and “Add connection” → click the first device → click the second
- Dashboard, Sites, IPs & networks (with hosts, next free IP), Devices (MikroTik ports, bridges, IP addresses,
  wireless; other brands), Connections (wired / wireless with link subnet), VLANs, History, Users and permissions
- Device IP addresses are listed as hosts of their network automatically; a new prefix creates its network
- Ping hosts and devices, open a MikroTik in WinBox, open a device's web page
- Backup copy, restore, CSV export for Excel (networks & hosts, devices, connections, devices with passwords)
- Light and dark mode

- Config backups: add a device's RouterOS export (.rsc file, dropped file or pasted /export), passwords hidden,
  view each version, see what changed between versions, save as .rsc again (same storage as the web version)
- Subnet calculator: IPv4/IPv6 analysis, bit map, check against all sites, size for hosts, subnet splitter
- Reports: PDF report (summary, and per site its map picture, networks, hosts, devices, connections, VLANs,
  config backups) and an Excel workbook (.xlsx) with one sheet per list — both written without extra libraries

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
