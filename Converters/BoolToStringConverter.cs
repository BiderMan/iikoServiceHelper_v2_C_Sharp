using System;
using System.Globalization;
using System.Windows.Data;

namespace iikoServiceHelper.Converters
{
    public class BoolToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string param)
            {
                var parts = param.Split('|');
                if (parts.Length == 2)
                {
                    return boolValue ? parts[0] : parts[1];
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && parameter is string param)
            {
                var parts = param.Split('|');
                if (parts.Length == 2)
                {
                    // Сравниваем с первым значением как с true
                    return stringValue == parts[0];
                }
            }
            return false;
        }
    }
}
