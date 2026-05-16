using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Suspended.Backend
{
    public static class GameSuspendController
    {

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtResumeProcess(IntPtr processHandle);

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_MINIMIZE = 6;
        private const int SW_SHOWMINIMIZED = 2;
        private const int SW_FORCEMINIMIZE = 11;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // Whitelisted processes that should never be suspended
        private static readonly string[] WhitelistedProcesses =
        {
            "ApplicationFrameHost",
            "dwm",
            "explorer",
            "perfmon",
            "SystemSettings",
            "Taskmgr",
            "TextInputHost",
            "WinStore.App",
            "steamwebhelper",
            "EpicGamesLauncher",
            "Tooth",
            "Suspended",
            "WindowsTerminal",
            "devenv",
            "msedge",
            "Code",
            "Discord",
            "NVIDIA Overlay",
            "NVIDIA App",
            "Notepad",
            "XboxPcApp",
            "Gamebar_Widget",
            "MSI Center M",
            "IntelGraphicsSoftware"
        };

        public static IntPtr GetMainWindowHandle(int pid)
        {
            IntPtr hwnd = IntPtr.Zero;

            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint windowPid);
                if (windowPid == pid && IsWindowVisible(h))
                {
                    hwnd = h;
                    return false; // stop enumerating
                }
                return true; // continue
            }, IntPtr.Zero);

            return hwnd;
        }

        public static bool IsProcessSuspended(Process process)
        {
            try
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    if (thread.ThreadState == ThreadState.Wait &&
                        thread.WaitReason == ThreadWaitReason.Suspended)
                    {
                        // Found a suspended thread
                        return true;
                    }
                }
            }
            catch
            {
                // Process might have exited or is protected
            }

            return false;
        }

        private static bool IsWhitelisted(string processName)
        {
            return WhitelistedProcesses.Any(p =>
                string.Equals(p, processName, StringComparison.OrdinalIgnoreCase));
        }

        public static async Task<int?> SuspendForegroundApp()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine("[GameSuspendController] No foreground window detected.");
                return null;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    if (IsWhitelisted(process.ProcessName))
                    {
                        Console.WriteLine($"[GameSuspendController] Skipping whitelisted process: {process.ProcessName}");
                        return null;
                    }

                    if (IsProcessSuspended(process))
                    {
                        Console.WriteLine($"[GameSuspendController] Process already suspended: {process.ProcessName}");
                        return null;
                    }

                    // Minimize the window first to avoid issues with certain games
                    ShowWindowAsync(hwnd, SW_FORCEMINIMIZE);

                    // 500 ms delay (async, does NOT block the caller)
                    await Task.Delay(500);

                    Console.WriteLine($"[GameSuspendController] Minimized and Suspending process: {process.ProcessName} ({processId})");
                    SuspendProcessTree(process.Id);
                    return (int)processId;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to suspend process: {ex.Message}");
                return null;
            }

            return null;
        }
        public static async Task<bool> SuspendApp(int processId)
        {
            try
            {
                IntPtr hwnd = GetMainWindowHandle(processId);
                if (hwnd == IntPtr.Zero)
                {
                    Console.WriteLine("[GameSuspendController] SuspendApp couldn't get Window handle.");
                    return false;
                }
                using (var process = Process.GetProcessById(processId))
                {
                    if (IsWhitelisted(process.ProcessName))
                    {
                        Console.WriteLine($"[GameSuspendController] Skipping whitelisted process: {process.ProcessName}");
                        return false;
                    }

                    if (IsProcessSuspended(process))
                    {
                        Console.WriteLine($"[GameSuspendController] Process already suspended: {process.ProcessName}");
                        return false;
                    }

                    // Minimize the window first to avoid issues with certain games
                    ShowWindowAsync(hwnd, SW_FORCEMINIMIZE);

                    // 500 ms delay (async, does NOT block the caller)
                    await Task.Delay(500);

                    Console.WriteLine($"[GameSuspendController] Minimized and Suspending process: {process.ProcessName} ({processId})");
                    SuspendProcessTree(processId);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to suspend process: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> ResumeForegroundApp()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine("[GameSuspendController] No foreground window detected.");
                return false;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    if (IsWhitelisted(process.ProcessName))
                    {
                        Console.WriteLine($"[GameSuspendController] Skipping whitelisted process: {process.ProcessName}");
                        return false;
                    }

                    Console.WriteLine($"[GameSuspendController] Resuming process: {process.ProcessName} ({processId})");
                    ResumeProcessTree(process.Id);

                    // After resuming, restore the window
                    ShowWindowAsync(hwnd, SW_RESTORE);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to resume process: {ex.Message}");
                return false;
            }

            return false;
        }

        public static async Task<bool> ResumeApp(int processId)
        {
            try
            {
                IntPtr hwnd = GetMainWindowHandle(processId);
                if (hwnd == IntPtr.Zero)
                {
                    Console.WriteLine("[GameSuspendController] ResumeApp couldn't get Window handle.");
                    return false;
                }
                using (var process = Process.GetProcessById(processId))
                {
                    if (IsWhitelisted(process.ProcessName))
                    {
                        Console.WriteLine($"[GameSuspendController] Skipping whitelisted process: {process.ProcessName}");
                        return false;
                    }

                    Console.WriteLine($"[GameSuspendController] Resuming process: {process.ProcessName} ({processId})");
                    ResumeProcessTree(processId);

                    // After resuming, restore the window
                    ShowWindowAsync(hwnd, SW_RESTORE);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to resume process: {ex.Message}");
                return false;
            }
        }

        private static List<int> GetChildProcesses(int parentPid)
        {
            var childPids = new List<int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE)
                return childPids;

            try
            {
                PROCESSENTRY32 entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32First(snapshot, ref entry))
                    return childPids;

                do
                {
                    if (entry.th32ParentProcessID == parentPid)
                        childPids.Add((int)entry.th32ProcessID);
                }
                while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return childPids;
        }

        public static void SuspendProcessTree(int pid)
        {
            if (pid == 0) return;

            SuspendOrResumeProcess(pid, suspend: true);

            foreach (var childPid in GetChildProcesses(pid))
                SuspendProcessTree(childPid);
        }

        public static void ResumeProcessTree(int pid)
        {
            if (pid == 0) return;

            SuspendOrResumeProcess(pid, suspend: false);

            foreach (var childPid in GetChildProcesses(pid))
                ResumeProcessTree(childPid);
        }


        private static void SuspendOrResumeProcess(int pid, bool suspend)
        {
            IntPtr hProc = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
            if (hProc == IntPtr.Zero)
            {
                Console.WriteLine($"[ProcessTreeController] Cannot open PID {pid}. Error: {Marshal.GetLastWin32Error()}");
                return;
            }

            try
            {
                uint result = suspend ? NtSuspendProcess(hProc) : NtResumeProcess(hProc);
                Console.WriteLine($"[{(suspend ? "Suspend" : "Resume")}] PID {pid} success: {result == 0}");
            }
            finally
            {
                CloseHandle(hProc);
            }
        }
    }
}
