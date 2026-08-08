# ResSwitcher9000

ResSwitcher9000 is a free, open-source Windows utility that creates desktop shortcuts for changing a selected monitor's resolution and refresh rate.

It supports Windows 10 and Windows 11 on x64 PCs. It lists Windows-reported display modes, tests a selected mode before creating a shortcut, and does not choose a default resolution or refresh rate.

## Platform

- Windows 10 and Windows 11, x64.
- The release build is `win-x64`.
- No installer, background service, or configuration file.
- The release executable is self-contained.

## What It Does

ResSwitcher9000:

1. Finds active Windows display devices.
2. Lets you select a display.
3. Shows resolutions and refresh rates reported by Windows.
4. Tests the selected mode before creating a shortcut.
5. Creates a desktop shortcut for that mode.

It does not choose a default resolution or refresh rate.

## Wizard

Double-click:

```text
ResSwitcher9000.exe
```

The wizard will:

1. List active displays.
2. Let you select a display.
3. List Windows-reported resolutions.
4. List refresh rates for the selected resolution.
5. Create a desktop shortcut.
6. Let you create another shortcut or exit.

Choose `0` in the resolution list to manually enter width, height, and refresh rate.

Choose `0` in the refresh-rate list to manually enter a refresh rate for the selected resolution.

Example only:

```text
1920 1080 60
```

Shortcuts created by the wizard apply the mode without requesting permanent profile persistence.

## Command Line

PowerShell requires `.\` before an executable in the current folder.

List active displays:

```powershell
.\ResSwitcher9000.exe --list
```

Apply a display mode:

```powershell
.\ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 1920 --height 1080 --refresh 60
```

Apply a mode and ask Windows to save it in the current user display profile:

```powershell
.\ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 1920 --height 1080 --refresh 60 --persist
```

Show success or error output:

```powershell
.\ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 1920 --height 1080 --refresh 60 --verbose
```

Show help:

```powershell
.\ResSwitcher9000.exe --help
```
