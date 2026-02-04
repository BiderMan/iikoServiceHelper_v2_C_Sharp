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
            try { File.WriteAllText(_filePath, JsonSerializer.Serialize(commands, new JsonSerializerOptions { WriteIndented = true })); } catch { }
        }

        public string GetFilePath() => _filePath;
    }
}