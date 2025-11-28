using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Globalization;
using Windows.UI;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace Suspended
{
    public class ColorToSuspendedStateConverter : IValueConverter
    {
        public Brush SuspendedBrush { get; set; } = new SolidColorBrush(Colors.DarkOrange);
        public Brush RunningBrush { get; set; } = new SolidColorBrush(Colors.Green);

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isSuspended)
                return isSuspended ? SuspendedBrush : RunningBrush;
            return RunningBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
