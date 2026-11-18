# MaxwellBalance 0.1b

A small Windows tray application that displays the Audeze Maxwell 2 game/chat balance when you turn its wheel.

## Use

Extract the release ZIP into a writable folder and run `MaxwellBalance.exe`. No driver installation or administrator rights are required. The program starts quietly; turn the game/chat wheel to display the indicator. Double-click the tray icon to show it manually, or right-click for **Zichtbaar houden** (keep visible) and **Afsluiten** (exit). The interface is in Dutch.

The indicator appears at the bottom center of the primary screen for four seconds. Two consecutive readings must confirm a change. Startup, reconnection and temporary failed reads do not trigger a popup. Values represent headset balance steps, not measured audio percentages.

To start automatically with Windows, create a shortcut to the extracted executable in your user's Startup folder (`shell:startup`). This runs in your desktop session, not as a Windows service. Remove that shortcut to disable automatic startup. Exit via the tray before deleting the extracted folder.

## Compatibility and limitations

- Windows x64 with .NET Framework 4.x; no external runtime packages.
- Tested with Maxwell 2 through a direct USB connection. The user confirmed the indicator follows the wheel.
- Wireless dongle support is implemented but **not yet verified working**. The simultaneously connected dongle did not return a valid response during testing.
- Only Maxwell 2 product IDs 4B28, 4B29, 4B2E and 4B2A are recognized. Not all variants have been tested.
- The visible indicator reasserts its topmost position without taking keyboard focus. It is **not guaranteed to appear over exclusive fullscreen games** and does not inject into games.
- Close the application before a headset firmware update. Other headset-control applications may compete for responses.
- This beta is unsigned and unofficial; it is not affiliated with Audeze.

The application only requests the current game/chat balance. It does not change headset settings, audio routing, firmware, or system volume. No audio is captured and no network requests are made.

## Build and test

Run `build.ps1` in Windows PowerShell. It uses the .NET Framework compiler already supplied with Windows, compiles the executable, and runs the included checks.

`MaxwellBalance.exe --test` checks response validation and popup change detection, writing `test-result.txt` beside the executable. `MaxwellBalance.exe --probe` writes a one-time device diagnostic to `probe-result.txt`; exit the tray application before running the probe.

Protocol reference: [audezeforlinux protocol documentation](https://github.com/junaydirfan/audezeforlinux/blob/master/docs/PROTOCOL.md). This implementation uses only the balance read request (0x2C83 / 0x0B), not setting-write commands.
