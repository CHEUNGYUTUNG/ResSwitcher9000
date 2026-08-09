# ResSwitcher9000 — Windows Resolution & Refresh-Rate Shortcuts

A free, open-source Windows utility for creating desktop shortcuts that switch a selected monitor's resolution and refresh rate.

Each shortcut uses a **15-second safe confirmation**: press `1` to keep the new display mode, press `0` to revert immediately, or wait for automatic rollback.

- Windows 10 and Windows 11
- x64 only
- No installer or always-running background service
- Self-contained executable

## Download

Download `ResSwitcher9000.exe` from the repository's **Releases** page.

Run the executable to open the shortcut-creation wizard.

## Create a Display Shortcut

1. Open `ResSwitcher9000.exe`.
2. Choose an active display.
3. Choose a resolution.
4. Choose a refresh rate.
5. Enter a shortcut name.
6. Double-click the new shortcut on your desktop.

Before creating a shortcut, ResSwitcher9000 asks Windows to test whether the selected display mode is supported.

## Safe Confirmation

When you open a shortcut, ResSwitcher9000 applies the selected mode and starts a 15-second countdown.

- Press `1` to keep the new mode.
- Press `0` to revert immediately.
- Do nothing to automatically restore the previous mode.

This helps prevent getting stuck with a black screen or unsupported display mode.

## Command Line

PowerShell requires `.\` before an executable in the current directory.

List active displays:

```powershell
.\ResSwitcher9000.exe --list
```

Apply a display mode:

```powershell
.\ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 1920 --height 1080 --refresh 60
```

Apply a mode and save it to the current Windows user display profile:

```powershell
.\ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 1920 --height 1080 --refresh 60 --persist
```

Apply a mode with the safe 15-second confirmation:

```powershell
.\ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 1920 --height 1080 --refresh 60 --confirm
```

Show detailed output:

```powershell
.\ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 1920 --height 1080 --refresh 60 --verbose
```

Show help:

```powershell
.\ResSwitcher9000.exe --help
```

## Notes

- ResSwitcher9000 lists display modes reported by Windows.
- A manually entered resolution or refresh rate is tested before it is applied.
- The tool does not automatically choose a “best” resolution or refresh rate.
- Shortcuts created by the wizard use safe confirmation mode and do not permanently save the display mode.
