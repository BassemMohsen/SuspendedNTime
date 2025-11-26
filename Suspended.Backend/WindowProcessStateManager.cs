using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Suspended.Backend
{
    public class WindowProcessManager
    {
        // ================================
        // EVENTS
        // ================================
        public event Action<WindowInfo>? OnWindowAppeared;
        public event Action<WindowInfo>? OnWindowClosed;
        public event Action<WindowInfo>? OnForegroundChanged;

        public event Action<WindowInfo>? OnProcessSuspended;
        public event Action<WindowInfo>? OnProcessResumed;

        // ================================
        // Internal tracking
        // ================================
        private readonly Dictionary<IntPtr, WindowInfo> windows = new();
        private IntPtr lastForeground = IntPtr.Zero;

        private readonly Timer refreshTimer;

        // ================================
        // Win32 API
        // ================================
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private const int DWMWA_CLOAKED = 14;

        // ================================
        // Whitelisted processes that should never be suspended
        // ================================

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
            "Notepad"
        };

        // ================================
        // Constructor
        // ================================
        public WindowProcessManager(TimeSpan refreshRate)
        {
            refreshTimer = new Timer(_ => RefreshState(), null, TimeSpan.Zero, refreshRate);
        }

        // ================================
        // Public Data Accessors
        // ================================
        public IReadOnlyCollection<WindowInfo> Windows => windows.Values.ToList();

        // ================================
        // Refresh Logic
        // ================================
        private void RefreshState()
        {
            try
            {
                RefreshWindows();
                RefreshForeground();
                RefreshProcessState();

                foreach (var w in windows.Values)
                {
                    Console.WriteLine($"[WINDOW] PID={w.ProcessId}, Hwnd={w.Handle}, Name='{w.ProcessName}' , Title='{w.Title}', IsSuspended='{w.IsSuspended}' ");
                }
            }
            catch { /* swallow errors; no crashes */ }
        }

        private static bool IsCloaked(IntPtr hWnd)
        {
            int value = 0;
            int hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out value, sizeof(int));
            if (hr != 0)
                return false;

            return value != 0; // 1,2,4 = cloaked
        }

        private static bool IsWhitelisted(string processName)
        {
            return WhitelistedProcesses.Any(p =>
                string.Equals(p, processName, StringComparison.OrdinalIgnoreCase));
        }

        // ================================
        // 1. Window Enumeration
        // ================================
        private void RefreshWindows()
        {
            var current = new HashSet<IntPtr>();

            EnumWindows((hWnd, _) =>
            {
            if (!IsWindowVisible(hWnd))
                return true; // skip

            if (IsCloaked(hWnd))
                return true; // skip cloaked/UWP windows

            current.Add(hWnd);

            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);

            var titleSb = new StringBuilder(512);
            GetWindowText(hWnd, titleSb, titleSb.Capacity);

            string title = titleSb.ToString();
            if (string.IsNullOrWhiteSpace(title))
                return true; // skip windows with no title

            // process executable
            String processName = "";
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                {
                        processName = process.ProcessName;
                        if (IsWhitelisted(process.ProcessName))
                        {
                            //Console.WriteLine($"[GameSuspendController] Skipping whitelisted process: {process.ProcessName}");
                            return true; // skip windows with no title
                        }
                }
            }
            catch
            {
                return true; // process no longer exists → skip
            }

            if (!windows.ContainsKey(hWnd))
            {
                var info = new WindowInfo
                {
                    Handle = hWnd,
                    ProcessId = (int)pid,
                    Title = titleSb.ToString(),
                    ProcessName = processName
                    };

                    windows[hWnd] = info;

                    OnWindowAppeared?.Invoke(info);
                }

                return true;
            }, IntPtr.Zero);

            // Detect closed windows
            var closed = windows.Keys.Where(h => !current.Contains(h)).ToList();
            foreach (var hwnd in closed)
            {
                var info = windows[hwnd];
                windows.Remove(hwnd);
                OnWindowClosed?.Invoke(info);
            }
        }

        // ================================
        // 2. Foreground Window Detection
        // ================================
        private void RefreshForeground()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != lastForeground)
            {
                lastForeground = fg;

                if (windows.TryGetValue(fg, out var info))
                    OnForegroundChanged?.Invoke(info);
            }
        }

        // ================================
        // 3. Process suspension tracking
        // ================================
        private void RefreshProcessState()
        {
            foreach (var proc in windows.Values)
            {
                bool isSuspended = CheckIfProcessSuspended(proc.ProcessId);

                if (isSuspended && !proc.IsSuspended)
                {
                    proc.IsSuspended = true;
                    OnProcessSuspended?.Invoke(proc);
                }
                else if (!isSuspended && proc.IsSuspended)
                {
                    proc.IsSuspended = false;
                    OnProcessResumed?.Invoke(proc);
                }
            }
        }

        private bool CheckIfProcessSuspended(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);

                if (process.Threads.Count > 0)
                {
                    var t = process.Threads[0];
                    return t.ThreadState == System.Diagnostics.ThreadState.Wait &&
                           t.WaitReason == ThreadWaitReason.Suspended;
                }

                /*
                foreach (ProcessThread thread in process.Threads)
                {
                    if (thread.ThreadState == System.Diagnostics.ThreadState.Wait &&
                        thread.WaitReason == ThreadWaitReason.Suspended)
                    {
                        return true;
                    }
                }*/
            }
            catch { }
            return false;
        }
    }

    // ================================
    //  Supporting Data Types
    // ================================
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public int ProcessId { get; set; }
        public string Title { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public bool IsSuspended { get; set; }
        public override string ToString() => $"{ProcessId} | {Handle} | {Title} | {ProcessName}";
    }
}
