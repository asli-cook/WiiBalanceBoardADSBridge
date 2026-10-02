# Installer files

`Build-Installer.ps1` publishes the Windows app and packages it with IExpress into `dist/Wii Balance Board ADS Setup.exe`.

`Install.ps1` requests elevation during installation, copies the app to Program Files, and registers a per-user logon task with highest privileges. It preserves calibration and preferences in Local AppData. The installed app requires the .NET 8 Desktop Runtime.
