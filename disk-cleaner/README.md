# Disk Cleaner (Windows)

Frees disk space by deleting temporary files, caches and the Recycle Bin.

## How to use

1. Copy the `disk-cleaner` folder to the Windows PC.
2. Double-click **`Disk-Cleaner.bat`** and click **Yes** when Windows asks for admin rights.
3. Pick an option from the menu:

| Option | What it cleans |
|---|---|
| **1 Scan** | Nothing. It only shows how much space can be freed. |
| **2 Quick** | Your temp files, the Windows temp folder, the Recycle Bin |
| **3 Full** | Quick, plus the Windows Update download cache, Delivery Optimization cache, thumbnail cache, web cache, error reports and crash dumps, graphics shader cache, and the Chrome, Edge and Firefox caches |
| **4 Deep** | Full, plus a DISM component cleanup that removes old Windows update files (slow) |

Close your browsers first so their caches can be cleaned.

## Safety

- Documents, downloads, passwords, cookies and browsing history are **never** touched.
- Files that a running program is using are skipped, not forced.
- A built-in guard refuses to empty system folders such as `C:\`, `C:\Windows` and `Program Files`.
- Emptying the Recycle Bin is permanent, so the tool asks before it cleans.
- Each run is logged to `cleanup-log.txt` next to the script.

## Run without the menu (for example from Task Scheduler)

```
powershell -ExecutionPolicy Bypass -File "C:\path\to\Disk-Cleaner.ps1" -Mode Full
```

`-Mode` can be `Scan`, `Quick`, `Full` or `Deep`. Run it as administrator to clean the Windows folders too.
