using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;

namespace ResSwitcher
{
    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeConsole();

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion; public short dmDriverVersion; public short dmSize; public short dmDriverExtra;
            public int dmFields; public int dmPositionX; public int dmPositionY; public int dmDisplayOrientation;
            public int dmDisplayFixedOutput; public short dmColor; public short dmDuplex; public short dmYResolution;
            public short dmTTOption; public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public short dmBitsPerPel; public int dmPelsWidth; public int dmPelsHeight;
            public int dmDisplayFlags; public int dmDisplayFrequency;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
        const int DM_PELSWIDTH = 0x00080000;
        const int DM_PELSHEIGHT = 0x00100000;
        const int DM_DISPLAYFREQUENCY = 0x00400000;
        const uint CDS_UPDATEREGISTRY = 0x00000001;

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                ApplySettings(args);
                return;
            }

            AllocConsole();
            RunWizard();
            FreeConsole();
        }

        static void ApplySettings(string[] args)
        {
            string device = "\\.\DISPLAY1";
            int width = 0, height = 0, refresh = 0;

            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "-d" || args[i] == "--device") && i + 1 < args.Length) device = args[++i];
                if ((args[i] == "-w" || args[i] == "--width") && i + 1 < args.Length) int.TryParse(args[++i], out width);
                if ((args[i] == "-h" || args[i] == "--height") && i + 1 < args.Length) int.TryParse(args[++i], out height);
                if ((args[i] == "-r" || args[i] == "--refresh") && i + 1 < args.Length) int.TryParse(args[++i], out refresh);
            }

            if (width == 0 || height == 0 || refresh == 0) return;

            DEVMODE v = new DEVMODE();
            v.dmSize = (short)Marshal.SizeOf(v);
            v.dmPelsWidth = width;
            v.dmPelsHeight = height;
            v.dmDisplayFrequency = refresh;
            v.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

            ChangeDisplaySettingsEx(device, ref v, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        }

        static void RunWizard()
        {
            Console.WriteLine("ResSwitcher - Shortcut Generator");
            Console.WriteLine("--------------------------------\n");

            List<string> devices = new List<string>();
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);
            uint id = 0;

            Console.WriteLine("Detected Monitors:");
            while (EnumDisplayDevices(null, id, ref d, 0))
            {
                if ((d.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == DISPLAY_DEVICE_ATTACHED_TO_DESKTOP)
                {
                    devices.Add(d.DeviceName);
                    Console.WriteLine($"[{devices.Count}] {d.DeviceName} ({d.DeviceString})");
                }
                id++;
                d.cb = Marshal.SizeOf(d);
            }

            if (devices.Count == 0)
            {
                Console.WriteLine("No displays found. Press any key to exit.");
                Console.ReadKey();
                return;
            }

            Console.Write($"\nSelect a monitor (1-{devices.Count}): ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel < 1 || sel > devices.Count)
            {
                Console.WriteLine("Invalid selection. Exiting.");
                System.Threading.Thread.Sleep(2000);
                return;
            }
            string selectedDevice = devices[sel - 1];

            Console.Write("\nEnter Target Width, Height, and Refresh Rate (e.g., 1920 1080 240): ");
            string[] inputs = Console.ReadLine().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (inputs.Length < 3 || !int.TryParse(inputs[0], out int w) || !int.TryParse(inputs[1], out int h) || !int.TryParse(inputs[2], out int r))
            {
                Console.WriteLine("Invalid input. Must be three numbers separated by spaces.");
                System.Threading.Thread.Sleep(2000);
                return;
            }

            Console.Write("\nEnter a name for your shortcut (e.g., CS2 1080p): ");
            string shortcutName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(shortcutName)) shortcutName = "Resolution Shortcut";

            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktopPath, shortcutName + ".lnk");
                
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                shortcut.Arguments = $"-d {selectedDevice} -w {w} -h {h} -r {r}";
                shortcut.WorkingDirectory = Path.GetDirectoryName(shortcut.TargetPath);
                shortcut.WindowStyle = 7;
                shortcut.Save();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\nSuccess! Shortcut '{shortcutName}.lnk' created on your Desktop.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nFailed to create shortcut: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
        }
    }
}
