Device Monitor 1.0
==================

Start it from the Start menu or the desktop icon "Device Monitor".

1. Click "Add device" and enter a name and the IP address (for example 192.168.88.1 for a MikroTik).
   Use "Test ping" to check it answers. Or import many at once: Devices > Import (see devices-example.csv).
2. Choose how often to ping at the top right ("Ping every"): pick 5 s, 10 s, 15 s, 30 s, 1 min ... from
   the list, or type any value (for example 45 or "2 min"). Each device can also have its own interval.
3. When a device stops answering, a red pop-up appears in the bottom-right corner of the screen;
   when it answers again, a green pop-up tells you how long it was off.

Log files: Documents\Device Monitor\Logs
  - events.csv                    every ON / OFF event (opens in Excel)
  - DeviceMonitor-YYYY-MM-DD.log  a readable text log, one file per day

Closing the window keeps monitoring in the background (icon next to the clock).
Right-click that icon > Exit to stop the program.

To remove the program: Settings > Apps > Device Monitor > Uninstall.
Your device list and log files are kept.
