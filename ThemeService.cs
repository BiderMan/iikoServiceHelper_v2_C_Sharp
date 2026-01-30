using System.Windows;
using System.Windows.Media;
using iikoServiceHelper.Models;

namespace iikoServiceHelper.Services
{
    public static class ThemeService
    {
        public static void ApplyTheme(ResourceDictionary resources, ThemeColorSet theme)
        {
            resources["BrushWindowBackground"] = ParseBrush(theme.WindowBackground);
            resources["BrushBackground"] = ParseBrush(theme.Background);
            resources["BrushForeground"] = ParseBrush(theme.Foreground);
            
            resources["BrushAccent"] = ParseBrush(theme.Accent);
            resources["ColorAccent"] = ParseColor(theme.Accent);

            resources["BrushButtonBackground"] = ParseBrush(theme.ButtonBackground);
            resources["BrushButtonForeground"] = ParseBrush(theme.ButtonForeground);
            resources["BrushButtonBorder"] = ParseBrush(theme.ButtonBorder);
            resources["BrushButtonHoverBackground"] = ParseBrush(theme.ButtonHoverBackground);

            resources["BrushInputBackground"] = ParseBrush(theme.InputBackground);
            resources["BrushInputForeground"] = ParseBrush(theme.InputForeground);

            resources["BrushCounterBackground"] = ParseBrush(theme.CounterBackground);
            resources["BrushCounterBorder"] = ParseBrush(theme.CounterBorder);
            resources["BrushCounterLabel"] = ParseBrush(theme.CounterLabel);
            resources["BrushCounterValue"] = ParseBrush(theme.CounterValue);

            resources["BrushTabForeground"] = ParseBrush(theme.TabForeground);
            resources["BrushTabHover"] = ParseBrush(theme.TabHover);

            resources["BrushToolTipForeground"] = ParseBrush(theme.ToolTipForeground);
            resources["BrushCheckBoxCheckMark"] = ParseBrush(theme.CheckBoxCheckMark);
            resources["BrushLogForeground"] = ParseBrush(theme.LogForeground);
        }

        private static Brush ParseBrush(string colorString)
        {
            if (string.IsNullOrWhiteSpace(colorString)) return Brushes.Transparent;
            
            try
            {
                if (colorString.Contains(","))
                {
                    var parts = colorString.Split(',');
                    if (parts.Length >= 2)
                    {
                        var color1 = (Color)ColorConverter.ConvertFromString(parts[0].Trim());
                        var color2 = (Color)ColorConverter.ConvertFromString(parts[1].Trim());
                        var gradient = new LinearGradientBrush();
                        gradient.StartPoint = new Point(0, 0);
                        gradient.EndPoint = new Point(1, 1);
                        gradient.GradientStops.Add(new GradientStop(color1, 0.0));
                        gradient.GradientStops.Add(new GradientStop(color2, 1.0));
                        return gradient;
                    }
                }
                
                var color = (Color)ColorConverter.ConvertFromString(colorString);
                return new SolidColorBrush(color);
            }
            catch
            {
                return Brushes.Transparent;
            }
        }

        private static Color ParseColor(string colorString)
        {
            try 
            {
                if (string.IsNullOrWhiteSpace(colorString)) return Colors.Transparent;
                if (colorString.Contains(",")) colorString = colorString.Split(',')[0];
                return (Color)ColorConverter.ConvertFromString(colorString.Trim());
            }
            catch { return Colors.Transparent; }
        }
    }
}
