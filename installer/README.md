# Windows installer

Run **Wii-Balance-Board-ADS-Setup.exe** and approve the Windows UAC prompt. The installer places the application in Program Files and creates a scheduled task that starts it at sign-in with administrator privileges. The installer starts the app after setup finishes.

The install preserves the user's saved settings and calibration. The app requires Windows 10 or Windows 11 and the **.NET 8 Desktop Runtime**.

To build the installer from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

Questions or feedback? [Contact ASLI on Instagram @as_li.3](https://www.instagram.com/as_li.3/).
