using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Gaming.Preview.GamesEnumeration;
using Suspended.GameIconExtractor;

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
        private readonly object windowsLock = new();
        private readonly Dictionary<IntPtr, WindowInfo> windows = new();
        private IntPtr lastForeground = IntPtr.Zero;

        private readonly Timer refreshTimer;
        private TimeSpan refreshRate;
        private bool refreshEnabled = true;
        string localState = "";
        public bool suspendOnFocusLost = false;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(
                IntPtr hProcess,
                int dwFlags,
                StringBuilder lpExeName,
                ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        private const int QueryLimitedInformation = 0x00001000;

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
            "NVIDIA App",
            "Notepad",
            "XboxPcApp",
            "Gamebar_Widget",
            "MSI Center M",
            "IntelGraphicsSoftware"
        };

        // ================================
        // Constructor
        // ================================
        public WindowProcessManager(TimeSpan refreshRate)
        {

            localState = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) +
                @"\Packages\BassemNomany.SuspendedNTime_ah2yj8jdj20z4\LocalState\Icons";

            this.refreshRate = refreshRate;
            refreshTimer = new Timer(
            _ => { if (refreshEnabled) RefreshState(); },
            null,
            TimeSpan.Zero,
            refreshRate);
        }

        // ================================
        // Public Data Accessors
        // ================================
        public IReadOnlyCollection<WindowInfo> Windows => windows.Values.ToList();

        // ================================
        // PUBLIC REFRESH CONTROL API
        // ================================
        public void StartRefresh()
        {
            refreshEnabled = true;
            refreshTimer.Change(TimeSpan.Zero, refreshRate);
        }

        public void StopRefresh()
        {
            refreshEnabled = false;
            refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void SetRefreshRate(TimeSpan newRate)
        {
            refreshRate = newRate;

            if (refreshEnabled)
                refreshTimer.Change(TimeSpan.Zero, newRate);
        }

        public TimeSpan GetRefreshRate() => refreshRate;
        public bool IsRefreshing => refreshEnabled;

        public bool IsForegroundAppSuspended = false;

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

                //if (IsCloaked(hWnd))
                //    return true; // skip cloaked/UWP windows

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
                    var size = 1024;
                    var iconSb = new StringBuilder(size);
                    var handle = OpenProcess(QueryLimitedInformation, false, (int)pid);
                    var success = QueryFullProcessImageName(handle, 0, iconSb, ref size);
                    CloseHandle(handle);


                    string fullExePath = iconSb.ToString();
                    string exeName = System.IO.Path.GetFileName(fullExePath);
                    string cacheFileName = exeName + ".png";
                    string cacheRelativePath = "Icons/" + cacheFileName;

                    string msAppDataUri = "ms-appdata:///local/" + cacheRelativePath;

                    TriggerIconBuild(fullExePath, exeName);

                    var info = new WindowInfo
                    {
                        Handle = hWnd,
                        ProcessId = (int)pid,
                        Title = titleSb.ToString(),
                        ProcessName = processName,
                        ProcessExePath = iconSb.ToString(),
                        ProcessIconPath = msAppDataUri
                    };
                    lock (windowsLock)
                    {
                        windows[hWnd] = info;
                    }
                    OnWindowAppeared?.Invoke(info);
                }

                return true;
            }, IntPtr.Zero);

            // Detect closed windows
            var closed = windows.Keys.Where(h => !current.Contains(h)).ToList();
            foreach (var hwnd in closed)
            {
                lock (windowsLock)
                {
                    var info = windows[hwnd];
                    windows.Remove(hwnd);
                    OnWindowClosed?.Invoke(info);
                }
            }
        }

        // ================================
        // 2. Foreground Window Detection
        // ================================
        private void RefreshForeground()
        {
            IntPtr fg = GetForegroundWindow();

            if (windows.TryGetValue(fg, out var currentFocusGame))
                IsForegroundAppSuspended = currentFocusGame.IsSuspended;

            if (fg != lastForeground)
            {
                if (suspendOnFocusLost)
                {
                    if (windows.TryGetValue(lastForeground, out var lastFocusGameInfo))
                    {
                        if (!lastFocusGameInfo.IsSuspended)
                        {
                            GameSuspendController.SuspendApp(lastFocusGameInfo.ProcessId);
                        }
                    }
                }

                lastForeground = fg;
                lock (windowsLock)
                {
                    if (windows.TryGetValue(fg, out var currentFocusGameInfo))
                        OnForegroundChanged?.Invoke(currentFocusGameInfo);
                }
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

                // If first thread is suspended, we consider the process suspended
                if (process.Threads.Count > 0)
                {
                    var t = process.Threads[0];
                    return t.ThreadState == System.Diagnostics.ThreadState.Wait &&
                           t.WaitReason == ThreadWaitReason.Suspended;
                }
            }
            catch { }
            return false;
        }


        public List<GameInfo> GetGamesList()
        {
            lock (windowsLock)
            {
                return windows.Values.Select(w => new GameInfo
                {
                    ProcessId = w.ProcessId,
                    Title = w.Title,
                    IconPath = w.ProcessIconPath,
                    IsSuspended = w.IsSuspended
                }).ToList();
            }
        }

        private void TriggerIconBuild(string fullExePath, string exeName)
        {
            Directory.CreateDirectory(localState);
            string cachePath = System.IO.Path.Combine(localState, exeName + ".png");

            if (!File.Exists(cachePath))
            {
                using Bitmap bmp = GameIconExtractor.IconHelper.GetExeIcon(fullExePath, 64, 6);
                bmp.Save(cachePath, ImageFormat.Png);

                string grayCachePath = Path.Combine(localState, exeName + "-grayscale.png");
                if (!File.Exists(grayCachePath))
                {
                    using Bitmap grayBmp = GameIconExtractor.IconHelper.MakeGrayscale(bmp);
                    grayBmp.Save(grayCachePath, ImageFormat.Png);
                }

            }
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
        public string ProcessExePath { get; set; } = "";
        public string ProcessIconPath { get; set; } = "";
        public bool IsSuspended { get; set; }
    }

    public struct GameInfo
    {
        public GameInfo()
        {
        }
        public int ProcessId { get; set; }
        public string Title { get; set; } = "";
        public string IconPath { get; set; } = "";
        public bool IsSuspended { get; set; }
    }
  
}
