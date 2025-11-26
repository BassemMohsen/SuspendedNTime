using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Controls;
using System.Text.Json;

namespace Suspended.Backend
{
    internal class Handler
    {
        private PowerPolicyController powerPolicyController;
        private ModernStandbyMonitor modernStandbymonitor;
        private Communication _communication;

        public enum ScalingModeMethod : int
        {
            DISPLAY_SCALING_MAINTAIN_ASPECT_RATIO = 0,
            GPU_SCALING_MAINTAIN_ASPECT_RATIO = 1,
            GPU_SCALING_STRETCH = 2,
            GPU_SCALING_CENTER = 3,
            RETRO_SCALING_INTEGER = 4,
            RETRO_SCALING_NEAREST_NEIGHBOUR = 5,
            UNKNOWN = 6
        }

        public Handler()
        {
            powerPolicyController = new PowerPolicyController();
            modernStandbymonitor = new ModernStandbyMonitor();

            var manager = new WindowProcessManager(TimeSpan.FromMilliseconds(2500));

        }

        public void Register(Communication comm)
        {
            _communication = comm;
            comm.ConnectedEvent += OnConnected;
            comm.ReceivedEvent += OnReceived;
        }

        void OnConnected(object sender, EventArgs e)
        {
            (sender as Communication).Send("connected");
        }

        void OnReceived(object sender, string message)
        {
            var comm = sender as Communication;
            string[] args = message.Split(' ');
            if (args.Length == 0)
                return;
            switch (args[0])
            {
                case "get-auto-suspend":
                    {
                        int autosuspend = SettingsManager.Get<int>("AutoSuspend");
                        Console.WriteLine($"[Server Handler] Responding with AutoSuspend Value {autosuspend}");
                        (sender as Communication).Send($"auto-suspend" + ' ' + $"{autosuspend}");
                    }
                    break;
                case "set-auto-suspend":
                    {
                        if (int.TryParse(args[1], out int autosuspend))
                        {
                            SettingsManager.Set("AutoSuspend", autosuspend);
                            modernStandbymonitor.UpdateAutoSuspendSetting(autosuspend);
                            Console.WriteLine($"[Server Handler] Setting AutoSuspend to {autosuspend}");
                        }
                    }
                    break;
                case "resume-active-game":
                    {
                        GameSuspendController.ResumeForegroundApp();
                        Console.WriteLine($"[Server Handler] ResumedForeground App");
                    }
                    break;
                case "suspend-active-game":
                    {
                        GameSuspendController.SuspendForegroundApp();
                        Console.WriteLine($"[Server Handler] Suspended Foreground App");
                    }
                    break;

                case "get-go-back-to-sleep":
                    {
                        int gobacktosleep = SettingsManager.Get<int>("GoBackToSleep");
                        Console.WriteLine($"[Server Handler] Responding with Go back to sleep Value {gobacktosleep}");
                        (sender as Communication).Send($"go-back-to-sleep" + ' ' + $"{gobacktosleep}");
                    }
                    break;
                case "set-go-back-to-sleep":
                    {
                        if (int.TryParse(args[1], out int gobacktosleep))
                        {
                            SettingsManager.Set("GoBackToSleep", gobacktosleep);
                            modernStandbymonitor.UpdateGoBackToSleepSetting(gobacktosleep);
                            Console.WriteLine($"[Server Handler] Setting Go back to sleep to {gobacktosleep}");
                        }
                    }
                    break;

                case "get-power-button-action":
                    {
                        if (powerPolicyController == null)
                        {
                            powerPolicyController = new PowerPolicyController();
                        }
                        Console.WriteLine($"[Server Handler] Responding with Power Button Action {powerPolicyController.GetPowerButtonAction().ToString()}");

                        (sender as Communication).Send("power-button-action" + ' ' + (int)powerPolicyController.GetPowerButtonAction());
                    }
                    break;
                case "set-power-button-action":
                    {
                        if (powerPolicyController == null)
                        {
                            powerPolicyController = new PowerPolicyController();
                        }
                        Console.WriteLine($"[Server Handler] Setting Power Button Action to {args[1]}");
                        if (Enum.TryParse(args[1], out PowerPolicyController.PowerButtonAction action))
                        {
                            powerPolicyController.SetPowerButtonAction(action);
                        }
                        else
                        {
                            Console.WriteLine($"[Server Handler] Invalid Power Button Action: {args[1]}");
                        }
                    }
                    break;

                case "get-enhanced-sleep":
                    {
                        int enhancedSleep = SettingsManager.Get<int>("EnhancedSleep");
                        Console.WriteLine($"[Server Handler] Responding with Enhanced sleep Value {enhancedSleep}");
                        (sender as Communication).Send($"enhanced-sleep" + ' ' + $"{enhancedSleep}");
                    }
                    break;
                case "set-enhanced-sleep":
                    {
                        if (int.TryParse(args[1], out int enhancedSleep))
                        {
                            SettingsManager.Set("EnhancedSleep", enhancedSleep);
                            Console.WriteLine($"[Server Handler] Setting Enhanced Sleep to {enhancedSleep}");
                            if (powerPolicyController == null)
                            {
                                powerPolicyController = new PowerPolicyController();
                            }
                            if(enhancedSleep == 0)
                            {
                                powerPolicyController.RestoreAll();
                            }
                            else
                            {
                                powerPolicyController.ApplyAll();
                            }
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        public void sendLaunchGameBarWidget()
        {
            if (_communication != null) { 
                Console.WriteLine($"[Server Handler] Send launch-gamebar-widget");
                _communication.Send("launch-gamebar-widget");
            }
        }
    }
}
