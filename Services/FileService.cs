using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using iikoServiceHelper.Models;
using iikoServiceHelper.Constants;

namespace iikoServiceHelper.Services
{
    /// <summary>
    /// Сервис для работы с файлами (настройки, заметки, темы)
    /// </summary>
    public class FileService
    {
        private readonly string _appDataPath;
        private readonly string _notesFile;
        private readonly string _settingsFile;
        private readonly string _themeSettingsFile;
        private readonly string _detailedLogFile;
        private readonly ILogger<FileService>? _logger;

        public FileService(ILogger<FileService>? logger = null)
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppDataFolderName);
            
            Directory.CreateDirectory(_appDataPath);
            
            _notesFile = Path.Combine(_appDataPath, AppConstants.NotesFileName);
            _settingsFile = Path.Combine(_appDataPath, AppConstants.SettingsFileName);
            _themeSettingsFile = Path.Combine(_appDataPath, AppConstants.ThemeSettingsFileName);
            _detailedLogFile = Path.Combine(_appDataPath, AppConstants.DetailedLogFileName);
            _logger = logger;
        }

        // Свойства для путей
        public string AppDataPath => _appDataPath;
        public string NotesFile => _notesFile;
        public string SettingsFile => _settingsFile;
        public string ThemeSettingsFile => _themeSettingsFile;
        public string DetailedLogFile => _detailedLogFile;

        #region Notes

        /// <summary>
        /// Загрузить заметки из файла
        /// </summary>
        public async Task<string> LoadNotesAsync()
        {
            try
            {
                if (File.Exists(_notesFile))
                {
                    return await File.ReadAllTextAsync(_notesFile);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load notes from {Path}", _notesFile);
            }
            return string.Empty;
        }

        /// <summary>
        /// Сохранить заметки в файл
        /// </summary>
        public async Task SaveNotesAsync(string content)
        {
            try
            {
                await File.WriteAllTextAsync(_notesFile, content);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save notes to {Path}", _notesFile);
            }
        }

        #endregion

        #region Settings

        /// <summary>
        /// Загрузить настройки приложения
        /// </summary>
        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load settings from {Path}", _settingsFile);
            }
            return new AppSettings();
        }

        /// <summary>
        /// Сохранить настройки приложения
        /// </summary>
        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(_settingsFile, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save settings to {Path}", _settingsFile);
            }
        }

        #endregion

        #region Theme

        /// <summary>
        /// Загрузить настройки темы
        /// </summary>
        public ThemeSettings? LoadThemeSettings()
        {
            try
            {
                if (File.Exists(_themeSettingsFile))
                {
                    var json = File.ReadAllText(_themeSettingsFile);
                    var loaded = JsonSerializer.Deserialize<ThemeSettings>(json);
                    
                    // Миграция: если загруженные настройки старые (без новых полей DataGrid)
                    if (loaded != null && NeedsMigration(loaded))
                    {
                        _logger?.LogInformation("Detected old theme settings, migrating to new format...");
                        loaded = MigrateThemeSettings(loaded);
                        SaveThemeSettings(loaded); // Сохраняем мигрированные настройки
                        _logger?.LogInformation("Theme settings migration completed");
                    }
                    
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load theme settings from {Path}", _themeSettingsFile);
            }
            return null;
        }

        /// <summary>
        /// Проверить, нужна ли миграция настроек темы
        /// </summary>
        private bool NeedsMigration(ThemeSettings settings)
        {
            // Если нет DataGridGridLines - это старые настройки
            var darkGridLines = settings.DarkTheme?.DataGridGridLines;
            var lightGridLines = settings.LightTheme?.DataGridGridLines;
            
            return string.IsNullOrEmpty(darkGridLines) || string.IsNullOrEmpty(lightGridLines);
        }

        /// <summary>
        /// Мигрировать старые настройки темы на новый формат
        /// </summary>
        private ThemeSettings MigrateThemeSettings(ThemeSettings old)
        {
            // Создаем новые настройки с значениями по умолчанию
            var defaults = new ThemeSettings();
            
            // Мигрируем темную тему
            if (old.DarkTheme != null)
            {
                defaults.DarkTheme.WindowBackground = old.DarkTheme.WindowBackground ?? defaults.DarkTheme.WindowBackground;
                defaults.DarkTheme.Background = old.DarkTheme.Background ?? defaults.DarkTheme.Background;
                defaults.DarkTheme.Foreground = old.DarkTheme.Foreground ?? defaults.DarkTheme.Foreground;
                defaults.DarkTheme.Accent = old.DarkTheme.Accent ?? defaults.DarkTheme.Accent;
                defaults.DarkTheme.ButtonBackground = old.DarkTheme.ButtonBackground ?? defaults.DarkTheme.ButtonBackground;
                defaults.DarkTheme.ButtonForeground = old.DarkTheme.ButtonForeground ?? defaults.DarkTheme.ButtonForeground;
                defaults.DarkTheme.ButtonBorder = old.DarkTheme.ButtonBorder ?? defaults.DarkTheme.ButtonBorder;
                defaults.DarkTheme.ButtonHoverBackground = old.DarkTheme.ButtonHoverBackground ?? defaults.DarkTheme.ButtonHoverBackground;
                defaults.DarkTheme.InputBackground = old.DarkTheme.InputBackground ?? defaults.DarkTheme.InputBackground;
                defaults.DarkTheme.InputForeground = old.DarkTheme.InputForeground ?? defaults.DarkTheme.InputForeground;
                defaults.DarkTheme.CounterBackground = old.DarkTheme.CounterBackground ?? defaults.DarkTheme.CounterBackground;
                defaults.DarkTheme.CounterBorder = old.DarkTheme.CounterBorder ?? defaults.DarkTheme.CounterBorder;
                defaults.DarkTheme.CounterLabel = old.DarkTheme.CounterLabel ?? defaults.DarkTheme.CounterLabel;
                defaults.DarkTheme.CounterValue = old.DarkTheme.CounterValue ?? defaults.DarkTheme.CounterValue;
                defaults.DarkTheme.TabForeground = old.DarkTheme.TabForeground ?? defaults.DarkTheme.TabForeground;
                defaults.DarkTheme.TabHover = old.DarkTheme.TabHover ?? defaults.DarkTheme.TabHover;
                defaults.DarkTheme.ToolTipForeground = old.DarkTheme.ToolTipForeground ?? defaults.DarkTheme.ToolTipForeground;
                defaults.DarkTheme.CheckBoxCheckMark = old.DarkTheme.CheckBoxCheckMark ?? defaults.DarkTheme.CheckBoxCheckMark;
                defaults.DarkTheme.LogForeground = old.DarkTheme.LogForeground ?? defaults.DarkTheme.LogForeground;
                // Новые поля DataGrid остаются со значениями по умолчанию
            }
            
            // Мигрируем светлую тему
            if (old.LightTheme != null)
            {
                defaults.LightTheme.WindowBackground = old.LightTheme.WindowBackground ?? defaults.LightTheme.WindowBackground;
                defaults.LightTheme.Background = old.LightTheme.Background ?? defaults.LightTheme.Background;
                defaults.LightTheme.Foreground = old.LightTheme.Foreground ?? defaults.LightTheme.Foreground;
                defaults.LightTheme.Accent = old.LightTheme.Accent ?? defaults.LightTheme.Accent;
                defaults.LightTheme.ButtonBackground = old.LightTheme.ButtonBackground ?? defaults.LightTheme.ButtonBackground;
                defaults.LightTheme.ButtonForeground = old.LightTheme.ButtonForeground ?? defaults.LightTheme.ButtonForeground;
                defaults.LightTheme.ButtonBorder = old.LightTheme.ButtonBorder ?? defaults.LightTheme.ButtonBorder;
                defaults.LightTheme.ButtonHoverBackground = old.LightTheme.ButtonHoverBackground ?? defaults.LightTheme.ButtonHoverBackground;
                defaults.LightTheme.InputBackground = old.LightTheme.InputBackground ?? defaults.LightTheme.InputBackground;
                defaults.LightTheme.InputForeground = old.LightTheme.InputForeground ?? defaults.LightTheme.InputForeground;
                defaults.LightTheme.CounterBackground = old.LightTheme.CounterBackground ?? defaults.LightTheme.CounterBackground;
                defaults.LightTheme.CounterBorder = old.LightTheme.CounterBorder ?? defaults.LightTheme.CounterBorder;
                defaults.LightTheme.CounterLabel = old.LightTheme.CounterLabel ?? defaults.LightTheme.CounterLabel;
                defaults.LightTheme.CounterValue = old.LightTheme.CounterValue ?? defaults.LightTheme.CounterValue;
                defaults.LightTheme.TabForeground = old.LightTheme.TabForeground ?? defaults.LightTheme.TabForeground;
                defaults.LightTheme.TabHover = old.LightTheme.TabHover ?? defaults.LightTheme.TabHover;
                defaults.LightTheme.ToolTipForeground = old.LightTheme.ToolTipForeground ?? defaults.LightTheme.ToolTipForeground;
                defaults.LightTheme.CheckBoxCheckMark = old.LightTheme.CheckBoxCheckMark ?? defaults.LightTheme.CheckBoxCheckMark;
                defaults.LightTheme.LogForeground = old.LightTheme.LogForeground ?? defaults.LightTheme.LogForeground;
                // Новые поля DataGrid остаются со значениями по умолчанию
            }
            
            return defaults;
        }

        /// <summary>
        /// Сохранить настройки темы
        /// </summary>
        public void SaveThemeSettings(ThemeSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(_themeSettingsFile, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save theme settings to {Path}", _themeSettingsFile);
            }
        }

        #endregion

        #region Logging

        /// <summary>
        /// Записать сообщение в детальный лог
        /// </summary>
        public void WriteDetailedLog(string message)
        {
            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(_detailedLogFile, logEntry);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to write to detailed log");
            }
        }

        #endregion

        /// <summary>
        /// Проверить существование файла
        /// </summary>
        public bool FileExists(string filePath) => File.Exists(filePath);

        /// <summary>
        /// Проверить существование заметок
        /// </summary>
        public bool NotesExists => File.Exists(_notesFile);
    }
}
