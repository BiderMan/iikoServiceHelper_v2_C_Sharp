using System;

namespace iikoServiceHelper.Constants
{
    /// <summary>
    /// Основные константы приложения
    /// </summary>
    public static class AppConstants
    {
        /// <summary>
        /// Название приложения
        /// </summary>
        public const string AppName = "iikoServiceHelper";

        /// <summary>
        /// Версия приложения
        /// </summary>
        public const string AppVersion = "2.5.0";

        /// <summary>
        /// Папка данных приложения в LocalApplicationData
        /// </summary>
        public const string AppDataFolderName = "iikoServiceHelper_v2";

        // Порты и сетевые настройки

        /// <summary>
        /// Порт для Chrome DevTools Protocol
        /// </summary>
        public const int ChromeDebugPort = 9222;

        // Настройки шрифтов

        /// <summary>
        /// Минимальный размер шрифта заметок
        /// </summary>
        public const int MinFontSize = 8;

        /// <summary>
        /// Максимальный размер шрифта заметок
        /// </summary>
        public const int MaxFontSize = 72;

        /// <summary>
        /// Размер шрифта по умолчанию
        /// </summary>
        public const double DefaultFontSize = 14.0;

        /// <summary>
        /// Шаг изменения шрифта при масштабировании
        /// </summary>
        public const double FontSizeStep = 2.0;

        // Уведомления

        /// <summary>
        /// Длительность уведомлений по умолчанию (секунды)
        /// </summary>
        public const int DefaultNotificationDuration = 3;

        /// <summary>
        /// Минимальная длительность уведомлений (секунды)
        /// </summary>
        public const int MinNotificationDuration = 1;

        /// <summary>
        /// Максимальная длительность уведомлений (секунды)
        /// </summary>
        public const int MaxNotificationDuration = 10;

        // CRM настройки

        /// <summary>
        /// Интервал автоматического входа в CRM (минуты)
        /// </summary>
        public const int CrmAutoLoginInterval = 30;

        /// <summary>
        /// Таймаут ожидания загрузки страницы CRM (секунды)
        /// </summary>
        public const int CrmPageLoadTimeout = 30;

        // Имена файлов

        /// <summary>
        /// Имя файла настроек
        /// </summary>
        public const string SettingsFileName = "settings.json";

        /// <summary>
        /// Имя файла заметок
        /// </summary>
        public const string NotesFileName = "notes.txt";

        /// <summary>
        /// Имя файла пользовательских команд
        /// </summary>
        public const string CustomCommandsFileName = "custom_commands.json";

        /// <summary>
        /// Имя файла настроек темы
        /// </summary>
        public const string ThemeSettingsFileName = "theme_colors.json";

        /// <summary>
        /// Имя файла детального лога
        /// </summary>
        public const string DetailedLogFileName = "detailed_log.txt";

        /// <summary>
        /// Имя файла crash log
        /// </summary>
        public const string CrashLogFileName = "crash_log.txt";

        /// <summary>
        /// Имя файла аудита безопасности
        /// </summary>
        public const string SecurityAuditFileName = "security_audit.log";

        // Размеры окна по умолчанию

        /// <summary>
        /// Ширина окна по умолчанию
        /// </summary>
        public const double DefaultWindowWidth = 950;

        /// <summary>
        /// Высота окна по умолчанию
        /// </summary>
        public const double DefaultWindowHeight = 600;

        /// <summary>
        /// Позиция окна слева по умолчанию
        /// </summary>
        public const double DefaultWindowLeft = 100;

        /// <summary>
        /// Позиция окна сверху по умолчанию
        /// </summary>
        public const double DefaultWindowTop = 100;
    }

    /// <summary>
    /// Константы задержек для макросов и команд
    /// </summary>
    public static class DelayConstants
    {
        /// <summary>
        /// Задержка между нажатиями клавиш (мс)
        /// </summary>
        public const int DefaultKeyPress = 50;

        /// <summary>
        /// Минимальная задержка между нажатиями (мс)
        /// </summary>
        public const int MinKeyPress = 0;

        /// <summary>
        /// Максимальная задержка между нажатиями (мс)
        /// </summary>
        public const int MaxKeyPress = 1000;

        /// <summary>
        /// Пауза между действиями (мс)
        /// </summary>
        public const int DefaultActionPause = 100;

        /// <summary>
        /// Минимальная пауза между действиями (мс)
        /// </summary>
        public const int MinActionPause = 0;

        /// <summary>
        /// Максимальная пауза между действиями (мс)
        /// </summary>
        public const int MaxActionPause = 5000;

        /// <summary>
        /// Ожидание фокуса ввода (мс)
        /// </summary>
        public const int DefaultFocusWait = 500;

        /// <summary>
        /// Минимальное ожидание фокуса (мс)
        /// </summary>
        public const int MinFocusWait = 0;

        /// <summary>
        /// Максимальное ожидание фокуса (мс)
        /// </summary>
        public const int MaxFocusWait = 10000;

        /// <summary>
        /// Задержка для debounce автосохранения (мс)
        /// </summary>
        public const int AutoSaveDebounce = 1000;
    }

    /// <summary>
    /// URL константы
    /// </summary>
    public static class UrlConstants
    {
        /// <summary>
        /// FTP сервер для файлов
        /// </summary>
        public const string FtpServer = "ftp://files.resto.lan";

        /// <summary>
        /// Базовый URL для POS инсталляторов
        /// </summary>
        public const string PosInstallerBaseUrl = "https://pos.iiko.ru/installers/";

        /// <summary>
        /// URL для проверки обновлений на GitHub
        /// </summary>
        public const string GitHubApiReleasesUrl = "https://api.github.com/repos/YOUR_REPO/releases/latest";

        /// <summary>
        /// URL страницы релизов
        /// </summary>
        public const string GitHubReleasesUrl = "https://github.com/YOUR_REPO/releases";
    }

    /// <summary>
    /// Виртуальные коды клавиш
    /// </summary>
    public static class VirtualKeyCodes
    {
        public const int VK_SHIFT = 16;
        public const int VK_CONTROL = 17;
        public const int VK_ALT = 18;
        public const int VK_LSHIFT = 160;
        public const int VK_RSHIFT = 161;
        public const int VK_LCONTROL = 162;
        public const int VK_RCONTROL = 163;
        public const int VK_LMENU = 164;    // Left Alt
        public const int VK_RMENU = 165;    // Right Alt
    }

    /// <summary>
    /// Windows сообщения
    /// </summary>
    public static class WindowsMessages
    {
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
    }
}
