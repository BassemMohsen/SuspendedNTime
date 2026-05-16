using Suspended.Backend;
using System;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;

class ModernStandbyMonitor
{
    [DllImport("user32.dll")]
    private static extern int SendMessage(int hWnd, int Msg, int wParam, int lParam);

    private const int HWND_BROADCAST = 0xffff;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_SUSPEND = 0xF170;
    private int autoSuspendSetting;
    private int goBackToSleepSetting;
    private int? lastSuspendedPid = null;

    private readonly XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public ModernStandbyMonitor()
    {
        autoSuspendSetting = SettingsManager.Get<int>("AutoSuspend");
        goBackToSleepSetting = SettingsManager.Get<int>("GoBackToSleep");

        string xpath = "*[System[(EventID=506 or EventID=507) and Provider[@Name='Microsoft-Windows-Kernel-Power']]]";

        var query = new EventLogQuery("System", PathType.LogName, xpath);
        var watcher = new EventLogWatcher(query);

        watcher.EventRecordWritten += OnEventRecordWritten;
        watcher.Enabled = true;

        Console.WriteLine("[ModernStandbyMonitor] Watching for sleep (506) / wake (507) events...");
    }

    private async Task SuspendSystem()
    {
        SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_SUSPEND, 2);
    }

    public void UpdateAutoSuspendSetting(int autoSuspendEnabled)
    {
        autoSuspendSetting = autoSuspendEnabled;
    }

    public void UpdateGoBackToSleepSetting(int goBackToSleepEnabled)
    {
        goBackToSleepSetting = goBackToSleepEnabled;
    }

    private void OnEventRecordWritten(object sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord == null)
            return;

        int eventId = e.EventRecord.Id;
        DateTime eventTime = e.EventRecord.TimeCreated?.ToLocalTime() ?? DateTime.Now;

        if (eventId == 506)
        {
            Console.WriteLine($"[ModernStandbyMonitor] System entered Modern Standby at {eventTime:yyyy-MM-dd HH:mm:ss}");
            if (autoSuspendSetting == 1)
            {
                // Await suspend to capture the suspended PID so we can resume later
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var pid = await GameSuspendController.SuspendForegroundApp();
                        if (pid.HasValue)
                        {
                            lastSuspendedPid = pid.Value;
                            Console.WriteLine($"[ModernStandbyMonitor] Recorded suspended PID: {pid.Value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ModernStandbyMonitor] Error suspending foreground app: {ex}");
                    }
                });
            }

        }
        else if (eventId == 507)
        {
            string reason = ParseWakeReason(e.EventRecord);
            Console.WriteLine($"[ModernStandbyMonitor] System woke up from Modern Standby at {eventTime:yyyy-MM-dd HH:mm:ss}. Reason: {reason}");
            if (!reason.Equals("Power Button", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[ModernStandbyMonitor] System woke up for other reason but power button");

                if (goBackToSleepSetting == 1)
                {
                    Console.WriteLine($"[ModernStandbyMonitor] Go back go to sleep!");

                    // Resume foreground app, so that they can respond to SC_SUSPEND
                    if (autoSuspendSetting == 1)
                        _ = GameSuspendController.ResumeForegroundApp();

                    _ = SuspendSystem();
                }
            }
            else
            {
                // It's power button, resume foreground app
                if (autoSuspendSetting == 1)
                {
                    // Resume the previously suspended app if we recorded one
                    if (lastSuspendedPid.HasValue)
                    {
                        _ = Task.Run(async () =>
                        {
                            bool ok = await GameSuspendController.ResumeApp(lastSuspendedPid.Value);
                            Console.WriteLine($"[ModernStandbyMonitor] ResumeApp({lastSuspendedPid.Value}) => {ok}");
                            lastSuspendedPid = null;
                        });
                    }
                    else
                    {
                        _ = GameSuspendController.ResumeForegroundApp();
                    }
                }

            }
        }
    }

    private string ParseWakeReason(EventRecord evt)
    {
        try
        {
            var xml = evt.ToXml();
            var doc = XDocument.Parse(xml);

            var reasonVal = doc
                .Descendants(ns + "Data")
                .FirstOrDefault(x => x.Attribute("Name")?.Value == "Reason")?.Value;

            if (!int.TryParse(reasonVal, out int reasonCode))
                return "Unknown";

            return reasonCode switch
            {
                0 => "Unknown",
                1 => "Power Button",
                7 => "Joystick",
                28 => "ChargerConnected",
                _ => $"Unknown (Reason code {reasonCode})"
            };
        }
        catch
        {
            return "Unknown (failed to parse)";
        }
    }
}
