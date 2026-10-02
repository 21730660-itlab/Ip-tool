IP Monitor for Windows
======================

INSTALL (no admin rights needed)
1. Unzip this whole folder (right-click > Extract All). Do not run it from inside the zip.
2. Double-click  Install-IP-Monitor.cmd
   If Windows SmartScreen shows "Windows protected your PC": press More info > Run anyway.
3. Answer the two questions (WinBox links, open now).

You get:
- "IP Monitor" in the Start menu and on the Desktop, with its own icon
- the app in its own window (no tabs, no address bar); pin it to the taskbar if you like
- your database in  Documents\IP Monitor\ip-monitor-db.json
- an entry in Settings > Apps > Installed apps, to uninstall it

FIRST START
Press "Open database file" and choose  Documents\IP Monitor\ip-monitor-db.json,
then allow editing. From then on every change is saved into that file automatically.
After restarting the PC the first click in the app reconnects the file.

UPDATE TO A NEW VERSION
Run the new Install-IP-Monitor.cmd. Your database is never overwritten.

UNINSTALL
Settings > Apps > Installed apps > IP Monitor > Uninstall.
Your database in Documents\IP Monitor is kept.

HOW IT WORKS
The app window is run by Microsoft Edge, which is part of Windows 10 and 11
(Google Chrome is used if Edge is missing). Nothing is sent to the internet;
fonts are loaded from Google Fonts when online, otherwise Windows fonts are used.
