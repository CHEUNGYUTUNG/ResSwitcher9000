using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    private const int DispChangeBadDualView = -6;

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
        // No arguments = interactive shortcut wizard.
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
            // Keep shortcuts silent unless the user explicitly asks for output.
            if (ContainsVerboseFlag(args))
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
                        !TryParsePositiveInteger(widthText, "Width", out int width, out error))
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
                        !TryParsePositiveInteger(heightText, "Height", out int height, out error))
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
                        !TryParsePositiveInteger(refreshText, "Refresh rate", out int refresh, out error))
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
                "Command-line mode requires --device, --width, --height, and --refresh. " +
                "Run ResSwitcher9000.exe --help for examples.";

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

    private static bool TryParsePositiveInteger(
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

    private static bool ContainsVerboseFlag(string[] args)
    {
        foreach (string argument in args)
        {
            if (argument == "--verbose")
            {
                return true;
            }
        }

        return false;
    }

    private static int ApplyMode(Options options, out string message)
    {
        int testResult = TestMode(options, out DEVMODEW mode, out message);

        if (testResult != ExitSuccess)
        {
            return testResult;
        }

        uint flags = options.Persist ? CdsUpdateRegistry : 0;

        int applyResult = ChangeDisplaySettingsExW(
            options.DeviceName,
            ref mode,
            IntPtr.Zero,
            flags,
            IntPtr.Zero);

        if (applyResult == DispChangeSuccessful)
        {
            message = options.Persist
                ? "Display mode applied and saved to the current Windows user profile."
                : "Display mode applied.";

            return ExitSuccess;
        }

        if (applyResult == DispChangeRestart)
        {
            message =
                "Windows saved the display mode, but reports that a restart is required.";

            return ExitRestartRequired;
        }

        message =
            $"Windows could not apply the requested mode: " +
            DescribeDisplayChangeResult(applyResult);

        return ExitApplyError;
    }

    private static int TestMode(
        Options options,
        out DEVMODEW mode,
        out string message)
    {
        mode = default;
        message = string.Empty;

        DisplayInfo? display = FindActiveDisplay(options.DeviceName);

        if (display is null)
        {
            message =
                $"'{options.DeviceName}' is not an active display device. " +
                "Run ResSwitcher9000.exe --list to find valid device names.";

            return ExitDisplayError;
        }

        if (!TryGetCurrentMode(options.DeviceName, out mode))
        {
            message =
                $"Could not read the current display mode for '{options.DeviceName}'.";

            return ExitDisplayError;
        }

        // Start from the driver-provided current mode, then change only
        // resolution and refresh rate.
        mode.DmPelsWidth = (uint)options.Width;
        mode.DmPelsHeight = (uint)options.Height;
        mode.DmDisplayFrequency = (uint)options.RefreshRate;
        mode.DmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

        int testResult = ChangeDisplaySettingsExW(
            options.DeviceName,
            ref mode,
            IntPtr.Zero,
            CdsTest,
            IntPtr.Zero);

        if (testResult != DispChangeSuccessful)
        {
            message =
                $"Windows rejected {options.Width}x{options.Height} @ " +
                $"{options.RefreshRate} Hz for '{options.DeviceName}': " +
                DescribeDisplayChangeResult(testResult);

            return ExitUnsupportedMode;
        }

        message = "The requested display mode is supported.";
        return ExitSuccess;
    }

    private static int RunWizard()
    {
        Console.WriteLine("ResSwitcher9000");
        Console.WriteLine("================");
        Console.WriteLine();
        Console.WriteLine("This wizard creates a desktop shortcut.");
        Console.WriteLine("The shortcut will apply a mode silently when opened.");
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

        if (!int.TryParse(
                Console.ReadLine(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int selectedDisplayNumber) ||
            selectedDisplayNumber < 1 ||
            selectedDisplayNumber > displays.Count)
        {
            WriteError("Invalid display selection.");
            Pause();
            return ExitInvalidArguments;
        }

        DisplayInfo selectedDisplay = displays[selectedDisplayNumber - 1];

        Console.WriteLine();
        Console.Write("Enter width, height, and refresh rate (example: 2560 1440 144): ");

        if (!TryParseModeInput(
                Console.ReadLine(),
                out int width,
                out int height,
                out int refreshRate,
                out string modeError))
        {
            WriteError(modeError);
            Pause();
            return ExitInvalidArguments;
        }

        Options options = new Options
        {
            DeviceName = selectedDisplay.DeviceName,
            Width = width,
            Height = height,
            RefreshRate = refreshRate,
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
        string desktopPath = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory);

        string shortcutPath = Path.Combine(desktopPath, shortcutName + ".lnk");

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
            Console.WriteLine();
            Console.WriteLine(
                "The generated shortcut applies the mode without requesting registry persistence.");
            Console.WriteLine(
                "Use --persist manually only if you specifically want Windows profile persistence.");

            Pause();
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            WriteError($"Could not create shortcut: {ex.Message}");
            Pause();
            return ExitShortcutError;
        }
    }

    private static bool TryParseModeInput(
        string? input,
        out int width,
        out int height,
        out int refreshRate,
        out string error)
    {
        width = 0;
        height = 0;
        refreshRate = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Enter width, height, and refresh rate.";
            return false;
        }

        string[] parts = input.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 3)
        {
            error = "Enter exactly three values, such as: 2560 1440 144";
            return false;
        }

        if (!TryParsePositiveInteger(parts[0], "Width", out width, out error) ||
            !TryParsePositiveInteger(parts[1], "Height", out height, out error) ||
            !TryParsePositiveInteger(parts[2], "Refresh rate", out refreshRate, out error))
        {
            return false;
        }

        return true;
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
        List<DisplayInfo> displays = new List<DisplayInfo>();

        for (uint index = 0; ; index++)
        {
            DISPLAY_DEVICEW displayDevice = new DISPLAY_DEVICEW
            {
                Cb = Marshal.SizeOf<DISPLAY_DEVICEW>()
            };

            if (!EnumDisplayDevicesW(null, index, ref displayDevice, 0))
            {
                break;
            }

            bool attached =
                (displayDevice.StateFlags & DisplayDeviceAttachedToDesktop) != 0;

            if (!attached || string.IsNullOrWhiteSpace(displayDevice.DeviceName))
            {
                continue;
            }

            string description = string.IsNullOrWhiteSpace(displayDevice.DeviceString)
                ? "Unknown display adapter"
                : displayDevice.DeviceString;

            displays.Add(
                new DisplayInfo(
                    displayDevice.DeviceName,
                    description,
                    displayDevice.StateFlags));
        }

        return displays;
    }

    private static DisplayInfo? FindActiveDisplay(string deviceName)
    {
        foreach (DisplayInfo display in GetActiveDisplays())
        {
            if (string.Equals(
                    display.DeviceName,
                    deviceName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return display;
            }
        }

        return null;
    }

    private static bool TryGetCurrentMode(
        string deviceName,
        out DEVMODEW mode)
    {
        mode = new DEVMODEW
        {
            DmSize = checked((ushort)Marshal.SizeOf<DEVMODEW>()),
            DmDriverExtra = 0
        };

        bool success = EnumDisplaySettingsW(
            deviceName,
            EnumCurrentSettings,
            ref mode);

        // This app does not pass driver-private trailing data.
        mode.DmSize = checked((ushort)Marshal.SizeOf<DEVMODEW>());
        mode.DmDriverExtra = 0;

        return success;
    }

    private static void PrintDisplay(DisplayInfo display, int number)
    {
        bool isPrimary =
            (display.StateFlags & DisplayDevicePrimaryDevice) != 0;

        Console.WriteLine(
            $"[{number}] {display.DeviceName}" +
            (isPrimary ? " (primary)" : string.Empty));

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
                "Could not determine the ResSwitcher9000 executable path.");

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
        StringBuilder cleaned = new StringBuilder();

        foreach (char character in name)
        {
            cleaned.Append(
                Array.IndexOf(invalidCharacters, character) >= 0
                    ? '_'
                    : character);
        }

        name = cleaned.ToString().Trim().TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(name))
        {
            return fallbackName;
        }

        return name.Length > 80 ? name[..80] : name;
    }

    private static bool IsYes(string? input)
    {
        return string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(input?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeDisplayChangeResult(int result)
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
            DispChangeBadDualView => "DISP_CHANGE_BADDUALVIEW",
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
        else
        {
            int error = Marshal.GetLastWin32Error();

            // ERROR_ACCESS_DENIED means this process already has a console.
            if (error != ErrorAccessDenied)
            {
                if (!AllocConsole())
                {
                    return ExitUnexpectedError;
                }

                detachWhenFinished = true;
            }
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
        Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        Console.InputEncoding = utf8;
        Console.OutputEncoding = utf8;

        Console.SetOut(
            new StreamWriter(Console.OpenStandardOutput(), utf8)
            {
                AutoFlush = true
            });

        Console.SetError(
            new StreamWriter(Console.OpenStandardError(), utf8)
            {
                AutoFlush = true
            });

        Console.SetIn(
            new StreamReader(
                Console.OpenStandardInput(),
                utf8,
                detectEncodingFromByteOrderMarks: false));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ResSwitcher9000");
        Console.WriteLine();
        Console.WriteLine("Wizard:");
        Console.WriteLine("  ResSwitcher9000.exe");
        Console.WriteLine();
        Console.WriteLine("List active display devices:");
        Console.WriteLine("  ResSwitcher9000.exe --list");
        Console.WriteLine();
        Console.WriteLine("Apply a display mode:");
        Console.WriteLine(
            @"  ResSwitcher9000.exe --device ""\\.\DISPLAY1"" --width 2560 --height 1440 --refresh 144");
        Console.WriteLine();
        Console.WriteLine("Apply and ask Windows to save it in the current user profile:");
        Console.WriteLine(
            @"  ResSwitcher9000.exe --device ""\\.\DISPLAY1"" --width 2560 --height 1440 --refresh 144 --persist");
        Console.WriteLine();
        Console.WriteLine("Show result output:");
        Console.WriteLine(
            @"  ResSwitcher9000.exe --device ""\\.\DISPLAY1"" --width 2560 --height 1440 --refresh 144 --verbose");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  -d, --device     Display device name from --list");
        Console.WriteLine("  -w, --width      Width in physical pixels");
        Console.WriteLine("  -h, --height     Height in physical pixels");
        Console.WriteLine("  -r, --refresh    Refresh rate in whole Hz");
        Console.WriteLine("  --persist        Save mode to the current Windows user profile");
        Console.WriteLine("  --verbose        Show success or error output");
        Console.WriteLine("  --list           List active display devices");
        Console.WriteLine("  --help, -?       Show this help");
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
        uint modeNumber,
        ref DEVMODEW deviceMode);
}