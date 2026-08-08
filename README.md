# ResSwitcher

A minimal, zero-dependency utility for Windows to manage monitor resolutions and refresh rates.

Designed for setups with high-refresh-rate OLEDs and multiple monitors where legacy display scaling tools often fail.

## Features

* **Interactive Setup Wizard:** Double-click the executable to auto-detect connected monitors and generate pre-configured desktop shortcuts.
* **Headless Background Execution:** Compiled as a `WinExe`. Shortcuts execute with zero console window pop-ups or terminal flashes.
* **Registry Persistence:** Uses native `CDS_UPDATEREGISTRY` flags so display modes persist across reboots and display sleep cycles.
* **Zero Bloat:** No background services, no registry modifications on install, and no config files.
* **Fractional Refresh Rates:** Supports advanced hardware configurations without crashing.

## How to Use

1. Download the executable or build it from source.
2. Double-click `ResSwitcher.exe`.
3. The setup wizard will detect your monitors. Select the monitor you want to configure by typing its number.
4. Enter your desired resolution and refresh rate (e.g., `1920 1080 240`).
5. Name your shortcut (e.g., `CS2 Mode`).
6. A shortcut will appear on your desktop. Double-clicking this shortcut will apply the settings instantly and silently.

## Advanced (Command Line)

If you prefer to bypass the setup wizard, you can call the executable directly via the command line or from custom scripts:

```cmd
ResSwitcher.exe -d \.\DISPLAY2 -w 1920 -h 1080 -r 240
```

Arguments:
* `-d, --device` : Display device name (default: `\.\DISPLAY1`)
* `-w, --width`  : Target screen width in pixels
* `-h, --height` : Target screen height in pixels
* `-r, --refresh`: Target refresh rate in Hz

## Uninstalling

ResSwitcher is fully portable. To uninstall, simply delete `ResSwitcher.exe` and any shortcuts you created.
