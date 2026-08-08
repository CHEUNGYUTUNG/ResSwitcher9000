using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ResSwitcher9000;

internal static class Program
{
    private const uint EnumCurrentSettings = 0xFFFFFFFF;

    private const uint DisplayDeviceAttachedToDesktop = 0x00000001;
    private const uint DisplayDevicePrimaryDevice = 0x00000004;

    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint DmDisplayFrequency = 0x00400000;

    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsTest = 0x00000002;

    private const int DispChangeSuccessful = 0;
    private const int DispChangeRestart = 1;
    private const int DispChangeFailed = -1;
    private const int DispChangeBadMode = -2;
    private const int DispChangeNotUpdated = -3;
    private const int DispChangeBadFlags = -4;
    private const int DispChangeBadParam = -5;

    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int ErrorAccessDenied = 5;

    private const int ExitSuccess = 0;
    private const int ExitRestartRequired = 1;
    private const int ExitInvalidArguments = 2;
    private const int ExitDisplayError = 3;
    private const int ExitUnsupportedMode = 4;
    private const int ExitApplyError = 5;
    private const int ExitShortcutError = 6;
    private const int ExitUnexpectedError = 7;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return RunWithConsole(RunWizard);
        }

        if (args.Length == 1 &&
            (args[0] == "--help" || args[0] == "-?"))
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

        if (!TryParseOptions(args, out Options options, out string parseError))
        {
            if (args.Contains("--verbose"))
            {
                return RunWithConsole(() =>
                {
                    WriteError(parseError);
                    return ExitInvalidArguments;
                });
            }

            return ExitInvalidArguments;
        }

        if (options.Verbose)
        {
            return RunWithConsole(() =>
            {
                int result = ApplyMode(options, out string message);

                if (result == ExitSuccess)
                {
                    WriteSuccess(message);
                }
                else if (result == ExitRestartRequired)
                {
                    WriteWarning(message);
                }
                else
                {
                    WriteError(message);
                }

                return result;
            });
        }

        // Shortcut mode is intentionally silent.
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
            string argument = args[i];

            switch (argument)
            {
                case "-d":
                case "--device":
                    if (hasDevice)
                    {
                        error = "Display device was specified more than once.";
                        return false;
                    }

                    if (!TryReadValue(args, ref i, argument, out string device, out error))
                    {
                        return false;
                    }

                    options.DeviceName = device;
                    hasDevice = true;
                    break;

                case "-w":
                case "--width":
                    if (hasWidth)
                    {
                        error = "Width was specified more than once.";
                        return false;
                    }

                    if (!TryReadValue(args, ref i, argument, out string widthText, out error) ||
                        !TryParsePositiveInt(widthText, "Width", out int width, out error))
                    {
                        return false;
                    }

                    options.Width = width;
                    hasWidth = true;
                    break;

                case "-h":
                case "--height":
                    if (hasHeight)
                    {
                        error = "Height was specified more than once.";
                        return false;
                    }

                    if (!TryReadValue(args, ref i, argument, out string heightText, out error) ||
                        !TryParsePositiveInt(heightText, "Height", out int height, out error))
                    {
                        return false;
                    }

                    options.Height = height;
                    hasHeight = true;
                    break;

                case "-r":
                case "--refresh":
                    if (hasRefresh)
                    {
                        error = "Refresh rate was specified more than once.";
                        return false;
                    }

                    if (!TryReadValue(args, ref i, argument, out string refreshText, out error) ||
                        !TryParsePositiveInt(refreshText, "Refresh rate", out int refresh, out error))
                    {
                        return false;
                    }

                    options.RefreshRate = refresh;
                    hasRefresh = true;
                    break;

                case "--persist":
                    options.Persist = true;
                    break;

                case "--verbose":
                    options.Verbose = true;
                    break;

                default:
                    error = $"Unknown option: {argument}";
                    return false;
            }
        }

        if (!hasDevice || !hasWidth || !hasHeight || !hasRefresh)
        {
            error =
                "Command-line mode requires --device, --width, --height, and --refresh.";

            return false;
        }

        return true;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string option,
        out string value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (index + 1 >= args.Length)
        {
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index].Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{option} requires a non-empty value.";
            return false;
        }

        return true;
    }

    private static bool TryParsePositiveInt(
        string value,
        string name,
        out int number,
        out string error)
    {
        error = string.Empty;

        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out number) ||
            number <= 0)
        {
            error = $"{name} must be a positive whole number.";
            return false;
        }

        return true;
    }

    private static int ApplyMode(Options options, out string message)
    {
        int testResult = TestMode(options, out DEVMODEW mode, out message);

        if (testResult != ExitSuccess)
        {
            return testResult;
        }

        uint flags = options.Persist ? CdsUpdateRegistry : 0;

        int result = ChangeDisplaySettingsExW(
            options.DeviceName,
            ref mode,
            IntPtr.Zero,
            flags,
            IntPtr.Zero);

        if (result == DispChangeSuccessful)
        {
            message = options.Persist
                ? "Display mode applied and saved to the current Windows user profile."
                : "Display mode applied.";

            return ExitSuccess;
        }

        if (result == DispChangeRestart)
        {
            message =
                "Windows saved the display mode, but reports that a restart is required.";

            return ExitRestartRequired;
        }

        message = $"Windows could not apply the mode: {DescribeResult(result)}";
        return ExitApplyError;
    }

    private static int TestMode(
        Options options,
        out DEVMODEW mode,
        out string message)
    {
        mode = default;
        message = string.Empty;

        if (FindActiveDisplay(options.DeviceName) is null)
        {
            message =
                $"'{options.DeviceName}' is not an active display device. " +
                "Run ResSwitcher9000.exe --list to see valid device names.";

            return ExitDisplayError;
        }

        if (!TryGetCurrentMode(options.DeviceName, out mode))
        {
            message =
                $"Could not read the current display mode for '{options.DeviceName}'.";

            return ExitDisplayError;
        }

        // Preserve the current driver-provided mode.
        mode.DmPelsWidth = (uint)options.Width;
        mode.DmPelsHeight = (uint)options.Height;
        mode.DmDisplayFrequency = (uint)options.RefreshRate;
        mode.DmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

        int result = ChangeDisplaySettingsExW(
            options.DeviceName,
            ref mode,
            IntPtr.Zero,
            CdsTest,
            IntPtr.Zero);

        if (result != DispChangeSuccessful)
        {
            message =
                $"Windows rejected {options.Width}x{options.Height} @ " +
                $"{options.RefreshRate} Hz: {DescribeResult(result)}";

            return ExitUnsupportedMode;
        }

        message = "The requested display mode is supported.";
        return ExitSuccess;
    }

    private static int RunWizard()
    {
        while (true)
        {
            Console.Clear();

            Console.WriteLine("ResSwitcher9000");
            Console.WriteLine("================");
            Console.WriteLine();
            Console.WriteLine("Create a shortcut for a display resolution and refresh rate.");
            Console.WriteLine();

            List<DisplayInfo> displays = GetActiveDisplays();

            if (displays.Count == 0)
            {
                WriteError("No active display devices were found.");
                Pause();
                return ExitDisplayError;
            }

            Console.WriteLine("Active display devices:");
            Console.WriteLine();

            for (int i = 0; i < displays.Count; i++)
            {
                PrintDisplay(displays[i], i + 1);
            }

            Console.Write($"Select a display (1-{displays.Count}): ");

            if (!TryReadChoice(1, displays.Count, out int displayChoice))
            {
                WriteError("Invalid display selection.");
                Pause();
                return ExitInvalidArguments;
            }

            DisplayInfo display = displays[displayChoice - 1];

            if (!TryChooseMode(display.DeviceName, out DisplayMode selectedMode))
            {
                WriteError("Invalid mode selection.");
                Pause();
                return ExitInvalidArguments;
            }

            Options options = new Options
            {
                DeviceName = display.DeviceName,
                Width = selectedMode.Width,
                Height = selectedMode.Height,
                RefreshRate = selectedMode.RefreshRate,
                Persist = false
            };

            Console.WriteLine();
            Console.WriteLine("Testing the requested mode...");

            int testResult = TestMode(options, out _, out string testMessage);

            if (testResult != ExitSuccess)
            {
                WriteError(testMessage);
                Pause();
                return testResult;
            }

            WriteSuccess("Mode supported.");

            Console.WriteLine();
            Console.Write("Shortcut name [Resolution Shortcut]: ");

            string shortcutName = CleanShortcutName(Console.ReadLine());

            string desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

            string shortcutPath = Path.Combine(desktop, shortcutName + ".lnk");

            if (File.Exists(shortcutPath))
            {
                Console.Write($"'{shortcutName}.lnk' already exists. Overwrite? [y/N]: ");

                if (!IsYes(Console.ReadLine()))
                {
                    Console.WriteLine("No shortcut was created.");
                    Pause();
                    return ExitSuccess;
                }
            }

            try
            {
                CreateShortcut(shortcutPath, options);
                WriteSuccess($"Created shortcut: {shortcutPath}");
            }
            catch (Exception ex)
            {
                WriteError($"Could not create shortcut: {ex.Message}");
                Pause();
                return ExitShortcutError;
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

    private static bool TryChooseMode(
        string deviceName,
        out DisplayMode selectedMode)
    {
        selectedMode = default;

        List<DisplayMode> modes = GetModes(deviceName);

        if (modes.Count == 0)
        {
            Console.WriteLine("Windows did not report any modes for this display.");
            return TryReadManualMode(out selectedMode);
        }

        DisplayMode? current = null;

        if (TryGetCurrentMode(deviceName, out DEVMODEW currentDevMode))
        {
            current = new DisplayMode(
                (int)currentDevMode.DmPelsWidth,
                (int)currentDevMode.DmPelsHeight,
                (int)currentDevMode.DmDisplayFrequency);
        }

        List<(int Width, int Height)> resolutions = modes
            .Select(mode => (mode.Width, mode.Height))
            .Distinct()
            .OrderByDescending(mode => (long)mode.Width * mode.Height)
            .ThenByDescending(mode => mode.Width)
            .ThenByDescending(mode => mode.Height)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"Windows-reported resolutions for {deviceName}:");
        Console.WriteLine();

        for (int i = 0; i < resolutions.Count; i++)
        {
            (int width, int height) = resolutions[i];

            bool isCurrent =
                current.HasValue &&
                current.Value.Width == width &&
                current.Value.Height == height;

            Console.WriteLine(
                $"[{i + 1}] {width}x{height}" +
                (isCurrent ? " (current)" : string.Empty));
        }

        Console.WriteLine("[0] Enter width, height, and refresh rate manually");
        Console.WriteLine();
        Console.Write($"Choose a resolution (0-{resolutions.Count}): ");

        if (!TryReadChoice(0, resolutions.Count, out int resolutionChoice))
        {
            return false;
        }

        if (resolutionChoice == 0)
        {
            return TryReadManualMode(out selectedMode);
        }

        (int selectedWidth, int selectedHeight) = resolutions[resolutionChoice - 1];

        List<int> refreshRates = modes
            .Where(mode =>
                mode.Width == selectedWidth &&
                mode.Height == selectedHeight)
            .Select(mode => mode.RefreshRate)
            .Distinct()
            .OrderBy(rate => rate)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"Windows-reported refresh rates for {selectedWidth}x{selectedHeight}:");
        Console.WriteLine();

        for (int i = 0; i < refreshRates.Count; i++)
        {
            int refreshRate = refreshRates[i];

            bool isCurrent =
                current.HasValue &&
                current.Value.Width == selectedWidth &&
                current.Value.Height == selectedHeight &&
                current.Value.RefreshRate == refreshRate;

            Console.WriteLine(
                $"[{i + 1}] {refreshRate} Hz" +
                (isCurrent ? " (current)" : string.Empty));
        }

        Console.WriteLine("[0] Enter a refresh rate manually");
        Console.WriteLine();
        Console.Write($"Choose a refresh rate (0-{refreshRates.Count}): ");

        if (!TryReadChoice(0, refreshRates.Count, out int refreshChoice))
        {
            return false;
        }

        if (refreshChoice == 0)
        {
            Console.Write("Enter refresh rate in Hz (example: 60): ");

            if (!TryParsePositiveInt(
                    Console.ReadLine() ?? string.Empty,
                    "Refresh rate",
                    out int manualRefresh,
                    out _))
            {
                return false;
            }

            selectedMode = new DisplayMode(
                selectedWidth,
                selectedHeight,
                manualRefresh);

            return true;
        }

        selectedMode = new DisplayMode(
            selectedWidth,
            selectedHeight,
            refreshRates[refreshChoice - 1]);

        return true;
    }

    private static bool TryReadManualMode(out DisplayMode mode)
    {
        mode = default;

        Console.WriteLine();
        Console.Write("Enter width, height, and refresh rate (example: 1920 1080 60): ");

        string? input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string[] values = input.Split(
            new[] { ' ', '\t' },
            StringSplitOptions.RemoveEmptyEntries);

        if (values.Length != 3 ||
            !TryParsePositiveInt(values[0], "Width", out int width, out _) ||
            !TryParsePositiveInt(values[1], "Height", out int height, out _) ||
            !TryParsePositiveInt(values[2], "Refresh rate", out int refresh, out _))
        {
            return false;
        }

        mode = new DisplayMode(width, height, refresh);
        return true;
    }

    private static bool TryReadChoice(int minimum, int maximum, out int choice)
    {
        choice = 0;

        return int.TryParse(
                   Console.ReadLine(),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out choice) &&
               choice >= minimum &&
               choice <= maximum;
    }

    private static int ListDisplays()
    {
        List<DisplayInfo> displays = GetActiveDisplays();

        if (displays.Count == 0)
        {
            WriteError("No active display devices were found.");
            return ExitDisplayError;
        }

        Console.WriteLine("Active display devices:");
        Console.WriteLine();

        for (int i = 0; i < displays.Count; i++)
        {
            PrintDisplay(displays[i], i + 1);
        }

        return ExitSuccess;
    }

    private static List<DisplayInfo> GetActiveDisplays()
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

            if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0 ||
                string.IsNullOrWhiteSpace(device.DeviceName))
            {
                continue;
            }

            displays.Add(new DisplayInfo(
                device.DeviceName,
                string.IsNullOrWhiteSpace(device.DeviceString)
                    ? "Unknown display adapter"
                    : device.DeviceString,
                device.StateFlags));
        }

        return displays;
    }

    private static DisplayInfo? FindActiveDisplay(string deviceName)
    {
        return GetActiveDisplays().FirstOrDefault(display =>
            string.Equals(
                display.DeviceName,
                deviceName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static List<DisplayMode> GetModes(string deviceName)
    {
        List<DisplayMode> modes = new();

        for (uint index = 0; ; index++)
        {
            DEVMODEW mode = NewDevMode();

            if (!EnumDisplaySettingsW(deviceName, index, ref mode))
            {
                break;
            }

            if (mode.DmPelsWidth == 0 ||
                mode.DmPelsHeight == 0 ||
                mode.DmDisplayFrequency == 0)
            {
                continue;
            }

            modes.Add(new DisplayMode(
                (int)mode.DmPelsWidth,
                (int)mode.DmPelsHeight,
                (int)mode.DmDisplayFrequency));
        }

        return modes
            .Distinct()
            .ToList();
    }

    private static bool TryGetCurrentMode(
        string deviceName,
        out DEVMODEW mode)
    {
        mode = NewDevMode();

        bool success = EnumDisplaySettingsW(
            deviceName,
            EnumCurrentSettings,
            ref mode);

        mode.DmSize = checked((ushort)Marshal.SizeOf<DEVMODEW>());
        mode.DmDriverExtra = 0;

        return success;
    }

    private static DEVMODEW NewDevMode()
    {
        return new DEVMODEW
        {
            DmSize = checked((ushort)Marshal.SizeOf<DEVMODEW>()),
            DmDriverExtra = 0
        };
    }

    private static void PrintDisplay(DisplayInfo display, int number)
    {
        bool primary =
            (display.StateFlags & DisplayDevicePrimaryDevice) != 0;

        Console.WriteLine(
            $"[{number}] {display.DeviceName}" +
            (primary ? " (primary)" : string.Empty));

        Console.WriteLine($"    {display.Description}");

        if (TryGetCurrentMode(display.DeviceName, out DEVMODEW mode))
        {
            Console.WriteLine(
                $"    Current mode: {mode.DmPelsWidth}x{mode.DmPelsHeight} @ " +
                $"{mode.DmDisplayFrequency} Hz");
        }

        Console.WriteLine();
    }

    private static void CreateShortcut(string shortcutPath, Options options)
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Could not determine the executable path.");

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");

        if (shellType is null)
        {
            throw new InvalidOperationException(
                "Windows Script Host is unavailable.");
        }

        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException(
                "Could not create the Windows shortcut service.");

        dynamic shortcut = shell.CreateShortcut(shortcutPath);

        shortcut.TargetPath = executablePath;
        shortcut.Arguments =
            $"--device \"{options.DeviceName}\" " +
            $"--width {options.Width} " +
            $"--height {options.Height} " +
            $"--refresh {options.RefreshRate}";

        shortcut.WorkingDirectory =
            Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;

        shortcut.Description =
            $"Apply {options.Width}x{options.Height} @ {options.RefreshRate} Hz";

        shortcut.WindowStyle = 7;
        shortcut.Save();
    }

    private static string CleanShortcutName(string? input)
    {
        const string fallbackName = "Resolution Shortcut";

        if (string.IsNullOrWhiteSpace(input))
        {
            return fallbackName;
        }

        string name = input.Trim();

        if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        StringBuilder result = new();

        foreach (char character in name)
        {
            result.Append(
                Array.IndexOf(invalidCharacters, character) >= 0
                    ? '_'
                    : character);
        }

        name = result.ToString().Trim().TrimEnd('.', ' ');

        return string.IsNullOrWhiteSpace(name)
            ? fallbackName
            : name;
    }

    private static bool IsYes(string? input)
    {
        return string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(input?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeResult(int result)
    {
        return result switch
        {
            DispChangeSuccessful => "DISP_CHANGE_SUCCESSFUL",
            DispChangeRestart => "DISP_CHANGE_RESTART",
            DispChangeFailed => "DISP_CHANGE_FAILED",
            DispChangeBadMode => "DISP_CHANGE_BADMODE (unsupported mode)",
            DispChangeNotUpdated => "DISP_CHANGE_NOTUPDATED",
            DispChangeBadFlags => "DISP_CHANGE_BADFLAGS",
            DispChangeBadParam => "DISP_CHANGE_BADPARAM",
            _ => $"Unknown display error ({result})"
        };
    }

    private static int RunWithConsole(Func<int> action)
    {
        bool detachWhenFinished = false;

        if (AttachConsole(AttachParentProcess))
        {
            detachWhenFinished = true;
        }
        else if (Marshal.GetLastWin32Error() != ErrorAccessDenied)
        {
            if (!AllocConsole())
            {
                return ExitUnexpectedError;
            }

            detachWhenFinished = true;
        }

        try
        {
            InitializeConsoleStreams();
            return action();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return ExitUnexpectedError;
        }
        finally
        {
            if (detachWhenFinished)
            {
                FreeConsole();
            }
        }
    }

    private static void InitializeConsoleStreams()
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
        Console.WriteLine("Apply a display mode:");
        Console.WriteLine(
            @"  ResSwitcher9000.exe --device ""\\.\DISPLAY1"" --width 1920 --height 1080 --refresh 60");
        Console.WriteLine();
        Console.WriteLine("Save a mode in the current Windows user profile:");
        Console.WriteLine(
            @"  ResSwitcher9000.exe --device ""\\.\DISPLAY1"" --width 1920 --height 1080 --refresh 60 --persist");
        Console.WriteLine();
        Console.WriteLine("Show success or error output:");
        Console.WriteLine(
            @"  ResSwitcher9000.exe --device ""\\.\DISPLAY1"" --width 1920 --height 1080 --refresh 60 --verbose");
    }

    private static void WriteSuccess(string message)
    {
        WriteColored(ConsoleColor.Green, message);
    }

    private static void WriteWarning(string message)
    {
        WriteColored(ConsoleColor.Yellow, message);
    }

    private static void WriteError(string message)
    {
        WriteColored(ConsoleColor.Red, message);
    }

    private static void WriteColored(ConsoleColor color, string message)
    {
        ConsoleColor originalColor = Console.ForegroundColor;

        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.Write("Press any key to exit...");
        Console.ReadKey(intercept: true);
        Console.WriteLine();
    }

    private sealed class Options
    {
        public string DeviceName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }
        public bool Persist { get; set; }
        public bool Verbose { get; set; }
    }

    private sealed class DisplayInfo
    {
        public DisplayInfo(string deviceName, string description, uint stateFlags)
        {
            DeviceName = deviceName;
            Description = description;
            StateFlags = stateFlags;
        }

        public string DeviceName { get; }
        public string Description { get; }
        public uint StateFlags { get; }
    }

    private readonly record struct DisplayMode(
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