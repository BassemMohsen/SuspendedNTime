using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace Suspended
{
    public struct GameInfo
    {
        public GameInfo()
        {
        }
        public int ProcessId { get; set; }
        public string Title { get; set; } = "";
        public string ProcessIconPath { get; set; } = "";
        public bool IsSuspended { get; set; }
    }

    internal class MainPageModelWrapper : INotifyPropertyChanged, IDisposable
	{
		private MainPageModel _base;
		private CoreDispatcher _dispatcher;



        public MainPageModelWrapper(MainPageModel baseModel, CoreDispatcher dispatcher)
		{
			_base = baseModel;
			_dispatcher = dispatcher;
		}

        public ObservableCollection<GameInfo> GamesList
        {
            get { lock (_base) { return _base.gamesList; } }
            set
            {
                lock (_base)
                {
                    if (_base.gamesList != value)
                    {
                        _base.gamesList = value;
                        _base.Notify("GamesList");
                    }
                }
            }
        }

        public bool AutoStart
        {
            get { lock (_base) { return _base.autoStart; } }
            set
            {
                lock (_base)
                {
                    if (_base.autoStart != value)
                    {
                        _base.autoStart = value;
                        _base.Notify("AutoStart");
                        Backend.Instance.Send($"autostart {value}");
                    }
                }
            }
        }
        public void SetAutoStartVar(bool value)
        {
            lock (_base)
            {
                if (_base.autoStart != value)
                {
                    _base.autoStart = value;
                    _base.Notify("AutoStart");
                }
            }
        }

        public void SuspendActiveGame()
        {
            
            Backend.Instance.Send("suspend-active-game");
        }

        public void ResumeActiveGame()
        {

            Backend.Instance.Send("resume-active-game");
        }

        public bool AutoSuspendEnabled
        {
            get { lock (_base) { return _base.autoSuspendEnabled; } }
            set
            {
                lock (_base)
                {
                    if (_base.autoSuspendEnabled != value)
                    {
                        _base.autoSuspendEnabled = value;
                        _base.Notify("AutoSuspendEnabled");
                    }
                }
            }
        }

        public void SetAutoSuspendEnabledVar(bool value)
        {
            lock (_base)
            {
                if (_base.autoSuspendEnabled != value)
                {
                    _base.autoSuspendEnabled = value;
                    Backend.Instance.Send($"set-auto-suspend {Convert.ToInt32(value)}");
                    _base.Notify("AutoSuspendEnabled");
                }
            }
        }

        public bool GoBackToSleepEnabled
        {
            get { lock (_base) { return _base.goBackToSleepEnabled; } }
            set
            {
                lock (_base)
                {
                    if (_base.goBackToSleepEnabled != value)
                    {
                        _base.goBackToSleepEnabled = value;
                        _base.Notify("GoBackToSleepEnabled");
                    }
                }
            }
        }

        public void SetGoBackToSleepEnabledVar(bool value)
        {
            lock (_base)
            {
                if (_base.goBackToSleepEnabled != value)
                {
                    _base.goBackToSleepEnabled = value;
                    Backend.Instance.Send($"set-go-back-to-sleep {Convert.ToInt32(value)}");
                    _base.Notify("GoBackToSleepEnabled");
                }
            }
        }

        public double PowerButtonAction
        {
            get { lock (_base) { return _base.powerButtonAction; } }
            set
            {
                lock (_base)
                {
                    if (_base.powerButtonAction != value)
                    {
                        _base.powerButtonAction = value;
                        _base.Notify("PowerButtonAction");
                    }
                }
            }
        }

        public void SetPowerButtonActionVar(double value)
        {
            lock (_base)
            {
                if (_base.powerButtonAction != value)
                {
                    _base.powerButtonAction = value;
                    Backend.Instance.Send($"set-power-button-action {Convert.ToInt32(value)}");
                    _base.Notify("PowerButtonAction");
                }
            }
        }

        public bool EnhancedSleepEnabled
        {
            get { lock (_base) { return _base.enhancedSleepEnabled; } }
            set
            {
                lock (_base)
                {
                    if (_base.enhancedSleepEnabled != value)
                    {
                        _base.enhancedSleepEnabled = value;
                        _base.Notify("EnhancedSleepEnabled");
                    }
                }
            }
        }

        public void SetEnhancedSleepEnabledVar(bool value)
        {
            lock (_base)
            {
                if (_base.enhancedSleepEnabled != value)
                {
                    _base.enhancedSleepEnabled = value;
                    Backend.Instance.Send($"set-enhanced-sleep {Convert.ToInt32(value)}");
                    _base.Notify("EnhancedSleepEnabled");
                }
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;

		public async Task Notify(string propertyName)
		{
			await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				if (PropertyChanged != null)
				{
					this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
				}
			});
		}

		public void Dispose()
		{
			_base.Remove(this);
		}
	}

	class MainPageModel
    {
        public bool autoStart = false;
        public bool isConnected = false;
        public bool autoSuspendEnabled = false;
        public bool goBackToSleepEnabled = false;
        public double powerButtonAction = 2; // 0: Sleep, 1: Hibernate
        public bool enhancedSleepEnabled = false;
        public ObservableCollection<GameInfo> gamesList;

        private List<MainPageModelWrapper> _wrappers = new List<MainPageModelWrapper>();

		public MainPageModelWrapper GetWrapper(CoreDispatcher dispatcher)
		{
			var wrapper = new MainPageModelWrapper(this, dispatcher);
			_wrappers.Add(wrapper);
			return wrapper;
		}

		public void Notify(string propertyName)
		{
			foreach (var wrapper in _wrappers)
				_ = wrapper.Notify(propertyName);
		}

		public void Remove(MainPageModelWrapper wraper)
		{
			_wrappers.Remove(wraper);
		}
	}
}
