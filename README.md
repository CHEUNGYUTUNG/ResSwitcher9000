# ResSwitcher9000

A small Windows utility for creating shortcuts that switch one display to a chosen resolution and refresh rate.

## Platform

- Windows 10 and Windows 11, x64.
- The release build is `win-x64`.
- No installer, background service, or configuration file.
- The release executable is self-contained: users do not need to install .NET.

## What It Does

ResSwitcher9000:

1. Finds active Windows display devices.
2. Lets you select one display.
3. Lets you enter a resolution and refresh rate.
4. Tests the requested mode before applying it.
5. Creates a desktop shortcut for that mode.

It does not choose a default resolution or refresh rate.

A mode is only applied if Windows and the current GPU driver accept it.

## Use the Wizard

Double-click:

```text
ResSwitcher9000.exe
```

Then:

1. Select a display.
2. Enter width, height, and refresh rate.

Example only:

```text
2560 1440 144
```

3. Enter a shortcut name.
4. Open the generated desktop shortcut whenever you want to apply that mode.

The wizard-generated shortcut does not request persistent Windows profile changes.

## Command Line

List active display devices:

```bat
ResSwitcher9000.exe --list
```

Apply a mode:

```bat
ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 2560 --height 1440 --refresh 144
```

Apply a mode and ask Windows to save it in the current user display profile:

```bat
ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 2560 --height 1440 --refresh 144 --persist
```

Show success or error output:

```bat
ResSwitcher9000.exe --device "\\.\DISPLAY1" --width 2560 --height 1440 --refresh 144 --verbose
```

Show help:

```bat
ResSwitcher9000.exe --help
```

## Arguments

| Argument | Description |
|---|---|
| `-d`, `--device` | Display device name from `--list` |
| `-w`, `--width` | Width in physical pixels |
| `-h`, `--height` | Height in physical pixels |
| `-r`, `--refresh` | Refresh rate in whole Hz |
| `--persist` | Ask Windows to save the mode in the current user profile |
| `--verbose` | Show output for troubleshooting |
| `--list` | List active display devices |
| `--help`, `-?` | Show command help |

## Build From Source

Install the .NET 8 SDK, then run:

```bat
build.bat
```

The release executable will be created here:

```text
publish\win-x64\ResSwitcher9000.exe
```

## Notes

- Display device names can change after reconnecting monitors, changing docks, or reinstalling GPU drivers.
- Run `ResSwitcher9000.exe --list` again if an old shortcut stops working.
- Resolution values are physical pixels, not Windows scaling percentages.
- The tool changes resolution and refresh rate only.
- It does not configure HDR, Windows scaling, monitor inputs, color profiles, or GPU-control-panel settings.

## Uninstall

Delete:

```text
ResSwitcher9000.exe
```

and any shortcuts created by the wizard.