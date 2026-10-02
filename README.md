# Wii Balance Board ADS Bridge

**Contact: [Instagram @as_li.3](https://www.instagram.com/as_li.3/)**

Wii Balance Board ADS Bridge connects a **Nintendo Wii Balance Board** to Windows over Bluetooth and turns a detected step into a held **Numpad 1** key. When you step off, the app releases the key. Bind Numpad 1 to ADS or another action in your game.

The app shows live pressure, lets you calibrate the board, and saves the calibration and settings. Its interface includes the **Made by ASLI** label.

## Features

- Searches for the board when the app starts and keeps searching until you wake it with the front button.
- Reconnects automatically if the board disconnects.
- Uses the saved Bluetooth pairing after the first pairing. Initial pairing may require pressing the red **SYNC** button inside the battery cover.
- Saves calibration, pressure threshold, and Bluetooth adapter selection.
- Holds Numpad 1 while you are standing on the board and releases it when you step off.
- Does not require vJoy and does not send mouse clicks or controller buttons.
- Includes a Windows installer that configures the app to run at sign-in with administrator privileges, after you approve UAC during installation.

## Installation

Requirements: Windows 10 or Windows 11 and the **.NET 8 Desktop Runtime**.

1. Download and run **Wii Balance Board ADS Setup.exe** from the [latest release](https://github.com/asli-cook/WiiBalanceBoardADSBridge/releases/latest).
2. Approve the UAC prompt. The installer copies the app to Program Files and creates a scheduled task to run it at sign-in. Your saved settings and calibration are preserved.
3. Wake the board with its front button. The app searches and connects automatically.
4. On first use, leave the board empty for five seconds to calibrate it. The app saves the calibration for later sessions.

## Configure a game

Bind **Numpad 1** to ADS or the desired action in the game's settings. First, verify the key in Notepad or a keyboard tester. Some games may ignore simulated keyboard input even when it works in ordinary Windows apps; behavior depends on the game and its settings.

## Build from source

Requirements: Windows and the .NET 8 SDK.

```powershell
dotnet publish .\BoardADSBridge.csproj -c Release -o .\publish
```

To build the Windows installer:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

The installer build uses IExpress, which is included with Windows. The app requires the .NET 8 Desktop Runtime on the target computer.

## Privacy and licensing

Calibration and settings are stored locally in %LOCALAPPDATA%\BalanceBoardADS\settings.json. The project does not upload data or connect to an internet service.

No reuse license has been added to this project. Review the Windows and .NET component licenses before redistribution.

## Contact

Questions or feedback? [Contact ASLI on Instagram @as_li.3](https://www.instagram.com/as_li.3/).
