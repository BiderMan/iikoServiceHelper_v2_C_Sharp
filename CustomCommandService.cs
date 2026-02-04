using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using iikoServiceHelper.Models;

namespace iikoServiceHelper.Services
{
    public class CustomCommandService
    {
        private readonly string _filePath;

        public CustomCommandService(string appDir)
        {
            _filePath = Path.Combine(appDir, "custom_commands.json");
        }

        public List<CustomCommand> LoadCommands()
        {
            if (!File.Exists(_filePath))
            {
                // Создаем файл с примером, если он не существует
                var defaults = DefaultCommandsProvider.GetDefaultCommands();
                SaveCommands(defaults);
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<CustomCommand>>(json) ?? new List<CustomCommand>();
            }
            catch
            {
                return new List<CustomCommand>();
            }
        }

        public void SaveCommands(List<CustomCommand> commands)
        {
            try
            {
                // Проверяем, что список команд не слишком велик для предотвращения чрезмерного использования дискового пространства
                if (commands.Count > 10000)
                {
                    throw new InvalidOperationException("Too many commands to save, exceeds maximum limit");
                }
                
                File.WriteAllText(_filePath, JsonSerializer.Serialize(commands, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                // Логируем ошибку сохранения команд
                System.Diagnostics.Debug.WriteLine($"Failed to save commands to {_filePath}: {ex.Message}");
                throw; // Перебрасываем исключение, чтобы вызывающий код мог обработать ошибку
            }
        }

        public string GetFilePath() => _filePath;
    }
}