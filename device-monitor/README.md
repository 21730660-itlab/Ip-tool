# Device Monitor for Windows

Watches MikroTik routers and any other device by IP address. It pings each device on the interval you choose,
shows a pop-up when a device turns OFF and another one when it comes back ON, and saves every event in log files.

## Download
`release/Device-Monitor-Setup.exe` is the small installer. It downloads the .NET 8 Desktop Runtime if it is missing.
Build `Device-Monitor-Setup-Full.exe` (.NET built in) with the steps below.

## Features (version 1.0)
- Ping interval drop-down (5 s, 10 s, 15 s, 30 s, 1, 2, 5, 10 min), or type any value ("45", "90 s", "2 min").
  Each device can also have its own interval.
- A device counts as OFF after N missed pings in a row (default 2). A missed ping is checked again after 2 s.
- Red pop-up when a device is OFF, green pop-up when it is back ON (with how long it was off). Optional sound.
- Logs in `Documents\Device Monitor\Logs`: `events.csv` (opens in Excel) and a text file for each day.
- Dashboard: key figures, status ring, slowest devices, latest events, and a live card for each device with a mini graph.
- Device details: reply-time graph (15 min / 1 h / 6 h) with a mouse read-out, ON/OFF timeline, uptime, loss, min/avg/max.
- Event log page: filters, events-over-time chart, devices with the most outages.
- Devices table: add, edit, delete, pause, check now, CSV import/export, continuous ping window, WinBox/web.
- Runs in the system tray when you close the window. Optional start with Windows. Dark and light themes.
- Scales to hundreds of devices: one scheduler and up to 64 pings at a time.

## Layout
- `src/DeviceMonitor.Core`: models, monitor engine, ping probe (`IProbe`, so you can add TCP/HTTP/SNMP checks later), storage, logs. No Windows-only code.
- `src/DeviceMonitor.App`: the WPF app. The windows are built in code.
- `tests/DeviceMonitor.Check`: automatic checks. Run `dotnet run --project tests/DeviceMonitor.Check`.

## Build
```
dotnet publish src/DeviceMonitor.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o out-fdd
dotnet publish src/DeviceMonitor.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o out
mkdir out-setup && cd installer && makensis Device-Monitor-Setup.nsi && makensis -DOFFLINE Device-Monitor-Setup.nsi
```
The setup installs for the current user without admin rights. Uninstalling keeps your device list and logs.
