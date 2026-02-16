using System;

namespace iikoServiceHelper.Models
{
    public class ThemeSettings
    {
        public string _Info { get; set; } = "Файл настройки цветов. Цвета указываются в формате HEX (#RRGGBB или #AARRGGBB) или названием (Red, Blue). Для градиента фона окна укажите два цвета через запятую.";
        
        public ThemeColorSet DarkTheme { get; set; }
        public ThemeColorSet LightTheme { get; set; }

        public ThemeSettings()
        {
            DarkTheme = new ThemeColorSet
            {
                _Description = "Настройки темной темы",
                WindowBackground = "#000000,#1A0029",
                Background = "#1E1E24",
                Foreground = "#E0E0E0",
                Accent = "#B026FF",
                ButtonBackground = "Transparent",
                ButtonForeground = "#B026FF",
                ButtonBorder = "#B026FF",
                ButtonHoverBackground = "#1AB026FF",
                InputBackground = "#25252A",
                InputForeground = "White",
                CounterBackground = "#1E1E24",
                CounterBorder = "#333333",
                CounterLabel = "#888888",
                CounterValue = "#B026FF",
                TabForeground = "#888888",
                TabHover = "White",
                ToolTipForeground = "White",
                CheckBoxCheckMark = "#B026FF",
                LogForeground = "#AAAAAA",
                DataGridBackground = "#150020,#1A0029",
                DataGridAlternateBackground = "#150020",
                DataGridGridLines = "#333333"
            };

            LightTheme = new ThemeColorSet
            {
                _Description = "Настройки светлой темы",
                WindowBackground = "#EEEEEE",
                Background = "#EEEEEE",
                Foreground = "#404040",
                Accent = "#404040",
                ButtonBackground = "#404040",
                ButtonForeground = "#EEEEEE",
                ButtonBorder = "#EEEEEE",
                ButtonHoverBackground = "#404040",
                InputBackground = "#404040",
                InputForeground = "#EEEEEE",
                CounterBackground = "#404040",
                CounterBorder = "#EEEEEE",
                CounterLabel = "#EEEEEE",
                CounterValue = "#EEEEEE",
                TabForeground = "#404040",
                TabHover = "#404040",
                ToolTipForeground = "#404040",
                CheckBoxCheckMark = "#EEEEEE",
                LogForeground = "#404040",
                DataGridBackground = "#F5F5F5,#EBEBEB",
                DataGridAlternateBackground = "#EBEBEB",
                DataGridGridLines = "#888888"
            };
        }
    }

    public class ThemeColorSet
    {
        public string _Description { get; set; } = "";
        
        public string _Desc_WindowBackground { get; set; } = "Фон окна (градиент через запятую или HEX)";
        public string WindowBackground { get; set; } = "";
        
        public string _Desc_Background { get; set; } = "Основной фон контента";
        public string Background { get; set; } = "";
        
        public string _Desc_Foreground { get; set; } = "Основной цвет текста";
        public string Foreground { get; set; } = "";
        
        public string _Desc_Accent { get; set; } = "Цвет акцента (выделения)";
        public string Accent { get; set; } = "";
        
        public string _Desc_ButtonBackground { get; set; } = "Фон кнопок";
        public string ButtonBackground { get; set; } = "";
        
        public string _Desc_ButtonForeground { get; set; } = "Цвет текста кнопок";
        public string ButtonForeground { get; set; } = "";
        
        public string _Desc_ButtonBorder { get; set; } = "Цвет рамки кнопок";
        public string ButtonBorder { get; set; } = "";
        
        public string _Desc_ButtonHoverBackground { get; set; } = "Фон кнопки при наведении";
        public string ButtonHoverBackground { get; set; } = "";
        
        public string _Desc_InputBackground { get; set; } = "Фон полей ввода";
        public string InputBackground { get; set; } = "";
        
        public string _Desc_InputForeground { get; set; } = "Цвет текста полей ввода";
        public string InputForeground { get; set; } = "";
        
        public string _Desc_CounterBackground { get; set; } = "Фон счетчика";
        public string CounterBackground { get; set; } = "";
        
        public string _Desc_CounterBorder { get; set; } = "Рамка счетчика";
        public string CounterBorder { get; set; } = "";
        
        public string _Desc_CounterLabel { get; set; } = "Цвет надписи 'Счетчик'";
        public string CounterLabel { get; set; } = "";
        
        public string _Desc_CounterValue { get; set; } = "Цвет значения счетчика";
        public string CounterValue { get; set; } = "";
        
        public string _Desc_TabForeground { get; set; } = "Цвет заголовков вкладок";
        public string TabForeground { get; set; } = "";
        
        public string _Desc_TabHover { get; set; } = "Цвет вкладки при наведении";
        public string TabHover { get; set; } = "";
        
        public string _Desc_ToolTipForeground { get; set; } = "Цвет текста подсказок";
        public string ToolTipForeground { get; set; } = "";
        
        public string _Desc_CheckBoxCheckMark { get; set; } = "Цвет галочки чекбокса";
        public string CheckBoxCheckMark { get; set; } = "";
        
        public string _Desc_LogForeground { get; set; } = "Цвет текста лога";
        public string LogForeground { get; set; } = "";
        
        public string _Desc_DataGridBackground { get; set; } = "Фон списка команд (градиент)";
        public string DataGridBackground { get; set; } = "";
        
        public string _Desc_DataGridAlternateBackground { get; set; } = "Фон чётных строк списка команд";
        public string DataGridAlternateBackground { get; set; } = "";
        
        public string _Desc_DataGridGridLines { get; set; } = "Цвет линий сетки списка команд";
        public string DataGridGridLines { get; set; } = "";
    }
}
