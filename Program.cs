using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ResSwitcher9000;

internal static class Program
{
    private const uint EnumCurrentSettings = 0xFFFFFFFF;

    private const uint DisplayDeviceAttached = 0x00000001;
    private const uint DisplayDevicePrimary = 0x00000004;

    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint DmDisplayFrequency = 0x00400000;

    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsTest = 0x00000002;

    private const int DispSuccess = 0;
    private const int DispRestart = 1;
    private const int DispFailed = -1;
    private const int DispBadMode = -2;
    private const int DispNotUpdated = -3;
    private const int DispBadFlags = -4;
    private const int DispBadParam = -5;

    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int ErrorAccessDenied = 5;

    private const int ExitSuccess = 0;
    private const int ExitRestart = 1;
    private const int ExitInvalidArguments = 2;
    private const int ExitDisplayError = 3;
    private const int ExitUnsupportedMode = 4;
    private const int ExitApplyError = 5;
    private const int ExitShortcutError = 6;
    private const int ExitUnexpectedError = 7;

    private static int Main(string[] args)
    {
        // Internal hidden rollback helper.
        if (args.Length > 0 && args[0] == "--watch")
        {
            return RunWatchdog(args);
        }

        if (args.Length == 0)
        {
            return RunWithConsole(RunWizard);
        }

        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-?"))
        {
            return RunWithConsole(() =>
            {
                PrintHelp();
                return ExitSuccess;
            });
        }

        if (args.Length == 1 && args[0] == "--list")
        {
            return RunWithConsole(ListDisplays);
        }

        if (!TryParseOptions(args, out Options options, out string error))
        {
            if (args.Contains("--verbose"))
            {
                return RunWithConsole(() =>
                {
                    Console.WriteLine(error);
                    return ExitInvalidArguments;
                });
            }

            return ExitInvalidArguments;
        }

        if (options.Verbose || options.Confirm)
        {
            return RunWithConsole(() =>
            {
                int result = ApplyMode(options, out string message);
                Console.WriteLine(message);
                return result;
            });
        }

        // Normal command-line mode remains silent.
        return ApplyMode(options, out _);
    }

    private static bool TryParseOptions(
        string[] args,
        out Options options,
        out string error)
    {
        options = new Options();
        error = string.Empty;

        bool hasDevice = false;
        bool hasWidth = false;
        bool hasHeight = false;
        bool hasRefresh = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-d":
                case "--device":
                {
                    if (hasDevice ||
                        !TryValue(args, ref i, out string device, out error))
                    {
                        if (hasDevice)
                        {
                            error = "Display device was specified more than once.";
                        }

                        return false;
                    }

                    options.DeviceName = device;
                    hasDevice = true;
                    break;
                }

                case "-w":
                case "--width":
                {
                    if (hasWidth ||
                        !TryNumber(
                            args,
                            ref i,
                            "Width",
                            out int width,
                            out error))
                    {
                        if (hasWidth)
                        {
                            error = "Width was specified more than once.";
                        }

                        return false;
                    }

                    options.Width = width;
                    hasWidth = true;
                    break;
                }

                case "-h":
                case "--height":
                {
                    if (hasHeight ||
                        !TryNumber(
                            args,
                            ref i,
                            "Height",
                            out int height,
                            out error))
                    {
                        if (hasHeight)
                        {
                            error = "Height was specified more than once.";
                        }

                        return false;
                    }

                    options.Height = height;
                    hasHeight = true;
                    break;
                }

                case "-r":
                case "--refresh":
                {
                    if (hasRefresh ||
                        !TryNumber(
                            args,
                            ref i,
                            "Refresh rate",
                            out int refresh,
                            out error))
                    {
                        if (hasRefresh)
                        {
                            error = "Refresh rate was specified more than once.";
                        }

                        return false;
                    }

                    options.RefreshRate = refresh;
                    hasRefresh = true;
                    break;
                }

                case "--persist":
                    options.Persist = true;
                    break;

                case "--confirm":
                    options.Confirm = true;
                    break;

                case "--verbose":
                    options.Verbose = true;
                    break;

                default:
                    error = $"Unknown option: {args[i]}";
                    return false;
            }
        }

        if (!hasDevice || !hasWidth || !hasHeight || !hasRefresh)
        {
            error = "Required: --device, --width, --height, and --refresh.";
            return false;
        }

        return true;
    }

    private static bool TryValue(
        string[] args,
        ref int index,
        out string value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            error = "Option requires a value.";
            return false;
        }

        value = args[index].Trim();
        return true;
    }

    private static bool TryNumber(
        string[] args,
        ref int index,
        string name,
        out int value,
        out string error)
    {
        value = 0;
        error = string.Empty;

        if (!TryValue(args, ref index, out string text, out error))
        {
            return false;
        }

        if (!int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value) ||
            value <= 0)
        {
            error = $"{name} must be a positive whole number.";
            return false;
        }

        return true;
    }

    private static int ApplyMode(Options options, out string message)
    {
        int prepareResult = PrepareMode(
            options,
            out Mode previous,
            out DEVMODEW requested,
            out message);

        if (prepareResult != ExitSuccess)
        {
            return prepareResult;
        }

        if (!options.Confirm)
        {
            int result = Change(
                options.DeviceName,
                ref requested,
                options.Persist ? CdsUpdateRegistry : 0);

            message = result == DispSuccess
                ? options.Persist
                    ? "Display mode applied and saved."
                    : "Display mode applied."
                : $"Windows could not apply the mode: {Describe(result)}";

            return ToExitCode(result);
        }

        string eventName = $"ResSwitcher9000-{Guid.NewGuid():N}";

        using EventWaitHandle keepEvent = new(
            false,
            EventResetMode.ManualReset,
            eventName);

        int applyResult = Change(
            options.DeviceName,
            ref requested,
            0);

        if (applyResult != DispSuccess)
        {
            message = $"Windows could not apply the mode: {Describe(applyResult)}";
            return ToExitCode(applyResult);
        }

        if (!StartWatchdog(eventName, options.DeviceName, previous))
        {
            RestoreMode(options.DeviceName, previous, out _);
            message = "Could not start the rollback helper. Original mode restored.";
            return ExitApplyError;
        }

        bool keep = AskToKeepMode();

        keepEvent.Set();

        if (!keep)
        {
            int restoreResult = RestoreMode(
                options.DeviceName,
                previous,
                out string restoreMessage);

            message = restoreResult == ExitSuccess
                ? "Original mode restored."
                : restoreMessage;

            return restoreResult;
        }

        if (!options.Persist)
        {
            message = "Mode kept for this session.";
            return ExitSuccess;
        }

        int persistResult = Change(
            options.DeviceName,
            ref requested,
            CdsUpdateRegistry);

        message = persistResult == DispSuccess
            ? "Mode kept and saved."
            : $"Mode kept, but could not save it: {Describe(persistResult)}";

        return ToExitCode(persistResult);
    }

    private static int PrepareMode(
        Options options,
        out Mode previous,
        out DEVMODEW requested,
        out string message)
    {
        previous = default;
        requested = default;
        message = string.Empty;

        if (FindDisplay(options.DeviceName) is null)
        {
            message =
                $"'{options.DeviceName}' is not an active display. Run --list first.";

            return ExitDisplayError;
        }

        if (!TryGetCurrentMode(options.DeviceName, out DEVMODEW current))
        {
            message = $"Could not read current mode for '{options.DeviceName}'.";
            return ExitDisplayError;
        }

        previous = ToMode(current);
        requested = current;

        requested.DmPelsWidth = (uint)options.Width;
        requested.DmPelsHeight = (uint)options.Height;
        requested.DmDisplayFrequency = (uint)options.RefreshRate;
        requested.DmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

        int test = Change(
            options.DeviceName,
            ref requested,
            CdsTest);

        if (test != DispSuccess)
        {
            message =
                $"Windows rejected {options.Width}x{options.Height} @ " +
                $"{options.RefreshRate} Hz: {Describe(test)}";

            return ExitUnsupportedMode;
        }

        message = "Mode supported.";
        return ExitSuccess;
    }

    private static int RestoreMode(
        string deviceName,
        Mode previous,
        out string message)
    {
        message = string.Empty;

        if (!TryGetCurrentMode(deviceName, out DEVMODEW mode))
        {
            message = "Could not read the display mode to restore it.";
            return ExitDisplayError;
        }

        mode.DmPelsWidth = (uint)previous.Width;
        mode.DmPelsHeight = (uint)previous.Height;
        mode.DmDisplayFrequency = (uint)previous.RefreshRate;
        mode.DmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

        int result = Change(deviceName, ref mode, 0);

        if (result != DispSuccess)
        {
            message = $"Could not restore the old mode: {Describe(result)}";
        }

        return ToExitCode(result);
    }

    private static bool AskToKeepMode()
    {
        DateTime end = DateTime.UtcNow.AddSeconds(15);

        Console.WriteLine();
        Console.WriteLine("Press 1 to keep this mode.");
        Console.WriteLine("Press 0 to revert now.");
        Console.WriteLine("Any other key is ignored.");
        Console.WriteLine();

        while (DateTime.UtcNow < end)
        {
            int seconds = Math.Max(
                1,
                (int)Math.Ceiling((end - DateTime.UtcNow).TotalSeconds));

            Console.Write(
                $"\rKeep this mode? [1] Keep  [0] Revert  Auto-revert: {seconds,2}s ");

            if (Console.KeyAvailable)
            {
                char key = Console.ReadKey(intercept: true).KeyChar;

                if (key == '1')
                {
                    Console.WriteLine();
                    return true;
                }

                if (key == '0')
                {
                    Console.WriteLine();
                    return false;
                }
            }

            Thread.Sleep(100);
        }

        Console.WriteLine();
        Console.WriteLine("Time expired. Reverting...");
        return false;
    }

    private static bool StartWatchdog(
        string eventName,
        string deviceName,
        Mode previous)
    {
        try
        {
            string exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not find executable.");

            ProcessStartInfo start = new(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            start.ArgumentList.Add("--watch");
            start.ArgumentList.Add(eventName);
            start.ArgumentList.Add(deviceName);
            start.ArgumentList.Add(previous.Width.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(previous.Height.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(previous.RefreshRate.ToString(CultureInfo.InvariantCulture));

            return Process.Start(start) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static int RunWatchdog(string[] args)
    {
        if (args.Length != 6 ||
            !int.TryParse(args[3], out int width) ||
            !int.TryParse(args[4], out int height) ||
            !int.TryParse(args[5], out int refresh))
        {
            return ExitInvalidArguments;
        }

        try
        {
            using EventWaitHandle keepEvent = EventWaitHandle.OpenExisting(args[1]);

            if (keepEvent.WaitOne(TimeSpan.FromSeconds(15)))
            {
                return ExitSuccess;
            }

            return RestoreMode(
                args[2],
                new Mode(width, height, refresh),
                out _);
        }
        catch
        {
            return ExitUnexpectedError;
        }
    }

    private static int RunWizard()
    {
        while (true)
        {
            Console.Clear();

            Console.WriteLine("ResSwitcher9000");
            Console.WriteLine("================");
            Console.WriteLine();
            Console.WriteLine("Create a safe display-mode shortcut.");
            Console.WriteLine();

            List<DisplayInfo> displays = GetDisplays();

            if (displays.Count == 0)
            {
                Back("No active display devices were found.");
                return ExitDisplayError;
            }

            for (int i = 0; i < displays.Count; i++)
            {
                PrintDisplay(displays[i], i + 1);
            }

            Console.Write($"Select a display (1-{displays.Count}): ");

            if (!TryChoice(1, displays.Count, out int displayNumber))
            {
                Back("Invalid display selection.");
                continue;
            }

            DisplayInfo display = displays[displayNumber - 1];

            if (!TryChooseMode(display.DeviceName, out Mode selected))
            {
                Back("Invalid mode selection.");
                continue;
            }

            Options options = new()
            {
                DeviceName = display.DeviceName,
                Width = selected.Width,
                Height = selected.Height,
                RefreshRate = selected.RefreshRate
            };

            int test = PrepareMode(
                options,
                out _,
                out _,
                out string testMessage);

            if (test != ExitSuccess)
            {
                Back(testMessage);
                continue;
            }

            Console.WriteLine();
            Console.WriteLine("Mode supported.");
            Console.Write("Shortcut name [Resolution Shortcut]: ");

            string name = CleanShortcutName(Console.ReadLine());
            string desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

            string path = Path.Combine(desktop, name + ".lnk");

            if (File.Exists(path))
            {
                Console.Write($"'{name}.lnk' exists. Overwrite? [y/N]: ");

                if (!IsYes(Console.ReadLine()))
                {
                    Back("No shortcut was created.");
                    continue;
                }
            }

            try
            {
                CreateShortcut(path, options);
                Console.WriteLine();
                Console.WriteLine($"Created: {path}");
                Console.WriteLine();
                Console.WriteLine("The shortcut gives you 15 seconds to keep or revert the mode.");
            }
            catch (Exception ex)
            {
                Back($"Shortcut creation failed: {ex.Message}");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine("[1] Create another shortcut");
            Console.WriteLine("[0] Exit or close this window");
            Console.Write("Choose: ");

            if (Console.ReadLine()?.Trim() != "1")
            {
                return ExitSuccess;
            }
        }
    }

    private static bool TryChooseMode(string deviceName, out Mode selected)
    {
        selected = default;

        List<Mode> modes = GetModes(deviceName);

        if (modes.Count == 0)
        {
            return TryManualMode(out selected);
        }

        bool hasCurrent = TryGetCurrentMode(deviceName, out DEVMODEW current);
        Mode currentMode = hasCurrent ? ToMode(current) : default;

        List<(int Width, int Height)> resolutions = modes
            .Select(mode => (mode.Width, mode.Height))
            .Distinct()
            .OrderByDescending(mode => (long)mode.Width * mode.Height)
            .ThenByDescending(mode => mode.Width)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Supported resolutions:");

        for (int i = 0; i < resolutions.Count; i++)
        {
            (int width, int height) = resolutions[i];

            bool isCurrent =
                hasCurrent &&
                currentMode.Width == width &&
                currentMode.Height == height;

            Console.WriteLine(
                $"[{i + 1}] {width}x{height}" +
                (isCurrent ? " (current)" : string.Empty));
        }

        Console.WriteLine("[0] Enter width, height, and refresh rate manually");
        Console.Write($"Choose (0-{resolutions.Count}): ");

        if (!TryChoice(0, resolutions.Count, out int resolutionChoice))
        {
            return false;
        }

        if (resolutionChoice == 0)
        {
            return TryManualMode(out selected);
        }

        (int selectedWidth, int selectedHeight) =
            resolutions[resolutionChoice - 1];

        List<int> refreshRates = modes
            .Where(mode =>
                mode.Width == selectedWidth &&
                mode.Height == selectedHeight)
            .Select(mode => mode.RefreshRate)
            .Distinct()
            .OrderBy(rate => rate)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"Supported refresh rates for {selectedWidth}x{selectedHeight}:");

        for (int i = 0; i < refreshRates.Count; i++)
        {
            int refresh = refreshRates[i];

            bool isCurrent =
                hasCurrent &&
                currentMode.Width == selectedWidth &&
                currentMode.Height == selectedHeight &&
                currentMode.RefreshRate == refresh;

            Console.WriteLine(
                $"[{i + 1}] {refresh} Hz" +
                (isCurrent ? " (current)" : string.Empty));
        }

        Console.WriteLine("[0] Enter refresh rate manually");
        Console.Write($"Choose (0-{refreshRates.Count}): ");

        if (!TryChoice(0, refreshRates.Count, out int refreshChoice))
        {
            return false;
        }

        if (refreshChoice == 0)
        {
            Console.Write("Enter refresh rate in Hz (example: 60): ");

            if (!TryPositive(Console.ReadLine(), out int refresh))
            {
                return false;
            }

            selected = new Mode(selectedWidth, selectedHeight, refresh);
            return true;
        }

        selected = new Mode(
            selectedWidth,
            selectedHeight,
            refreshRates[refreshChoice - 1]);

        return true;
    }

    private static bool TryManualMode(out Mode mode)
    {
        mode = default;

        Console.WriteLine();
        Console.Write("Enter width, height, refresh rate (example: 1920 1080 60): ");

        string[] values = (Console.ReadLine() ?? string.Empty)
            .Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

        if (values.Length != 3 ||
            !TryPositive(values[0], out int width) ||
            !TryPositive(values[1], out int height) ||
            !TryPositive(values[2], out int refresh))
        {
            return false;
        }

        mode = new Mode(width, height, refresh);
        return true;
    }

    private static bool TryChoice(int minimum, int maximum, out int choice)
    {
        return int.TryParse(
                   Console.ReadLine(),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out choice) &&
               choice >= minimum &&
               choice <= maximum;
    }

    private static bool TryPositive(string? text, out int value)
    {
        return int.TryParse(
                   text,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value > 0;
    }

    private static int ListDisplays()
    {
        List<DisplayInfo> displays = GetDisplays();

        if (displays.Count == 0)
        {
            Console.WriteLine("No active display devices were found.");
            return ExitDisplayError;
        }

        for (int i = 0; i < displays.Count; i++)
        {
            PrintDisplay(displays[i], i + 1);
        }

        return ExitSuccess;
    }

    private static List<DisplayInfo> GetDisplays()
    {
        List<DisplayInfo> displays = new();

        for (uint index = 0; ; index++)
        {
            DISPLAY_DEVICEW device = new()
            {
                Cb = Marshal.SizeOf<DISPLAY_DEVICEW>()
            };

            if (!EnumDisplayDevicesW(null, index, ref device, 0))
            {
                break;
            }

            if ((device.StateFlags & DisplayDeviceAttached) == 0 ||
                string.IsNullOrWhiteSpace(device.DeviceName))
            {
                continue;
            }

            displays.Add(new DisplayInfo(
                device.DeviceName,
                device.DeviceString ?? "Unknown display",
                device.StateFlags));
        }

        return displays;
    }

    private static DisplayInfo? FindDisplay(string deviceName)
    {
        return GetDisplays().FirstOrDefault(display =>
            string.Equals(
                display.DeviceName,
                deviceName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static List<Mode> GetModes(string deviceName)
    {
        List<Mode> modes = new();

        for (uint index = 0; ; index++)
        {
            DEVMODEW mode = NewDevMode();

            if (!EnumDisplaySettingsW(deviceName, index, ref mode))
            {
                break;
            }

            if (mode.DmPelsWidth > 0 &&
                mode.DmPelsHeight > 0 &&
                mode.DmDisplayFrequency > 0)
            {
                modes.Add(ToMode(mode));
            }
        }

        return modes.Distinct().ToList();
    }

    private static bool TryGetCurrentMode(
        string deviceName,
        out DEVMODEW mode)
    {
        mode = NewDevMode();

        return EnumDisplaySettingsW(
            deviceName,
            EnumCurrentSettings,
            ref mode);
    }

    private static DEVMODEW NewDevMode()
    {
        return new DEVMODEW
        {
            DmSize = checked((ushort)Marshal.SizeOf<DEVMODEW>()),
            DmDriverExtra = 0
        };
    }

    private static Mode ToMode(DEVMODEW mode)
    {
        return new Mode(
            (int)mode.DmPelsWidth,
            (int)mode.DmPelsHeight,
            (int)mode.DmDisplayFrequency);
    }

    private static int Change(
        string deviceName,
        ref DEVMODEW mode,
        uint flags)
    {
        return ChangeDisplaySettingsExW(
            deviceName,
            ref mode,
            IntPtr.Zero,
            flags,
            IntPtr.Zero);
    }

    private static int ToExitCode(int result)
    {
        return result switch
        {
            DispSuccess => ExitSuccess,
            DispRestart => ExitRestart,
            DispBadMode => ExitUnsupportedMode,
            _ => ExitApplyError
        };
    }

    private static string Describe(int result)
    {
        return result switch
        {
            DispSuccess => "Success",
            DispRestart => "Restart required",
            DispFailed => "Display change failed",
            DispBadMode => "Unsupported mode",
            DispNotUpdated => "Settings could not be saved",
            DispBadFlags => "Invalid display flags",
            DispBadParam => "Invalid display parameters",
            _ => $"Unknown error ({result})"
        };
    }

    private static void PrintDisplay(DisplayInfo display, int number)
    {
        bool primary = (display.StateFlags & DisplayDevicePrimary) != 0;

        Console.WriteLine(
            $"[{number}] {display.DeviceName}" +
            (primary ? " (primary)" : string.Empty));

        Console.WriteLine($"    {display.Description}");

        if (TryGetCurrentMode(display.DeviceName, out DEVMODEW mode))
        {
            Console.WriteLine(
                $"    Current: {mode.DmPelsWidth}x{mode.DmPelsHeight} @ " +
                $"{mode.DmDisplayFrequency} Hz");
        }

        Console.WriteLine();
    }

    private static void CreateShortcut(string path, Options options)
    {
        string exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not find executable.");

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");

        if (shellType is null)
        {
            throw new InvalidOperationException("Windows Script Host is unavailable.");
        }

        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create shortcut service.");

        dynamic shortcut = shell.CreateShortcut(path);

        shortcut.TargetPath = exe;
        shortcut.Arguments =
            $"--device \"{options.DeviceName}\" " +
            $"--width {options.Width} " +
            $"--height {options.Height} " +
            $"--refresh {options.RefreshRate} " +
            "--confirm";

        shortcut.WorkingDirectory =
            Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory;

        shortcut.Description =
            $"Apply {options.Width}x{options.Height} @ {options.RefreshRate} Hz";

        shortcut.WindowStyle = 1;
        shortcut.Save();
    }

    private static string CleanShortcutName(string? input)
    {
        const string fallback = "Resolution Shortcut";

        if (string.IsNullOrWhiteSpace(input))
        {
            return fallback;
        }

        string name = input.Trim();

        if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        StringBuilder clean = new();

        foreach (char character in name)
        {
            clean.Append(
                Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0
                    ? '_'
                    : character);
        }

        name = clean.ToString().Trim().TrimEnd('.', ' ');

        return string.IsNullOrWhiteSpace(name)
            ? fallback
            : name;
    }

    private static bool IsYes(string? input)
    {
        return input?.Trim().Equals(
                   "y",
                   StringComparison.OrdinalIgnoreCase) == true ||
               input?.Trim().Equals(
                   "yes",
                   StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void Back(string message)
    {
        Console.WriteLine();
        Console.WriteLine(message);
        Console.Write("Press any key to return...");
        Console.ReadKey(intercept: true);
    }

    private static int RunWithConsole(Func<int> action)
    {
        bool freeConsole = false;

        if (AttachConsole(AttachParentProcess))
        {
            freeConsole = true;
        }
        else if (Marshal.GetLastWin32Error() != ErrorAccessDenied)
        {
            if (!AllocConsole())
            {
                return ExitUnexpectedError;
            }

            freeConsole = true;
        }

        try
        {
            BindConsole();
            return action();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return ExitUnexpectedError;
        }
        finally
        {
            if (freeConsole)
            {
                FreeConsole();
            }
        }
    }

    private static void BindConsole()
    {
        Encoding encoding = new UTF8Encoding(false);

        Console.SetOut(new StreamWriter(
            Console.OpenStandardOutput(),
            encoding)
        {
            AutoFlush = true
        });

        Console.SetError(new StreamWriter(
            Console.OpenStandardError(),
            encoding)
        {
            AutoFlush = true
        });

        Console.SetIn(new StreamReader(
            Console.OpenStandardInput(),
            encoding,
            detectEncodingFromByteOrderMarks: false));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ResSwitcher9000");
        Console.WriteLine();
        Console.WriteLine("Wizard:");
        Console.WriteLine("  ResSwitcher9000.exe");
        Console.WriteLine();
        Console.WriteLine("List displays:");
        Console.WriteLine("  ResSwitcher9000.exe --list");
        Console.WriteLine();
        Console.WriteLine("Apply immediately:");
        Console.WriteLine(
            @"  ResSwitcher9000.exe --device ""\\.\DISPLAY1"" --width 1920 --height 1080 --refresh 60");
        Console.WriteLine();
        Console.WriteLine("Apply with a 15-second keep/revert confirmation:");
        Console.WriteLine(
            @"  ResSwitcher9000.exe --device ""\\.\DISPLAY1"" --width 1920 --height 1080 --refresh 60 --confirm");
        Console.WriteLine();
        Console.WriteLine("Save a confirmed mode in the Windows user profile:");
        Console.WriteLine(
            @"  ResSwitcher9000.exe --device ""\\.\DISPLAY1"" --width 1920 --height 1080 --refresh 60 --confirm --persist");
    }

    private sealed class Options
    {
        public string DeviceName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }
        public bool Persist { get; set; }
        public bool Confirm { get; set; }
        public bool Verbose { get; set; }
    }

    private sealed record DisplayInfo(
        string DeviceName,
        string Description,
        uint StateFlags);

    private readonly record struct Mode(
        int Width,
        int Height,
        int RefreshRate);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DmDeviceName;

        public ushort DmSpecVersion;
        public ushort DmDriverVersion;
        public ushort DmSize;
        public ushort DmDriverExtra;
        public uint DmFields;

        public int DmPositionX;
        public int DmPositionY;
        public uint DmDisplayOrientation;
        public uint DmDisplayFixedOutput;

        public short DmColor;
        public short DmDuplex;
        public short DmYResolution;
        public short DmTTOption;
        public short DmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DmFormName;

        public ushort DmLogPixels;
        public uint DmBitsPerPel;
        public uint DmPelsWidth;
        public uint DmPelsHeight;
        public uint DmDisplayFlags;
        public uint DmDisplayFrequency;
        public uint DmIcmMethod;
        public uint DmIcmIntent;
        public uint DmMediaType;
        public uint DmDitherType;
        public uint DmReserved1;
        public uint DmReserved2;
        public uint DmPanningWidth;
        public uint DmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICEW
    {
        public int Cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceKey;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport(
        "user32.dll",
        EntryPoint = "ChangeDisplaySettingsExW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int ChangeDisplaySettingsExW(
        string deviceName,
        ref DEVMODEW deviceMode,
        IntPtr hwnd,
        uint flags,
        IntPtr lParam);

    [DllImport(
        "user32.dll",
        EntryPoint = "EnumDisplayDevicesW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevicesW(
        string? deviceName,
        uint deviceIndex,
        ref DISPLAY_DEVICEW displayDevice,
        uint flags);

    [DllImport(
        "user32.dll",
        EntryPoint = "EnumDisplaySettingsW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(
        string deviceName,
        uint modeIndex,
        ref DEVMODEW deviceMode);
}