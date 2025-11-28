using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;
using Windows.UI.Xaml.Data;

namespace Suspended
{
    public class BooleanToNegativeVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool flag = value is bool b && b;
            return flag ? Windows.UI.Xaml.Visibility.Collapsed : Windows.UI.Xaml.Visibility.Visible ;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Windows.UI.Xaml.Visibility v && v == Windows.UI.Xaml.Visibility.Collapsed;
        }
    }
}
