using Microsoft.Gaming.XboxGameBar;
using SharpDX.Direct3D9;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.ServiceModel.Channels;
using System.Text.Json;
using System.Windows.Input;
using Windows.ApplicationModel.AppExtensions;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Security.Authentication.Web;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Suspended
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a <see cref="Frame">.
    /// </summary>
    public sealed partial class MainPage : IDisposable
    {


        private static MainPageModel _modelBase = new MainPageModel();
        private MainPageModelWrapper _model;

        public MainPage()
        {
            InitializeComponent();
            _model = _modelBase.GetWrapper(Dispatcher);
            this.DataContext = _model;

            Backend.Instance.MessageReceivedEvent += Backend_OnMessageReceived;
            Backend.Instance.ClosedOrFailedEvent += Backend_OnClosedOrFailed;
            if (Backend.Instance.IsConnected)
                ConnectedInitialize();
            else
                PanelSwitch(false);
        }

        public void Dispose()
        {
            Backend.Instance.MessageReceivedEvent -= Backend_OnMessageReceived;
            Backend.Instance.ClosedOrFailedEvent -= Backend_OnClosedOrFailed;
        }

        private void ConnectedInitialize()
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => PanelSwitch(true));
            Backend.Instance.Send("get-auto-suspend");
            Backend.Instance.Send("get-go-back-to-sleep");
            Backend.Instance.Send("get-power-button-action");
            Backend.Instance.Send("get-enhanced-sleep");
            Backend.Instance.Send("get-game-list");
            Backend.Instance.Send("init");
        }

        private void PanelSwitch(bool isBackendAlive)
        {
            if (isBackendAlive)
            {
                StartingBackgroundserviceTextBlock.Visibility = Visibility.Collapsed;
                LaunchBackendButton.IsTapEnabled = false;
            }
            else
            {
                StartingBackgroundserviceTextBlock.Visibility = Visibility.Visible;
                LaunchBackendButton.IsTapEnabled = true;
            }
        }

        private void Backend_OnMessageReceived(object sender, string message)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Backend_OnMessageReceived_Impl(sender, message));
        }

        private void Backend_OnMessageReceived_Impl(object sender, string message)
        {
            var backend = sender as Backend;
            string[] args = message.Split(' ');
            if (args.Length == 0)
                return;
            switch (args[0])
            {
                case "connected":
                    ConnectedInitialize();
                    break;
                case "autostart":
                    _model.SetAutoStartVar(bool.Parse(args[1]));
                    break;
                case "auto-suspend":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Auto Suspend Enabled to {args[1]}");
                    _model.AutoSuspendEnabled = Convert.ToBoolean(int.Parse(args[1]));
                    AutoSuspendToggle.IsOn = _model.AutoSuspendEnabled;
                    break;
                case "go-back-to-sleep":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Go back to sleep Enabled to {args[1]}");
                    _model.GoBackToSleepEnabled = Convert.ToBoolean(int.Parse(args[1]));
                    GoBackToSleepToggle.IsOn = _model.GoBackToSleepEnabled;
                    break;
                case "power-button-action":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Power Button Action {args[1]}");
                    _model.PowerButtonAction = int.Parse(args[1]);
                    PowerButtonActionComboBox.SelectedValue = _model.PowerButtonAction;
                    break;
                case "game-list":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating GameList");
                    
                    args = message.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (args.Length < 2)
                    {
                        Trace.WriteLine("Malformed message: missing JSON payload");
                        break;
                    }
                    //Trace.WriteLine("Raw payload: " + args[1]);
                    
                    try
                    {
                        if (args.Length < 2)
                            return;
                        var games = System.Text.Json.JsonSerializer.Deserialize<List<GameInfo>>(args[1]);
                        _model.GamesList = new ObservableCollection<GameInfo>(games);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Failed to parse Games List: {ex.Message}");
                    }
                    break;
                case "enhanced-sleep":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Enhanced Sleep {args[1]}");
                    _model.EnhancedSleepEnabled = Convert.ToBoolean(int.Parse(args[1]));
                    EnhancedSleepToggle.IsOn = _model.EnhancedSleepEnabled;
                    break;
            }
        }

        private async void launchGameBarWidget()
        {
            var app = (App)Application.Current;
            var widgetControl = app._xboxGameBarWidgetControl;

            if (widgetControl != null)
            {
                Trace.WriteLine($"[MainPage.xaml.cs] widgetControl.ActivateAsync");
                await widgetControl.ActivateAsync("Suspended.XboxGameBarUI");
            }
        }

        private void Backend_OnClosedOrFailed(object _, EventArgs args)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => PanelSwitch(false));
        }

        private void LaunchBackendButton_OnClick(object sender, RoutedEventArgs e)
        {
            _ = Backend.LaunchBackend();
        }

        private void OnResumeButtonClick(object sender, RoutedEventArgs e)
        {
            _model.ResumeActiveGame();
        }

        private void OnSuspendButtonClick(object sender, RoutedEventArgs e)
        {
            _model.SuspendActiveGame();
        }

        private void AutoSuspedToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // handle Auto Suspend toggle changes
            // handle Enhanced Sleep toggle changes
            if (sender is ToggleSwitch toggleSwitch)
            {
                _model.SetAutoSuspendEnabledVar(toggleSwitch.IsOn);
            }
        }

        private async void GamesListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is GameInfo game)
            {
                if (game.IsSuspended)
                {
                    Backend.Instance.Send($"resume-game { Convert.ToInt32(game.ProcessId)}");
                }
                else
                {
                    Backend.Instance.Send($"suspend-game {Convert.ToInt32(game.ProcessId)}");
                }

            }
        }

        private void PowerButtonActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle Power Button Presses
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            {
                // Extract the Tag (0, 1, or 2)
                if (item.Tag is double tagValue)
                {
                    if (DataContext is MainPageModelWrapper model)
                    {
                        _model.SetPowerButtonActionVar(tagValue);
                    }
                }
            }
        }

        private void EnhancedSleepToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // handle Enhanced Sleep toggle changes
            if (sender is ToggleSwitch toggleSwitch)
            {
                _model.SetEnhancedSleepEnabledVar(toggleSwitch.IsOn);
            }
        }

        private void GoBackToSleepToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // handle Go back to sleep changes
            if (sender is ToggleSwitch toggleSwitch)
            {
                _model.SetGoBackToSleepEnabledVar(toggleSwitch.IsOn);
            }
        }
    }
}
